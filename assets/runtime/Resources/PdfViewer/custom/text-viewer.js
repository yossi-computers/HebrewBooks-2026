// HebrewBooks text-book viewer (Otzraya HTML in .txt files).
//
// The host calls into this module via a small set of HB_* globals — same pattern
// as the PDF viewer (viewer.js). The .txt file IS HTML; we fetch it, inject it
// into #textBody, build a TOC from <h2> anchors, and optionally highlight a list
// of search terms passed in by the corpus search.
//
// Input contract (HB_loadText):
//   url      — full URL on the otzraya.local virtual host
//   title    — display name, shown in the toolbar
//   terms    — string[] of search terms to highlight on first paint (may be empty)
//   anchorIx — 1-based index into the <h2> TOC to scroll to on open (or null)

const $ = (sel) => document.querySelector(sel);

const state = {
  // Protect-mode (kiosk) — see viewer.js HB_setProtectMode for the contract. Pushed by
  // the host before every HB_loadText call.
  protectMode: false,
  textBody: null,
  toolbar: null,
  toc: [],            // [{ label, el }]
  tocBuilt: false,    // TOC is built LAZILY — only when the sidebar is first opened
                      // or anchor nav is used. Building it eagerly on load cost
                      // 15-30s on huge Otzraya works (ערוך השולחן has thousands of
                      // headings), blocking the whole UI thread even though the
                      // sidebar starts hidden.
  anchorIndex: 0,     // 0-based index into `toc`
  zoom: 1.0,          // font-size multiplier (1.0 == default 18px from CSS)
  findMatches: [],    // [HTMLElement] for the find bar
  findIndex: -1,
  hitMatches: [],     // [HTMLElement] for corpus-search highlights
  hitCurrentIndex: -1, // which span in hitMatches has the .current class
  // Monotonic counter bumped on each HB_loadText. Async work (fetch + deferred
  // toc/highlight pass) captures the value at start and bails if the user has
  // since switched to a different book — without this, two rapid clicks left
  // the OLDER terms being wrapped into the NEWER book's innerHTML (and the
  // visible result was "no highlights" when the older book's terms didn't
  // match the newer body's text).
  _loadGen: 0,
};

function init() {
  state.textBody = $('#textBody');
  state.toolbar = $('#toolbar');

  bindToolbar();
  bindFindBar();
  bindContextMenu();
  bindKeyboard();

  // Tell the host we're alive — the PdfJsHost waits for this before calling
  // HB_loadText (mirrors hb-viewer-ready from the PDF viewer).
  postHost('hb-viewer-ready');
  hideOverlay();
}

// ---------- host bridge ----------

function postHost(msg) {
  try { window.chrome?.webview?.postMessage(msg); } catch { /* ignore */ }
}

window.HB_loadText = async function (url, title, terms, anchorIx) {
  const myGen = ++state._loadGen;
  showLoading(true);
  const t0 = performance.now();
  postHost('hb-debug:HB_loadText url=' + url + ' gen=' + myGen + ' termsType=' + (Array.isArray(terms) ? 'array' : typeof terms) + ' termsCount=' + (Array.isArray(terms) ? terms.length : 0) + ' termsSample=' + JSON.stringify(Array.isArray(terms) ? terms.slice(0, 3) : terms));
  try {
    document.getElementById('docTitle').textContent = title || '';
    const html = await fetchText(url);
    const tFetch = performance.now();
    postHost('hb-debug:HB_loadText step=fetched gen=' + myGen + ' ms=' + Math.round(tFetch - t0) + ' bytes=' + html.length);
    if (myGen !== state._loadGen) {
      postHost('hb-debug:HB_loadText step=superseded-after-fetch gen=' + myGen);
      return;
    }
    state.textBody.innerHTML = html;
    // Wrap each heading-delimited run into a <section class="hb-sec"> so the CSS
    // content-visibility:auto can skip layout/paint of off-screen sections. The
    // Otzraya HTML is flat (h1..h6 + bare text between), so without this the browser
    // lays out the ENTIRE 32MB document on first paint — ~18s of frozen UI. With it,
    // only the visible viewport is laid out; the rest is sized from an estimate and
    // rendered lazily on scroll.
    const tSecStart = performance.now();
    wrapIntoSections(state.textBody);
    postHost('hb-debug:sections n=' + state.textBody.children.length + ' ms=' + Math.round(performance.now() - tSecStart));
    state.hitMatches = [];   // fresh body → drop stale highlight refs immediately
    state.hitCurrentIndex = -1;
    updateHitPos();          // hide the result counter until the new book is highlighted
    state.findMatches = [];
    state.findIndex = -1;
    state.toc = [];
    state.tocTree = [];      // renderToc walks the TREE, not the flat list — reset BOTH
    state.tocBuilt = false;  // new book → TOC rebuilt lazily on first sidebar/anchor use
    state.anchorIndex = 0;
    // Wipe the previously-rendered outline + anchor counter NOW, so the prior book's TOC
    // can't stay on screen when switching books with the sidebar already open. (The lazy
    // "build on sidebar open" path doesn't fire while the sidebar is already open; the
    // deferred block below rebuilds it for the new book in that case.)
    renderToc();
    updateAnchorPos();
    const tHtml = performance.now();
    postHost('hb-debug:HB_loadText step=innerHTML gen=' + myGen + ' ms=' + Math.round(tHtml - tFetch));

    // Show the book + hide the spinner AS SOON as the HTML is parsed — do a
    // coarse initial scroll (anchor, or top) right away. The heavy whole-document
    // work (TOC scan + multi-term highlight walk) used to run BEFORE hb-loaded,
    // so on big Otzraya tractates the spinner stayed for seconds. It's now
    // deferred off the first paint; the precise jump-to-hit happens once the
    // highlight pass finishes (a beat later — content is already readable).
    const hasAnchor = typeof anchorIx === 'number' && anchorIx >= 1;
    if (!hasAnchor) window.scrollTo(0, 0);
    postHost('hb-loaded');
    showLoading(false);
    postHost('hb-debug:HB_loadText step=visible gen=' + myGen + ' totalMs=' + Math.round(performance.now() - t0));

    requestAnimationFrame(() => setTimeout(() => {
      // Bail if a newer HB_loadText fired between fetch+innerHTML and this
      // deferred callback — otherwise we'd build a TOC + apply OLD terms onto
      // the NEW body's content (visible symptom: book switch shows no
      // highlights because the prior call's terms don't match this book's text).
      if (myGen !== state._loadGen) {
        postHost('hb-debug:HB_loadText step=superseded-before-enhance gen=' + myGen);
        return;
      }
      postHost('hb-debug:HB_loadText step=enhance-start gen=' + myGen);
      try {
        if (hasAnchor) {
          // Anchor (chapter) open needs the TOC to map index → element. Rare path.
          ensureTocBuilt();
          if (anchorIx <= state.toc.length) goToAnchor(anchorIx - 1);
        } else {
          // No anchor: if the sidebar is already OPEN, the user is looking at the outline,
          // so build + render THIS book's TOC now (the lazy build-on-open path won't fire
          // since it's already open). Deferred to here so it doesn't delay first paint.
          const sidebar = document.getElementById('sidebar');
          if (sidebar && !sidebar.classList.contains('hidden')) {
            ensureTocBuilt();
            renderToc();
          }
        }
        // Highlight runs CHUNKED + async (yields between batches) so it never blocks
        // the UI on a huge book, and it scrolls to the first hit the moment one is
        // found (scrollToFirst=true only for search opens, not anchor opens). Fire-
        // and-forget — the generation guard inside aborts if the user switches book.
        if (Array.isArray(terms) && terms.length > 0) {
          highlightTerms(terms, myGen, !hasAnchor);
        }
        postHost('hb-debug:textload-hit fetch=' + Math.round(tFetch - t0) +
          'ms html=' + Math.round(tHtml - tFetch) +
          'ms total=' + Math.round(performance.now() - t0));
      } catch (e) {
        postHost('hb-debug:textload enhance failed ' + String(e?.message || e) + ' stack=' + String(e?.stack || ''));
      }
    }, 0));
  } catch (e) {
    postHost('hb-debug:HB_loadText THREW gen=' + myGen + ' err=' + String(e?.message || e) + ' stack=' + String(e?.stack || ''));
    showError(String(e?.message || e));
    showLoading(false);
  }
};

window.HB_clearDocument = function () {
  state.textBody.innerHTML = '';
  state.toc = [];
  state.tocBuilt = false;
  state.findMatches = [];
  state.hitMatches = [];
  state.hitCurrentIndex = -1;
  renderToc();
  updateAnchorPos();
  updateHitPos();
};

window.HB_goToAnchor = function (index1) {
  ensureTocBuilt();
  if (typeof index1 !== 'number' || index1 < 1 || index1 > state.toc.length) return;
  goToAnchor(index1 - 1);
};

/// Host-callable: prepare the document for full-document PDF capture before the
/// C# print path calls CoreWebView2.PrintToPdfAsync. The pinned WebView2 build
/// doesn't reliably apply @media print stylesheets in time for PrintToPdfAsync,
/// so we mirror the print rules under a regular class (body.hb-print-prepared)
/// and let the browser settle one frame before returning. Without this only the
/// currently-scrolled viewport ends up in the PDF.
window.HB_preparePrint = function () {
  // Belt-and-braces inline styles: even with the .hb-print-prepared class rules,
  // the root `html, body { height:100%; overflow:hidden }` from line 35 of
  // text-viewer.css will clip the print output to one page unless we also force
  // the document to flow. Inline + !important so nothing later can override.
  const force = (el, decls) => {
    if (!el) return;
    for (const [prop, val] of decls) el.style.setProperty(prop, val, 'important');
  };
  force(document.documentElement, [['height', 'auto'], ['overflow', 'visible']]);
  force(document.body,            [['height', 'auto'], ['overflow', 'visible']]);
  document.body.classList.add('hb-print-prepared');
  force(document.getElementById('body'), [['display', 'block'], ['height', 'auto']]);
  force(document.getElementById('textContainer'),
        [['overflow', 'visible'], ['height', 'auto'], ['padding', '0'], ['background', '#fff']]);
  // Force-render every virtualized section. content-visibility:auto on .hb-sec
  // skips layout for off-screen blocks; without resetting, only the on-screen
  // sections survive the print snapshot.
  document.querySelectorAll('#textBody .hb-sec').forEach(el => {
    el.style.setProperty('content-visibility', 'visible', 'important');
    el.style.setProperty('contain-intrinsic-size', 'auto', 'important');
  });
  // Force a synchronous layout pass so PrintToPdfAsync sees the new dimensions.
  // Reading offsetHeight commits pending style/layout work.
  void document.body.offsetHeight;
};

/// Counterpart of HB_preparePrint — restore the live reading layout after the
/// PDF capture. In practice the host immediately swaps the WebView2 to PDF mode
/// after PrintToPdfAsync completes, so this is mostly a safety net.
window.HB_endPrint = function () {
  const clear = (el, props) => {
    if (!el) return;
    for (const p of props) el.style.removeProperty(p);
  };
  clear(document.documentElement, ['height', 'overflow']);
  clear(document.body, ['height', 'overflow']);
  document.body.classList.remove('hb-print-prepared');
  clear(document.getElementById('body'), ['display', 'height']);
  clear(document.getElementById('textContainer'), ['overflow', 'height', 'padding', 'background']);
  document.querySelectorAll('#textBody .hb-sec').forEach(el => {
    el.style.removeProperty('content-visibility');
    el.style.removeProperty('contain-intrinsic-size');
  });
};

/// Host-callable: turn protect-mode (kiosk) on/off — same contract as viewer.js
/// HB_setProtectMode. Suppresses right-click, clipboard, and external-link follows.
window.HB_setProtectMode = function (active) {
  state.protectMode = !!active;
  // Re-bind the body-level <a href> capture every time, idempotent.
  if (!state._protectClickBound) {
    document.addEventListener('click', (e) => {
      if (!state.protectMode) return;
      const a = e.target && e.target.closest && e.target.closest('a[href]');
      if (!a) return;
      const href = a.getAttribute('href') || '';
      // Internal anchors (#fragment) stay live — they're how the in-page TOC
      // navigates. Everything else (http://, https://, mailto:, file://, ...) is
      // swallowed: in a kiosk we never shell out from a book's HTML.
      if (href.startsWith('#')) return;
      e.preventDefault(); e.stopPropagation();
    }, true);
    state._protectClickBound = true;
  }
};

// Palette theming from the host — identical contract to viewer.js HB_setTheme. Applies a map of
// CSS custom properties to :root so the text reader's chrome follows the app palette. See
// docs/color-system-spec.md surface D.
window.HB_setTheme = function (vars) {
  try {
    if (!vars || typeof vars !== 'object') return;
    const root = document.documentElement;
    for (const k in vars) {
      if (Object.prototype.hasOwnProperty.call(vars, k)) root.style.setProperty(k, vars[k]);
    }
  } catch (e) { /* non-fatal */ }
};

window.HB_setHighlightTerms = function (terms) {
  clearHighlights('.hb-hit');
  state.hitMatches = [];
  state.hitCurrentIndex = -1;
  if (Array.isArray(terms) && terms.length > 0) highlightTerms(terms);
};

// ---------- fetching the .txt file ----------

let _lastFetch = { url: null, html: null };

async function fetchText(url) {
  // One-entry cache: reopening the SAME book (toggling in-book search, going
  // back, the rapid-click superseded reopen) skips the USB read + UTF-8 decode
  // of a multi-MB file. Different book → normal fetch (and replace the cache).
  if (_lastFetch.url === url && _lastFetch.html !== null) return _lastFetch.html;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
  // The Otzraya files are UTF-8 text containing HTML. .text() gives us the raw
  // string — we then drop it straight into innerHTML so the browser parses it
  // (same as opening a .html file would).
  const html = await res.text();
  _lastFetch = { url, html };
  return html;
}

// Wrap the flat heading+text stream into <section class="hb-sec"> blocks so CSS
// content-visibility:auto can skip off-screen layout. Sections are split BOTH at
// headings AND by size (~CHUNK chars) — some works have very few headings (דור
// המלקטים: 53MB / 221 headings) which would otherwise produce huge sections that
// content-visibility can't skip. Each section also gets a char-derived
// contain-intrinsic-size so the scrollbar height (and thus scroll-to-hit) is
// reasonably accurate before the section has been rendered. Cheap: moves/ splits
// existing nodes, no re-parse.
function wrapIntoSections(container) {
  // Pack the content into <section class="hb-sec"> blocks of ~CHUNK chars so CSS
  // content-visibility:auto can skip off-screen layout. Two structures must both
  // work: a flat heading+text stream (ערוך השולחן, 1562 headings) AND content
  // buried inside a few GIANT elements (דור המלקטים: 53MB, text wrapped in huge
  // <b>/<small> runs) — so we RECURSE into any node bigger than CHUNK instead of
  // appending it whole. No per-section intrinsic-size: a uniform CSS default keeps
  // the first paint fast (per-section estimates measurably regressed it).
  const CHUNK = 16000;
  const isHeading = (n) =>
    n.nodeType === 1 && n.tagName.length === 2 && n.tagName[0] === 'H' &&
    n.tagName[1] >= '1' && n.tagName[1] <= '6';

  // Decide the strategy from the book's shape. A book with MANY headings (ערוך
  // השולחן: 1562) already breaks into small simanim — splitting those further by
  // size measurably slowed its first paint, so we keep heading-only sections.
  // A book with FEW headings over a lot of text (דור המלקטים: 221 headings /
  // 31M chars → 143K chars each) needs size-splitting or its on-screen section
  // is enormous. Threshold = average chars between headings.
  let headingCount = 0, totalChars = 0;
  {
    const w = document.createTreeWalker(container, NodeFilter.SHOW_ELEMENT, {
      acceptNode: (n) => isHeading(n) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP,
    });
    while (w.nextNode()) headingCount++;
    totalChars = (container.textContent || '').length;
  }
  const avgPerHeading = headingCount > 0 ? totalChars / headingCount : Infinity;
  const splitBySize = avgPerHeading > 50000;   // few headings / giant runs → size-split

  const frag = document.createDocumentFragment();
  let sec = null, secChars = 0;
  // Give each closed section a char-derived intrinsic height so the scrollbar (and
  // therefore scroll-to-hit) is accurate before the section has actually rendered —
  // a uniform CSS estimate made the jump-to-result land far from the highlight on
  // big books ("marks badly"). ~55 Hebrew chars/line at our width, ~30px line.
  const closeSec = () => {
    if (sec) sec.style.containIntrinsicSize = 'auto ' + Math.max(120, Math.round(secChars / 55 * 30)) + 'px';
  };
  const newSec = () => { closeSec(); sec = document.createElement('section'); sec.className = 'hb-sec'; secChars = 0; frag.appendChild(sec); };
  newSec();
  const place = (node, chars) => {
    sec.appendChild(node);
    secChars += chars;
    if (splitBySize && secChars >= CHUNK) newSec();
  };

  const emit = (node) => {
    if (isHeading(node)) {
      if (secChars > 0) newSec();                  // heading starts a fresh section
      place(node, (node.textContent || '').length);
      return;
    }
    const len = node.nodeType === 3 ? node.nodeValue.length : (node.textContent || '').length;
    // Heading-only mode (many headings): append whole, never split.
    if (!splitBySize || len <= CHUNK) { place(node, len); return; }
    // Size mode, oversized node: split text at whitespace; recurse into elements.
    if (node.nodeType === 3) {
      const text = node.nodeValue;
      let i = 0;
      while (i < text.length) {
        let end = Math.min(i + CHUNK, text.length);
        if (end < text.length) {
          const sp = text.lastIndexOf(' ', end);
          if (sp > i + CHUNK / 2) end = sp + 1;
        }
        place(document.createTextNode(text.slice(i, end)), end - i);
        i = end;
      }
    } else {
      for (const k of Array.from(node.childNodes)) emit(k);
    }
  };

  for (const n of Array.from(container.childNodes)) emit(n);
  closeSec();   // finalise the last section's intrinsic size
  container.appendChild(frag);
}

// ---------- TOC from h1-h6 anchors ----------

// Build the TOC on demand (first sidebar open / anchor nav), once per loaded book.
// Returns true if there are any entries.
function ensureTocBuilt() {
  if (!state.tocBuilt) {
    buildToc();
    state.tocBuilt = true;
  }
  return state.toc.length > 0;
}

function buildToc() {
  state.toc = [];      // Flat: same as before, used by prev/next + current-anchor highlighting.
  state.tocTree = [];  // Hierarchical: each entry carries .children + .expanded; renderToc walks this.

  // Walk h1..h6 in document order and bucket each one into the deepest
  // enclosing parent — a heading at level N is a child of the most recent
  // preceding heading with level < N. The level-stack is the standard way to
  // turn a flat heading list into a tree.
  const stack = [];
  const headings = state.textBody.querySelectorAll('h1, h2, h3, h4, h5, h6');
  headings.forEach((h, i) => {
    h.id = 'anchor-' + i;
    const level = parseInt(h.tagName.substring(1), 10) || 2;
    const entry = {
      label: h.textContent.trim(),
      el: h,
      level,
      flatIndex: i,
      children: [],
      expanded: true,   // Default fully expanded; user collapses on demand.
    };
    state.toc.push(entry);
    // Default expand only for the top two levels. On big Otzraya books (e.g.
    // ערוך השולחן has 3000+ headings) defaulting EVERY entry to expanded made
    // renderToc create 3000+ <li>s synchronously — 30+ seconds of blocked JS
    // even though the user typically only navigates the top sections. Top two
    // levels keep the familiar "everything in view" feel without the cost;
    // deeper levels start collapsed and expand on click via the ▸ toggle.
    entry.expanded = level <= 2;
    // Pop everything at or deeper than this heading's level — those subtrees
    // are closed; this one starts a new sibling/child.
    while (stack.length > 0 && stack[stack.length - 1].level >= level) {
      stack.pop();
    }
    if (stack.length === 0) state.tocTree.push(entry);
    else stack[stack.length - 1].children.push(entry);
    stack.push(entry);
  });

  state.anchorIndex = 0;
  renderToc();
  updateAnchorPos();
}

function renderToc() {
  const list = $('#tocList');
  list.innerHTML = '';
  for (const entry of state.tocTree) {
    list.appendChild(renderTocNode(entry));
  }
}

function renderTocNode(entry) {
  const hasKids = entry.children.length > 0;
  const li = document.createElement('li');
  let cls = 'toc-item toc-level-' + entry.level;
  if (hasKids) cls += ' toc-has-children';
  if (hasKids && !entry.expanded) cls += ' toc-collapsed';
  if (entry.flatIndex === state.anchorIndex) cls += ' current';
  li.className = cls;

  // Row = the clickable line. We keep label and toggle as separate spans so
  // clicking the toggle expands/collapses without scrolling, and clicking
  // anywhere else on the row scrolls to the heading.
  const row = document.createElement('div');
  row.className = 'toc-row';

  if (hasKids) {
    const toggle = document.createElement('span');
    toggle.className = 'toc-toggle';
    toggle.textContent = entry.expanded ? '▾' : '▸';  // ▾ / ▸
    toggle.addEventListener('click', (e) => {
      e.stopPropagation();
      entry.expanded = !entry.expanded;
      // Re-render in place — small enough that doing a full rebuild is simpler
      // and more reliable than mutating the DOM.
      renderToc();
    });
    row.appendChild(toggle);
  } else {
    // Spacer so labels of leaf siblings align with their parents' labels (the
    // toggle takes a fixed width; without a spacer the leaf text would shift).
    const spacer = document.createElement('span');
    spacer.className = 'toc-toggle toc-toggle-spacer';
    row.appendChild(spacer);
  }

  const label = document.createElement('span');
  label.className = 'toc-label';
  label.textContent = entry.label;
  row.appendChild(label);

  row.addEventListener('click', (e) => {
    // The toggle stopped propagation already; this fires only when the user
    // clicked the label or row gutter.
    if (e.target.classList.contains('toc-toggle')) return;
    goToAnchor(entry.flatIndex);
  });
  li.appendChild(row);

  // Children are rendered ONLY when this entry is expanded. The previous
  // unconditional recursion built the full TOC tree (all descendants) on the
  // first paint — fine for the legacy ~50-entry corpus but pathological on
  // big Otzraya works where it added 30+ seconds of synchronous DOM work to
  // every open. Now collapsed branches keep their data in entry.children but
  // contribute zero <li> until the user expands them. Toggle click triggers
  // renderToc() which re-walks the tree — newly-expanded entries now emit
  // their children, freshly-collapsed entries don't.
  if (hasKids && entry.expanded) {
    const ul = document.createElement('ul');
    ul.className = 'toc-children';
    for (const child of entry.children) {
      ul.appendChild(renderTocNode(child));
    }
    li.appendChild(ul);
  }

  return li;
}

function goToAnchor(i) {
  if (i < 0 || i >= state.toc.length) return;
  state.anchorIndex = i;
  // Use the convergent scroll, NOT the one-shot scrollIntoView: on a FAR jump the
  // content-visibility:auto sections in between report estimated heights, so a single
  // scroll lands near-but-not-on the target and the user had to click twice. This
  // re-scrolls until the target's real rect is in view, so one click lands exactly.
  scrollIntoViewStable(state.toc[i].el, state._loadGen);
  renderToc();
  updateAnchorPos();
}

function updateAnchorPos() {
  const pos = state.toc.length === 0 ? '—' : `${state.anchorIndex + 1}/${state.toc.length}`;
  $('#anchorPos').textContent = pos;
}

// ---------- toolbar ----------

function bindToolbar() {
  $('#btnSidebar').addEventListener('click', () => {
    const sidebar = $('#sidebar');
    const opening = sidebar.classList.contains('hidden');
    // Build the (possibly expensive) TOC only now, the first time it's shown.
    if (opening) ensureTocBuilt();
    sidebar.classList.toggle('hidden');
  });
  $('#btnPrev').addEventListener('click', () => {
    if (!ensureTocBuilt()) return;
    const i = (state.anchorIndex - 1 + state.toc.length) % state.toc.length;
    goToAnchor(i);
  });
  $('#btnNext').addEventListener('click', () => {
    if (!ensureTocBuilt()) return;
    const i = (state.anchorIndex + 1) % state.toc.length;
    goToAnchor(i);
  });
  // Search-result navigation (◀▶) — operates on the corpus-search highlights.
  $('#btnHitPrev').addEventListener('click', () => window.HB_prevHit());
  $('#btnHitNext').addEventListener('click', () => window.HB_nextHit());
  $('#btnZoomIn').addEventListener('click', () => setZoom(state.zoom * 1.1));
  $('#btnZoomOut').addEventListener('click', () => setZoom(state.zoom / 1.1));
  $('#btnFind').addEventListener('click', toggleFindBar);
  $('#btnCopy').addEventListener('click', copySelection);
  $('#btnPrint').addEventListener('click', () => {
    // Post 'hb-print' so the host shows our custom PrintWindow (same dialog as
    // the PDF viewer). Previously this called window.print(), which opened the
    // browser's native print preview — two different print UIs depending on the
    // book type was confusing, and the WebView2 native dialog also exposes a
    // printer picker we want hidden in protect-mode.
    try { window.chrome?.webview?.postMessage('hb-print'); }
    catch (_) { window.print(); /* fallback if the bridge is missing */ }
  });
}

function setZoom(z) {
  state.zoom = Math.max(0.5, Math.min(4.0, z));
  state.textBody.style.fontSize = (18 * state.zoom).toFixed(1) + 'px';
  $('#zoomLabel').textContent = Math.round(state.zoom * 100) + '%';
}

function bindKeyboard() {
  document.addEventListener('keydown', (e) => {
    // Protect-mode (kiosk): hard-block DevTools shortcuts before anything else.
    // See viewer.js for the same gate; defense-in-depth around
    // AreBrowserAcceleratorKeysEnabled=false on the host side.
    if (state.protectMode) {
      if (e.key === 'F12') { e.preventDefault(); return; }
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'I' || e.key === 'i' || e.key === 'J' || e.key === 'j' || e.key === 'C' || e.key === 'c')) {
        e.preventDefault(); return;
      }
    }
    // ── Global shortcuts mirrored from the PDF viewer ────────────────────────
    // WebView2 owns keyboard focus while reading, so WPF never sees these keys.
    // Post `hb-shortcut:<token>` to the host (PdfJsHost.ShortcutRequested),
    // which routes them through ShortcutKeyMap.FromViewerToken into the page's
    // HandleShortcut — same handler the WPF chrome's PreviewKeyDown uses, so
    // text books get identical Next/Prev/focus-search behaviour as PDFs.
    // (This used to be missing on text-viewer.js, so Ctrl+ArrowDown / Alt+Right
    // etc were dead while reading an Otzraya book.)
    const ae = document.activeElement;
    const inField = ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA' || ae.isContentEditable);
    if (e.key === 'F11') {
      e.preventDefault();
      try { window.chrome?.webview?.postMessage('hb-immersive-toggle'); } catch (_) {}
      return;
    }
    if (e.key === 'Escape' && !inField) {
      try { window.chrome?.webview?.postMessage('hb-immersive-exit'); } catch (_) {}
      return;
    }
    const _sc = (t) => {
      e.preventDefault();
      try { window.chrome?.webview?.postMessage('hb-shortcut:' + t); } catch (_) {}
    };
    // Ctrl+Shift+Arrow = next/prev BOOK (jumps SelectedRow across the search
    // results list). Checked BEFORE the plain Ctrl+Arrow rule so Shift wins.
    if (e.ctrlKey && e.shiftKey && e.key === 'ArrowDown') { _sc('next-book'); return; }
    if (e.ctrlKey && e.shiftKey && e.key === 'ArrowUp')   { _sc('prev-book'); return; }
    if ((e.ctrlKey && e.key === 'ArrowDown') || (e.altKey && e.key === 'ArrowRight')) { _sc('next'); return; }
    if ((e.ctrlKey && e.key === 'ArrowUp')   || (e.altKey && e.key === 'ArrowLeft'))  { _sc('prev'); return; }
    // Ctrl+[ / Ctrl+] = browser-style back/forward in the per-VM history (parity
    // with the WPF chrome, which handles them when focus is NOT in the WebView).
    if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key === '[') { _sc('nav-back'); return; }
    if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key === ']') { _sc('nav-forward'); return; }
    // Open-book tabs (main window) — forwarded so they work while a text book has focus.
    if (e.ctrlKey && e.shiftKey && e.key === 'Tab') { _sc('tab-prev'); return; }
    if (e.ctrlKey && !e.shiftKey && e.key === 'Tab') { _sc('tab-next'); return; }
    if (e.ctrlKey && e.key === 'PageDown') { _sc('tab-next'); return; }
    if (e.ctrlKey && e.key === 'PageUp')   { _sc('tab-prev'); return; }
    if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === 'w' || e.key === 'W')) { _sc('tab-close'); return; }
    if (e.altKey && (e.key === 'c' || e.key === 'C')) { _sc('goto-search'); return; }
    if (e.altKey && (e.key === 'k' || e.key === 'K')) { _sc('goto-catalog'); return; }
    if (e.ctrlKey && (e.key === 'e' || e.key === 'E')) { _sc('focus-main'); return; }
    if (!e.ctrlKey && !e.altKey && !inField && e.key === '/') { _sc('focus-main'); return; }
    // Ctrl+F focuses the WPF in-book search (parity with PDF viewer). The local
    // find bar is still reachable via the toolbar 🔍 button (and bare F below).
    if (e.ctrlKey && (e.key === 'f' || e.key === 'F')) { _sc('focus-inbook'); return; }
    if (!e.ctrlKey && !e.altKey && !inField && (e.key === 'f' || e.key === 'F')) { _sc('focus-inbook'); return; }

    // ── Local keyboard handling (in-page only, no host hop) ───────────────────
    if (e.ctrlKey && (e.key === '+' || e.key === '=')) {
      e.preventDefault(); setZoom(state.zoom * 1.1);
    } else if (e.ctrlKey && e.key === '-') {
      e.preventDefault(); setZoom(state.zoom / 1.1);
    } else if (e.ctrlKey && e.key === '0') {
      e.preventDefault(); setZoom(1.0);
    } else if (e.ctrlKey && (e.key === 'p' || e.key === 'P')) {
      // Route through the host PrintWindow, same as the toolbar ⎙ button.
      // preventDefault + stopPropagation + stopImmediatePropagation: in this
      // WebView2 build the browser-accelerator path can still grab Ctrl+P
      // before our handler unless we cut every possible bubbling route.
      e.preventDefault(); e.stopPropagation(); e.stopImmediatePropagation();
      try { window.chrome?.webview?.postMessage('hb-print'); } catch (_) { window.print(); }
      return false;
    }
  });

  // Ctrl+wheel zoom — WebView2's built-in CSS-zoom is disabled by the host, so
  // we provide our own smooth font-size zoom for parity with the PDF viewer.
  document.addEventListener('wheel', (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    setZoom(state.zoom * (e.deltaY < 0 ? 1.05 : 0.95));
  }, { passive: false });

  // Auto-hide chrome: report top-band enter/leave so the host can reveal / re-hide
  // the WPF in-book toolbar (same contract as the PDF viewer). State-change only;
  // the host ignores it unless auto-hide is on.
  let _hbChromeNear = false;
  const _HB_CHROME_BAND = 64;
  window.addEventListener('pointermove', (e) => {
    const near = e.clientY <= _HB_CHROME_BAND;
    if (near === _hbChromeNear) return;
    _hbChromeNear = near;
    try {
      window.chrome && window.chrome.webview &&
        window.chrome.webview.postMessage(near ? 'hb-chrome-show' : 'hb-chrome-hide');
    } catch (_) { /* host gone */ }
  }, { passive: true });
}

// ---------- find bar ----------

function bindFindBar() {
  $('#findInput').addEventListener('input', () => runFind($('#findInput').value));
  $('#findInput').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') (e.shiftKey ? findPrev : findNext)();
    else if (e.key === 'Escape') { hideFindBar(); }
  });
  $('#findNext').addEventListener('click', findNext);
  $('#findPrev').addEventListener('click', findPrev);
  $('#btnFindClose').addEventListener('click', hideFindBar);
}

function toggleFindBar() {
  const bar = $('#findBar');
  if (bar.classList.contains('hidden')) {
    bar.classList.remove('hidden');
    $('#findInput').focus();
    $('#findInput').select();
  } else {
    hideFindBar();
  }
}

function hideFindBar() {
  $('#findBar').classList.add('hidden');
  clearHighlights('.hb-find');
  state.findMatches = [];
  state.findIndex = -1;
  $('#findStatus').textContent = '';
}

function runFind(query) {
  clearHighlights('.hb-find');
  state.findMatches = [];
  state.findIndex = -1;
  if (!query || query.length === 0) { $('#findStatus').textContent = ''; return; }

  state.findMatches = wrapTermInBody(query, 'hb-find');
  if (state.findMatches.length === 0) {
    $('#findStatus').textContent = 'אין תוצאות';
  } else {
    state.findIndex = 0;
    state.findMatches[0].classList.add('current');
    scrollIntoView(state.findMatches[0]);
    $('#findStatus').textContent = `1 / ${state.findMatches.length}`;
  }
}

function findNext() { stepFind(+1); }
function findPrev() { stepFind(-1); }
function stepFind(delta) {
  if (state.findMatches.length === 0) return;
  const prev = state.findMatches[state.findIndex];
  if (prev) prev.classList.remove('current');
  state.findIndex = (state.findIndex + delta + state.findMatches.length) % state.findMatches.length;
  const cur = state.findMatches[state.findIndex];
  cur.classList.add('current');
  scrollIntoView(cur);
  $('#findStatus').textContent = `${state.findIndex + 1} / ${state.findMatches.length}`;
}

// ---------- corpus-search highlights ----------

// Reliably bring ANY element (search hit OR a TOC/anchor target) into view despite
// content-visibility. Off-screen content-visibility:auto sections report ESTIMATED
// geometry, so a single scrollIntoView lands APPROXIMATELY on a far jump; as the
// target's section renders at its real size the position shifts, leaving the target
// off-screen (the user had to click the TOC entry a second time). Re-scroll a few
// times until the element's real rect actually sits inside the viewport (or we run
// out of tries) — estimate-independent, so one click lands exactly.
function scrollIntoViewStable(el, myGen) {
  let tries = 0;
  const step = () => {
    if (!el || !el.isConnected || (myGen !== undefined && myGen !== state._loadGen)) return;
    el.scrollIntoView({ block: 'center' });
    const r = el.getBoundingClientRect();
    const inView = r.height > 0 && r.top >= 0 && r.top <= window.innerHeight;
    if (!inView && tries++ < 10) setTimeout(step, 80);
  };
  step();
}

async function highlightTerms(terms, myGen, scrollToFirst) {
  // CHUNKED + async so a huge book never freezes the UI and the first hit is
  // reached as soon as it's found (not after the whole doc is scanned). Each node
  // is stripped once and matched against all needles; the expensive niqud→original
  // index map is built ONLY for nodes that actually contain a match (most don't).
  const stripNiqud = s => s.replace(/[֑-ׇ]/g, '');
  const needles = [...new Set(
    terms.filter(Boolean)
      .map(t => stripNiqud(String(t).trim()).toLowerCase())
      .filter(n => n.length > 0))]
    .sort((a, b) => b.length - a.length); // longest-first → longer form wins overlaps
  if (needles.length === 0) return;

  const targets = collectHighlightTargets();
  const BATCH = 250;          // text nodes per frame
  let scrolled = false;
  for (let i = 0; i < targets.length; i += BATCH) {
    const end = Math.min(i + BATCH, targets.length);
    for (let j = i; j < end; j++) wrapNode(targets[j], needles, 'hb-hit', state.hitMatches);
    // Jump to the first hit the instant we have one — the user doesn't wait for
    // the rest of the document to be scanned.
    if (scrollToFirst && !scrolled && state.hitMatches.length > 0) {
      scrolled = true;
      setCurrentHit(0);
      scrollIntoViewStable(state.hitMatches[0], myGen);
    }
    if (myGen !== undefined && myGen !== state._loadGen) return; // book switched away
    await new Promise(r => setTimeout(r, 0));                    // yield to the UI
  }
  postHost('hb-debug:hl-done gen=' + myGen + ' hits=' + state.hitMatches.length);

  // Proximity filter: dtSearch only returns books where the multi-term query
  // satisfies the proximity constraint (default ~30 words between distinct
  // terms), but our naive "wrap every occurrence of every term" highlighter
  // doesn't know about that. Without the filter, a query like
  // "word1 w/30 word2" would mark every lone occurrence of either word
  // throughout the book — making the cluster matches indistinguishable from
  // background noise. Keep only hits that have a DIFFERENT-term hit within
  // ~300 chars (≈30 Hebrew words; Hebrew avg word + separator ≈ 10 chars).
  const distinctTerms = new Set();
  for (const m of state.hitMatches) {
    if (m.dataset.term) distinctTerms.add(m.dataset.term);
    if (distinctTerms.size > 1) break;
  }
  if (distinctTerms.size > 1 && state.hitMatches.length > 1) {
    applyProximityFilter(300);
  }

  postHost('hb-debug:hl-final gen=' + myGen + ' hits=' + state.hitMatches.length);
  updateHitPos();   // show the final "current / total" counter
  // The proximity filter may have dropped the span we scrolled to mid-scan;
  // re-anchor on the first SURVIVING hit so the user ends up on a real cluster.
  if (scrollToFirst && state.hitMatches.length > 0) {
    setCurrentHit(0);
    scrollIntoViewStable(state.hitMatches[0], myGen);
  }
}

function applyProximityFilter(windowChars) {
  // Build a cumulative char-position map keyed by text-node identity. Single
  // pass, O(N) over text nodes — much faster than computing each span's
  // position via Range.startOffset + ancestor walks per span (which is what
  // a naive "for each span, where is it in the doc" would do).
  const posMap = new Map();
  const walker = document.createTreeWalker(state.textBody, NodeFilter.SHOW_TEXT);
  let pos = 0;
  let n;
  while ((n = walker.nextNode())) {
    posMap.set(n, pos);
    pos += (n.nodeValue || '').length;
  }

  // Build the sorted hit list with positions.
  const items = [];
  for (const el of state.hitMatches) {
    const tn = el.firstChild;
    if (!tn) continue;
    const p = posMap.get(tn);
    if (p === undefined) continue;
    items.push({ el, term: el.dataset.term || '', pos: p });
  }
  items.sort((a, b) => a.pos - b.pos);

  // For each hit, check both directions until we exit the window or find a
  // hit with a DIFFERENT canonical term — that's the cluster signal we keep.
  const keep = new Array(items.length).fill(false);
  for (let i = 0; i < items.length; i++) {
    const cur = items[i];
    for (let j = i - 1; j >= 0; j--) {
      if (cur.pos - items[j].pos > windowChars) break;
      if (items[j].term !== cur.term) { keep[i] = true; break; }
    }
    if (keep[i]) continue;
    for (let j = i + 1; j < items.length; j++) {
      if (items[j].pos - cur.pos > windowChars) break;
      if (items[j].term !== cur.term) { keep[i] = true; break; }
    }
  }

  // Safety: never filter down to ZERO marks. If the cluster window dropped
  // everything (e.g. the matched terms sit a bit farther apart than the window,
  // or only one term is in the highlight set), keep all hits so the user still
  // sees the results rather than a blank page.
  if (!keep.some(Boolean)) keep.fill(true);

  // Unwrap non-cluster spans (preserve their text).
  for (let i = 0; i < items.length; i++) {
    if (keep[i]) continue;
    const el = items[i].el;
    const text = document.createTextNode(el.textContent);
    el.parentNode.replaceChild(text, el);
  }
  // NOTE: deliberately NOT calling textBody.normalize() here — on a 50MB body it
  // walks/merges every text node (slow), and nothing downstream needs merged nodes.

  state.hitMatches = items.filter((_, i) => keep[i]).map(it => it.el);
}

function setCurrentHit(i) {
  state.hitMatches.forEach(el => el.classList.remove('current'));
  if (i >= 0 && i < state.hitMatches.length) {
    state.hitMatches[i].classList.add('current');
    state.hitCurrentIndex = i;
  } else {
    state.hitCurrentIndex = -1;
  }
  updateHitPos();
}

// Show/update the toolbar's "current / total" result counter (and the ◀▶ group).
// Hidden when the open book has no corpus-search hits.
function updateHitPos() {
  const nav = $('#hitNav');
  const pos = $('#hitPos');
  if (!nav || !pos) return;
  const total = state.hitMatches ? state.hitMatches.length : 0;
  if (total === 0) { nav.style.display = 'none'; return; }
  nav.style.display = '';
  const cur = state.hitCurrentIndex >= 0 ? state.hitCurrentIndex + 1 : 0;
  pos.textContent = cur + ' / ' + total;
}

// Host-driven next/prev across the corpus-search highlights in the open text
// book. Mirrors PDF mode's NextMatch/PrevMatch on hit pages — wraps around at
// the ends so the user can flick through repeatedly without an "end of list"
// dead-spot. Posted from PdfJsHost.NextTextHitAsync / PrevTextHitAsync, which
// the page's HandleShortcut routes to when IsTextMode is true (the in-book
// hit navigation otherwise used by PDF mode is a no-op for text books because
// CurrentBookHits stays null).
window.HB_nextHit = function () {
  if (!state.hitMatches || state.hitMatches.length === 0) return;
  const next = state.hitCurrentIndex < 0
    ? 0
    : (state.hitCurrentIndex + 1) % state.hitMatches.length;
  setCurrentHit(next);
  scrollIntoViewStable(state.hitMatches[next], state._loadGen);
};

window.HB_prevHit = function () {
  if (!state.hitMatches || state.hitMatches.length === 0) return;
  const prev = state.hitCurrentIndex <= 0
    ? state.hitMatches.length - 1
    : state.hitCurrentIndex - 1;
  setCurrentHit(prev);
  scrollIntoViewStable(state.hitMatches[prev], state._loadGen);
};

// ---------- DOM term-wrapping (shared by find-bar and corpus highlight) ----------

/**
 * Walks #textBody's text nodes and wraps every occurrence of `term` (case-
 * insensitive, niqud-aware) in a span with the given className. Returns the
 * list of created span elements in document order.
 *
 * "niqud-aware" = we strip Hebrew niqud marks (U+0591..U+05C7) BOTH from the
 * source text and from the search term before matching, but the wrapped span
 * still contains the *original* niqud-decorated text. This way a search for
 * "ברכת" finds "בְּרְכַּת" without the user needing to type the vowels.
 */
// Word-char predicate for the highlight word-boundary check below. Hebrew
// letters (אונסי U+05D0..U+05EA), digits, and Latin letters all count as
// word chars; everything else (space, punctuation, geresh ׳, gershayim ״,
// ASCII apostrophe ', niqud — niqud already stripped before reaching here)
// is a boundary. Keep this in sync with the identical helper in viewer.js.
function isHbWordChar(code) {
  if (code >= 0x05D0 && code <= 0x05EA) return true; // Hebrew letters
  if (code >= 0x0030 && code <= 0x0039) return true; // 0-9
  if (code >= 0x0041 && code <= 0x005A) return true; // A-Z
  if (code >= 0x0061 && code <= 0x007A) return true; // a-z
  return false;
}

// Single-term entry point (find-bar). Synchronous (one term, used interactively).
function wrapTermInBody(term, className) {
  const needle = (term || '').replace(/[֑-ׇ]/g, '').toLowerCase();
  if (needle.length === 0) return [];
  const created = [];
  for (const node of collectHighlightTargets()) wrapNode(node, [needle], className, created);
  return created;
}

// Snapshot every highlightable text node (skips nodes already inside a highlight
// wrapper so repeated passes don't nest).
function collectHighlightTargets() {
  const walker = document.createTreeWalker(state.textBody, NodeFilter.SHOW_TEXT, {
    acceptNode(n) {
      if (!n.nodeValue) return NodeFilter.FILTER_REJECT;
      const p = n.parentElement;
      if (p && (p.classList.contains('hb-hit') || p.classList.contains('hb-find'))) {
        return NodeFilter.FILTER_REJECT;
      }
      return NodeFilter.FILTER_ACCEPT;
    },
  });
  const targets = [];
  while (walker.nextNode()) targets.push(walker.currentNode);
  return targets;
}

/**
 * Wrap every whole-word occurrence of any `needle` (niqud-stripped + lowercased,
 * longest-first) found in ONE text node, pushing the created spans (tagged with
 * dataset.term) into `created`. The niqud→original index map — the expensive
 * per-char build — is constructed ONLY when the node actually contains a match,
 * so the thousands of non-matching nodes in a huge book cost just one indexOf each.
 */
function wrapNode(node, needles, className, created) {
  const original = node.nodeValue;
  const stripped = stripNiqudLower(original);

  // Collect whole-word matches first (cheap indexOf scan; no map yet).
  const cands = [];
  for (const needle of needles) {
    const L = needle.length;
    if (L > stripped.length) continue;
    let from = 0;
    while (from <= stripped.length - L) {
      const idx = stripped.indexOf(needle, from);
      if (idx < 0) break;
      const end = idx + L;
      const leftOk = idx === 0 || !isHbWordChar(stripped.charCodeAt(idx - 1));
      const rightOk = end === stripped.length || !isHbWordChar(stripped.charCodeAt(end));
      if (leftOk && rightOk) cands.push({ start: idx, end, len: L, term: needle });
      from = end;
    }
  }
  if (cands.length === 0) return; // no match → skip the costly map build entirely

  // Build the niqud→original index map only now that we know we'll use it.
  const map = [];
  for (let oi = 0; oi < original.length; oi++) {
    const ch = original.charCodeAt(oi);
    if (ch >= 0x0591 && ch <= 0x05C7) continue;
    map.push(oi);
  }
  if (stripped.length !== map.length) return; // sanity check

  cands.sort((a, b) => a.start - b.start || b.len - a.len);
  const frag = document.createDocumentFragment();
  let cursor = 0;        // original-string cursor
  let lastEnd = -1;      // stripped-index end of last chosen match (overlap guard)
  for (const c of cands) {
    if (c.start < lastEnd) continue; // overlaps a previously chosen (longer) match
    lastEnd = c.end;
    const oStart = map[c.start];
    const oEnd = map[c.end - 1] + 1;
    if (oStart > cursor) frag.appendChild(document.createTextNode(original.slice(cursor, oStart)));
    const span = document.createElement('span');
    span.className = className;
    span.textContent = original.slice(oStart, oEnd);
    span.dataset.term = c.term;
    frag.appendChild(span);
    created.push(span);
    cursor = oEnd;
  }
  if (cursor < original.length) frag.appendChild(document.createTextNode(original.slice(cursor)));
  node.parentNode.replaceChild(frag, node);
}

function stripNiqudLower(s) { return s.replace(/[֑-ׇ]/g, '').toLowerCase(); }

function clearHighlights(selector) {
  const nodes = state.textBody.querySelectorAll(selector);
  nodes.forEach(span => {
    const text = document.createTextNode(span.textContent);
    span.parentNode.replaceChild(text, span);
  });
  // Re-merge adjacent text nodes so subsequent walks see clean strings.
  state.textBody.normalize();
}

// ---------- copy / context menu ----------

async function copySelection() {
  if (state.protectMode) return;             // kiosk: no clipboard writes
  const sel = window.getSelection();
  const text = sel?.toString() ?? '';
  if (!text) return;
  try { await navigator.clipboard.writeText(text); }
  catch { /* clipboard API may be blocked — silent fail */ }
}

function bindContextMenu() {
  const menu = $('#ctxMenu');
  document.addEventListener('contextmenu', (e) => {
    if (e.target.closest('#toolbar') || e.target.closest('#findBar')) return;
    e.preventDefault();
    if (state.protectMode) return;           // kiosk: swallow right-click; no menu
    showContextMenu(e.clientX, e.clientY);
  });
  document.addEventListener('click', () => menu.classList.add('hidden'));
  menu.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-action]');
    if (!btn) return;
    handleContextAction(btn.dataset.action);
    menu.classList.add('hidden');
  });
}

function showContextMenu(x, y) {
  const menu = $('#ctxMenu');
  const hasSelection = (window.getSelection()?.toString().length ?? 0) > 0;
  $('#ctxCopy').disabled = !hasSelection;
  $('#ctxFind').disabled = !hasSelection;
  menu.style.left = x + 'px';
  menu.style.top = y + 'px';
  menu.classList.remove('hidden');
}

function handleContextAction(action) {
  switch (action) {
    case 'copy':  copySelection(); break;
    case 'print':
      try { window.chrome?.webview?.postMessage('hb-print'); } catch (_) { window.print(); }
      break;
    case 'find':  {
      const sel = window.getSelection()?.toString() ?? '';
      if (sel.length > 0) {
        $('#findBar').classList.remove('hidden');
        $('#findInput').value = sel;
        runFind(sel);
      } else {
        toggleFindBar();
      }
      break;
    }
  }
}

// ---------- helpers ----------

function scrollIntoView(el) {
  if (!el) return;
  el.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

function showLoading(yes) {
  const o = $('#loadingOverlay');
  if (yes) o.classList.remove('hidden'); else o.classList.add('hidden');
}

function hideOverlay() { showLoading(false); }

function showError(msg) {
  const o = $('#errorOverlay');
  $('#errorText').textContent = msg;
  o.classList.remove('hidden');
}

// ---------- bootstrap ----------

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}
