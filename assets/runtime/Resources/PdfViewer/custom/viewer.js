// HebrewBooks custom PDF viewer.
//
// Built on top of pdf.js as a *library* — we don't use Mozilla's viewer.html / PDFViewerApplication.
// All page rendering, scroll, zoom, find, outline and dtSearch hit-overlay logic lives here.
//
// Loaded as an ES module from https://viewer.local/viewer.js (see PdfJsHost.xaml.cs).
// pdf.js itself is served from https://pdfjs.local/build/pdf.mjs, the same vendored copy
// the legacy viewer.html used.
//
// WPF host bridge:
//   window.HB_loadPdf(url, page, xmlOrNull, termsOrNull)
//   window.HB_goToPage(page)
//   window.HB_setHighlightXml(xmlOrNull, termsOrNull)
//   posts 'hb-loaded' on chrome.webview when the requested page has rendered.

import * as pdfjsLib from 'https://pdfjs.local/build/pdf.mjs';

pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://pdfjs.local/build/pdf.worker.mjs';

// Forward JS errors and pdf-render failures to the WPF host so they hit the Serilog file.
// Without this, anything thrown inside an async render task is invisible — the WebView2
// console isn't captured anywhere by default.
function postDebug(msg) {
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage('hb-debug:' + msg);
    }
  } catch (e) { /* host gone */ }
  try { console.error('[hb]', msg); } catch (e) {}
}
window.addEventListener('error', (e) => {
  postDebug('window.error: ' + (e.error?.stack || e.message || e));
});
window.addEventListener('unhandledrejection', (e) => {
  postDebug('unhandled rejection: ' + (e.reason?.stack || e.reason || e));
});

// Some pdf.js workers expect a cMap URL for non-Latin fonts (Hebrew rarely needs it but
// some scanned PDFs do). standardFontDataUrl points to the bundled fallback fonts.
//
// `disableAutoFetch: true` is the key knob for "don't load everything at once". By
// default pdf.js prefetches every page in the background after the catalog is parsed
// — for a 600-page book this floods the worker with hundreds of getPage requests we
// don't actually need (we render on-demand via virtualization). Disabling auto-fetch
// keeps the worker free for the page the user is actually reading.
const PDFJS_OPTS = {
  cMapUrl: 'https://pdfjs.local/web/cmaps/',
  cMapPacked: true,
  standardFontDataUrl: 'https://pdfjs.local/web/standard_fonts/',
  disableAutoFetch: true,
};

// ============================================================================
// State
// ============================================================================

const state = {
  // Kiosk / protect-mode: pushed from the host (PdfJsHost.OpenAsync / OpenTextAsync) via
  // window.HB_setProtectMode(true) BEFORE the first PDF load. When true: no context menu,
  // no clipboard writes, no F12 / Ctrl+Shift+I, no external PDF link follows.
  protectMode: false,
  pdfDoc: null,
  totalPages: 0,
  currentPage: 1,
  scale: 1,                // overwritten on first doc load when fitMode is set
  fitMode: 'page-width',   // 'auto' | 'page-fit' | 'page-width' | 'page-height' | null
  rotation: 0,             // 0/90/180/270
  pages: [],               // PageView[] (1-indexed; pages[0] unused)
  citationsByPage: {},     // { "<pageNo>": [{ref, fid, page, box:[x0,y0,x1,y1 page-fraction]}] } — clickable cross-refs
  pageWidth: 0,            // page-1 width @ current scale — fallback when per-page dims unknown
  pageHeight: 0,           // page-1 height @ current scale — fallback when per-page dims unknown
  // Per-page layout for non-uniform PDFs (mixed portrait/landscape, varying scan sizes).
  // pageTops[i] = scroll-top of page i; pageTops[N+1] = the bottom edge plus PAGE_GAP_Y.
  // Filled lazily as pdf.js delivers page handles; until then we use page-1 as a stride
  // estimate. ensureAllPageDims() walks every page in the background after open and
  // rebuilds the layout once the real dimensions are in.
  pageTops: null,
  hitsByPage: new Map(),   // page -> [{pos, len}]
  matchedTerms: [],
  highlightXml: '',
  // Fill/stroke for the highlight rects, populated by HB_setHighlightColor. Defaults
  // to the original gold so a host that never sets a color still renders the same
  // visual as before. drawRect reads these on every rect.
  // Alpha values are calibrated for CSS mix-blend-mode: multiply on the layer
  // (set in viewer.css). With multiply, the yellow tints the white page area
  // but black glyphs stay black — so higher alphas don't obscure text, they
  // just make the highlight more vivid. Stroke kept lower than fill so it
  // acts as a soft edge without competing with the fill.
  highlightFill: 'rgba(255, 213, 0, 0.7)',
  highlightStroke: 'rgba(180, 120, 0, 0.5)',
  // Page the user JUMPED to on load (first hit page). While this is set, every
  // rebuildLayoutFromDims re-snaps scrollTop to keep it on this page — without
  // it, opening a hit on page 201 in a book with a tall cover drifted to
  // ~225 after ensureAllPageDims rebuilt the layout. Cleared after all dims are
  // in OR when the user manually scrolls.
  initialAnchorPending: 0,
  // Set by scrollToPage/rebuildLayoutFromDims when they programmatically adjust
  // scrollTop, so the wheel/keyboard handlers don't treat that adjustment as a
  // user gesture that should release the initial anchor.
  _programmaticScroll: false,
  initialPage: 1,
  loadedNotified: false,
  // Pages the user actually DWELLED on (>=2s) this session. Drives the rail's
  // "viewed" dot. Session-only by design — reset on every document load (a fresh
  // book starts with no marks).
  viewedPages: new Set(),
  _viewTimer: null,            // pending 2s dwell timer for the current page
  _railClickNoRecenter: false, // one-shot: a rail-row click must not re-centre the rail
  // Whether the rail is shown at all. Default OFF — the host (C#) pushes the
  // user's persisted preference via HB_setPageRailEnabled as soon as the viewer
  // page loads, so a fresh book never builds the rail before we hear from C#.
  // When false, buildPageRail/updateRail short-circuit and the #pageRail div
  // stays empty + hidden via CSS (:empty selector).
  pageRailEnabled: false,
  // Book TOC supplied by the host (C#) via HB_setBookToc. Each entry is {Title, Page}.
  // Re-rendered into #outlinePane on every set; click → goToPage.
  bookToc: [],
  // Target DPI for the in-book "העתק קטע כתמונה" capture. Pushed from the host
  // (PdfJsHost) via window.HB_setRegionCopyDpi at viewer init; defaults to 200
  // (print-quality) until then. captureRegionBlob always re-renders offscreen at
  // this DPI regardless of the user's current zoom — copies stay crisp even at
  // fit-page. Clamped 72..600 here as a safety net (the host clamps too).
  regionCopyDpi: 200,
  // dtSearch Fuzziness setting (0..10). Pushed from the host via HB_setFuzziness.
  // When > 0, the highlight layer ALSO scans the page text for words within this
  // many Levenshtein edits of each search term — mirrors dtSearch's behaviour so a
  // book that matched the corpus search via fuzzy still gets precise rectangles in
  // the viewer instead of the misleading "approximate band" fallback. 0 = off.
  fuzziness: 0,
  findState: {
    query: '',
    caseSensitive: false,
    matches: [],           // [{pageNumber, itemIndex, start, end, matchIdInPage}]
    currentIndex: -1,
    perPageScanned: new Set(),
  },
};

// pdf.js applies a page's intrinsic /Rotate ONLY when getViewport is called
// WITHOUT an explicit rotation; passing one (even 0) OVERRIDES it. Many scanned
// / Personal PDFs are landscape with /Rotate 90 (e.g. the Shottenstein set) and
// were rendering sideways because every call forced rotation:0. So: honour the
// page's own rotation and treat state.rotation as the USER's extra rotation on
// top of it. This MUST be used at every getViewport call — canvas, text layer
// and highlight overlay have to share one transform or the hit rects desync.
function effRotation(pdfPage) {
  const intrinsic = (pdfPage && typeof pdfPage.rotate === 'number') ? pdfPage.rotate : 0;
  return (((intrinsic + state.rotation) % 360) + 360) % 360;
}

// Resolution the canvas backing store is rendered at, in device-pixels-per-CSS-pixel.
// pdf.js renders each page to a canvas sized viewport×dpr; on a high-DPI display
// (e.g. Windows scaling at 225% → devicePixelRatio ≈ 2.25) that is ~5× the pixels of
// a 1.0 surface, and the per-page render — dominated on scanned books by decoding
// and scaling the page image — scales with that pixel count. We CAP it: the scanned
// page image has a fixed source resolution, so rendering its canvas above ~1.5×
// just upscales the same scan with no real detail gain while paying the full paint
// cost. Capping turns a ~1.4s render into a fraction of that; the compositor then
// upscales the slightly-smaller canvas to the physical pixels — imperceptible on a
// scan, a small softening on crisp vector text. Highlight/text layers derive their
// scale from the canvas dimensions, so they stay aligned automatically.
const MAX_RENDER_DPR = 1.5;
function renderDpr() {
  return Math.min(window.devicePixelRatio || 1, MAX_RENDER_DPR);
}

// ============================================================================
// DOM refs
// ============================================================================

const $ = (id) => document.getElementById(id);
const dom = {
  toolbar:        $('toolbar'),
  btnSidebar:     $('btnSidebar'),
  btnPrev:        $('btnPrev'),
  btnNext:        $('btnNext'),
  pageInput:      $('pageInput'),
  pageCount:      $('pageCount'),
  btnZoomIn:      $('btnZoomIn'),
  btnZoomOut:     $('btnZoomOut'),
  zoomSelect:     $('zoomSelect'),
  btnFind:        $('btnFind'),
  btnCopy:        $('btnCopy'),
  btnRotate:      $('btnRotate'),
  btnPrint:       $('btnPrint'),
  ctxMenu:        $('ctxMenu'),
  ctxCopy:        $('ctxCopy'),
  ctxPrint:       $('ctxPrint'),
  ctxFind:        $('ctxFind'),
  docTitle:       $('docTitle'),
  findBar:        $('findBar'),
  findInput:      $('findInput'),
  findPrev:       $('findPrev'),
  findNext:       $('findNext'),
  findStatus:     $('findStatus'),
  findHighlightAll: $('findHighlightAll'),
  findCaseSensitive: $('findCaseSensitive'),
  btnFindClose:   $('btnFindClose'),
  body:           $('body'),
  sidebar:        $('sidebar'),
  tabThumbs:      $('tabThumbs'),
  tabOutline:     $('tabOutline'),
  thumbsPane:     $('thumbsPane'),
  outlinePane:    $('outlinePane'),
  outlineList:    $('outlineList'),
  btnTocAdd:      $('btnTocAdd'),
  btnTocEdit:     $('btnTocEdit'),
  pageRail:       $('pageRail'),
  viewerContainer: $('viewerContainer'),
  viewer:         $('viewer'),
  loadingOverlay: $('loadingOverlay'),
  errorOverlay:   $('errorOverlay'),
  errorText:      $('errorText'),
};

// ============================================================================
// PageView — manages one rendered page
// ============================================================================

class PageView {
  constructor(pageNumber) {
    this.pageNumber = pageNumber;
    this.pdfPage = null;          // PDFPageProxy, lazily fetched
    this.viewport = null;
    this.container = null;
    this.canvas = null;
    this.textLayerDiv = null;
    this.highlightLayer = null;
    this.annotationLayer = null;
    this.rendered = false;
    this.rendering = null;        // Promise of in-flight render
    this.renderTask = null;       // pdf.js RenderTask (cancelable)
    this.textLayerTask = null;
    this.findMatches = [];        // [{itemIndex, start, end}]
    // Bumped on every unrender/resize. In-flight render() captures the value at start
    // and bails out at each await boundary if it changed — without this, an awaiting
    // render that resumes after unrender would touch nulled DOM refs and either crash
    // or paint to a detached canvas (visible as "stuck white" pages on fast scroll).
    this._gen = 0;
  }

  // Attach the page DIV at its calculated scroll position. Called when the page is
  // about to enter the visible window — until then, the PageView is a pure JS object
  // with no DOM cost. detach() reverses this.
  attach() {
    if (this.container) return;
    const div = document.createElement('div');
    div.className = 'page';
    div.dataset.pageNumber = String(this.pageNumber);
    div.style.top = pageTopOf(this.pageNumber) + 'px';
    // Per-page sizing — overrides the global --page-w/--page-h fallback when we
    // already know this specific page's dimensions. Crucial for non-uniform PDFs
    // (landscape inserts, varying scan sizes) where assuming page-1 size clips.
    const dims = this.viewport ?? viewportFor(this.pageNumber);
    if (dims) {
      div.style.width = dims.width + 'px';
      div.style.height = dims.height + 'px';
      noteMaxPageWidth(dims.width);
    }
    dom.viewer.appendChild(div);
    this.container = div;
  }

  /// Reposition + resize the placeholder div to match the current viewport. Called
  /// from rebuildLayoutFromDims after we learn a page's actual dimensions.
  refreshLayout() {
    if (!this.container) return;
    this.container.style.top = pageTopOf(this.pageNumber) + 'px';
    const dims = this.viewport ?? viewportFor(this.pageNumber);
    if (dims) {
      this.container.style.width = dims.width + 'px';
      this.container.style.height = dims.height + 'px';
    }
  }

  // Remove the page DIV from the DOM (and free any inner canvas memory). The PageView
  // object stays around so re-entering the visible window restores everything.
  detach() {
    if (!this.container) return;
    this.unrender();
    this.container.remove();
    this.container = null;
  }

  // Lazily create the inner canvas/text/annotation layers on first render. Skips work
  // if they've already been built once.
  _ensureLayers() {
    if (this.canvas) return;
    const viewport = this.viewport;
    const dpr = renderDpr();
    const cssW = viewport.width + 'px';
    const cssH = viewport.height + 'px';
    const pxW = Math.floor(viewport.width * dpr);
    const pxH = Math.floor(viewport.height * dpr);

    const canvas = document.createElement('canvas');
    canvas.className = 'pdf-canvas';
    canvas.width = pxW; canvas.height = pxH;
    canvas.style.width = cssW; canvas.style.height = cssH;
    this.container.appendChild(canvas);

    const hl = document.createElement('canvas');
    hl.className = 'highlightLayer';
    hl.width = pxW; hl.height = pxH;
    hl.style.width = cssW; hl.style.height = cssH;
    this.container.appendChild(hl);

    const tl = document.createElement('div');
    tl.className = 'textLayer';
    tl.style.width = cssW; tl.style.height = cssH;
    this.container.appendChild(tl);

    const al = document.createElement('div');
    al.className = 'annotationLayer';
    al.style.width = cssW; al.style.height = cssH;
    this.container.appendChild(al);

    // Clickable cross-reference (מסורת הש"ס) overlay — topmost layer so its links
    // receive clicks; the layer itself is pointer-events:none so it doesn't block
    // text selection elsewhere.
    const cl = document.createElement('div');
    cl.className = 'citeLayer';
    cl.style.width = cssW; cl.style.height = cssH;
    this.container.appendChild(cl);

    this.canvas = canvas;
    this.highlightLayer = hl;
    this.textLayerDiv = tl;
    this.annotationLayer = al;
    this.citeLayer = cl;
  }

  // Resize the placeholder and reset the rendered state. Called on zoom / rotate.
  resize(viewport) {
    this.viewport = viewport;
    const cssW = viewport.width + 'px';
    const cssH = viewport.height + 'px';
    this.container.style.width = cssW;
    this.container.style.height = cssH;
    if (this.canvas) {
      const dpr = renderDpr();
      const pxW = Math.floor(viewport.width * dpr);
      const pxH = Math.floor(viewport.height * dpr);
      for (const el of [this.canvas, this.highlightLayer]) {
        el.width = pxW; el.height = pxH;
        el.style.width = cssW; el.style.height = cssH;
      }
      for (const el of [this.textLayerDiv, this.annotationLayer, this.citeLayer]) {
        if (el) { el.style.width = cssW; el.style.height = cssH; }
      }
    }
    // Discard previous render — caller will re-render if visible.
    this.unrender();
  }

  // Drop the layered DOM entirely so the canvas pixel buffers (~3-20 MB each) are freed.
  // The placeholder div + dimensions stay so the scroll layout doesn't shift.
  unrender() {
    // Bump generation FIRST — any in-flight render() will see this on its next await
    // and bail before touching the about-to-be-nulled DOM.
    this._gen++;
    if (this.renderTask) { try { this.renderTask.cancel(); } catch (e) {} this.renderTask = null; }
    if (this.textLayerTask) { try { this.textLayerTask.cancel(); } catch (e) {} this.textLayerTask = null; }
    this.rendering = null;
    this.rendered = false;
    if (this.canvas) {
      // Free GPU/canvas memory by setting dimensions to 0 BEFORE detaching.
      this.canvas.width = 0; this.canvas.height = 0;
      this.highlightLayer.width = 0; this.highlightLayer.height = 0;
    }
    if (this.canvas) this.canvas.remove();
    if (this.highlightLayer) this.highlightLayer.remove();
    if (this.textLayerDiv) this.textLayerDiv.remove();
    if (this.annotationLayer) this.annotationLayer.remove();
    this.canvas = null;
    this.highlightLayer = null;
    this.textLayerDiv = null;
    this.annotationLayer = null;
    this._textContent = null;
  }

  async render() {
    if (this.rendered) return;
    if (this.rendering) return this.rendering;

    const myGen = this._gen;
    const isStale = () => myGen !== this._gen;
    const isCancel = (e) => {
      if (!e) return false;
      const name = e.name || '';
      const msg = e.message || '';
      return name === 'RenderingCancelledException' || name === 'AbortException' ||
             msg.includes('Rendering cancelled') || msg.includes('aborted') || msg.includes('cancelled');
    };

    // The whole IIFE swallows errors internally. We never reject the returned promise —
    // multiple scrolls attach multiple .catch handlers to the same in-flight render
    // (since `this.rendering` is shared between calls), and a rejected promise would
    // fire each one, producing log spam. Cancellations are normal during fast scroll.
    // The IIFE resolves as soon as the CANVAS is painted. textContent fetch,
    // highlight draw, textLayer build and annotation layer ALL happen async
    // afterward in #afterCanvas — the user gets to see the page faster, the
    // host's loading overlay disappears sooner, and highlights paint as soon as
    // pdf.js delivers items[] (typically <100ms behind the canvas).
    this.rendering = (async () => {
      try {
        if (!this.pdfPage) this.pdfPage = await state.pdfDoc.getPage(this.pageNumber);
        if (isStale()) return;
        // Always re-derive viewport from the actual page rather than trusting a value
        // copied from page-1's viewport at load time. Without this, a landscape page
        // 17 in a portrait book would render at portrait dimensions and get clipped.
        this.viewport = this.pdfPage.getViewport({ scale: state.scale, rotation: effRotation(this.pdfPage) });
        // Container dims may be stale if it was attached using page-1's estimate before
        // we knew this page's real size. Refresh now so the canvas fits inside.
        if (this.container) {
          this.container.style.width = this.viewport.width + 'px';
          this.container.style.height = this.viewport.height + 'px';
        }
        this._ensureLayers();
        const viewport = this.viewport;
        const dpr = renderDpr();
        const transform = dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null;
        const ctx = this.canvas.getContext('2d');

        // Canvas + textContent fired in parallel. The textContentPromise is
        // handed off to #afterCanvas; we don't await it on the critical path.
        const renderTask = this.pdfPage.render({
          canvasContext: ctx,
          viewport,
          transform,
        });
        this.renderTask = renderTask;
        const textContentPromise = this.pdfPage.getTextContent();

        // In-PDF find matches use the text-item coordinates (rectForItemSubstring)
        // and don't need the text-layer DOM, so they can paint as soon as
        // textContent arrives. dtSearch highlights moved to _afterCanvas where the
        // built text layer gives us pixel-perfect rects via DOM Range — see
        // renderHighlightsForPage.
        textContentPromise.then(tc => {
          if (myGen !== this._gen) return;
          this._textContent = tc;
          renderFindMatchesForPage(this);
        }).catch(() => { /* surfaced again in #afterCanvas */ });

        await renderTask.promise;
        this.renderTask = null;
        if (isStale()) return;
        this.rendered = true;

        // Hand off the rest of the work to a detached async task — render()
        // itself resolves NOW so the awaiter (loadDocument's notifyLoaded, or
        // renderVisiblePages) doesn't block on textLayer/annotation builds.
        //
        // We store the IIFE promise on the PageView so loadDocument can
        // optionally await it for the TARGET page before queuing buffer-page
        // renders — without that, target's `await textContentPromise` inside
        // _afterCanvas sits behind buffer pages' getPage/render/getTextContent
        // worker calls and the highlight pass is delayed by ~6-10s on books
        // with dense buffer-page text.
        this._afterCanvasPromise = this._afterCanvas(viewport, textContentPromise, myGen);
      } catch (e) {
        // Cancellation + stale-gen are expected during fast scroll — silent.
        // Anything else is a real bug worth logging.
        if (!isStale() && !isCancel(e)) {
          postDebug(`render p${this.pageNumber}: ${e.message || e}`);
        }
      } finally {
        this.rendering = null;
      }
    })();
    return this.rendering;
  }

  // Background phase of rendering — runs after the canvas is painted and
  // render()'s caller has been released. Builds the textLayer (for selection)
  // and annotation layer (for in-PDF links). Failures are logged but never
  // propagated.
  _afterCanvas(viewport, textContentPromise, myGen) {
    const isStale = () => myGen !== this._gen;
    const isCancel = (e) => {
      if (!e) return false;
      const name = e.name || '';
      const msg = e.message || '';
      return name === 'RenderingCancelledException' || name === 'AbortException' ||
             msg.includes('Rendering cancelled') || msg.includes('aborted') || msg.includes('cancelled');
    };
    const _acT0 = performance.now();
    return (async () => {
      try {
        const textContent = await textContentPromise;
        const _acT1 = performance.now();
        if (isStale()) return;
        // Build the text layer for select-and-copy. (Highlights were drawn
        // earlier from the same textContent — see the .then() above.)
        if (this.textLayerDiv) {
          this.textLayerDiv.innerHTML = '';
          try {
            const textLayer = new pdfjsLib.TextLayer({
              textContentSource: textContent,
              container: this.textLayerDiv,
              viewport,
            });
            this.textLayerTask = textLayer;
            await textLayer.render();
            this.textLayerTask = null;
          } catch (e) {
            if (!isStale() && !isCancel(e)) postDebug(`textLayer p${this.pageNumber}: ${e.message || e}`);
          }
        }
        if (isStale()) return;
        // dtSearch highlights are drawn here — after the text layer DOM exists —
        // so each rect can be computed by querying the rendered span via DOM
        // Range. Doing this before textLayer.render is built was the old
        // approximate path and produced rects offset from the actual glyphs on
        // variable-width Hebrew fonts.
        const _acT2 = performance.now();
        postDebug(`afterCanvas p${this.pageNumber}: tlChildren=${this.textLayerDiv?.children.length} t_wait_tc=${Math.round(_acT1 - _acT0)}ms t_textlayer=${Math.round(_acT2 - _acT1)}ms`);
        renderHighlightsForPage(this);
        renderCiteLayer(this);
        try {
          const annotations = await this.pdfPage.getAnnotations();
          if (!isStale() && annotations && annotations.length > 0 && this.annotationLayer) {
            this.annotationLayer.innerHTML = '';
            const layer = new pdfjsLib.AnnotationLayer({
              div: this.annotationLayer,
              page: this.pdfPage,
              viewport: viewport.clone({ dontFlip: true }),
            });
            await layer.render({ annotations, linkService, renderForms: false });
          }
        } catch (e) {
          if (!isStale() && !isCancel(e)) postDebug(`annotation p${this.pageNumber}: ${e.message || e}`);
        }
        emitPageRendered(this.pageNumber);
      } catch (e) {
        if (!isStale() && !isCancel(e)) postDebug(`afterCanvas p${this.pageNumber}: ${e.message || e}`);
      }
    })();
  }

  async ensureTextContent() {
    if (this._textContent) return this._textContent;
    if (!this.pdfPage) this.pdfPage = await state.pdfDoc.getPage(this.pageNumber);
    this._textContent = await this.pdfPage.getTextContent();
    return this._textContent;
  }
}

// ============================================================================
// Minimal LinkService for AnnotationLayer (handles internal goto-page links)
// ============================================================================

const linkService = {
  externalLinkTarget: 2, // _blank
  externalLinkRel: 'noopener noreferrer',
  // External-link follow gets flipped to false in HB_setProtectMode so kiosk PDFs
  // don't shell out to the system browser when a user clicks an http:// annotation.
  externalLinkEnabled: true,
  isInPresentationMode: false,
  isPageVisible: () => true,
  rotation: 0,
  pagesCount: 0,
  page: 1,

  async goToDestination(dest) {
    try {
      const target = await resolveDestination(dest);
      if (target && target.pageNumber) goToPage(target.pageNumber);
    } catch (e) { console.warn('goToDestination failed', e); }
  },
  goToPage(p) { goToPage(p); },
  addLinkAttributes(link, url, newWindow) {
    link.href = url;
    link.target = newWindow || this.externalLinkTarget === 2 ? '_blank' : '';
    link.rel = this.externalLinkRel;
  },
  getDestinationHash(dest) { return '#'; },
  getAnchorUrl(hash) { return hash; },
  setHash() {},
  executeNamedAction() {},
  cachePageRef() {},
};

async function resolveDestination(dest) {
  if (!state.pdfDoc) return null;
  let explicit = dest;
  if (typeof dest === 'string') {
    explicit = await state.pdfDoc.getDestination(dest);
  }
  if (!Array.isArray(explicit) || explicit.length === 0) return null;
  const ref = explicit[0];
  const pageIndex = await state.pdfDoc.getPageIndex(ref);
  return { pageNumber: pageIndex + 1, dest: explicit };
}

// ============================================================================
// Document loading
// ============================================================================

async function loadDocument(url, initialPage) {
  showLoading(true);
  hideError();
  state.loadedNotified = false;
  state.initialPage = initialPage > 0 ? initialPage : 1;
  // Reset layout-anchor state from the previous doc. pageTops will be rebuilt
  // by ensureAllPageDims; initialAnchorPending is set further down once we know
  // we have a usable initial page.
  state.pageTops = null;
  state.initialAnchorPending = 0;
  state._programmaticScroll = false;
  // Hard guard against scheduleRender firing during initial load. scrollToPage
  // (below) sets scrollTop, the scroll event fires in a later task, and the
  // scroll handler calls scheduleRender → queues every buffer page's getPage
  // / render / getTextContent on the pdf.js worker IN PARALLEL with the
  // target's still-pending getTextContent. That's the "highlights take
  // forever" symptom — diagnostic logs showed target's t_wait_tc at 4-10s
  // because its own textContent was queued behind the buffer-page flood.
  // We keep this flag set until target._afterCanvasPromise resolves; the
  // explicit scheduleRender() call after that picks up buffer pages.
  state._initialLoad = true;

  // Tear down previous doc, if any.
  // NOTE: We do NOT clear state.hitsByPage / state.matchedTerms here. HB_loadPdf
  // populates those right before calling loadDocument, and clearing them at this
  // point would erase the hit set for the NEW document — which is exactly the
  // "highlights don't always show" symptom on second-and-later book opens.
  if (state.pdfDoc) {
    for (let i = 1; i <= state.totalPages; i++) {
      const pv = state.pages[i];
      if (pv) {
        pv.unrender();
        pv.container = null; // mass-cleared below by innerHTML, just forget the ref
      }
    }
    state.pages = [];
    dom.viewer.innerHTML = '';
    dom.viewer.style.height = '0px';
    state.findState.matches = [];
    state.findState.perPageScanned.clear();
    state.findState.query = '';
    state.findState.currentIndex = -1;
    // Fire-and-forget destroy. Awaiting was costing ~100-300ms on every book open
    // because pdf.js waits for any in-flight worker operations to finish before
    // resolving. The new doc creates its own worker channel, so the old one's
    // wind-down is irrelevant to the user-visible critical path.
    const oldDoc = state.pdfDoc;
    state.pdfDoc = null;
    oldDoc.destroy().catch(() => {});
    // Force V8 to reclaim NOW. Without --expose-gc this is a no-op (window.gc is
    // undefined) — the WebViewEnvironment passes that flag explicitly so the
    // collection actually runs. Empirically cuts the post-destroy RSS by 80-150MB
    // on books with image-heavy pages where pdf.js holds raster caches.
    try { if (typeof window.gc === 'function') window.gc(); } catch (e) {}
  }

  try {
    const t0 = state._loadT0 || performance.now();
    const tGet = performance.now();
    const loadingTask = pdfjsLib.getDocument({
      url,
      ...PDFJS_OPTS,
    });
    const pdf = await loadingTask.promise;
    postDebug(`TIMING: pdfjs.getDocument done at +${Math.round(performance.now() - t0)}ms (took ${Math.round(performance.now() - tGet)}ms) pages=${pdf.numPages}`);
    state.pdfDoc = pdf;
    state.totalPages = pdf.numPages;
    // Kick off the whole-book fuzzy scan in the background when fuzziness is on.
    // Fire-and-forget; it self-cancels via _fuzzyScanId if a newer document
    // load supersedes this one. Deferred so the visible-page render runs first
    // and the user sees content immediately instead of competing with the scan.
    if ((state.fuzziness | 0) > 0 && state.matchedTerms && state.matchedTerms.length > 0) {
      setTimeout(() => { runBookFuzzyScan(); }, 250);
    }
    linkService.pagesCount = pdf.numPages;

    dom.pageCount.textContent = String(pdf.numPages);
    dom.pageInput.value = String(state.initialPage);

    // Fresh book → no "viewed" marks yet (session-only). The rail itself is
    // built AFTER notifyLoaded (see below) so the synchronous DOM build (~N
    // rows) doesn't sit on the critical path before the target page paints.
    state.viewedPages = new Set();

    // Drop the previous book's thumbnails. If the thumbs sidebar is currently
    // open we rebuild for the new book right away (after notifyLoaded, below);
    // otherwise the next toggleSidebar/populateThumbs builds them on demand.
    resetThumbs();

    // Build all page placeholders so the scrollbar reflects total height immediately.
    // Render only the visible window. We fetch the TARGET page (the hit page the
    // caller asked us to open) rather than page 1 for two reasons:
    //   1. The target's PDFPageProxy is reused below — pv.pdfPage is pre-populated
    //      so target.render() doesn't need another `pdf.getPage()` round-trip.
    //   2. Its viewport is a better layout estimate than page 1's. For books with
    //      an oversized cover, anchoring layout on page 1 produced wildly off
    //      pageTops fallbacks (already fixed by the page-anchor logic in
    //      rebuildLayoutFromDims, but using the target page makes the FIRST guess
    //      already close — fewer corrections needed during ensureAllPageDims).
    const layoutAnchorPage = state.initialPage > 0 && state.initialPage <= pdf.numPages
      ? state.initialPage
      : 1;
    const anchorPdfPage = await pdf.getPage(layoutAnchorPage);
    const baseViewport = anchorPdfPage.getViewport({ scale: state.scale, rotation: effRotation(anchorPdfPage) });
    const containerW = dom.viewerContainer.clientWidth - 24;
    const containerH = dom.viewerContainer.clientHeight - 24;

    // Resolve fit-mode now that we know page dimensions.
    state.scale = resolveScaleFor(baseViewport, containerW, containerH, state.scale);
    syncZoomSelect();

    // Layout state — every page's scroll-top is derived from these via pageTopOf().
    // Setting --page-w/--page-h before any page is attached means the first attached
    // page already has the right CSS dimensions when it appears in the DOM.
    const initialVp = anchorPdfPage.getViewport({ scale: state.scale, rotation: effRotation(anchorPdfPage) });
    state.pageHeight = initialVp.height;
    state.pageWidth = initialVp.width;
    document.documentElement.style.setProperty('--page-w', initialVp.width + 'px');
    document.documentElement.style.setProperty('--page-h', initialVp.height + 'px');
    resetMaxPageWidth(initialVp.width);   // scroll-area baseline; grows as wider pages attach
    // pdf.js v4 TextLayer sizes every text span's font via
    //   font-size: calc(var(--scale-factor) * Npx)
    // and without --scale-factor set the calc resolves to invalid → browser
    // falls back to inherited font-size (~16px), so glyph widths stay frozen
    // at the initial render and the dtSearch highlight rects (computed from
    // range.getClientRects on those spans) don't grow when the user zooms in.
    // Keep this in sync with state.scale anywhere we adjust it.
    document.documentElement.style.setProperty('--scale-factor', String(state.scale));

    // Total scrollable height. Calculated from page count so the scrollbar reflects
    // "number of pages" without us having to actually create 600 placeholder divs.
    dom.viewer.style.height = computeTotalHeight(pdf.numPages) + 'px';

    // Build PageView OBJECTS only — no DOM. Each attaches its <div class="page">
    // lazily when scroll/render brings it into the visible window. Memory cost
    // for the unused 595+ pages is just the JS objects (~few KB total).
    state.pages = new Array(pdf.numPages + 1);
    for (let i = 1; i <= pdf.numPages; i++) {
      const pv = new PageView(i);
      pv.viewport = initialVp;
      if (i === layoutAnchorPage) pv.pdfPage = anchorPdfPage;
      state.pages[i] = pv;
    }

    // Render the user's TARGET PAGE first and notify the host as soon as it's on
    // screen — buffer pages, outline (which calls into pdf.js and competes for the
    // single worker), and everything else happen after notifyLoaded so the user
    // sees their page in the shortest possible time.
    //
    // initialAnchorPending tells rebuildLayoutFromDims to re-snap scrollTop to
    // this page after every dim batch — guards against the "page-1 stride was
    // wrong, you drifted to page +N after we got real dims" bug.
    state.initialAnchorPending = state.initialPage;
    // scrollToPage handles its own _programmaticScroll flag (cleared via rAF so
    // the deferred scroll event still sees it as true and doesn't wipe the
    // initial-anchor state).
    scrollToPage(state.initialPage, false);
    const target = state.pages[state.initialPage];
    if (target) {
      target.attach();
      const tRender = performance.now();
      await target.render();
      postDebug(`TIMING: target page ${state.initialPage} render done at +${Math.round(performance.now() - t0)}ms (took ${Math.round(performance.now() - tRender)}ms)`);
    }
    notifyLoaded();
    postDebug(`TIMING: notifyLoaded fired at +${Math.round(performance.now() - t0)}ms (user-visible "loaded")`);
    // Build the rail AFTER notifyLoaded so the user sees the first page as
    // fast as possible — the rail is purely informational (current-page
    // indicator + viewed dots) and can appear a frame or two later without
    // anyone noticing. Wrapped in rAF so the browser actually paints the
    // page first; for very large books the rail's synchronous DOM build
    // (~N rows) used to block paint by 50-200ms before this move.
    requestAnimationFrame(() => { try { buildPageRail(); } catch (e) { postDebug('buildPageRail: ' + e); } });
    // Sidebar already open on the thumbs tab when the user switched books?
    // Rebuild thumbnails now so the new book's pages replace the old ones
    // without needing a manual sidebar toggle.
    if (!dom.sidebar.classList.contains('hidden') && dom.thumbsPane.classList.contains('active'))
      populateThumbs();
    // Let the TARGET page's _afterCanvas (textLayer build + highlight pass)
    // finish BEFORE we flood the pdf.js worker with buffer-page getPage/
    // render/getTextContent calls. The target's own getTextContent — kicked
    // off in render() alongside the canvas render — would otherwise sit
    // behind buffer pages in the worker FIFO, and the target's highlight
    // pass would be delayed by seconds (10s on books with dense buffer-page
    // text, per the diagnostic logs). The user already sees the canvas;
    // delaying buffer-page loading by ~200-500ms is invisible compared to
    // the gain of seeing highlights on the open page immediately.
    if (target && target._afterCanvasPromise) {
      try { await target._afterCanvasPromise; }
      catch (e) { postDebug(`target afterCanvas await: ${e?.message || e}`); }
    }
    // Initial-load window done — release the scheduleRender guard so the
    // explicit call below (and future scroll-driven calls) can run.
    state._initialLoad = false;
    scheduleRender();   // buffer pages around the target — non-blocking
    populateOutline();  // fire-and-forget; defers to after the user sees their page
    // pdfDoc is set now — if we got opened with a search context (hitsByPage
    // populated by HB_loadPdf via parseHighlightXml), cross-check each candidate
    // page's text against the matched terms and tell the host which pages truly
    // contain a match. See verifyHitsAndNotifyHost for the why.
    if (state.hitsByPage && state.hitsByPage.size > 0) verifyHitsAndNotifyHost();
    // Fill in every page's REAL height in the background so the cumulative pageTops
    // table is built from true per-page dimensions instead of the single anchor-page
    // stride. THE OVERLAP BUG: each .page box is sized to its own real viewport
    // height, but the NEXT page's top comes from the uniform stride (anchor height +
    // PAGE_GAP_Y). When a page's real height exceeds the anchor by more than the 12px
    // gap — common in scanned Hebrew books whose per-page MediaBoxes differ by a few
    // source px, magnified at fit-width — the gap goes NEGATIVE and every page covers
    // the top of the next, all the way down (and which page you opened on decides the
    // sign, so reopening sometimes "fixes" it). Building pageTops from real heights
    // makes tops and box-heights agree. We defer to idle — AFTER the target render,
    // its highlights, buffer pages and outline — and ensureAllPageDims batches getPage
    // in 16s with awaits, so the walk no longer competes with the user's visible
    // render (the contention that made the old unconditional load-time call too
    // costly). A user zoom/resize already heals it via rebuildLayoutFromDims; this
    // makes it heal on its own shortly after open, without any interaction.
    scheduleAllPageDims();
  } catch (err) {
    console.error('PDF load failed', err);
    showError('שגיאה בטעינת הקובץ: ' + (err.message || err));
    notifyLoaded();
  } finally {
    // Always release the guard, even on error / cancellation, so future opens
    // aren't stuck with scheduleRender silently no-oping.
    state._initialLoad = false;
    showLoading(false);
  }
}

function notifyLoaded() {
  if (state.loadedNotified) return;
  state.loadedNotified = true;
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage('hb-loaded');
    }
  } catch (e) { /* host gone */ }
}

// ============================================================================
// Zoom + fit modes
// ============================================================================

const ZOOM_STEPS = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

function resolveScaleFor(viewport, containerW, containerH, fallback) {
  switch (state.fitMode) {
    case 'page-width':
      return containerW / (viewport.width / state.scale);
    case 'page-height':
      return containerH / (viewport.height / state.scale);
    case 'page-fit':
      return Math.min(
        containerW / (viewport.width / state.scale),
        containerH / (viewport.height / state.scale));
    case 'auto':
      // viewer.html-style: width-fit on small pages, otherwise 1.0
      return Math.min(1.5, containerW / (viewport.width / state.scale));
    default:
      return fallback;
  }
}

function setZoom(newScale, fitMode = null) {
  state.fitMode = fitMode;
  state.scale = clamp(newScale, 0.1, 10);
  applyZoom();
  syncZoomSelect();
}

function applyZoom() {
  if (!state.pdfDoc) return;
  // Pick ANY page whose pdfPage is already loaded as the baseline-dimensions
  // donor. Used to be `state.pages[1]`, but since we switched the initial
  // fetch from "always page 1" to "the target page the user opened on",
  // page 1 may not be loaded — and the old check bailed silently, freezing
  // zoom controls. We prefer the current/visible page (it's almost certainly
  // loaded), and fall back to the first loaded page we find.
  let donor = state.pages[state.currentPage];
  if (!donor || !donor.pdfPage) {
    donor = null;
    for (let i = 1; i <= state.totalPages; i++) {
      const pv = state.pages[i];
      if (pv && pv.pdfPage) { donor = pv; break; }
    }
  }
  if (!donor || !donor.pdfPage) return;
  const baseVp = donor.pdfPage.getViewport({ scale: state.scale, rotation: effRotation(donor.pdfPage) });

  // Remember where the user was BEFORE the layout changes. We preserve both
  // the page AND the offset within that page (as a fraction of the page's
  // height in the OLD scale) so a resize / zoom that re-fits page-width
  // doesn't drag the user back to the top of the page. Critical for the
  // "open at search hit, then F11" flow — the F11 immersive toggle widens
  // the WebView2, fires resize → page-width re-fit → applyZoom, and without
  // this offset preservation the scroll snaps to top-of-page and the
  // highlight disappears off-screen.
  const savedPage = state.currentPage;
  const oldPageH = state.pageHeight;
  const oldPageTop = pageTopOf(savedPage);
  const offsetFraction = oldPageH > 0
    ? Math.max(0, (dom.viewerContainer.scrollTop - oldPageTop) / oldPageH)
    : 0;

  // 1. Update the fallback dims (used when a per-page dim is unknown).
  state.pageWidth = baseVp.width;
  state.pageHeight = baseVp.height;
  document.documentElement.style.setProperty('--page-w', baseVp.width + 'px');
  document.documentElement.style.setProperty('--page-h', baseVp.height + 'px');
  // Re-baseline the scroll width to THIS scale; step 2 below detaches every page, so each
  // one re-attaches and grows it again if it's wider. Without the reset, zooming out would
  // keep the scroll area as wide as the previous (larger) scale.
  resetMaxPageWidth(baseVp.width);
  // pdf.js TextLayer reads --scale-factor lazily from CSS to size span fonts —
  // see the matching set on initial load. Without this update, zooming would
  // re-render the canvas at the new size but text-span fontSize would stay
  // frozen at the previous scale, and highlight rects (derived from
  // getClientRects on those spans) would stay frozen too.
  document.documentElement.style.setProperty('--scale-factor', String(state.scale));

  // 2. Detach every attached page so the next render pass re-attaches at the new
  // scale. Doing it first avoids the canvas rendering against the stale viewport.
  for (let i = 1; i <= state.totalPages; i++) {
    const pv = state.pages[i];
    if (!pv) continue;
    if (pv.container) pv.detach();
    // Drop the cached viewport — render() and refreshLayout() will derive a new one
    // from pdfPage at the current scale.
    pv.viewport = null;
  }

  // 3. Rebuild the cumulative-tops table. Pages already loaded contribute their real
  // (scaled) dimensions; un-loaded ones fall back to the donor-page estimate.
  rebuildLayoutFromDims();

  // 4. Restore: same FRACTION within savedPage at the NEW scale. Multiply by
  // the new page height (state.pageHeight was just set above) so the user
  // stays put visually as content scales. scrollToPage(savedPage) — the old
  // behaviour — would snap to the top of the page and lose the highlight
  // position the search-hit auto-scroll just established.
  const newPageH = state.pageHeight;
  const newPageTop = pageTopOf(savedPage);
  state._programmaticScroll = true;
  dom.viewerContainer.scrollTop = Math.max(0, newPageTop + offsetFraction * newPageH);
  requestAnimationFrame(() => { state._programmaticScroll = false; });
  scheduleRender();
}

function zoomIn() {
  const next = ZOOM_STEPS.find(s => s > state.scale) || state.scale * 1.1;
  setZoom(next);
}

function zoomOut() {
  const reverse = [...ZOOM_STEPS].reverse();
  const next = reverse.find(s => s < state.scale) || state.scale / 1.1;
  setZoom(next);
}

function syncZoomSelect() {
  if (state.fitMode) {
    dom.zoomSelect.value = state.fitMode;
  } else {
    // Find closest preset, or set to a numeric value.
    const presets = [...dom.zoomSelect.options].map(o => o.value);
    const numeric = presets.filter(v => !isNaN(parseFloat(v)));
    let best = numeric[0]; let bestD = Infinity;
    for (const v of numeric) {
      const d = Math.abs(parseFloat(v) - state.scale);
      if (d < bestD) { bestD = d; best = v; }
    }
    dom.zoomSelect.value = best;
  }
}

// ============================================================================
// Navigation + scroll-tracked current page
// ============================================================================

function goToPage(p) {
  if (!state.pdfDoc) return;
  p = clamp(Math.floor(p) || 1, 1, state.totalPages);

  // For far jumps, eagerly detach every page that's currently attached but won't
  // be in the target window. Two reasons this matters:
  //   (1) The pdf.js worker is single-threaded — if 5 stale renders are queued,
  //       the target's render waits behind them. unrender() inside detach()
  //       cancels in-flight renderTasks so the worker frees up.
  //   (2) The target's attach + render starts at the front of the queue, so the
  //       user sees their page within ~one render time instead of several.
  // Without this step, jumps of 100+ pages left the target white for several
  // seconds while old pages finished/cancelled their render passes.
  if (Math.abs(p - state.currentPage) > RENDER_BUFFER * 2) {
    for (let i = 1; i <= state.totalPages; i++) {
      if (i < p - RENDER_BUFFER || i > p + RENDER_BUFFER) {
        const pv = state.pages[i];
        if (pv && pv.container) pv.detach();
      }
    }
  }

  // If we're still inside the initial-load stabilization window (anchor pending),
  // retarget the anchor to the new page so subsequent dim-batch rebuilds snap
  // here instead of the original initial page.
  if (state.initialAnchorPending) state.initialAnchorPending = p;

  // Instant scroll, NOT smooth. With virtualization, a smooth scroll across hundreds
  // of pages would fire a scroll event per animation frame; each one would attach +
  // render every page passing through the viewport, swamping the worker queue and
  // leaving the user staring at white placeholders. Instant lands at the target so
  // only the target's window gets rendered.
  scrollToPage(p, false);
  state.currentPage = p;
  dom.pageInput.value = String(p);
  updateThumbsCurrent();
  updateRail();
  scheduleRender();
  // Search-result landing: if the target page already had its highlights drawn
  // (because the renderer ran ahead of the host's re-target call — typical when
  // verifyHits narrows pg+1 down to pg AFTER all the candidate pages painted),
  // its firstHitPageY is already known and we can scroll right now. If it hasn't
  // rendered yet, renderHighlightsForPage's own maybeScrollInitialAnchorToHighlight
  // call will pick it up the moment paint finishes.
  const targetPv = state.pages[p];
  if (targetPv) maybeScrollInitialAnchorToHighlight(targetPv);
}

function scrollToPage(p, smooth) {
  if (!state.pdfDoc) return;
  // Math-based — the page may not even be in the DOM yet (virtualization). The
  // scroll container will fire scroll events, scheduleRender will attach the page,
  // and the user lands on the correct offset whether or not the div exists yet.
  const cTop = pageTopOf(p);
  // Flag the scroll as programmatic so the container's scroll listener doesn't
  // mistake it for a user gesture and release the initial-load anchor. Setting
  // scrollTop is synchronous but the scroll EVENT fires asynchronously (next
  // task/animation frame); clearing the flag synchronously here was racing
  // against that event, which then saw the flag as false and wiped
  // state.initialAnchorPending right after the book opened — defeating the
  // "scroll to first highlight" logic. Defer the clear to a rAF so the event
  // has dispatched by then.
  state._programmaticScroll = true;
  if (smooth) dom.viewerContainer.scrollTo({ top: cTop, behavior: 'smooth' });
  else dom.viewerContainer.scrollTop = cTop;
  requestAnimationFrame(() => { state._programmaticScroll = false; });
}

function updateCurrentPageFromScroll() {
  if (!state.pdfDoc || state.pageHeight <= 0) return;
  const c = dom.viewerContainer;
  // The page that owns the upper-third of the viewport is the "current" page —
  // matches viewer.html behaviour and feels more natural than middle (which would
  // flip pages too eagerly).
  const probe = c.scrollTop + c.clientHeight / 3;
  const p = clamp(findPageAtScroll(probe), 1, state.totalPages);
  if (state.currentPage !== p) {
    state.currentPage = p;
    dom.pageInput.value = String(p);
    linkService.page = p;
    updateThumbsCurrent();
    updateRail();
    // Tell the host the reader moved so it can keep the "pages with results" strip
    // highlight on the page actually in view. Page-granular (only fires on change),
    // so no throttle needed. The host treats this as a highlight-only sync — it does
    // NOT scroll back.
    try { window.chrome?.webview?.postMessage('hb-page:' + p); } catch (_) { /* ignore */ }
  }
}

// ============================================================================
// Render scheduling — only render pages near viewport
// ============================================================================

// Pages to keep rendered above + below the viewport. The user originally asked for
// 4 either side but doubling the buffer doubled the parallel getTextContent calls
// pdf.js's single worker had to service — and highlights wait on getTextContent,
// so the visible behaviour was the target page's highlights getting stuck behind
// 8 other pages' text extractions. Back to 2 either side; the user can still
// scroll smoothly because attached pages render in render-from-center-out order
// (scheduleRender), so the page they actually land on jumps the queue.
const RENDER_BUFFER = 2;
const FAR_GRACE = 1;
const PAGE_GAP_Y = 12;     // px between consecutive pages
const PAGE_PADDING_Y = 12; // px above the first page / below the last page

// Layout math. `pageTopOf(n)` and `computeTotalHeight()` return values from the
// per-page cumulative table when available (mixed-size PDFs); fall back to the
// uniform-stride estimate during the initial render before ensureAllPageDims has
// finished walking every page.
function pageTopOf(pageNumber) {
  if (state.pageTops && state.pageTops[pageNumber] !== undefined) return state.pageTops[pageNumber];
  return PAGE_PADDING_Y + (pageNumber - 1) * (state.pageHeight + PAGE_GAP_Y);
}
function computeTotalHeight(numPages) {
  if (numPages <= 0) return 0;
  if (state.pageTops && state.pageTops[numPages + 1] !== undefined) {
    return state.pageTops[numPages + 1] - PAGE_GAP_Y + PAGE_PADDING_Y;
  }
  return PAGE_PADDING_Y * 2 + numPages * state.pageHeight + (numPages - 1) * PAGE_GAP_Y;
}

/// Returns the viewport for page `n` at the current zoom/rotation, or null if the
/// page hasn't been fetched yet. Synchronous — pdf.js caches PDFPageProxy instances
/// after first getPage; getViewport is a pure transform.
/// Widens the scroll area to fit the widest page seen at the CURRENT scale.
///
/// #viewer sizes itself to `max(100%, --max-page-w)`. Without that, a page zoomed wider than
/// the container has its left half painted at a negative x by the centring transform, where
/// no scrollbar can reach it. Pages arrive one at a time and only the loaded ones expose
/// their dimensions, so the maximum is accumulated as they attach rather than computed
/// up-front. `resetMaxPageWidth` re-baselines it on every zoom — otherwise zooming back out
/// would leave the scroll area as wide as the largest page ever shown.
function noteMaxPageWidth(width) {
  if (!(width > 0)) return;
  const current = parseFloat(
    document.documentElement.style.getPropertyValue('--max-page-w')) || 0;
  if (width > current)
    document.documentElement.style.setProperty('--max-page-w', width + 'px');
}

function resetMaxPageWidth(width) {
  document.documentElement.style.setProperty('--max-page-w', (width > 0 ? width : 0) + 'px');
}

function viewportFor(n) {
  const pv = state.pages?.[n];
  if (!pv?.pdfPage) return null;
  return pv.pdfPage.getViewport({ scale: state.scale, rotation: effRotation(pv.pdfPage) });
}

/// Finds the page that contains scroll-y `y`, via binary search through pageTops.
/// Falls back to the uniform-stride estimate when the table isn't populated yet.
function findPageAtScroll(y) {
  const total = state.totalPages || 1;
  if (state.pageTops) {
    let lo = 1, hi = total;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (state.pageTops[mid] <= y) lo = mid; else hi = mid - 1;
    }
    return lo;
  }
  const stride = state.pageHeight + PAGE_GAP_Y;
  return clamp(Math.floor((y - PAGE_PADDING_Y) / stride) + 1, 1, total);
}

/// Rebuilds the cumulative-tops table from currently-known page viewports and
/// repositions/resizes any attached page DIVs in place so layout shifts don't
/// strand the user's scroll position.
///
/// Critical: pixel-based scrollTop is meaningless across a rebuild — the same
/// scrollTop represents different LOGICAL pages before vs. after dimensions are
/// refined. We capture the logical page (+ pixel offset inside it) before the
/// rebuild and restore it after, so the user keeps viewing the same page even
/// when ensureAllPageDims fills in the size of an earlier page that turned out
/// to be larger or smaller than the page-1 stride we initially guessed. Without
/// this fix, opening a hit on page 201 in a book with a tall cover landed the
/// user on page ~225 after the layout corrected itself in the background.
function rebuildLayoutFromDims() {
  if (!state.pdfDoc) return;
  const total = state.totalPages;

  // Snapshot the logical position we want to preserve. Prefer state.initialPage
  // during the initial load (so a hit jump survives the layout-correction passes
  // even if a renderVisiblePages pass briefly updated currentPage to a guessed
  // neighbour); otherwise use the user's actual viewport position.
  const container = dom.viewerContainer;
  const prevScrollTop = container ? container.scrollTop : 0;
  let anchorPage = state.initialAnchorPending || state.currentPage || state.initialPage || 1;
  let anchorOffset = 0;
  // Use the OLD pageTops (still in state.pageTops as of this call) to compute
  // the offset inside the anchor page. If pageTops isn't built yet, the
  // uniform-stride fallback inside pageTopOf is what scrollToPage used, so the
  // offset math is consistent.
  const oldTopOfAnchor = pageTopOf(anchorPage);
  anchorOffset = prevScrollTop - oldTopOfAnchor;
  if (anchorOffset < 0) anchorOffset = 0;

  const tops = new Array(total + 2);
  tops[1] = PAGE_PADDING_Y;
  for (let i = 1; i <= total; i++) {
    const vp = viewportFor(i);
    const h = vp?.height ?? state.pageHeight;
    tops[i + 1] = tops[i] + h + PAGE_GAP_Y;
  }
  state.pageTops = tops;
  // Snap viewer height to the new total so the scrollbar reflects reality.
  dom.viewer.style.height = computeTotalHeight(total) + 'px';
  // Re-anchor every attached page — both width/height (mixed-size books) and top
  // (cumulative offsets shift after each new dim arrives).
  for (let i = 1; i <= total; i++) {
    const pv = state.pages[i];
    if (!pv) continue;
    const vp = viewportFor(i);
    if (vp) pv.viewport = vp;
    pv.refreshLayout();
  }

  // Restore the logical position. We mark this as a programmatic scroll so the
  // wheel/keyboard listeners don't mistake it for the user navigating. Clear via
  // rAF — see scrollToPage for why the synchronous clear races the scroll event.
  if (container) {
    state._programmaticScroll = true;
    container.scrollTop = pageTopOf(anchorPage) + anchorOffset;
    requestAnimationFrame(() => { state._programmaticScroll = false; });
  }
}

/// Walk every page in the background after open, fetching its PDFPageProxy so
/// `viewportFor` can return real dimensions instead of falling back to page-1.
/// Done in batches of 16 so pdf.js's worker isn't flooded with 600+ getPage calls
/// at once. The layout rebuilds after each batch — the scrollbar grows/shrinks
/// smoothly as actual sizes come in.
async function ensureAllPageDims() {
  if (!state.pdfDoc) return;
  const total = state.totalPages;
  const BATCH = 16;
  for (let start = 1; start <= total; start += BATCH) {
    if (!state.pdfDoc) return; // doc was swapped out
    const end = Math.min(start + BATCH - 1, total);
    const tasks = [];
    for (let i = start; i <= end; i++) {
      const pv = state.pages[i];
      if (!pv || pv.pdfPage) continue;
      tasks.push(state.pdfDoc.getPage(i).then(p => { pv.pdfPage = p; }));
    }
    if (tasks.length === 0) continue;
    try { await Promise.all(tasks); }
    catch { /* one bad page shouldn't stop the rest */ }
    rebuildLayoutFromDims();
  }
  // All real dimensions are in — no more layout shifts are coming, so release
  // the initial-anchor lock and let the user scroll wherever they want from now
  // on. Doing it here (rather than after the first batch) means hit pages near
  // the end of the book stay anchored through every refinement pass, not just
  // the early ones.
  state.initialAnchorPending = 0;
}

/// Kick off the full per-page dimension walk once the browser is idle, so it runs
/// after the user-visible critical path (target render, highlights, buffer pages,
/// outline) rather than competing with it. Captures the current doc so a callback
/// left over from a previous book can't run against a freshly-opened one.
function scheduleAllPageDims() {
  const doc = state.pdfDoc;
  if (!doc) return;
  const run = () => { if (state.pdfDoc === doc) ensureAllPageDims().catch(() => {}); };
  if (typeof window.requestIdleCallback === 'function')
    window.requestIdleCallback(run, { timeout: 2000 });
  else
    setTimeout(run, 1200);
}

let renderQueued = false;
function scheduleRender() {
  // Hard-block during the initial load window — see state._initialLoad doc in
  // loadDocument. Without this, the scroll event triggered by scrollToPage(target)
  // calls in here while target.getTextContent is still pending on the worker,
  // queues 4+ buffer pages' worker calls behind it, and delays the target's
  // highlights by seconds.
  if (state._initialLoad) return;
  if (renderQueued) return;
  renderQueued = true;
  requestAnimationFrame(() => {
    renderQueued = false;
    renderVisiblePages();
  });
}

async function renderVisiblePages() {
  if (!state.pdfDoc || state.pageHeight <= 0) return;
  const c = dom.viewerContainer;
  const top = c.scrollTop;
  const bot = top + c.clientHeight;

  // Math-based visibility: which pages overlap [top, bot]? Uses cumulative tops
  // when available so mixed-size PDFs (landscape inserts etc.) get the right
  // window; falls back to the uniform-stride estimate during the initial render.
  const first = clamp(findPageAtScroll(top), 1, state.totalPages);
  const last = clamp(findPageAtScroll(bot), 1, state.totalPages);

  const startPage = Math.max(1, first - RENDER_BUFFER);
  const endPage = Math.min(state.totalPages, last + RENDER_BUFFER);

  // Attach + render pages in window. We RENDER FROM THE CENTER OUTWARD so the page
  // the user is most likely looking at gets in front of the worker queue. Naive
  // ascending order means after a fast scroll, the user waits for several earlier
  // pages to render before their actual visible page starts.
  const center = clamp(Math.round((first + last) / 2), startPage, endPage);
  const order = [center];
  for (let off = 1; off <= Math.max(center - startPage, endPage - center); off++) {
    if (center - off >= startPage) order.push(center - off);
    if (center + off <= endPage) order.push(center + off);
  }
  for (const i of order) {
    const pv = state.pages[i]; if (!pv) continue;
    pv.attach();
    pv.render();
  }
  // Detach pages outside the window — DEFERRED so a fast scroll doesn't thrash the
  // DOM. After 250ms of scroll idle, anything outside [start-FAR_GRACE, end+FAR_GRACE]
  // is removed entirely (page DIV + canvas + text layers).
  scheduleDetachFar(startPage, endPage);
}

let detachTimer = null;
function scheduleDetachFar(startPage, endPage) {
  if (detachTimer) clearTimeout(detachTimer);
  detachTimer = setTimeout(() => {
    detachTimer = null;
    for (let i = 1; i <= state.totalPages; i++) {
      if (i < startPage - FAR_GRACE || i > endPage + FAR_GRACE) {
        const pv = state.pages[i];
        if (pv && pv.container) pv.detach();
      }
    }
  }, 250);
}

// ============================================================================
// dtSearch hit overlay (ported from Resources/PdfViewer/overlay.js)
// ============================================================================

function parseHighlightXml(xml) {
  const map = new Map();
  // Original dtSearch-claimed pages (without the ±1 expansion). Used by
  // verifyHitsAndNotifyHost as the fallback set when none of the ±1 candidates
  // were verifiable via indexOf — we then trust dtSearch's exact claim instead
  // of leaving the ±1 expanded set in hitsByPage (which would draw fallback
  // bands on neighbours that dtSearch never claimed).
  state.originalHitPages = new Set();
  if (!xml || typeof xml !== 'string') return map;
  const re = /<loc\b([^>]*)>/gi;
  let m;
  while ((m = re.exec(xml)) !== null) {
    const attrs = m[1];
    const pg = readIntAttr(attrs, 'pg');
    const pos = readIntAttr(attrs, 'pos');
    const len = readIntAttr(attrs, 'len');
    if (pg === null || pos === null || len === null) continue;
    // dtSearch's pg is normally 0-based and our +1 lines up with pdf.js's 1-based
    // page index — but for some PDFs (e.g. אוצר רש"י 69412) dtSearch's count of
    // pages diverges from pdf.js's by one, putting the hit on the wrong page.
    // We register the hit on pg, pg+1, and pg+2 (UI-1-based) so the renderer runs
    // on whichever pdf.js page actually contains the term; indexOf on the wrong
    // pages returns -1 and draws nothing, so the extra registrations are harmless.
    const center = pg + 1;
    state.originalHitPages.add(center);
    for (const page of [center - 1, center, center + 1]) {
      if (page < 1) continue;
      let arr = map.get(page);
      if (!arr) { arr = []; map.set(page, arr); }
      arr.push({ pos, len });
    }
  }
  return map;
}

function readIntAttr(attrs, name) {
  const re = new RegExp('\\b' + name + '\\s*=\\s*"?([#\\w-]+)"?', 'i');
  const m = re.exec(attrs);
  if (!m) return null;
  const n = parseInt(m[1], 10);
  return isNaN(n) ? null : n;
}

window.HB_setHighlightXml = function (xml, terms) {
  state.highlightXml = xml || '';
  state.matchedTerms = Array.isArray(terms) ? terms.slice() : [];
  state.hitsByPage = parseHighlightXml(state.highlightXml);
  // Bump the hit generation. A verifyHitsAndNotifyHost started for a PREVIOUS
  // query may still be awaiting getTextContent when this fires (user ran a new
  // in-book search before the old verification finished). The stale run would
  // otherwise post the OLD query's pages and clobber the new chip strip — the
  // gen check inside verifyHitsAndNotifyHost makes it bail instead.
  state.hitsGen = (state.hitsGen || 0) + 1;
  // Fresh query — reset the per-page running tally so the live counter starts
  // back at 0 rather than continuing from the previous query's total.
  state.drawnPerPage = new Map();
  _progressLastTotal = -1;
  notifyHighlightProgress();
  postDebug(`HB_setHighlightXml: hitsPages=${state.hitsByPage.size} terms=${state.matchedTerms.length} sample=${JSON.stringify(state.matchedTerms.slice(0, 20))}`);
  // Re-draw on every currently rendered page.
  for (let i = 1; i <= state.totalPages; i++) {
    const pv = state.pages[i];
    if (pv && pv.rendered) renderHighlightsForPage(pv);
  }
  // Async: verify each candidate page actually contains a matched term (via
  // pdf.js's textContent), drop the bogus ones, and post the corrected list
  // back to the host so the page-chip strip and CurrentPage navigation
  // reflect where the term ACTUALLY lives — not where dtSearch's pg index
  // claimed it lived (the two diverge by ±1 for some PDFs).
  verifyHitsAndNotifyHost();
};

// Palette theming from the host (PdfJsHost.EnsureViewerPageLoadedAsync + the ThemeService
// broadcast). Receives a map of CSS custom properties → values and applies them to :root, so the
// whole viewer chrome (toolbar, sidebar, page area, accent) tracks the app's selected palette.
// Omitted vars keep their CSS default. This is the real bridge the CSS header always promised —
// see docs/color-system-spec.md surface D.
window.HB_setTheme = function (vars) {
  try {
    if (!vars || typeof vars !== 'object') return;
    const root = document.documentElement;
    for (const k in vars) {
      if (Object.prototype.hasOwnProperty.call(vars, k)) root.style.setProperty(k, vars[k]);
    }
  } catch (e) { /* non-fatal: viewer keeps its default palette */ }
};

async function verifyHitsAndNotifyHost() {
  if (!state.pdfDoc) return;
  // Snapshot the generation this run belongs to. If a newer HB_setHighlightXml /
  // HB_loadPdf fires while we're awaiting getTextContent below, the gen bumps
  // and this (now-stale) run must NOT mutate state.hitsByPage or post pages —
  // it would overwrite the newer query's chip strip with the old query's pages.
  const myGen = state.hitsGen || 0;
  const stripped = (state.matchedTerms || [])
    .map(t => stripNiqud(t || ''))
    .filter(t => t.length > 0);
  if (stripped.length === 0) return;
  const original = [...(state.originalHitPages || new Set())].sort((a, b) => a - b);
  if (original.length === 0) return;

  // Page-drift is consistent across a single PDF: every dtSearch <loc pg=N> in
  // book X is off by the same amount from pdf.js's 1-based page index (typically
  // 0, occasionally ±1 due to cover-page counting differences). Sampling ONE hit
  // lets us infer the drift for the whole book — much faster than verifying
  // every candidate (~300 pages × ~100ms getTextContent each on big results).
  // We try drift=0 first because that's the common case; ±1 only kicks in for
  // the handful of PDFs where dtSearch and pdf.js disagree on numbering.
  const sample = original[0];
  let drift = null;
  for (const candidateDrift of [0, -1, 1]) {
    const p = sample + candidateDrift;
    if (p < 1 || p > state.totalPages) continue;
    try {
      const page = await state.pdfDoc.getPage(p);
      const tc = await page.getTextContent();
      let concat = '';
      for (const item of tc.items) { concat += item.str || ''; concat += ' '; }
      const flat = hasNiqud(concat) ? stripNiqudWithMap(concat).stripped : concat;
      if (stripped.some(needle => flat.indexOf(needle) !== -1)) {
        drift = candidateDrift;
        break;
      }
    } catch { /* try next drift */ }
    if ((state.hitsGen || 0) !== myGen) return; // superseded by a newer query
  }
  if ((state.hitsGen || 0) !== myGen) return; // superseded while sampling

  // Rebuild hitsByPage with the resolved drift (or fall back to the original
  // dtSearch pages when pdf.js text extraction can't find the term anywhere —
  // those pages get the band-marker treatment in renderHighlightsForPage).
  const newMap = new Map();
  const finalPages = new Set();
  if (drift !== null) {
    for (const p of original) {
      const target = p + drift;
      if (target < 1) continue;
      // Same {pos, len} tuples, just re-indexed under the resolved page.
      newMap.set(target, state.hitsByPage.get(p) || state.hitsByPage.get(target) || []);
      finalPages.add(target);
    }
  } else {
    for (const p of original) {
      finalPages.add(p);
      newMap.set(p, state.hitsByPage.get(p) || []);
    }
  }
  state.hitsByPage = newMap;
  const verifiedPages = [...finalPages].sort((a, b) => a - b);
  postDebug(`verifyHits: sample=${sample} drift=${drift} pages=${verifiedPages.length}`);

  // Re-render any pages already on screen so highlights reflect the resolved
  // page-set (drops bands on neighbours we shouldn't have marked, draws them on
  // the right pages in the fallback case).
  for (const p of verifiedPages) {
    const pv = state.pages[p];
    if (pv && pv.rendered) renderHighlightsForPage(pv);
  }
  if (verifiedPages.length > 0) {
    try {
      window.chrome?.webview?.postMessage(`hb-verified-hit-pages:${verifiedPages.join(',')}`);
    } catch { /* ignore */ }
  }
}

// Place clickable cross-reference links (מסורת הש"ס etc.) over a page, using the
// page-fraction boxes the host supplied in state.citationsByPage. Coordinates scale
// with the current viewport, so zoom/rotate re-render keeps them aligned.
//
// Two kinds:
//   'daf'  — gemara cross-ref. c.fid is a single FileID + c.page is the target page.
//   'book' — book-name link. c.fid is a '|'-joined CSV of candidate FileIDs; on click
//            the host opens a picker (or opens directly when there's only one).
function renderCiteLayer(pv) {
  if (!pv || !pv.citeLayer || !pv.viewport) return;
  const cites = state.citationsByPage[pv.pageNumber] || state.citationsByPage[String(pv.pageNumber)];
  pv.citeLayer.innerHTML = '';
  if (!cites || !cites.length) return;
  const W = pv.viewport.width, H = pv.viewport.height;
  for (const c of cites) {
    const b = c.box;
    if (!b || b.length < 4) continue;
    const a = document.createElement('div');
    const isBook = c.kind === 'book';
    a.className = isBook ? 'citeLink citeLink-book' : 'citeLink';
    a.style.left = (b[0] * W) + 'px';
    a.style.top = (b[1] * H) + 'px';
    a.style.width = Math.max(6, (b[2] - b[0]) * W) + 'px';
    a.style.height = Math.max(6, (b[3] - b[1]) * H) + 'px';
    a.title = isBook
      ? (c.ref || '') + '\nלחיצה — בחירה מבין הספרים בקטלוג\nCtrl+לחיצה — פתיחה בחלון חדש'
      : (c.ref || '') + '\nCtrl+לחיצה — פתיחה בחלון חדש';
    a.addEventListener('click', (e) => {
      e.preventDefault(); e.stopPropagation();
      try {
        if (isBook) {
          // Book-name link: the host gets the candidate list + viewport-pixel anchor of
          // the clicked overlay (so the picker can position near the cursor), and either
          // opens directly (single candidate) or shows the picker. Ctrl/⌘ asks for the
          // result to open in a NEW window — a single candidate goes straight to a new
          // window; a multi-candidate picker inherits the flag so the chosen row opens
          // in a new window too.
          const aRect = a.getBoundingClientRect();
          const payload = JSON.stringify({
            fids: String(c.fid).split('|'),
            title: c.ref || '',
            srcPage: pv.pageNumber,
            anchorRect: [aRect.left, aRect.top, aRect.width, aRect.height],
            newWindow: !!(e.ctrlKey || e.metaKey),
          });
          window.chrome?.webview?.postMessage('hb-open-book:' + payload);
        } else if (e.ctrlKey || e.metaKey) {
          // Ctrl / ⌘-click → open the cited book in a NEW window (it owns its own history,
          // so no srcPage is sent).
          window.chrome?.webview?.postMessage('hb-open-ref-new:' + c.fid + ':' + c.page);
        } else {
          // Plain click → navigate in place; include the source page so the host can
          // return here via the floating "back" button.
          window.chrome?.webview?.postMessage('hb-open-ref:' + c.fid + ':' + c.page + ':' + pv.pageNumber);
        }
      } catch (_) {}
    });
    pv.citeLayer.appendChild(a);
  }
}

// Host pushes the whole book's citations once after load: { "<pageNo>": [ {ref,fid,page,box} ] }.
// Strategy: future page-renders pick the data up automatically (see the renderCiteLayer call
// inside the per-page render path). For pages that have ALREADY rendered before this call
// arrives (target page, plus any pages pdf.js pre-rendered around it), refresh them — but
// only ones with a CANVAS, because state.pages may be sparsely populated for un-realized
// pages and iterating them is wasted work on big books. The refresh is also deferred to
// requestIdleCallback so the target-page render path (the user-visible critical work) is
// never blocked by cite-layer DOM construction.
window.HB_setCitations = function (map) {
  state.citationsByPage = map || {};
  const refreshRendered = () => {
    for (let i = 1; i < state.pages.length; i++) {
      const pv = state.pages[i];
      // pv.canvas exists only on pages whose render has actually started — un-rendered
      // pages still have a citeLayer DIV (created in the page constructor) but no canvas
      // content. Skipping them avoids touching DOM for ranges the user can't see yet;
      // they'll get the cite layer on their first real render anyway.
      if (pv && pv.canvas && pv.citeLayer) renderCiteLayer(pv);
    }
  };
  if (typeof window.requestIdleCallback === 'function')
    window.requestIdleCallback(refreshRendered, { timeout: 250 });
  else
    setTimeout(refreshRendered, 0);
};

// Floating "back" button for cross-reference navigation. The host owns the history
// stack and toggles visibility via HB_setBackVisible after each open / back.
let _hbBackBtn = null;
function _ensureBackBtn() {
  if (_hbBackBtn) return _hbBackBtn;
  const b = document.createElement('button');
  b.id = 'hbBackBtn';
  b.textContent = '→ חזור';   // "→ חזור"
  b.title = 'חזור למקור הקישור';
  b.style.display = 'none';
  b.addEventListener('click', () => { try { window.chrome?.webview?.postMessage('hb-nav-back'); } catch (_) {} });
  document.body.appendChild(b);
  _hbBackBtn = b;
  return b;
}
window.HB_setBackVisible = function (v) { _ensureBackBtn().style.display = v ? 'block' : 'none'; };

/// Host-callable: turn protect-mode (kiosk) on or off. Pushed by PdfJsHost.OpenAsync /
/// OpenTextAsync before every PDF/Text load, so reloads with --protect-mode pick up the
/// flag synchronously. In protect-mode we:
///   * preventDefault contextmenu but never show our menu (right-click → nothing),
///   * make copySelection / clipboard handlers no-op (no text/image to clipboard),
///   * block F12 and Ctrl+Shift+I keyboard shortcuts (in case the host SDK lets them through),
///   * disable pdf.js external-link follow (clicked http:// annotations don't shell out).
/// The host-side WebView2 DevTools / Default context menu / Browser accelerators are
/// already disabled at the WebView2.Settings level when protect-mode is set; the JS
/// gates above defend in depth if a setting fails or a frame escapes the host wiring.
window.HB_setProtectMode = function (active) {
  state.protectMode = !!active;
  try { linkService.externalLinkEnabled = !state.protectMode; } catch (_) {}
  // Body class drives CSS-level lockdowns: PDF annotation link <a> elements
  // (pointer-events:none) and any other "kiosk hides me" rule. Cheaper and
  // more reliable than walking the annotation layer per page.
  document.body.classList.toggle('hb-protect', state.protectMode);
  // Hide the TOC sidebar's add/edit icons — the host (PdfJsHost) already
  // drops hb-toc-add / hb-toc-edit messages in protect-mode, but the icons
  // were still visible. Hide them so the user doesn't even see the buttons.
  if (dom.btnTocAdd)  dom.btnTocAdd.style.display  = state.protectMode ? 'none' : '';
  if (dom.btnTocEdit) dom.btnTocEdit.style.display = state.protectMode ? 'none' : '';
};

// ---------------------------------------------------------------------------
// Book-name picker. Triggered by clicking a citeLink-book overlay. The flow:
//   1. citeLink-book click → postMessage('hb-open-book:<json payload>')
//   2. Host parses, opens directly if 1 candidate, else queries Katalog.db
//      and calls HB_showBookPicker with the resolved metadata.
//   3. User clicks a row → postMessage('hb-open-ref:<fid>:1:<srcPage>'),
//      which reuses the existing daf-link nav flow (back button works too).
//      When the picker was opened via Ctrl/⌘-click (payload.newWindow), the row
//      instead posts 'hb-open-ref-new:<fid>:1' so the host opens a new window.
// ---------------------------------------------------------------------------
let _hbBookPicker = null;
let _hbBookPickerOutside = null;

function _ensureBookPicker() {
  if (_hbBookPicker) return _hbBookPicker;
  const root = document.createElement('div');
  root.id = 'hbBookPicker';
  document.body.appendChild(root);
  _hbBookPicker = root;
  return root;
}

function _hideBookPicker() {
  if (!_hbBookPicker) return;
  _hbBookPicker.classList.remove('open');
  document.removeEventListener('keydown', _bookPickerKey, true);
  if (_hbBookPickerOutside) {
    document.removeEventListener('mousedown', _hbBookPickerOutside, true);
    _hbBookPickerOutside = null;
  }
}

function _bookPickerKey(e) {
  if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); _hideBookPicker(); }
}

/// Host-callable: open the candidate picker for a book-name citation.
/// payload: {
///   title:     string,        // canonical matched title (header)
///   candidates: [{ fid, name, author, year, place }],
///   srcPage:   number,        // page in the source book (for the back stack)
///   anchorRect: [x, y, w, h]? // optional viewport-coord rect to anchor the popup near
/// }
/// If `anchorRect` is missing the popup centers in the viewport (safer fallback when
/// the source page is no longer visible by the time the catalog query returns).
window.HB_showBookPicker = function (payload) {
  payload = payload || {};
  const pop = _ensureBookPicker();
  const title = payload.title || '';
  const candidates = Array.isArray(payload.candidates) ? payload.candidates : [];
  const srcPage = (payload.srcPage | 0) || 0;
  // Ctrl/⌘-click on the book-name link asks for the chosen row to open in a NEW window
  // (the flag rides along from the original click through the host into this payload).
  const newWindow = !!payload.newWindow;

  pop.innerHTML = '';

  const header = document.createElement('div');
  header.className = 'hbBookPickerHeader';
  const t = document.createElement('span');
  t.className = 'hbBookPickerTitle';
  t.textContent = title || 'בחר ספר';
  const cnt = document.createElement('span');
  cnt.className = 'hbBookPickerCount';
  cnt.textContent = candidates.length === 1 ? 'ספר אחד' : (candidates.length + ' ספרים');
  const titleWrap = document.createElement('div');
  titleWrap.style.flex = '1';
  titleWrap.style.overflow = 'hidden';
  titleWrap.appendChild(t);
  titleWrap.appendChild(cnt);
  header.appendChild(titleWrap);
  const close = document.createElement('button');
  close.className = 'hbBookPickerClose';
  close.textContent = '×';
  close.title = 'סגור';
  close.addEventListener('click', _hideBookPicker);
  header.appendChild(close);
  pop.appendChild(header);

  const list = document.createElement('div');
  list.className = 'hbBookPickerList';
  for (const c of candidates) {
    const row = document.createElement('div');
    row.className = 'hbBookPickerRow';
    const nm = document.createElement('div');
    nm.className = 'hbBookName';
    nm.textContent = c.name || c.fid;
    row.appendChild(nm);
    const parts = [];
    if (c.author) parts.push(c.author);
    if (c.year)   parts.push(c.year);
    if (c.place)  parts.push(c.place);
    if (parts.length) {
      const m = document.createElement('div');
      m.className = 'hbBookMeta';
      m.textContent = parts.join(' • ');
      row.appendChild(m);
    }
    row.addEventListener('click', () => {
      _hideBookPicker();
      try {
        // tgt_page = 1: open at the first page; the user navigates inside themselves.
        if (newWindow)
          window.chrome?.webview?.postMessage('hb-open-ref-new:' + c.fid + ':1');
        else
          window.chrome?.webview?.postMessage('hb-open-ref:' + c.fid + ':1:' + srcPage);
      } catch (_) {}
    });
    list.appendChild(row);
  }
  pop.appendChild(list);

  // Position: prefer the click anchor. Strategy:
  //   1. Stash off-screen + open so we can measure the actual rendered size
  //      (candidate count makes the popup variable-height; CSS max is 60vh).
  //   2. Default to BELOW the anchor.
  //   3. If that would overflow the bottom of the viewport, flip ABOVE.
  //   4. Horizontally clamp to keep both edges inside the viewport.
  pop.style.transform = '';
  pop.style.left = '-9999px';
  pop.style.top  = '0px';
  pop.classList.add('open');

  const ar = payload.anchorRect;
  const W = window.innerWidth, H = window.innerHeight;
  const margin = 8;

  if (Array.isArray(ar) && ar.length >= 4) {
    const pr = pop.getBoundingClientRect();
    const pw = pr.width, ph = pr.height;

    let left = Math.max(margin, Math.min(W - pw - margin, ar[0]));
    let top  = ar[1] + ar[3] + 6;                   // default: just below the anchor
    if (top + ph > H - margin) {                    // overflow below → try above
      const above = ar[1] - ph - 6;
      top = above >= margin ? above : Math.max(margin, H - ph - margin);
    }
    pop.style.left = left + 'px';
    pop.style.top  = top  + 'px';
  } else {
    pop.style.left = '50%';
    pop.style.top  = '20%';
    pop.style.transform = 'translateX(-50%)';
  }

  // Close on Esc or click-outside. Capturing so we get the event before pdf.js.
  document.addEventListener('keydown', _bookPickerKey, true);
  _hbBookPickerOutside = (e) => {
    if (!_hbBookPicker || !_hbBookPicker.contains(e.target)) _hideBookPicker();
  };
  // Defer to next tick so the click that opened us doesn't also close us.
  setTimeout(() => {
    if (_hbBookPickerOutside)
      document.addEventListener('mousedown', _hbBookPickerOutside, true);
  }, 0);
};

function renderHighlightsForPage(pv) {
  if (!pv || !pv.highlightLayer || !pv.textLayerDiv) return;
  const ctx = pv.highlightLayer.getContext('2d');
  ctx.clearRect(0, 0, pv.highlightLayer.width, pv.highlightLayer.height);

  const hits = state.hitsByPage.get(pv.pageNumber);
  if (!hits || hits.length === 0) return;
  const rawTerms = state.matchedTerms;
  if (!rawTerms || rawTerms.length === 0) return;
  if (pv.textLayerDiv.children.length === 0) return;

  // Pre-strip terms (drop ones that collapse to empty after niqud removal) and
  // sort longest-first. Length-desc order is what makes the no-overlap pass
  // do the right thing for Hybur-expanded variants: "וברכה" claims its char
  // range before the shorter "ברכה" pass runs and finds the same letters
  // inside it, so the longer surface form wins one highlight per glyph.
  const ts = rawTerms
    .filter(t => t && t.length > 0)
    .map(t => ({ stripped: stripNiqud(t) }))
    .filter(t => t.stripped.length > 0);
  if (ts.length === 0) return;
  ts.sort((a, b) => b.stripped.length - a.stripped.length);

  // Build a page-level concatenation of every text node's content with a single
  // space inserted between distinct nodes. The fake separator is what makes a
  // phrase term like "משה פורמן" match when pdf.js renders each word as its
  // own absolutely-positioned <span>: without it the concat would be
  // "משהפורמן" and the indexOf would miss; with it the concat reads
  // "משה פורמן" exactly like the search term. The separator has no DOM
  // representation, but the Range we eventually build spans straight from the
  // end of one text node to the start of the next, and getClientRects()
  // computes the visible rects across that gap correctly.
  const nodes = [];
  let concat = '';
  const walker = document.createTreeWalker(pv.textLayerDiv, NodeFilter.SHOW_TEXT);
  let tn;
  while ((tn = walker.nextNode())) {
    const text = tn.nodeValue || '';
    if (text.length === 0) continue;
    nodes.push({ node: tn, start: concat.length, end: concat.length + text.length });
    concat += text;
    concat += ' ';
  }
  if (nodes.length === 0) return;

  // Strip niqud once for the whole page if any is present; otherwise the
  // working string is the concat itself. The map translates positions in the
  // stripped string back to positions in concat — needed because the Range
  // operates in original-char coordinates (niqud glyphs are real DOM chars).
  let workingText, niqudMap;
  if (hasNiqud(concat)) {
    const stripped = stripNiqudWithMap(concat);
    workingText = stripped.stripped;
    niqudMap = stripped.map;
  } else {
    workingText = concat;
    niqudMap = null;
  }

  const marked = new Uint8Array(workingText.length);
  const dpr = pv.highlightLayer.width / parseFloat(pv.highlightLayer.style.width);
  const overlayRect = pv.highlightLayer.getBoundingClientRect();
  const _tHi0 = performance.now();

  // Proximity filter with adaptive tolerance. The dtSearch <loc pos> values and
  // our `pos` index live in niqud-stripped char coordinates of the page text,
  // so a small tolerance is usually enough to absorb the residual drift between
  // dtSearch's text extraction and pdf.js's (different newline counting,
  // occasional ligature splits) while still rejecting unrelated occurrences
  // elsewhere on the page. But the drift can be much bigger in practice — odd
  // OCR text, scanned PDFs with reflowed glyphs — so we grow the window on
  // demand and re-run until our drawn count covers what dtSearch claims is on
  // the page. The invariant is `drawn >= hits.length`: dtSearch is authoritative
  // for how many matches exist, and a tight filter that drops below that count
  // is hiding real results from the user. If pdf.js's text genuinely doesn't
  // contain enough occurrences (mismatched OCR layers), the loop saturates at
  // maxTolerance and we draw whatever we found — no infinite loop.
  const baseTolerance = Math.min(400, Math.max(80, Math.floor(workingText.length * 0.08)));
  const maxTolerance = Math.max(workingText.length, 4000);

  // Pre-compute EVERY (pos, endPos) where any needle matches workingText. Done
  // ONCE here, not per tolerance iteration — without this the indexOf walk for
  // every term repeats up to MAX_ATTEMPTS times (~5× redundant work on pages
  // that go to the tolerance ceiling). Sorted longest-needle-first so the
  // marked-overlap pass keeps preferring a longer match over a shorter sibling
  // that nests inside it, identical to the prior term-loop ordering.
  const candidates = [];
  for (const tEntry of ts) {
    const needle = tEntry.stripped;
    if (needle.length > workingText.length) continue;
    let from = 0;
    while (from <= workingText.length - needle.length) {
      const pos = workingText.indexOf(needle, from);
      if (pos === -1) break;
      // Word-boundary check: a single-letter gematria like "ג" would otherwise
      // match as a substring inside "גדול" / "ברגלים" / etc. The user wants
      // search terms to mark WHOLE WORDS only — both edges of the match must
      // sit against a non-word-char (or the text boundary). Skip the match
      // when either side is mid-word.
      const endPos = pos + needle.length;
      const leftOk = pos === 0 || !isHbWordChar(workingText.charCodeAt(pos - 1));
      const rightOk = endPos === workingText.length || !isHbWordChar(workingText.charCodeAt(endPos));
      if (leftOk && rightOk) {
        // Tag with the needle itself so the post-proximity coverage pass below
        // can ask "did term X get at least one rect?" without re-scanning.
        candidates.push({ pos, endPos, nLen: needle.length, needle });
      }
      from = pos + 1;
    }
  }

  // Fuzzy candidate pass — mirrors dtSearch's behaviour at the in-page level.
  // When the user enables Fuzziness (a corpus-level setting that lets dtSearch
  // match words within N character-edits of the query), the precise-rect
  // indexOf above misses every word that dtSearch matched via fuzzy: the page
  // text contains "חלונית" but the query terms list has "חילונית" — strict
  // indexOf fails and the misleading wide fallback band gets drawn at a
  // position estimated from dtSearch's char offset.
  //
  // To match dtSearch's behaviour visually, we walk every word on the page and
  // accept any word that's within `fuzziness` Levenshtein edits of any term.
  // The existing proximity-cluster code then favours the candidate set that
  // forms a tight cluster — same outcome as dtSearch's w/N filter, just done
  // in the viewer with words it can actually locate on the page.
  //
  // Skipped when fuzziness is 0 (matches dtSearch's "exact only" mode) or when
  // the exact pass already produced ≥ hits.length candidates (we already have
  // enough; no need to introduce fuzzy noise).
  const fz = state.fuzziness | 0;
  // Always run the fuzzy candidate scan when fuzziness is on — even if exact
  // indexOf already produced some candidates. dtSearch can match a fuzzy
  // variant the user typed (חילוניות when searching חילונית, etc.) that we
  // need to highlight in addition to whatever the prefix-expanded terms found.
  if (fz > 0) {
    // Per-page fuzzy candidate scan. For each word on the page, accept it as a
    // match for any term whose Levenshtein distance is within the user's
    // fuzziness setting. Earlier versions tightened this to a per-term
    // length-based budget to suppress noise on short Hebrew terms (מעיר ↔
    // מעבר/מעשר are 1 edit apart but obviously unrelated). The user prefers
    // SEEING what dtSearch matched — even noisy 1-char-off variants — over
    // staring at a book the corpus search listed as a hit with nothing
    // highlighted on it. So we honour the full fz budget here and live with
    // the false positives.
    const exactSpans = new Set();
    for (const c of candidates) exactSpans.add(c.pos + ':' + c.endPos);
    const fuzzyAdded = [];
    forEachWord(workingText, (wStart, wEnd) => {
      const wLen = wEnd - wStart;
      if (wLen === 0) return;
      const wordKey = wStart + ':' + wEnd;
      if (exactSpans.has(wordKey)) return;
      let bestNeedle = null;
      let bestDist = fz + 1;
      let wordStr = null;
      for (const tEntry of ts) {
        const needle = tEntry.stripped;
        if (needle.length === 0) continue;
        if (Math.abs(needle.length - wLen) > fz) continue;
        if (wordStr === null) wordStr = workingText.substring(wStart, wEnd);
        const d = fuzzyDistance(wordStr, needle, fz);
        if (d > 0 && d < bestDist) {  // d > 0 — exact handled by indexOf above
          bestDist = d;
          bestNeedle = needle;
          if (d === 1) break;
        }
      }
      if (bestNeedle !== null) {
        fuzzyAdded.push({ pos: wStart, endPos: wEnd, nLen: bestNeedle.length, needle: bestNeedle });
      }
    });
    if (fuzzyAdded.length > 0) {
      postDebug(`hl p${pv.pageNumber}: fuzzy fz=${fz} added=${fuzzyAdded.length} (exact=${candidates.length}, hits=${hits.length})`);
      for (const f of fuzzyAdded) candidates.push(f);
    }
  }

  // Visual-order (reversed) text-layer fallback. Some scanned hebrewbooks PDFs store
  // their text layer in VISUAL order — each line's characters are reversed, so the
  // logical word "בני" sits in the layer as "ינב". dtSearch normalizes RTL to logical
  // order when indexing (so it still lists the page as a hit), but our logical-order
  // indexOf above then finds nothing → a dtSearch hit with no in-page highlight (the
  // "cand=0, drew=0" case). When the forward pass found NOTHING on the whole page, retry
  // each term REVERSED; a match covers the real visual glyphs, so the rects land
  // correctly. Gated on candidates.length===0 so logical-order pages (the common case)
  // never risk a coincidental reversed-substring false positive.
  if (candidates.length === 0) {
    let revAdded = 0;
    for (const tEntry of ts) {
      const needle = tEntry.stripped;
      if (needle.length < 2) continue;                 // 1-char reversed == itself
      const rneedle = needle.split('').reverse().join('');
      if (rneedle === needle) continue;                // palindrome — forward already covered it
      if (rneedle.length > workingText.length) continue;
      let from = 0;
      while (from <= workingText.length - rneedle.length) {
        const pos = workingText.indexOf(rneedle, from);
        if (pos === -1) break;
        const endPos = pos + rneedle.length;
        const leftOk = pos === 0 || !isHbWordChar(workingText.charCodeAt(pos - 1));
        const rightOk = endPos === workingText.length || !isHbWordChar(workingText.charCodeAt(endPos));
        // Tag with the ORIGINAL (logical) needle so the coverage pass recognises the term.
        if (leftOk && rightOk) { candidates.push({ pos, endPos, nLen: needle.length, needle }); revAdded++; }
        from = pos + 1;
      }
    }
    if (revAdded > 0) postDebug(`hl p${pv.pageNumber}: reversed(visual-order) added=${revAdded} (forward=0, hits=${hits.length})`);
  }

  // Tolerant quoted-phrase match (per page). A quoted phrase ("בנימין זאב")
  // only produced a candidate via the literal indexOf above when its words sat
  // with EXACTLY one separator char between them. Real pages put a comma, maqaf,
  // niqud, or an interleaved empty text-node (→ a double space in concat) between
  // the words, so the literal join misses even though the words ARE adjacent
  // (dtSearch matched the phrase, and our per-word indexOf already found each
  // constituent). Re-find each phrase by its constituent WORD SEQUENCE: consecutive
  // whole-word tokens equal to the constituents, in order, separated only by
  // non-word chars. The emitted candidate spans the whole run and carries the
  // phrase needle, so length-desc ordering + the adjacency suppression below treat
  // it exactly like a literal phrase hit (one combined rect; lone words dropped).
  // Handles both word orders and letter-spaced glyphs — see the inner comments.
  // Runs for EVERY term, not just phrases: a lone word is a 1-part sequence, so the
  // same token-run logic recovers a letter-spaced single word ("ב נ י מ י ן" → "בנימין")
  // that the literal indexOf above missed. Lone words already located literally are
  // skipped (exactNeedles) so normal pages stay cheap; phrases always run (they may be
  // separator-split or reverse-ordered anywhere). The proximity filter downstream gates
  // which of these actually paint.
  if (ts.length > 0) {
    const words = [];
    forEachWord(workingText, (s, e) => { words.push({ s, e, w: workingText.substring(s, e) }); });
    const seenSpan = new Set();
    const exactNeedles = new Set();
    for (const c of candidates) { seenSpan.add(c.pos + ':' + c.endPos); exactNeedles.add(c.needle); }
    for (const pt of ts) {
      const isPhrase = pt.stripped.indexOf(' ') !== -1;
      if (!isPhrase && exactNeedles.has(pt.stripped)) continue;  // word already found literally
      const parts = pt.stripped.split(/\s+/).filter(x => x.length > 0);
      if (parts.length === 0) continue;
      let added = 0;
      // Match the constituent WORD SEQUENCE in forward OR reverse token order. pdf.js
      // often extracts an RTL line in VISUAL (left-to-right) order, which reverses the
      // word order: the phrase the user sees as "בנימין זאב" lands in the text layer as
      // the tokens [זאב, בנימין] (each word's chars stay logical — only their order on
      // the line flips). So a forward-only scan misses the very adjacency dtSearch
      // matched. Trying the reversed sequence too recovers it; both orders mean the same
      // two words sit adjacent, which is exactly what the quoted phrase asked for.
      // Each part matches a RUN of consecutive tokens whose concatenation EQUALS the
      // part — not a single token. Letter-spaced typesetting emits one token per glyph
      // (the whole word is a multi-token run); dtSearch reads the content-stream string
      // and matched, but our per-word indexOf (workingText keeps the gaps) and a
      // single-token scan both miss. Anchoring on token boundaries + exact equality
      // stops a run from bleeding across real word boundaries. matchRun returns the
      // token index AFTER the matched run, or -1.
      const matchRun = (ti, target) => {
        let acc = '';
        for (let j = ti; j < words.length; j++) {
          acc += words[j].w;
          if (acc.length === target.length) return acc === target ? j + 1 : -1;
          if (acc.length > target.length) return -1;
        }
        return -1;
      };
      const orders = [parts];
      const rev = parts.slice().reverse();
      if (rev.join(' ') !== parts.join(' ')) orders.push(rev);
      for (const seq of orders) {
        for (let i = 0; i < words.length; i++) {
          let ti = i, ok = true;
          for (const part of seq) {
            const next = matchRun(ti, part);
            if (next === -1) { ok = false; break; }
            ti = next;
          }
          if (!ok) continue;
          const startPos = words[i].s;
          const endPos = words[ti - 1].e;
          const key = startPos + ':' + endPos;
          if (seenSpan.has(key)) continue;                // literal indexOf already added it
          seenSpan.add(key);
          candidates.push({ pos: startPos, endPos, nLen: endPos - startPos, needle: pt.stripped });
          added++;
        }
      }
      if (added > 0) postDebug(`hl p${pv.pageNumber}: token-run ${isPhrase ? 'phrase' : 'word'} "${pt.stripped}" matched ${added}× (pageWords=${words.length})`);
    }
  }

  // Quoted-phrase adjacency preference (per page). When the user quotes
  // "מחמת איסור", the term list carries BOTH the joined phrase AND each
  // constituent word — the C# SearchOrchestrator keeps the words as a fallback
  // for pages where pdf.js can't reconstitute the joined phrase (RTL text items
  // reordered, glyph-only nodes interleaved between the words). But on a page
  // where the joined phrase DID match, also drawing the constituents lights up
  // every lone, non-adjacent "מחמת"/"איסור" across the page — the noise the user
  // reported. So: if a phrase produced ≥1 candidate on THIS page, drop its
  // single-word constituent candidates here; the words survive only on pages the
  // phrase couldn't match (the fallback the comment in QueryBuilder describes).
  //
  // Limitation: the flat term list can't tell a constituent apart from the same
  // word typed bare OUTSIDE the quotes (C# dedups them to one string), so a word
  // typed both ways loses its standalone marks on phrase-matching pages. Rare;
  // accepted for the far more common all-page-noise case.
  if (candidates.length > 0) {
    const phraseConstituents = new Set();
    for (const tEntry of ts) {
      if (tEntry.stripped.indexOf(' ') === -1) continue;       // single word, not a phrase
      let phraseHit = false;
      for (const c of candidates) { if (c.needle === tEntry.stripped) { phraseHit = true; break; } }
      if (!phraseHit) continue;                                 // phrase absent here → keep fallback words
      for (const w of tEntry.stripped.split(/\s+/)) if (w.length > 0) phraseConstituents.add(w);
    }
    if (phraseConstituents.size > 0) {
      const before = candidates.length;
      for (let i = candidates.length - 1; i >= 0; i--) {
        const c = candidates[i];
        if (c.needle.indexOf(' ') === -1 && phraseConstituents.has(c.needle)) candidates.splice(i, 1);
      }
      if (candidates.length !== before)
        postDebug(`hl p${pv.pageNumber}: phrase-adjacency dropped ${before - candidates.length} lone constituent rects (phrase matched)`);
    }
  }

  candidates.sort((a, b) => b.nLen - a.nLen || a.pos - b.pos);
  const _tHi1 = performance.now();

  // Sort hits by pos so the proximity check (next-hit-by-pos) can binary-step.
  // Allocated once; per-candidate inner loop stays O(hits.length) worst-case
  // but in practice early-exits on the first hit within tolerance.
  // (Left unsorted — was already random/dtSearch order — keep simple linear scan.)

  // drawnSet survives ACROSS tolerance iterations: each (pos:endPos) range is
  // drawn at most once via drawConcatMatch — easily the hottest call on this
  // page (creates a DOM Range, calls getClientRects which forces layout, then
  // fillRect per glyph rect). The prior loop reset `marked` + `drawn` between
  // rounds, which re-issued drawConcatMatch for every match already painted on
  // the canvas. We still reset `marked` so longest-first overlap suppression is
  // recomputed each round (a longer term newly within tolerance must still
  // win over a previously-marked shorter sibling).
  //
  // No explicit attempt cap: with the memoized drawnSet + pre-computed
  // candidates + early-exit on drawnTotal >= hits.length, even the worst
  // case (page where no candidate is ever within tolerance) terminates
  // naturally via the `tolerance >= maxTolerance` check below — typically
  // 6-8 iterations. An earlier MAX_ATTEMPTS=5 safety net stopped one
  // iteration BEFORE tolerance could saturate at maxTolerance on
  // 14k-char pages, leaving drew=0 and forcing the wide fallback band
  // even when a single extra round would have produced precise word rects.
  const drawnSet = new Set();
  const drawnNeedles = new Set(); // which MatchedTerms got at least one rect
  let tolerance = baseTolerance;
  let drawnTotal = 0;
  let attempts = 0;
  let firstMatchTop = null; // viewport-coord top of the topmost rect drawn
  while (candidates.length > 0) {
    attempts++;
    marked.fill(0);
    const tolNow = tolerance;
    for (const c of candidates) {
      // Early exit: dtSearch tells us how many matches actually exist on this
      // page; once we've painted that many, drawing more is just noise from
      // a too-wide tolerance and pure wasted DOM-Range work.
      if (drawnTotal >= hits.length) break;
      if (rangeMarked(marked, c.pos, c.endPos)) continue;
      let near = false;
      for (const h of hits) {
        if (Math.abs(c.pos - h.pos) <= tolNow) { near = true; break; }
      }
      if (!near) continue;
      for (let m = c.pos; m < c.endPos; m++) marked[m] = 1;
      const key = c.pos + ':' + c.endPos;
      if (drawnSet.has(key)) continue;
      drawnSet.add(key);
      const cStart = niqudMap ? niqudMap[c.pos] : c.pos;
      const cEnd = niqudMap ? (c.endPos < niqudMap.length ? niqudMap[c.endPos] : concat.length) : c.endPos;
      const top = drawConcatMatch(nodes, cStart, cEnd, overlayRect, ctx, dpr);
      if (top !== null) {
        drawnTotal++;
        drawnNeedles.add(c.needle);
        if (firstMatchTop === null || top < firstMatchTop) firstMatchTop = top;
      }
    }
    if (drawnTotal >= hits.length) break;
    if (tolerance >= maxTolerance) break;
    tolerance = Math.min(tolerance * 2, maxTolerance);
  }

  // Per-term coverage pass. dtSearch reports ONE <loc> per proximity match
  // anchored at the first-word position; the drawn-total >= hits.length early
  // exit above stops as soon as we've drawn that many rects, even when other
  // expanded forms of the search (number-equivalence variants, root variants,
  // Hybur prefixes…) sit right next to them but never get painted. For each
  // needle that has at least one occurrence on this page AND wasn't drawn in
  // the proximity loop, pick the occurrence CLOSEST to one of dtSearch's hit
  // positions and draw it — that's the one that actually belongs to the
  // proximity match the user is reading, not some unrelated occurrence
  // elsewhere on the page. Bound: one extra rect per distinct needle.
  if ((drawnTotal > 0 || hits.length > 0) && candidates.length > 0) {
    const byNeedle = new Map();
    for (const c of candidates) {
      if (drawnNeedles.has(c.needle)) continue;
      let arr = byNeedle.get(c.needle);
      if (!arr) { arr = []; byNeedle.set(c.needle, arr); }
      arr.push(c);
    }
    for (const [, cs] of byNeedle) {
      let best = cs[0];
      let bestDist = Infinity;
      if (hits.length > 0) {
        for (const c of cs) {
          let minDist = Infinity;
          for (const h of hits) {
            const d = Math.abs(c.pos - h.pos);
            if (d < minDist) minDist = d;
          }
          if (minDist < bestDist) { bestDist = minDist; best = c; }
        }
      }
      const key = best.pos + ':' + best.endPos;
      if (drawnSet.has(key)) continue;
      const cStart = niqudMap ? niqudMap[best.pos] : best.pos;
      const cEnd = niqudMap ? (best.endPos < niqudMap.length ? niqudMap[best.endPos] : concat.length) : best.endPos;
      const top = drawConcatMatch(nodes, cStart, cEnd, overlayRect, ctx, dpr);
      if (top !== null) {
        drawnSet.add(key);
        drawnNeedles.add(best.needle);
        drawnTotal++;
        if (firstMatchTop === null || top < firstMatchTop) firstMatchTop = top;
      }
    }
  }

  const _tHi2 = performance.now();
  let drawn = drawnTotal; // mutable: the fallback band path below bumps it to hits.length
  postDebug(`hl p${pv.pageNumber}: nodes=${nodes.length} concat=${concat.length} terms=${ts.length} cand=${candidates.length} hits=${hits.length} attempts=${attempts} tol=${tolerance} drew=${drawn} t_idx=${Math.round(_tHi1 - _tHi0)}ms t_draw=${Math.round(_tHi2 - _tHi1)}ms`);
  // Fallback marker when indexOf found nothing but dtSearch insists there ARE hits
  // here. In EXACT mode (fuzziness=0): common with scanned Hebrew PDFs whose
  // pdf.js text extraction differs from dtSearch's — paint a bold horizontal
  // band at the estimated row so the user can still locate the match visually.
  //
  // In FUZZY mode (fuzziness>0): a 0-candidate result is the JS verification
  // saying "dtSearch matched a 1-char-off variant on this page that I can't
  // textually find here, which means whatever it matched is on a word the user
  // didn't ask for (e.g. מעיר→מעבר/מעשר)". Suppress ANY marker and ALSO drop
  // the page from hitsByPage so the in-book navigation doesn't list it — the
  // user would just click a hit page only to find nothing legitimate.
  // Fallback band fully removed. In FUZZY mode the per-page candidate scan
  // above already loops every word on the page and accepts any within the
  // user's fuzziness Levenshtein budget — so the cases where no candidate is
  // found are the cases where no fuzzy variant is actually on the page.
  // Painting a band there only misleads the user about where the match is
  // (they specifically rejected both the big rectangle and the thin band).
  // In EXACT mode (fuzziness=0), absence of indexOf candidates on a page
  // dtSearch flagged is rare and almost always a text-extraction mismatch the
  // user can chase via the page number alone; no synthetic marker.
  if (drawn === 0 && hits.length > 0) {
    postDebug(`hl p${pv.pageNumber}: no candidates found (dtSearch hits=${hits.length}, fz=${state.fuzziness | 0}, terms=${ts.length})`);
  }
  // Phase-2 live count: tally per-page draw counts and tell the host the running
  // total so the status line ticks up as pdf.js paints highlights ("מסומן 12 מתוך
  // 87"). Per-page totals are recorded so re-renders (zoom / scroll back) don't
  // double-count.
  if (!state.drawnPerPage) state.drawnPerPage = new Map();
  state.drawnPerPage.set(pv.pageNumber, drawn);
  notifyHighlightProgress();
  // Remember the topmost-rect Y in page-relative coordinates so goToPage can scroll
  // to it later, even if it fires AFTER the highlight render — which is the
  // typical case: pages render → renderHighlightsForPage stores firstHitPageY →
  // verifyHits posts verified pages → host navigates → goToPage scrolls to the
  // remembered offset on the target page.
  if (firstMatchTop !== null) {
    const overlayRectNow = pv.highlightLayer.getBoundingClientRect();
    pv.firstHitPageY = firstMatchTop - overlayRectNow.top;
  }
  maybeScrollInitialAnchorToHighlight(pv);
}

/// If `pv` is the page the user just landed on from a search result (still inside
/// the initial-anchor window) AND we know where on that page the first highlight
/// sits, slide the viewer so the highlight is visible. One-shot: clears
/// initialAnchorPending after the scroll so subsequent re-renders (zoom, scroll
/// back) don't yank the user back to the original highlight.
function maybeScrollInitialAnchorToHighlight(pv) {
  if (!pv) { postDebug(`maybeScroll: no pv`); return; }
  if (pv.firstHitPageY === undefined) { postDebug(`maybeScroll p${pv.pageNumber}: no firstHitPageY yet`); return; }
  if (state.initialAnchorPending !== pv.pageNumber) {
    postDebug(`maybeScroll p${pv.pageNumber}: anchor=${state.initialAnchorPending} mismatch (firstHitPageY=${Math.round(pv.firstHitPageY)})`);
    return;
  }
  scrollHighlightIntoView(pv.pageNumber, pv.firstHitPageY);
  state.initialAnchorPending = 0;
}

function scrollHighlightIntoView(pageNumber, pageRelativeY) {
  const c = dom.viewerContainer;
  if (!c) return;
  // Page top + offset within the page → absolute scrollTop, minus a margin so the
  // highlight lands ~25% from the top of the viewer (context above, line below).
  const pTop = pageTopOf(pageNumber);
  const targetScrollTop = pTop + pageRelativeY - c.clientHeight * 0.25;
  postDebug(`scrollHL p${pageNumber}: pageRelY=${Math.round(pageRelativeY)} pageTop=${Math.round(pTop)} clientH=${c.clientHeight} target=${Math.round(targetScrollTop)} from=${Math.round(c.scrollTop)}`);
  if (Math.abs(c.scrollTop - targetScrollTop) < 4) return;
  state._programmaticScroll = true;
  try {
    c.scrollTop = Math.max(0, targetScrollTop);
  } finally {
    requestAnimationFrame(() => { state._programmaticScroll = false; });
  }
}

// Throttle the progress posts so a burst of page renders (scroll, zoom) doesn't
// flood the host with dozens of PropertyChanged events per frame. ~80ms feels
// live to the user without making WPF re-bind the status text on every page.
let _progressThrottleTimer = null;
let _progressLastTotal = -1;
function notifyHighlightProgress() {
  if (!state.drawnPerPage) return;
  if (_progressThrottleTimer !== null) return;
  _progressThrottleTimer = setTimeout(() => {
    _progressThrottleTimer = null;
    let total = 0;
    for (const n of state.drawnPerPage.values()) total += n;
    if (total === _progressLastTotal) return;
    _progressLastTotal = total;
    try {
      window.chrome?.webview?.postMessage(`hb-highlight-progress:${total}`);
    } catch { /* ignore */ }
  }, 80);
}

// Binary search the `nodes` table (sorted by `start` from construction) for the
// node that contains `pos` in [start, end). Returns the index or -1. Was a
// linear walk inside drawConcatMatch which on busy pages (hundreds of spans)
// did `nodes.length × hits` comparisons per page — fine for small pages, but a
// measurable slice of the highlight pass on long ones.
function findNodeIdxAtPos(nodes, pos) {
  let lo = 0, hi = nodes.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >>> 1;
    const n = nodes[mid];
    if (pos < n.start) hi = mid - 1;
    else if (pos >= n.end) lo = mid + 1;
    else return mid;
  }
  return -1;
}

// Convert a [cStart, cEnd) span over the page-level concat string into a DOM
// Range and paint its client rects. cStart is required to land inside a real
// text node — needles always begin with a non-separator character so this is
// guaranteed by the search. cEnd may land exactly on the boundary between a
// node and the fake inter-node separator: we treat that as "end of the
// previous node" so the Range terminates cleanly inside DOM content.
function drawConcatMatch(nodes, cStart, cEnd, overlayRect, ctx, dpr) {
  const startIdx = findNodeIdxAtPos(nodes, cStart);
  if (startIdx < 0) return null;
  const startN = nodes[startIdx];
  const startInfo = { node: startN.node, offset: cStart - startN.start };

  // cEnd may be exactly on a node boundary (cEnd == n.end). Look up the node
  // containing cEnd-1 (the last actual char) and offset from there, capping at
  // the node's length so we stay inside DOM content.
  let endIdx = startIdx;
  if (cEnd > startN.end) {
    endIdx = findNodeIdxAtPos(nodes, cEnd - 1);
    if (endIdx < 0) endIdx = startIdx;
  }
  const endN = nodes[endIdx];
  const endOffset = Math.min(cEnd - endN.start, endN.end - endN.start);
  const endInfo = { node: endN.node, offset: endOffset };
  if (!startInfo || !endInfo) return null;

  const range = document.createRange();
  try {
    range.setStart(startInfo.node, startInfo.offset);
    range.setEnd(endInfo.node, endInfo.offset);
    // getClientRects returns one rect per line the Range covers. For a phrase
    // that wraps from end-of-line to start-of-next, the browser gives us two
    // rects (one per visual line), each tight to the actual rendered glyphs.
    const rects = range.getClientRects();
    let firstTop = null;
    for (const r of rects) {
      if (r.width <= 0 || r.height <= 0) continue;
      drawRect(ctx, dpr, {
        x: r.left - overlayRect.left,
        y: r.top - overlayRect.top,
        w: r.width,
        h: r.height,
      });
      // Capture the topmost rect's viewport-coord top so renderHighlightsForPage
      // can scroll to the first highlight on the page when this is the initial
      // anchor (book just opened on this page from a search result).
      if (firstTop === null || r.top < firstTop) firstTop = r.top;
    }
    return firstTop; // null if no usable rect was drawn
  } finally {
    if (range.detach) range.detach();
  }
}

// Hebrew niqud / cantillation marks (U+0591..U+05C7). dtSearch's matched terms
// come back stripped of these (the indexer drops them at tokenize time), but
// pdf.js renders the raw glyph stream — so for ספרי קודש the term "האריה"
// would never indexOf-match the text node "הָאַריֵה" without stripping first.
const NIQUD_RE = /[֑-ׇ]/g;
function stripNiqud(s) { return s.replace(NIQUD_RE, ''); }

// Word-char predicate for the highlight word-boundary check. Hebrew letters
// (U+05D0..U+05EA), digits, and Latin letters all count; everything else —
// space, punctuation, geresh ׳, gershayim ״, ASCII apostrophe ', niqud —
// is a boundary. Kept in sync with the identical helper in text-viewer.js.
function isHbWordChar(code) {
  if (code >= 0x05D0 && code <= 0x05EA) return true; // Hebrew letters
  if (code >= 0x0030 && code <= 0x0039) return true; // 0-9
  if (code >= 0x0041 && code <= 0x005A) return true; // A-Z
  if (code >= 0x0061 && code <= 0x007A) return true; // a-z
  return false;
}

// Quick scan to skip the per-char strip+map allocation in the common case
// (modern Hebrew text without niqud). Was measured as the single biggest
// per-page win on slow machines back when the highlight loop did this on
// every text item.
function hasNiqud(s) {
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c >= 0x0591 && c <= 0x05C7) return true;
  }
  return false;
}
function stripNiqudWithMap(s) {
  let stripped = '';
  const map = [];
  for (let i = 0; i < s.length; i++) {
    const code = s.charCodeAt(i);
    if (code >= 0x0591 && code <= 0x05C7) continue;
    stripped += s.charAt(i);
    map.push(i);
  }
  return { stripped, map };
}

function rangeMarked(marked, start, end) {
  for (let i = start; i < end; i++) if (marked[i]) return true;
  return false;
}

// Hebrew "letter prefix" set the QueryBuilder's Hybur expansion produces:
// ה, ו, ב, כ, ל, מ, ש, ד as single letters, plus the common 2-letter combos
// (וה / וב / כש). Used by the whole-book scan to accept "בעיר" / "ועיר" /
// "מעיר" when the base term is "עיר" without going through Levenshtein
// (which would also accept unrelated 4-char words). Anchored ^…$ so the
// caller only matches against the prefix slice, not the whole word.
const HEBREW_PREFIX_RE = /^(?:[הובכלמשד]|וה|וב|וכ|ול|ומ|וש|וד|כש|כשה|לכש)$/;

// Banded Levenshtein with early termination — returns either the real distance
// or maxDist+1 if it exceeds the budget. The early-exit on per-row minimum keeps
// the cost bounded; for the in-page fuzzy candidate scan we only care about
// distance ≤ fuzziness (≤ 10), so most candidates abort within a couple rows.
function fuzzyDistance(a, b, maxDist) {
  const al = a.length, bl = b.length;
  if (Math.abs(al - bl) > maxDist) return maxDist + 1;
  if (al === 0) return bl;
  if (bl === 0) return al;
  let prev = new Uint16Array(bl + 1);
  let curr = new Uint16Array(bl + 1);
  for (let j = 0; j <= bl; j++) prev[j] = j;
  for (let i = 1; i <= al; i++) {
    curr[0] = i;
    let rowMin = i;
    const ca = a.charCodeAt(i - 1);
    for (let j = 1; j <= bl; j++) {
      const cost = ca === b.charCodeAt(j - 1) ? 0 : 1;
      const v = Math.min(prev[j] + 1, curr[j - 1] + 1, prev[j - 1] + cost);
      curr[j] = v;
      if (v < rowMin) rowMin = v;
    }
    if (rowMin > maxDist) return maxDist + 1;
    const tmp = prev; prev = curr; curr = tmp;
  }
  return prev[bl];
}

// Walk every Hebrew/digit/ASCII-letter word in `text` and call cb(wordStart, wordEnd).
// Mirrors the isHbWordChar boundary rule used by the indexOf path so the two
// candidate sources can coexist (exact + fuzzy) without contradicting boundaries.
function forEachWord(text, cb) {
  let start = -1;
  const len = text.length;
  for (let i = 0; i <= len; i++) {
    const inWord = i < len && isHbWordChar(text.charCodeAt(i));
    if (inWord) {
      if (start === -1) start = i;
    } else if (start !== -1) {
      cb(start, i);
      start = -1;
    }
  }
}

// Sliding-window cluster: returns the SHORTEST span across positions[i] arrays
// that contains at least one element from every i — or null if no such window
// exists within maxGap. Mirrors dtSearch's w/N proximity operator (with the
// caveat that our gap is in CHAR units, not word units; the caller multiplies
// to convert).
function findProximityCluster(termPositions, maxGap) {
  const n = termPositions.length;
  if (n === 0) return null;
  if (termPositions.some(arr => !arr || arr.length === 0)) return null;
  if (n === 1) return [termPositions[0][0]];

  const all = [];
  for (let i = 0; i < n; i++) {
    for (const p of termPositions[i]) all.push({ pos: p.pos, end: p.end, termIdx: i });
  }
  all.sort((a, b) => a.pos - b.pos);

  const counts = new Array(n).fill(0);
  let covered = 0;
  let left = 0;
  let bestSpan = Infinity, bestLeft = -1, bestRight = -1;
  for (let right = 0; right < all.length; right++) {
    if (counts[all[right].termIdx] === 0) covered++;
    counts[all[right].termIdx]++;
    while (counts[all[left].termIdx] > 1) {
      counts[all[left].termIdx]--;
      left++;
    }
    if (covered === n) {
      const span = all[right].pos - all[left].pos;
      if (span < bestSpan) { bestSpan = span; bestLeft = left; bestRight = right; }
    }
  }
  if (bestSpan > maxGap || bestLeft < 0) return null;

  const out = [];
  const seen = new Set();
  for (let i = bestLeft; i <= bestRight; i++) {
    if (!seen.has(all[i].termIdx)) {
      seen.add(all[i].termIdx);
      out.push(all[i]);
      if (seen.size === n) break;
    }
  }
  return out;
}

// Whole-book fuzzy scan — when fuzziness > 0, walks every page of the open
// document and runs a JS-side proximity search using fuzzy word matching, so
// the viewer doesn't depend on dtSearch's reported pages alone. Pages that
// already have dtSearch hits are skipped; new hit pages are added to
// state.hitsByPage and the visible page (if affected) re-renders.
//
// Each run gets a fresh _fuzzyScanId; any earlier run aborts the moment it
// notices a newer id (or that state.pdfDoc has been replaced) so a fast
// "open book A, then immediately open book B" sequence doesn't leave a stale
// scan polluting B's hitsByPage.
let _fuzzyScanId = 0;
async function runBookFuzzyScan() {
  if ((state.fuzziness | 0) <= 0) return;
  const myDoc = state.pdfDoc;
  if (!myDoc) return;
  if (!state.matchedTerms || state.matchedTerms.length === 0) return;

  const myId = ++_fuzzyScanId;
  const fz = state.fuzziness | 0;
  const maxWordGap = 30;           // mirrors the default MaxProximity in settings
  const maxCharGap = maxWordGap * 8; // ~7-8 chars/Hebrew word on average

  // Collapse the prefix-expanded matchedTerms (עיר → עיר/בעיר/דעיר/...) down to
  // the BASE words the user actually typed. The QueryBuilder fans each base
  // into ~9 prefix variants; treating each as a separate proximity-term group
  // demands every variant be present on the page, which is never true and
  // would zero out the scan. The base-extractor below sorts by length and
  // drops any term whose 1- or 2-char-leading-substring is already a base —
  // i.e. a prefix variant.
  const allStripped = [];
  const seenAll = new Set();
  for (const t of state.matchedTerms) {
    if (!t) continue;
    const s = hasNiqud(t) ? stripNiqud(t) : t;
    if (s.length === 0 || seenAll.has(s)) continue;
    seenAll.add(s);
    allStripped.push(s);
  }
  allStripped.sort((a, b) => a.length - b.length);
  const baseSet = new Set();
  const stripped = [];
  for (const s of allStripped) {
    let isBase = true;
    for (let n = 1; n <= 2 && n < s.length; n++) {
      if (baseSet.has(s.substring(n))) { isBase = false; break; }
    }
    if (isBase) { stripped.push(s); baseSet.add(s); }
  }
  if (stripped.length === 0) return;

  const total = myDoc.numPages;
  postDebug(`fuzzyScan: start id=${myId} pages=${total} terms=${stripped.length} fz=${fz}`);

  let foundPages = 0;
  for (let p = 1; p <= total; p++) {
    if (myId !== _fuzzyScanId || myDoc !== state.pdfDoc) {
      postDebug(`fuzzyScan: id=${myId} cancelled at p=${p}`);
      return;
    }
    // dtSearch is authoritative for the pages it found — only fill in the gaps.
    if (state.hitsByPage.has(p)) continue;

    try {
      const page = await myDoc.getPage(p);
      if (myId !== _fuzzyScanId || myDoc !== state.pdfDoc) return;
      const tc = await page.getTextContent();
      if (myId !== _fuzzyScanId || myDoc !== state.pdfDoc) return;

      const raw = tc.items.map(it => it.str || '').join(' ');
      const text = hasNiqud(raw) ? stripNiqud(raw) : raw;
      if (text.length === 0) continue;

      // Build positions array per BASE term — early-skip if any has zero
      // candidates (proximity cluster requires one of EACH base word).
      //
      // Per-term match mode:
      //   - needle.length >= 5: fuzzy with budget = min(fz, floor(len/5))
      //     (cap relative to length so a 10-char term gets 2 edits, not 10)
      //   - needle.length < 5: EXACT match only. A single-char edit on a 3-4
      //     char Hebrew word catches unrelated noise (מעיר↔מעשר/מעבר), so
      //     short bases must be present verbatim — but with the OR-variants
      //     collapsed, "verbatim" includes any prefix variant the user's
      //     query expanded to (we generate them on the fly here).
      const termPositions = new Array(stripped.length);
      let anyEmpty = false;
      for (let ti = 0; ti < stripped.length; ti++) {
        const needle = stripped[ti];
        const positions = [];
        if (needle.length >= 5) {
          const perTermBudget = Math.min(fz, Math.floor(needle.length / 5));
          forEachWord(text, (s, e) => {
            const wLen = e - s;
            if (Math.abs(wLen - needle.length) > perTermBudget) return;
            const word = text.substring(s, e);
            const d = fuzzyDistance(word, needle, perTermBudget);
            if (d <= perTermBudget) positions.push({ pos: s, end: e });
          });
        } else {
          // Short base: exact match per word, but ACCEPT the prefix-variants
          // the user's Hybur expansion produced ("עיר"→accepts "בעיר","מעיר",
          // …). The match is whole-word: a 3-char base inside a longer word
          // (גדול ↔ ל) would be wrong, so we restrict the leading-prefix slice
          // to the standard 1-2 char Hebrew prefixes (ה,ו,ב,כ,ל,מ,ש,ד plus
          // simple combos).
          forEachWord(text, (s, e) => {
            const wLen = e - s;
            // Whole-word equality.
            if (wLen === needle.length) {
              if (text.substring(s, e) === needle) positions.push({ pos: s, end: e });
              return;
            }
            // 1- or 2-char Hebrew prefix on the same base.
            if (wLen === needle.length + 1 || wLen === needle.length + 2) {
              const tail = text.substring(e - needle.length, e);
              if (tail !== needle) return;
              // Validate the leading prefix is a known Hebrew letter prefix.
              const lead = text.substring(s, e - needle.length);
              if (HEBREW_PREFIX_RE.test(lead)) positions.push({ pos: s, end: e });
            }
          });
        }
        if (positions.length === 0) { anyEmpty = true; break; }
        termPositions[ti] = positions;
      }
      if (anyEmpty) continue;

      const cluster = findProximityCluster(termPositions, maxCharGap);
      if (!cluster) continue;

      // Synthesize dtSearch-shaped hit entries so the existing render path
      // (renderHighlightsForPage → drawConcatMatch) accepts them with no
      // changes — pos + len in niqud-stripped char coords is exactly the
      // schema parseHighlightXml produces.
      const newHits = cluster.map(c => ({ pos: c.pos, len: c.end - c.pos }));
      state.hitsByPage.set(p, newHits);
      foundPages++;
      postDebug(`fuzzyScan: found p=${p} hits=${newHits.length}`);

      // If this page is already on screen + rendered, paint the new hits now
      // instead of waiting for the next scroll/zoom cycle.
      const pv = state.pages[p];
      if (pv && pv.rendered) renderHighlightsForPage(pv);
    } catch (err) {
      postDebug(`fuzzyScan p${p}: ${err?.message || err}`);
    }

    // Yield every few pages so pdf.js render + UI interaction stay snappy.
    if (p % 5 === 0) await new Promise(r => setTimeout(r, 0));
  }

  postDebug(`fuzzyScan: done id=${myId} foundPages=${foundPages}`);
  if (foundPages > 0) notifyHighlightProgress();
}

function rectForItemSubstring(item, relStart, relEnd, viewport) {
  const text = item.str || '';
  if (text.length === 0) return null;
  const tx = item.transform;
  let fontHeight = Math.hypot(tx[2], tx[3]);
  if (!fontHeight) fontHeight = Math.hypot(tx[0], tx[1]);
  const p = viewport.convertToViewportPoint(tx[4], tx[5]);
  const x = p[0], y = p[1];
  const totalW = item.width * viewport.scale;
  const fracStart = relStart / text.length;
  const fracEnd = relEnd / text.length;

  let hx, hw;
  if (item.dir === 'rtl') {
    hx = x - totalW * fracEnd;
    hw = totalW * (fracEnd - fracStart);
  } else {
    hx = x + totalW * fracStart;
    hw = totalW * (fracEnd - fracStart);
  }
  const hh = fontHeight * viewport.scale;
  const hy = y - hh;
  return { x: hx, y: hy, w: hw, h: hh };
}

function drawRect(ctx, dpr, r) {
  if (!r || r.w <= 0 || r.h <= 0) return;
  ctx.fillStyle = state.highlightFill;
  ctx.fillRect(r.x * dpr, r.y * dpr, r.w * dpr, r.h * dpr);
}

// Host bridge — called by PdfJsHost.SetHighlightColorAsync. hex is "#RRGGBB". We
// derive a darker shade for the stroke (multiply each channel by 0.7) so users
// only need to pick one base color and the outline stays visually balanced.
// Re-paints every currently-rendered page so the user sees the change instantly
// without re-opening the book.
// Host bridge — invoked by PdfJsHost.OnPageRailEnabledChanged whenever the user
// toggles the "show page rail" setting AND on every viewer-page load to push the
// persisted preference before the first book opens. On a transition we either
// build the rail (off→on, with a doc already open) or clear it out entirely
// (on→off) so the next loadDocument doesn't pay the per-page DOM cost.
window.HB_setPageRailEnabled = function (enabled) {
  const v = !!enabled;
  if (state.pageRailEnabled === v) return;
  state.pageRailEnabled = v;
  if (!v) {
    // Hide + clear. The :empty CSS rule on #pageRail collapses the strip so the
    // PDF area reclaims the space.
    dom.pageRail.innerHTML = '';
    state._railCurrent = 0;
    clearTimeout(state._viewTimer);
    state._viewTimer = null;
  } else if (state.pdfDoc && state.totalPages > 0) {
    // Build now if a book is already open; otherwise loadDocument's deferred
    // build will handle the next open.
    buildPageRail();
  }
};

// Region-copy target DPI. Pushed at viewer init from the host (PdfJsHost) so a
// fresh book picks up the user's saved preference; also broadcast on the fly
// from SettingsViewModel so changing the value while a book is open takes
// effect on the very next copy without a reload.
window.HB_setRegionCopyDpi = function (dpi) {
  const n = Number(dpi);
  if (!isFinite(n) || n <= 0) return;
  state.regionCopyDpi = Math.max(72, Math.min(600, Math.round(n)));
  postDebug(`HB_setRegionCopyDpi: ${state.regionCopyDpi}`);
};

// dtSearch Fuzziness (0..10). Drives the in-page fuzzy candidate scan in the
// highlight code. Default 0 (off — exact match only) until the host pushes the
// user's setting.
window.HB_setFuzziness = function (fz) {
  const n = Number(fz);
  if (!isFinite(n) || n < 0) return;
  state.fuzziness = Math.max(0, Math.min(10, Math.floor(n)));
  postDebug(`HB_setFuzziness: ${state.fuzziness}`);
};

window.HB_setHighlightColor = function (hex) {
  postDebug(`HB_setHighlightColor: received ${hex}`);
  if (typeof hex !== 'string' || !/^#?[0-9a-fA-F]{6}$/.test(hex)) {
    postDebug(`HB_setHighlightColor: bad hex, ignoring`);
    return;
  }
  const h = hex.startsWith('#') ? hex.slice(1) : hex;
  const r = parseInt(h.slice(0, 2), 16);
  const g = parseInt(h.slice(2, 4), 16);
  const b = parseInt(h.slice(4, 6), 16);
  state.highlightFill = `rgba(${r}, ${g}, ${b}, 0.7)`;
  state.highlightStroke = `rgba(${Math.round(r * 0.7)}, ${Math.round(g * 0.7)}, ${Math.round(b * 0.7)}, 0.5)`;
  let redrawn = 0;
  for (let i = 1; i <= state.totalPages; i++) {
    const pv = state.pages[i];
    if (pv && pv.rendered) { renderHighlightsForPage(pv); redrawn++; }
  }
  postDebug(`HB_setHighlightColor: fill=${state.highlightFill}, redrew ${redrawn} pages`);
};

// ============================================================================
// Find-in-PDF
// ============================================================================

function showFindBar(show) {
  dom.findBar.classList.toggle('hidden', !show);
  dom.btnFind.classList.toggle('toggled', show);
  if (show) {
    dom.findInput.focus();
    dom.findInput.select();
  } else {
    clearFindHighlights();
    state.findState.query = '';
    state.findState.matches = [];
    state.findState.currentIndex = -1;
    dom.findStatus.textContent = '';
  }
}

async function runFind(direction) {
  const q = dom.findInput.value;
  const caseSensitive = dom.findCaseSensitive.checked;
  const fs = state.findState;
  const queryChanged = (q !== fs.query) || (caseSensitive !== fs.caseSensitive);
  fs.query = q;
  fs.caseSensitive = caseSensitive;

  if (q.length === 0) {
    fs.matches = [];
    fs.currentIndex = -1;
    clearFindHighlights();
    dom.findStatus.textContent = '';
    return;
  }

  if (queryChanged) {
    clearFindHighlights();
    fs.matches = [];
    fs.currentIndex = -1;
    fs.perPageScanned.clear();
    await scanAllPagesForFind();
    if (dom.findHighlightAll.checked) drawAllFindMatches();
  }

  if (fs.matches.length === 0) {
    dom.findStatus.textContent = 'אין תוצאות';
    return;
  }

  if (direction === 'next') {
    fs.currentIndex = (fs.currentIndex + 1) % fs.matches.length;
  } else if (direction === 'prev') {
    fs.currentIndex = (fs.currentIndex - 1 + fs.matches.length) % fs.matches.length;
  } else if (fs.currentIndex < 0) {
    fs.currentIndex = 0;
  }

  const m = fs.matches[fs.currentIndex];
  dom.findStatus.textContent = `${fs.currentIndex + 1} מתוך ${fs.matches.length}`;
  goToPage(m.pageNumber);
  // Re-draw to reflect "current" highlight.
  if (dom.findHighlightAll.checked) {
    drawAllFindMatches();
  } else {
    clearFindHighlights();
    drawFindMatch(m, true);
  }
}

async function scanAllPagesForFind() {
  const fs = state.findState;
  const q = fs.caseSensitive ? fs.query : fs.query.toLowerCase();
  const matches = [];
  // Scan in current-page-first order so the user sees results from where they are.
  const order = pageOrderFromCurrent();
  for (const p of order) {
    const pv = state.pages[p];
    if (!pv) continue;
    const tc = await pv.ensureTextContent();
    const items = tc.items;
    const pageMatches = [];
    for (let i = 0; i < items.length; i++) {
      let text = items[i].str || '';
      if (!fs.caseSensitive) text = text.toLowerCase();
      let from = 0;
      while (from < text.length) {
        const pos = text.indexOf(q, from);
        if (pos === -1) break;
        pageMatches.push({
          pageNumber: p,
          itemIndex: i,
          start: pos,
          end: pos + q.length,
        });
        from = pos + 1;
      }
    }
    pv.findMatches = pageMatches;
    matches.push(...pageMatches);
  }
  // Sort by page so navigation is monotone.
  matches.sort((a, b) => a.pageNumber - b.pageNumber || a.itemIndex - b.itemIndex || a.start - b.start);
  fs.matches = matches;
  fs.currentIndex = -1;
}

function pageOrderFromCurrent() {
  const order = [];
  for (let i = state.currentPage; i <= state.totalPages; i++) order.push(i);
  for (let i = 1; i < state.currentPage; i++) order.push(i);
  return order;
}

function drawAllFindMatches() {
  for (let p = 1; p <= state.totalPages; p++) {
    const pv = state.pages[p]; if (!pv) continue;
    if (pv.rendered) renderFindMatchesForPage(pv);
  }
}

function drawFindMatch(match, isCurrent) {
  const pv = state.pages[match.pageNumber];
  if (!pv || !pv.rendered) return;
  pv.ensureTextContent().then(tc => {
    const item = tc.items[match.itemIndex];
    if (!item) return;
    const r = rectForItemSubstring(item, match.start, match.end, pv.viewport);
    if (!r) return;
    const dpr = pv.highlightLayer.width / parseFloat(pv.highlightLayer.style.width);
    const ctx = pv.highlightLayer.getContext('2d');
    ctx.fillStyle = isCurrent ? 'rgba(255, 102, 0, 0.7)' : 'rgba(255, 213, 0, 0.55)';
    ctx.fillRect(r.x * dpr, r.y * dpr, r.w * dpr, r.h * dpr);
  });
}

function renderFindMatchesForPage(pv) {
  if (!dom.findHighlightAll.checked) return;
  if (!pv.findMatches || pv.findMatches.length === 0) return;
  pv.ensureTextContent().then(tc => {
    const dpr = pv.highlightLayer.width / parseFloat(pv.highlightLayer.style.width);
    const ctx = pv.highlightLayer.getContext('2d');
    const fs = state.findState;
    const currentMatch = fs.currentIndex >= 0 ? fs.matches[fs.currentIndex] : null;
    for (const m of pv.findMatches) {
      const item = tc.items[m.itemIndex];
      if (!item) continue;
      const r = rectForItemSubstring(item, m.start, m.end, pv.viewport);
      if (!r) continue;
      const isCurrent = currentMatch &&
        currentMatch.pageNumber === m.pageNumber &&
        currentMatch.itemIndex === m.itemIndex &&
        currentMatch.start === m.start;
      ctx.fillStyle = isCurrent ? 'rgba(255, 102, 0, 0.7)' : 'rgba(255, 213, 0, 0.55)';
      ctx.fillRect(r.x * dpr, r.y * dpr, r.w * dpr, r.h * dpr);
    }
  });
}

function clearFindHighlights() {
  // Re-run the dtSearch hit overlay (which clears + redraws). Find-only match
  // rectangles get wiped because they share the same canvas.
  for (let i = 1; i <= state.totalPages; i++) {
    const pv = state.pages[i];
    if (pv && pv.rendered) renderHighlightsForPage(pv);
  }
}

// ============================================================================
// Outline panel
// ============================================================================

async function populateOutline() {
  // The outline pane shows up to TWO sources, in order:
  //   1. The C#-supplied book TOC (state.bookToc, from the catalog DB), set via
  //      HB_setBookToc on every book open — the editable source of truth.
  //   2. The PDF's OWN embedded outline (pdfDoc.getOutline()) — bookmarks baked into the
  //      file. Most scanned hebrewbooks PDFs have none, but modern / Otzraya PDFs do, and
  //      it's a useful hierarchical TOC straight from the file.
  // When both exist we render the catalog entries first, then a "מתוך הקובץ" divider, then
  // the embedded outline. We write into #outlineList, NOT #outlinePane — the pane also
  // contains the sticky #outlineActions header (the "+" / pencil buttons) which must NOT
  // be cleared.
  dom.outlineList.innerHTML = '';
  const toc = Array.isArray(state.bookToc) ? state.bookToc : [];

  // Pull the embedded outline up-front so the empty-state decision accounts for it.
  // getOutline() needs pdfDoc; populateOutline runs once on document load (pdfDoc ready)
  // and again from HB_setBookToc (which may fire before load — then this is just skipped
  // and the post-load call picks it up).
  let pdfOutline = null;
  try {
    if (state.pdfDoc) pdfOutline = await state.pdfDoc.getOutline();
  } catch (e) { postDebug('getOutline failed: ' + e); }
  const hasPdfOutline = Array.isArray(pdfOutline) && pdfOutline.length > 0;

  if (toc.length === 0 && !hasPdfOutline) {
    const p = document.createElement('div');
    p.className = 'outline-empty';
    p.textContent = 'אין תוכן עניינים — לחץ ＋ להוסיף את העמוד הנוכחי';
    dom.outlineList.appendChild(p);
    return;
  }

  // 1. Catalog TOC ({Title, Page, Level}) → a COLLAPSIBLE tree built from the flat Level values
  //    (each entry nests under the nearest preceding entry with a smaller Level).
  if (toc.length > 0) {
    const ul = document.createElement('ul');
    ul.className = 'outline-list';
    renderTocTree(buildTocTree(toc), ul);
    dom.outlineList.appendChild(ul);
  }

  // 2. PDF's embedded outline (hierarchical, via buildOutlineLevel → resolveDestination).
  //    A "מתוך הקובץ" divider separates it from the catalog entries — only when both exist.
  if (hasPdfOutline) {
    if (toc.length > 0) {
      const sep = document.createElement('div');
      sep.className = 'outline-source-divider';
      sep.textContent = 'מתוך הקובץ';
      dom.outlineList.appendChild(sep);
    }
    const ul = document.createElement('ul');
    ul.className = 'outline-list';
    buildOutlineLevel(pdfOutline, ul);
    dom.outlineList.appendChild(ul);
  }
}

// Build a hierarchy from the FLAT catalog TOC: each entry nests under the nearest preceding entry
// with a smaller Level (0 = top). Returns an array of { entry, children } root nodes.
function buildTocTree(flat) {
  const roots = [];
  const stack = [];   // [{ node, level }], deepest last
  for (const entry of flat) {
    const level = Math.max(0, entry.Level | 0);
    const node = { entry, children: [] };
    while (stack.length && stack[stack.length - 1].level >= level) stack.pop();
    if (stack.length) stack[stack.length - 1].node.children.push(node);
    else roots.push(node);
    stack.push({ node, level });
  }
  return roots;
}

// Render the catalog-TOC tree with ▾/▸ collapse toggles on parents. Click a row → goToPage;
// click a toggle → expand/collapse that subtree. Children sit in a nested <ul> (indented inline).
function renderTocTree(nodes, ul) {
  for (const node of nodes) {
    const li = document.createElement('li');
    const row = document.createElement('div');
    row.className = 'outline-item';
    const hasKids = node.children.length > 0;

    const toggle = document.createElement('span');
    toggle.textContent = hasKids ? '▾' : '';
    toggle.style.display = 'inline-block';
    toggle.style.width = '16px';
    toggle.style.cursor = hasKids ? 'pointer' : 'default';
    toggle.style.userSelect = 'none';
    toggle.style.opacity = '0.65';
    row.appendChild(toggle);

    const label = document.createElement('span');
    label.textContent = node.entry.Title || '(ללא כותרת)';
    row.appendChild(label);

    row.title = `${label.textContent} — עמ' ${node.entry.Page}`;
    row.style.cursor = 'pointer';
    row.addEventListener('click', () => goToPage(node.entry.Page));
    li.appendChild(row);

    if (hasKids) {
      const childUl = document.createElement('ul');
      childUl.className = 'outline-list';
      childUl.style.paddingInlineStart = '16px';   // indent the nested level (START = right under RTL)
      renderTocTree(node.children, childUl);
      li.appendChild(childUl);
      toggle.addEventListener('click', (ev) => {
        ev.stopPropagation();                       // don't also jump to the page
        const collapsed = childUl.style.display === 'none';
        childUl.style.display = collapsed ? '' : 'none';
        toggle.textContent = collapsed ? '▾' : '▸';
      });
    }
    ul.appendChild(li);
  }
}

function buildOutlineLevel(items, ul) {
  for (const item of items) {
    const li = document.createElement('li');
    const label = document.createElement('div');
    label.className = 'outline-item';
    if (item.bold) label.style.fontWeight = '600';
    if (item.italic) label.style.fontStyle = 'italic';
    label.textContent = item.title || '(ללא כותרת)';
    label.title = label.textContent;
    label.addEventListener('click', async () => {
      const tgt = await resolveDestination(item.dest);
      if (tgt) goToPage(tgt.pageNumber);
    });
    li.appendChild(label);
    if (item.items && item.items.length > 0) {
      const child = document.createElement('ul');
      buildOutlineLevel(item.items, child);
      li.appendChild(child);
    }
    ul.appendChild(li);
  }
}

// ============================================================================
// Thumbnails panel
// ============================================================================

const thumbsState = {
  built: false,
  observer: null,
};

// Throw away the previous document's thumbnails so the next populateThumbs()
// rebuilds from scratch. Without this, thumbsState.built stays true across a
// book switch and populateThumbs() early-returns — leaving the OLD book's
// thumbnails visible. Called from loadDocument for every document.
function resetThumbs() {
  thumbsState.built = false;
  if (thumbsState.observer) { thumbsState.observer.disconnect(); thumbsState.observer = null; }
  dom.thumbsPane.innerHTML = '';
}

async function populateThumbs() {
  if (!state.pdfDoc) return;
  if (thumbsState.built) return;
  thumbsState.built = true;
  dom.thumbsPane.innerHTML = '';

  for (let i = 1; i <= state.totalPages; i++) {
    const wrap = document.createElement('div');
    wrap.className = 'thumb';
    wrap.dataset.pageNumber = String(i);
    const c = document.createElement('canvas');
    const num = document.createElement('span');
    num.className = 'thumb-num';
    num.textContent = String(i);
    wrap.appendChild(c);
    wrap.appendChild(num);
    wrap.addEventListener('click', () => goToPage(i));
    dom.thumbsPane.appendChild(wrap);
  }
  updateThumbsCurrent();

  // Lazy-render thumbs as they scroll into view.
  if (thumbsState.observer) thumbsState.observer.disconnect();
  thumbsState.observer = new IntersectionObserver(async entries => {
    for (const ent of entries) {
      if (!ent.isIntersecting) continue;
      const wrap = ent.target;
      const p = parseInt(wrap.dataset.pageNumber, 10);
      const c = wrap.querySelector('canvas');
      if (c.dataset.rendered === '1') continue;
      try {
        const pdfPage = await state.pdfDoc.getPage(p);
        const baseVp = pdfPage.getViewport({ scale: 1, rotation: effRotation(pdfPage) });
        const TARGET_W = 140;
        const scale = TARGET_W / baseVp.width;
        const vp = pdfPage.getViewport({ scale, rotation: effRotation(pdfPage) });
        const dpr = window.devicePixelRatio || 1;
        c.width = Math.floor(vp.width * dpr);
        c.height = Math.floor(vp.height * dpr);
        c.style.width = vp.width + 'px';
        c.style.height = vp.height + 'px';
        const transform = dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null;
        await pdfPage.render({ canvasContext: c.getContext('2d'), viewport: vp, transform }).promise;
        c.dataset.rendered = '1';
      } catch (e) { /* skip */ }
    }
  }, { root: dom.thumbsPane, rootMargin: '200px' });

  for (const wrap of dom.thumbsPane.querySelectorAll('.thumb'))
    thumbsState.observer.observe(wrap);
}

function updateThumbsCurrent() {
  const wraps = dom.thumbsPane.querySelectorAll('.thumb');
  for (const w of wraps) {
    const p = parseInt(w.dataset.pageNumber, 10);
    w.classList.toggle('current', p === state.currentPage);
  }
  const cur = dom.thumbsPane.querySelector('.thumb.current');
  if (cur) cur.scrollIntoView({ block: 'nearest' });
}

// ============================================================================
// Compact page rail (left side, RTL)
// ============================================================================

// Rebuild the rail for the just-loaded document. One lightweight row per page —
// no canvases, so even a few-thousand-page book is cheap. Called from
// loadDocument once totalPages is known; viewedPages is reset there first.
//
// Short-circuits when the user disabled the rail in settings: nothing is built
// AND any old DOM is cleared, so a large book pays zero cost for this feature.
function buildPageRail() {
  state._railCurrent = 0;
  clearTimeout(state._viewTimer);
  state._viewTimer = null;
  state._railClickNoRecenter = false;
  dom.pageRail.innerHTML = '';
  if (!state.pageRailEnabled) return;
  if (!state.pdfDoc || state.totalPages <= 0) return;
  const frag = document.createDocumentFragment();
  for (let i = 1; i <= state.totalPages; i++) {
    const row = document.createElement('div');
    row.className = 'rail-pg';
    row.dataset.page = String(i);
    row.textContent = String(i);
    // goToPage handles scroll + currentPage + updateRail. The flag tells updateRail
    // NOT to re-centre the rail for THIS change so the clicked row stays put under
    // the cursor instead of jumping away.
    row.addEventListener('click', () => { state._railClickNoRecenter = true; goToPage(i); });
    frag.appendChild(row);
  }
  dom.pageRail.appendChild(frag);
  updateRail();
}

// Move the current/viewed highlight to state.currentPage and keep that row in
// view inside the rail (its own scroll only — never the document). O(1): we only
// touch the previously-current row and the new one, so this stays cheap even when
// fired on every scroll tick of a huge book.
function updateRail() {
  if (!state.pageRailEnabled) return;
  const rail = dom.pageRail;
  if (!rail.firstChild) return;
  const p = state.currentPage;
  if (p === state._railCurrent) return;

  // A rail-row click sets this so the rail does NOT re-centre under the cursor
  // (jarring — the row would run away from where the user just clicked). Scroll-
  // and nav-driven changes still re-centre so the rail tracks the document.
  const recenter = !state._railClickNoRecenter;
  state._railClickNoRecenter = false;

  const prev = state._railCurrent && rail.children[state._railCurrent - 1];
  if (prev) prev.classList.remove('current');   // keeps .viewed only if it earned the dwell

  const row = rail.children[p - 1];
  if (!row) return;
  row.classList.add('current');
  // Already earned a dot on an earlier visit → show it again immediately.
  if (state.viewedPages.has(p)) row.classList.add('viewed');
  state._railCurrent = p;

  // "Viewed" requires actually dwelling on the page — at least 2s. Flicking past
  // pages (fast scroll) or clicking straight through cancels before the dot lands.
  // Single timer: only one page is "current" at a time.
  clearTimeout(state._viewTimer);
  state._viewTimer = setTimeout(() => {
    if (state.currentPage !== p) return;        // left before the dwell elapsed
    state.viewedPages.add(p);
    const r = rail.children[p - 1];
    if (r) r.classList.add('viewed');
  }, 2000);

  // Centre the row within the rail only — adjust the rail's own scrollTop, never
  // scrollIntoView (which could nudge the document/viewer container).
  if (recenter)
    rail.scrollTop = row.offsetTop - rail.clientHeight / 2 + row.offsetHeight / 2;
}

// ============================================================================
// Sidebar / tabs
// ============================================================================

function toggleSidebar(force) {
  const showing = force === undefined ? dom.sidebar.classList.contains('hidden') : !!force;
  dom.sidebar.classList.toggle('hidden', !showing);
  dom.btnSidebar.classList.toggle('toggled', showing);
  if (showing) populateThumbs();
}

function showSidebarTab(name) {
  const isThumbs = name === 'thumbs';
  dom.tabThumbs.classList.toggle('active', isThumbs);
  dom.tabOutline.classList.toggle('active', !isThumbs);
  dom.thumbsPane.classList.toggle('active', isThumbs);
  dom.outlinePane.classList.toggle('active', !isThumbs);
}

// ============================================================================
// Rotation
// ============================================================================

function rotate() {
  state.rotation = (state.rotation + 90) % 360;
  applyZoom(); // resize + re-render with new rotation
}

// Copy whatever the user has selected in the textLayer to the clipboard. Called
// from the toolbar button and the right-click menu. Uses navigator.clipboard when
// available, falls back to execCommand('copy').
async function copySelection() {
  if (state.protectMode) return;            // kiosk: no clipboard writes
  const sel = window.getSelection();
  const text = sel ? sel.toString() : '';
  if (!text) return;
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
    } else {
      document.execCommand('copy');
    }
  } catch (e) {
    try { document.execCommand('copy'); } catch (e2) {}
  }
}

// ---- Right-click context menu ----
function showContextMenu(x, y) {
  const m = dom.ctxMenu;
  // Disable Copy + Find when there's no text selected — the actions wouldn't do
  // anything useful.
  const sel = window.getSelection()?.toString() || '';
  dom.ctxCopy.disabled = !sel;
  dom.ctxFind.disabled = !sel;
  m.classList.remove('hidden');
  // First show, then measure so we can clamp to viewport.
  m.style.left = '0'; m.style.top = '0';
  const rect = m.getBoundingClientRect();
  const maxX = window.innerWidth - rect.width - 4;
  const maxY = window.innerHeight - rect.height - 4;
  m.style.left = Math.max(0, Math.min(x, maxX)) + 'px';
  m.style.top = Math.max(0, Math.min(y, maxY)) + 'px';
}
function hideContextMenu() {
  dom.ctxMenu.classList.add('hidden');
}
function handleContextAction(action) {
  hideContextMenu();
  if (action === 'copy') copySelection();
  else if (action === 'print') requestPrint();
  else if (action === 'find') {
    const sel = window.getSelection()?.toString().trim() || '';
    showFindBar(true);
    if (sel) {
      dom.findInput.value = sel;
      runFind('next');
    }
  }
}

// Hand the print request to the WPF host. The host owns the user-facing flow now:
// it shows the standard Windows print dialog (printer, page range, copies) and only
// THEN calls back into JS via HB_renderPageForPrint to render the specific pages
// the user asked for — no upfront full-book render.
function requestPrint() {
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage('hb-print');
    }
  } catch (e) { /* host gone */ }
}

// Helpers exposed to the WPF host's print pipeline.
window.HB_getPageCount = () => state.totalPages || 0;

// The currently visible page (1-based). Used by the host to default the print dialog's
// page-range to wherever the user is reading instead of resetting them to page 1.
window.HB_getCurrentPage = () => state.currentPage || 1;

// Opened by the WPF host on Ctrl+F (browser accelerator keys are disabled, so the
// host owns the shortcut and forwards it here to our in-book find bar).
window.HB_showFind = () => { try { showFindBar(true); } catch (e) {} };

// Render a single page for printing and return it as a base64 PNG data URL.
// Called by the host once per page in the user-selected range, AFTER the print
// dialog has been confirmed — so we only ever rasterize the pages that are about
// to hit the printer. dpi is the requested rasterization DPI (PDF user space is
// 1/72 inch, so scale = dpi/72).
window.HB_renderPageForPrint = async (pageNum, dpi) => {
  if (!state.pdfDoc) return null;
  if (pageNum < 1 || pageNum > state.totalPages) return null;
  try {
    const pdfPage = await state.pdfDoc.getPage(pageNum);
    const scale = (dpi || 200) / 72;
    const vp = pdfPage.getViewport({ scale, rotation: effRotation(pdfPage) });
    const c = document.createElement('canvas');
    c.width = Math.floor(vp.width);
    c.height = Math.floor(vp.height);
    const ctx = c.getContext('2d');
    await pdfPage.render({ canvasContext: ctx, viewport: vp }).promise;
    const url = c.toDataURL('image/png');
    c.width = 0; c.height = 0;
    return url;
  } catch (e) {
    postDebug(`HB_renderPageForPrint p${pageNum}: ${e?.message || e}`);
    return null;
  }
};

// ============================================================================
// Loading / error UI
// ============================================================================

function showLoading(show, text) {
  dom.loadingOverlay.classList.toggle('hidden', !show);
  if (show) {
    const lt = dom.loadingOverlay.querySelector('div:last-child');
    if (lt) lt.textContent = text || 'טוען…';
  }
}
function showError(msg) {
  dom.errorText.textContent = msg;
  dom.errorOverlay.classList.remove('hidden');
}
function hideError() {
  dom.errorOverlay.classList.add('hidden');
}

// ============================================================================
// Page-rendered event (kept for parity with legacy controller hooks)
// ============================================================================

function emitPageRendered(pageNumber) {
  // Internal hook — surfaces a custom event so external scripts could listen if needed.
  document.dispatchEvent(new CustomEvent('hb-pagerendered', { detail: { pageNumber } }));
}

// ============================================================================
// WPF host bridge
// ============================================================================

window.HB_loadPdf = function (url, page, xml, terms) {
  state._loadT0 = performance.now();
  state.highlightXml = xml || '';
  state.matchedTerms = Array.isArray(terms) ? terms.slice() : [];
  state.hitsByPage = parseHighlightXml(state.highlightXml);
  // New document → new hit generation (see HB_setHighlightXml for why).
  state.hitsGen = (state.hitsGen || 0) + 1;
  // Reset per-page draw tally so the live highlight-progress counter starts
  // from zero for this book.
  state.drawnPerPage = new Map();
  _progressLastTotal = -1;
  notifyHighlightProgress();
  // New document → drop the previous book's catalog TOC. Unlike hitsByPage/matchedTerms
  // (repopulated above from this call's args), the TOC arrives in a SEPARATE host call
  // (HB_setBookToc). Without clearing it here, loadDocument's populateOutline() at load
  // would render the prior book's outline, and it would linger if the incoming book has
  // none. The host re-sends the real TOC right after this; until then the pane is empty.
  state.bookToc = [];
  postDebug(`TIMING: HB_loadPdf entry page=${page} hitsPages=${state.hitsByPage.size} terms=${state.matchedTerms.length}`);
  loadDocument(url, page > 0 ? page : 1);
};

window.HB_goToPage = function (page) {
  // Re-arm the highlight-anchor so navigating to a hit page from the chip strip
  // / Next-Prev / search-result selection re-runs the "scroll to first rect on
  // this page" logic, the same way the initial book open does. Internal nav
  // (keyboard PageDown, Home/End) goes straight to goToPage and skips this.
  state.initialAnchorPending = page;
  goToPage(page);
};

/// Returns the current page number (1-based). Used by the host's quick-add flow
/// to capture which page the user is on at the moment they click the button.
window.HB_getCurrentPage = () => state.currentPage || 1;

/// Replaces the in-memory book TOC and re-renders the outline pane. `entries` is an
/// array of {Title, Page} objects — same shape as TocEntry on the C# side. Pass an
/// empty array to clear.
window.HB_setBookToc = function (entries) {
  state.bookToc = Array.isArray(entries) ? entries.slice() : [];
  populateOutline();
};

window.HB_clearDocument = function () {
  if (state.pdfDoc) {
    for (let i = 1; i <= state.totalPages; i++) {
      const pv = state.pages[i];
      if (pv) pv.unrender();
    }
    state.pages = [];
    dom.viewer.innerHTML = '';
    resetThumbs();
    try { state.pdfDoc.destroy(); } catch (e) {}
    state.pdfDoc = null;
    state.totalPages = 0;
    dom.pageCount.textContent = '—';
    dom.pageInput.value = '1';
    // Same V8 cleanup as on book-switch path — fires when the host hides the
    // viewer (e.g. user navigates away from the catalog) so the now-idle
    // WebView2 doesn't hold onto the prior book's raster pages.
    try { if (typeof window.gc === 'function') window.gc(); } catch (e) {}
  }
  showLoading(true);
};

// ============================================================================
// Helpers
// ============================================================================

function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

// ============================================================================
// Wire up event handlers
// ============================================================================

function bindEvents() {
  dom.btnPrev.addEventListener('click', () => goToPage(state.currentPage - 1));
  dom.btnNext.addEventListener('click', () => goToPage(state.currentPage + 1));
  dom.pageInput.addEventListener('change', () => {
    const p = parseInt(dom.pageInput.value, 10);
    if (!isNaN(p)) goToPage(p);
    else dom.pageInput.value = String(state.currentPage);
  });
  dom.pageInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { dom.pageInput.blur(); }
  });

  dom.btnZoomIn.addEventListener('click', zoomIn);
  dom.btnZoomOut.addEventListener('click', zoomOut);
  dom.zoomSelect.addEventListener('change', () => {
    const v = dom.zoomSelect.value;
    if (v === 'auto' || v === 'page-fit' || v === 'page-width' || v === 'page-height') {
      // Find any page with a loaded pdfPage to use as the dimension donor. Was
      // hard-coded to state.pages[1], but loadDocument only pre-loads pdfPage for
      // the user's TARGET page (e.g. opening a hit on page 173 means page 1 has
      // no pdfPage yet) — so the old check silently bailed and the fit-mode
      // dropdown felt broken on any book opened past page 1. Same fallback chain
      // applyZoom uses: current page first, then scan for any loaded page.
      let donor = state.pages[state.currentPage];
      if (!donor || !donor.pdfPage) {
        donor = null;
        for (let i = 1; i <= state.totalPages; i++) {
          const pv = state.pages[i];
          if (pv && pv.pdfPage) { donor = pv; break; }
        }
      }
      if (donor && donor.pdfPage) {
        const vp = donor.pdfPage.getViewport({ scale: 1, rotation: effRotation(donor.pdfPage) });
        const containerW = dom.viewerContainer.clientWidth - 24;
        const containerH = dom.viewerContainer.clientHeight - 24;
        let s = state.scale;
        if (v === 'page-width') s = containerW / vp.width;
        else if (v === 'page-height') s = containerH / vp.height;
        else if (v === 'page-fit') s = Math.min(containerW / vp.width, containerH / vp.height);
        else s = Math.min(1.5, containerW / vp.width);
        setZoom(s, v);
      }
    } else {
      setZoom(parseFloat(v), null);
    }
  });

  dom.btnFind.addEventListener('click', () => showFindBar(dom.findBar.classList.contains('hidden')));
  dom.btnFindClose.addEventListener('click', () => showFindBar(false));
  dom.findInput.addEventListener('input', () => runFind(null));
  dom.findInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') runFind(e.shiftKey ? 'prev' : 'next');
    else if (e.key === 'Escape') showFindBar(false);
  });
  dom.findNext.addEventListener('click', () => runFind('next'));
  dom.findPrev.addEventListener('click', () => runFind('prev'));
  dom.findHighlightAll.addEventListener('change', () => {
    if (dom.findHighlightAll.checked) drawAllFindMatches();
    else clearFindHighlights();
  });
  dom.findCaseSensitive.addEventListener('change', () => runFind(null));

  dom.btnRotate.addEventListener('click', rotate);
  dom.btnPrint.addEventListener('click', requestPrint);
  dom.btnCopy.addEventListener('click', copySelection);
  dom.btnSidebar.addEventListener('click', () => toggleSidebar());

  // Right-click context menu inside the PDF area. Only intercepts contextmenu on
  // the viewer container so the chrome (toolbar, sidebar) keeps the browser default.
  // In protect-mode (kiosk) we still preventDefault — that swallows the WebView2
  // native menu — but never show our own. End result: right-click does nothing.
  dom.viewerContainer.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (state.protectMode) return;
    showContextMenu(e.clientX, e.clientY);
  });
  dom.ctxMenu.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-action]');
    if (!btn || btn.disabled) return;
    handleContextAction(btn.dataset.action);
  });
  // Hide the context menu on any outside click / scroll / window blur.
  document.addEventListener('mousedown', (e) => {
    if (!dom.ctxMenu.contains(e.target)) hideContextMenu();
  });
  window.addEventListener('blur', hideContextMenu);
  dom.viewerContainer.addEventListener('scroll', hideContextMenu);

  // Ctrl+wheel zoom — replace the browser's default CSS-zoom behaviour (which just
  // scales the rendered canvas → blurry). We re-render the page at the new scale
  // for crisp output, just like the +/- toolbar buttons.
  dom.viewerContainer.addEventListener('wheel', (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    if (e.deltaY < 0) zoomIn();
    else if (e.deltaY > 0) zoomOut();
  }, { passive: false });
  dom.tabThumbs.addEventListener('click', () => showSidebarTab('thumbs'));
  dom.tabOutline.addEventListener('click', () => showSidebarTab('outline'));

  // TOC sidebar action buttons. The host (C#) handles the actual quick-add / editor —
  // we just notify it; that way the WPF Window dialogs (TocQuickAddDialog,
  // TocEditorWindow) live in the correct place and the JS doesn't need to know about
  // the catalog row ID at all.
  function postHostMsg(msg) {
    try { window.chrome?.webview?.postMessage(msg); } catch { /* ignore */ }
  }
  dom.btnTocAdd?.addEventListener('click', () => postHostMsg('hb-toc-add'));
  dom.btnTocEdit?.addEventListener('click', () => postHostMsg('hb-toc-edit'));

  dom.viewerContainer.addEventListener('scroll', () => {
    // A scroll that didn't come from our own scrollToPage / rebuild restore is a
    // user gesture — they want to leave the initial anchor, so let go of it.
    if (!state._programmaticScroll && state.initialAnchorPending) {
      state.initialAnchorPending = 0;
    }
    updateCurrentPageFromScroll();
    scheduleRender();
  });

  window.addEventListener('resize', () => {
    if (state.fitMode) {
      // Re-resolve fit-based scale on window resize.
      dom.zoomSelect.dispatchEvent(new Event('change'));
    }
    scheduleRender();
  });

  window.addEventListener('keydown', (e) => {
    // Protect-mode (kiosk): hard-block DevTools shortcuts up front, before any other
    // handler. AreBrowserAcceleratorKeysEnabled=false on the host side already kills
    // most of these, but a defense-in-depth swallow here covers cases where the host
    // setting fails to apply (older WebView2 SDK, race during init, ...).
    if (state.protectMode) {
      if (e.key === 'F12') { e.preventDefault(); return; }
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'I' || e.key === 'i' || e.key === 'J' || e.key === 'j' || e.key === 'C' || e.key === 'c')) {
        e.preventDefault(); return;
      }
    }
    // Don't intercept while typing in an input.
    const ae = document.activeElement;
    const inField = ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA' || ae.isContentEditable);
    // Immersive reading: F11 toggles, Esc exits. Posted to the host (PdfJsHost),
    // which flips the shared MainViewModel flag so the side nav AND the page's own
    // chrome collapse/restore. Handled here (not only in WPF) because while the user
    // is reading, focus is inside this WebView2 and it swallows the keys before WPF
    // can see them. F11 is checked before the in-field guard (no text meaning).
    // Region-copy mode is modal — ESC cancels it before any other ESC handler
    // (immersive-exit, find-bar) gets a shot, and other shortcuts are silently
    // ignored so a stray Ctrl+F mid-drag doesn't flip the user into Find.
    if (regionState.active) {
      if (e.key === 'Escape') { e.preventDefault(); exitRegionMode(); return; }
      return;
    }
    if (e.key === 'F11') { e.preventDefault(); try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage('hb-immersive-toggle'); } catch (_) {} return; }
    if (inField && !(e.ctrlKey && (e.key === 'f' || e.key === 'F'))) return;
    if (e.key === 'Escape') { try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage('hb-immersive-exit'); } catch (_) {} return; }

    // --- Global keyboard shortcuts. The pinned WebView2 SDK can't let the WPF
    // host intercept keys while the PDF has focus, so we capture here and post
    // 'hb-shortcut:<token>' -> PdfJsHost.ShortcutRequested -> the active surface's
    // HandleShortcut (same path as the WPF chrome via ShortcutKeyMap). Result nav
    // is Ctrl+Down/Alt+Right = next, Ctrl+Up/Alt+Left = prev — PHYSICAL keys,
    // intentionally NOT the RTL bare-arrow page-turn handled further below. ---
    const _sc = (t) => { e.preventDefault(); try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage('hb-shortcut:' + t); } catch (_) {} };
    // Ctrl+Shift+Arrow = next/prev BOOK (jumps SelectedRow across the search
    // results list). Checked BEFORE the plain Ctrl+Arrow rule so Shift wins.
    if (e.ctrlKey && e.shiftKey && e.key === 'ArrowDown') { _sc('next-book'); return; }
    if (e.ctrlKey && e.shiftKey && e.key === 'ArrowUp')   { _sc('prev-book'); return; }
    if ((e.ctrlKey && e.key === 'ArrowDown') || (e.altKey && e.key === 'ArrowRight')) { _sc('next'); return; }
    if ((e.ctrlKey && e.key === 'ArrowUp') || (e.altKey && e.key === 'ArrowLeft')) { _sc('prev'); return; }
    // Ctrl+[ / Ctrl+] = browser-style back/forward in the per-VM history (parity
    // with the WPF chrome, which handles them when focus is NOT in the WebView).
    if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key === '[') { _sc('nav-back'); return; }
    if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key === ']') { _sc('nav-forward'); return; }
    // Open-book tabs (main window) — forwarded so they work while a book has focus.
    if (e.ctrlKey && e.shiftKey && e.key === 'Tab') { _sc('tab-prev'); return; }
    if (e.ctrlKey && !e.shiftKey && e.key === 'Tab') { _sc('tab-next'); return; }
    if (e.ctrlKey && e.key === 'PageDown') { _sc('tab-next'); return; }
    if (e.ctrlKey && e.key === 'PageUp')   { _sc('tab-prev'); return; }
    if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === 'w' || e.key === 'W')) { _sc('tab-close'); return; }
    if (e.altKey && (e.key === 'c' || e.key === 'C')) { _sc('goto-search'); return; }
    if (e.altKey && (e.key === 'k' || e.key === 'K')) { _sc('goto-catalog'); return; }
    if (e.ctrlKey && (e.key === 'e' || e.key === 'E')) { _sc('focus-main'); return; }
    if (!e.ctrlKey && !e.altKey && e.key === '/') { _sc('focus-main'); return; }
    // Ctrl+F (and bare F) now focus the WPF in-book search box instead of the
    // pdf.js find bar — per the chosen mapping. pdf.js find stays reachable from
    // the toolbar 🔍 button.
    if (e.ctrlKey && (e.key === 'f' || e.key === 'F')) { _sc('focus-inbook'); return; }
    if (!e.ctrlKey && !e.altKey && (e.key === 'f' || e.key === 'F')) { _sc('focus-inbook'); return; }
    // Ctrl+P is intentionally NOT bound: the pinned WebView2 SDK can't stop
    // Chromium's own Ctrl+P print, and binding here too would open two dialogs.
    // The ⎙ toolbar button (and its context-menu item) is the entry to our print.
    if (e.ctrlKey && (e.key === '+' || e.key === '=')) { e.preventDefault(); zoomIn(); return; }
    if (e.ctrlKey && (e.key === '-' || e.key === '_')) { e.preventDefault(); zoomOut(); return; }
    if (e.ctrlKey && e.key === '0') { e.preventDefault(); setZoom(1, null); return; }

    if (e.key === 'PageDown' || e.key === ' ') { e.preventDefault(); goToPage(state.currentPage + 1); return; }
    if (e.key === 'PageUp') { e.preventDefault(); goToPage(state.currentPage - 1); return; }
    if (e.key === 'Home') { e.preventDefault(); goToPage(1); return; }
    if (e.key === 'End') { e.preventDefault(); goToPage(state.totalPages); return; }
    if (e.key === 'ArrowLeft') {
      // RTL UI: ArrowLeft = next page (logical forward in Hebrew reading order).
      e.preventDefault(); goToPage(state.currentPage + 1); return;
    }
    if (e.key === 'ArrowRight') {
      e.preventDefault(); goToPage(state.currentPage - 1); return;
    }
  });

  // Auto-hide chrome: while the WPF in-book toolbar is collapsed the WebView2 fills
  // that space, so WPF never sees the mouse near the top edge. Report top-band
  // enter/leave to the host (PdfJsHost.ChromeRevealRequested) so it can reveal /
  // re-hide the toolbar. Posted ONLY on state change (not every move). The host
  // ignores these unless the user enabled auto-hide, so it's always safe to emit.
  let _hbChromeNear = false;
  const _HB_CHROME_BAND = 64;
  window.addEventListener('pointermove', (e) => {
    // Suppress the reveal signal while the user is drawing a region-copy
    // selection — otherwise the chrome bar pops up the moment the pointer
    // crosses the top band and distracts mid-drag.
    if (regionState.active) return;
    const near = e.clientY <= _HB_CHROME_BAND;
    if (near === _hbChromeNear) return;
    _hbChromeNear = near;
    try {
      window.chrome && window.chrome.webview &&
        window.chrome.webview.postMessage(near ? 'hb-chrome-show' : 'hb-chrome-hide');
    } catch (_) { /* host gone */ }
  }, { passive: true });

  // Region-copy: pointer events inside the scrollable viewer (capture phase so
  // we beat the text layer's selection handling). Pointer-up listens on window
  // so a release outside the container still completes the selection.
  dom.viewerContainer.addEventListener('pointerdown', onRegionPointerDown, true);
  dom.viewerContainer.addEventListener('pointermove', onRegionPointerMove, true);
  window.addEventListener('pointerup', onRegionPointerUp, true);

  // (The viewer-area contextmenu is intercepted above to show our custom menu.
  // Other regions keep the browser default — useful for the toolbar/sidebar.)
}

// ============================================================================
// Region copy — drag a rectangle over the PDF (possibly crossing page breaks),
// composite the rasterised page canvases into one image, copy as PNG to the
// system clipboard. Triggered by the WPF chrome's "העתק אזור" button via the
// HB_startRegionCopy bridge. Cross-page selections fill in missing pages by
// rendering them off-screen at the current scale, so a selection that drops
// into a not-yet-rendered page (beyond the lazy-render buffer) still works.
// ============================================================================

const regionState = {
  active: false,
  mode: 'image',  // 'image' | 'text' — what the captured rect is copied AS
  dragging: false,
  startX: 0,      // viewer-relative coords (i.e. inside #viewer's content rect)
  startY: 0,
  selDiv: null,
  toast: null,
  toastTimer: null,
  edgeScrollTimer: null,
  edgeScrollDy: 0,
};

function enterRegionMode(mode) {
  if (regionState.active) return;
  regionState.active = true;
  regionState.mode = mode === 'text' ? 'text' : 'image';
  document.body.classList.add('region-mode');
  const hint = regionState.mode === 'text'
    ? 'סמן אזור להעתקה כטקסט (Esc לביטול)'
    : 'סמן אזור להעתקה כתמונה (Esc לביטול)';
  showRegionToast(hint, 0);
}

function exitRegionMode() {
  if (!regionState.active) return;
  regionState.active = false;
  regionState.dragging = false;
  document.body.classList.remove('region-mode');
  if (regionState.selDiv) { regionState.selDiv.remove(); regionState.selDiv = null; }
  stopEdgeScroll();
  hideRegionToast();
}

function showRegionToast(msg, autoHideMs) {
  if (!regionState.toast) {
    const t = document.createElement('div');
    t.className = 'region-toast';
    document.body.appendChild(t);
    regionState.toast = t;
  }
  if (regionState.toastTimer) { clearTimeout(regionState.toastTimer); regionState.toastTimer = null; }
  regionState.toast.textContent = msg;
  regionState.toast.classList.remove('hidden');
  if (autoHideMs && autoHideMs > 0) {
    regionState.toastTimer = setTimeout(() => {
      if (regionState.toast) regionState.toast.classList.add('hidden');
      regionState.toastTimer = null;
    }, autoHideMs);
  }
}
function hideRegionToast() {
  if (regionState.toast) regionState.toast.classList.add('hidden');
}

function viewerCoordsFromClient(clientX, clientY) {
  const vr = dom.viewer.getBoundingClientRect();
  return { x: clientX - vr.left, y: clientY - vr.top };
}

function onRegionPointerDown(e) {
  if (!regionState.active) return;
  if (e.button !== 0) return; // left-button only
  e.preventDefault();
  e.stopPropagation();
  const p = viewerCoordsFromClient(e.clientX, e.clientY);
  regionState.startX = p.x;
  regionState.startY = p.y;
  regionState.dragging = true;
  const d = document.createElement('div');
  d.className = 'region-sel';
  d.style.left = p.x + 'px';
  d.style.top = p.y + 'px';
  d.style.width = '0px';
  d.style.height = '0px';
  dom.viewer.appendChild(d);
  regionState.selDiv = d;
  try { dom.viewerContainer.setPointerCapture(e.pointerId); } catch (_) {}
}

function onRegionPointerMove(e) {
  if (!regionState.active || !regionState.dragging) return;
  e.preventDefault();
  const p = viewerCoordsFromClient(e.clientX, e.clientY);
  const x = Math.min(regionState.startX, p.x);
  const y = Math.min(regionState.startY, p.y);
  const w = Math.abs(p.x - regionState.startX);
  const h = Math.abs(p.y - regionState.startY);
  const d = regionState.selDiv;
  if (d) {
    d.style.left = x + 'px'; d.style.top = y + 'px';
    d.style.width = w + 'px'; d.style.height = h + 'px';
  }
  // Auto-scroll when the cursor approaches the top/bottom of the visible
  // viewport — lets the user extend a selection across more than one screenful
  // by simply dragging into the edge.
  const cr = dom.viewerContainer.getBoundingClientRect();
  const margin = 40;
  let dy = 0;
  if (e.clientY < cr.top + margin) dy = -Math.max(6, (cr.top + margin - e.clientY) * 0.6);
  else if (e.clientY > cr.bottom - margin) dy = Math.max(6, (e.clientY - (cr.bottom - margin)) * 0.6);
  if (dy !== 0) startEdgeScroll(dy); else stopEdgeScroll();
}

function startEdgeScroll(dy) {
  regionState.edgeScrollDy = dy;
  if (regionState.edgeScrollTimer) return;
  regionState.edgeScrollTimer = setInterval(() => {
    if (!regionState.dragging) { stopEdgeScroll(); return; }
    dom.viewerContainer.scrollTop += regionState.edgeScrollDy;
  }, 16);
}
function stopEdgeScroll() {
  if (regionState.edgeScrollTimer) {
    clearInterval(regionState.edgeScrollTimer);
    regionState.edgeScrollTimer = null;
  }
  regionState.edgeScrollDy = 0;
}

async function onRegionPointerUp(e) {
  if (!regionState.active || !regionState.dragging) return;
  regionState.dragging = false;
  stopEdgeScroll();
  try { dom.viewerContainer.releasePointerCapture(e.pointerId); } catch (_) {}
  const d = regionState.selDiv;
  if (!d) { exitRegionMode(); return; }
  const sel = {
    x: parseFloat(d.style.left) || 0,
    y: parseFloat(d.style.top) || 0,
    w: parseFloat(d.style.width) || 0,
    h: parseFloat(d.style.height) || 0,
  };
  // Pull the dashed div BEFORE the composite so it doesn't show up in the
  // captured image (it's overlaid on #viewer at high z-index).
  d.remove(); regionState.selDiv = null;
  if (sel.w < 8 || sel.h < 8) {
    showRegionToast('האזור קטן מדי — נסה שוב', 1200);
    setTimeout(exitRegionMode, 1200);
    return;
  }
  showRegionToast('מעתיק…', 0);
  try {
    if (regionState.mode === 'text') {
      const txt = await captureRegionText(sel);
      if (!txt || txt.trim().length === 0) throw new Error('לא נמצא טקסט באזור');
      await writeTextToClipboard(txt);
      showRegionToast('הטקסט הועתק ל-Clipboard', 1500);
    } else {
      const blob = await captureRegionBlob(sel);
      if (!blob) throw new Error('empty blob');
      await writeImageBlobToClipboard(blob);
      showRegionToast('הועתק ל-Clipboard', 1500);
    }
  } catch (err) {
    postDebug('region-copy failed: ' + (err?.message || err));
    showRegionToast('שגיאה בהעתקה: ' + (err?.message || err), 2500);
  }
  setTimeout(exitRegionMode, 1500);
}

async function writeImageBlobToClipboard(blob) {
  if (!navigator.clipboard || !window.ClipboardItem) {
    throw new Error('Clipboard API not available');
  }
  await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
}

async function writeTextToClipboard(text) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }
  // Old-school fallback for environments without the async clipboard API.
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.style.position = 'fixed';
  ta.style.left = '-9999px';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.select();
  try { document.execCommand('copy'); } finally { document.body.removeChild(ta); }
}

// Text-mode capture: walk the textLayer spans of every page intersecting the
// selection and collect the ones whose CENTRE falls inside the rect. Centre-
// inside (rather than any-overlap) is what makes the column-extraction use case
// work — a span sitting right on the column gutter has its centre on one side
// or the other, so we cleanly pick the correct column instead of grabbing
// neighbour-column tails. Document/DOM order = pdf.js reading order, which is
// already RTL within Hebrew lines, so we just keep the order we see and let
// vertical gaps insert newlines. Pages without a rendered textLayer (outside
// the lazy-render window) fall back to pdf.js getTextContent + the same
// rectForItemSubstring math the highlight pass already trusts.
async function captureRegionText(sel) {
  const viewerRect = dom.viewer.getBoundingClientRect();
  const selL = sel.x + viewerRect.left;
  const selT = sel.y + viewerRect.top;
  const selR = selL + sel.w;
  const selB = selT + sel.h;

  // Iterate pages top-to-bottom so multi-page selections come out in reading
  // order rather than DOM-attach order.
  const pageDivs = Array.from(dom.viewer.querySelectorAll('.page'))
    .sort((a, b) => a.offsetTop - b.offsetTop);

  const parts = [];
  let prevBottom = -Infinity;
  let prevPage = -1;

  const appendLineBreakIfNeeded = (top, lineHeight) => {
    if (parts.length === 0) return;
    if (top > prevBottom + Math.max(2, lineHeight * 0.3)) parts.push('\n');
    else if (!/\s$/.test(parts[parts.length - 1])) parts.push(' ');
  };

  for (const pd of pageDivs) {
    const pdRect = pd.getBoundingClientRect();
    if (pdRect.right < selL || pdRect.left > selR || pdRect.bottom < selT || pdRect.top > selB) continue;

    const pageNumber = parseInt(pd.dataset.pageNumber, 10);
    if (prevPage > 0 && pageNumber !== prevPage && parts.length > 0) {
      parts.push('\n');           // page break → newline
      prevBottom = -Infinity;
    }
    prevPage = pageNumber;

    const textLayer = pd.querySelector('.textLayer');
    if (textLayer && textLayer.children.length > 0) {
      for (const span of textLayer.querySelectorAll('span')) {
        const str = span.textContent || '';
        if (str.length === 0) continue;
        // pdf.js's TextLayer wraps every text item in a span; the helper
        // `.endOfContent` div doesn't have text, but skip empties defensively.
        const r = span.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) continue;
        const cx = (r.left + r.right) / 2;
        const cy = (r.top + r.bottom) / 2;
        if (cx < selL || cx > selR || cy < selT || cy > selB) continue;
        appendLineBreakIfNeeded(r.top, r.height);
        parts.push(str);
        prevBottom = r.bottom;
      }
    } else {
      // Page not in the lazy-render window — fall back to pdf.js text content
      // + the same per-item rect math the highlight pass uses.
      if (!state.pdfDoc) continue;
      const pv = state.pages[pageNumber];
      const pdfPage = pv?.pdfPage || await state.pdfDoc.getPage(pageNumber);
      const vp = pdfPage.getViewport({ scale: state.scale, rotation: effRotation(pdfPage) });
      const tc = await pdfPage.getTextContent();
      for (const item of tc.items) {
        const str = item.str || '';
        if (str.length === 0) continue;
        const r = rectForItemSubstring(item, 0, str.length, vp);
        if (!r || r.w <= 0 || r.h <= 0) continue;
        // r is in page CSS coords; lift into client coords via the page rect.
        const cl = pdRect.left + r.x;
        const ct = pdRect.top + r.y;
        const cx = cl + r.w / 2;
        const cy = ct + r.h / 2;
        if (cx < selL || cx > selR || cy < selT || cy > selB) continue;
        appendLineBreakIfNeeded(ct, r.h);
        parts.push(str);
        prevBottom = ct + r.h;
      }
    }
  }

  return parts.join('');
}

// Walks every page DIV that intersects the selection rect (in #viewer
// coords) and composites the overlapping pixels onto an offscreen canvas.
//
// Resolution model: the capture is ALWAYS re-rendered offscreen at the user's
// configured target DPI (state.regionCopyDpi, default 200) regardless of the
// current zoom — so a copy taken at fit-page is just as crisp as one taken at
// 300%. The on-screen liveCanvas is intentionally bypassed because it's
// rendered at state.scale × devicePixelRatio (typically 72–150 DPI), which
// would blur when pasted into Word at print size. We pay 1–2s of pdf.js
// rasterisation per page in exchange for predictable print-quality output.
async function captureRegionBlob(sel) {
  // CSS-pixel → output-pixel scaling: targetDpi / 72 gives the offscreen render
  // scale; dividing by state.scale converts the CSS-coord selection into the
  // matching number of output pixels (page CSS width = pageWidthPt × state.scale).
  const targetDpi = Math.max(72, Math.min(600, state.regionCopyDpi || 200));
  const captureScale = targetDpi / 72;
  const r = captureScale / Math.max(0.01, state.scale);

  const out = document.createElement('canvas');
  out.width = Math.max(1, Math.floor(sel.w * r));
  out.height = Math.max(1, Math.floor(sel.h * r));
  const ctx = out.getContext('2d');
  // White background: page gutters and any un-renderable area are left
  // white rather than transparent so the pasted image looks like a clean
  // book extract (transparent gaps look broken in Word / OneNote).
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, out.width, out.height);

  const selRight = sel.x + sel.w;
  const selBot = sel.y + sel.h;
  // The selection rect is in #viewer's post-transform CSS coord space (the same
  // space the dashed overlay was rendered in). Page divs use `left: 50%;
  // transform: translateX(-50%)` to centre, so `pd.offsetLeft` is the
  // PRE-transform reference (half the viewer width) — NOT where the page
  // actually sits on screen. Use getBoundingClientRect relative to #viewer to
  // get the post-transform rect, otherwise we'd composite half a page off the
  // captured image. Y has no transform so this also gives correct top/height.
  const viewerRect = dom.viewer.getBoundingClientRect();
  const pageDivs = Array.from(dom.viewer.querySelectorAll('.page'));
  for (const pd of pageDivs) {
    const pdRect = pd.getBoundingClientRect();
    const pLeft = pdRect.left - viewerRect.left;
    const pTop = pdRect.top - viewerRect.top;
    const pW = pdRect.width;
    const pH = pdRect.height;
    const pRight = pLeft + pW;
    const pBot = pTop + pH;
    const ix = Math.max(sel.x, pLeft);
    const iy = Math.max(sel.y, pTop);
    const ixe = Math.min(selRight, pRight);
    const iye = Math.min(selBot, pBot);
    if (ixe <= ix || iye <= iy) continue;
    const dx = (ix - sel.x) * r;
    const dy = (iy - sel.y) * r;
    const dw = (ixe - ix) * r;
    const dh = (iye - iy) * r;
    const pageNumber = parseInt(pd.dataset.pageNumber, 10);
    if (!(pageNumber > 0)) continue;
    try {
      const tmp = await renderPageOffscreen(pageNumber, captureScale);
      if (tmp) {
        // Page offscreen canvas is at captureScale; page CSS width is at
        // state.scale, so the source ratio collapses to the same r.
        const sxRatio = tmp.width / pW;
        const syRatio = tmp.height / pH;
        ctx.drawImage(tmp,
          (ix - pLeft) * sxRatio, (iy - pTop) * syRatio,
          (ixe - ix) * sxRatio,   (iye - iy) * syRatio,
          dx, dy, dw, dh);
        // Release the offscreen canvas pixel buffer right away — at 300 DPI
        // these can hit ~40 MB per page, and we may iterate over several.
        tmp.width = 0; tmp.height = 0;
      }
    } catch (err) { postDebug('region p' + pageNumber + ': ' + (err?.message || err)); }
  }
  return new Promise((resolve) => out.toBlob((b) => resolve(b), 'image/png'));
}

// `scale` is the pdf.js viewport scale to render at. Pass undefined to fall
// back to the on-screen scale (state.scale × devicePixelRatio) — used by
// callers that just want a freshly-rasterised page at the current zoom.
async function renderPageOffscreen(pageNumber, scale) {
  if (!state.pdfDoc) return null;
  const pdfPage = await state.pdfDoc.getPage(pageNumber);
  const renderScale = (typeof scale === 'number' && scale > 0)
    ? scale
    : state.scale * (window.devicePixelRatio || 1);
  const vp = pdfPage.getViewport({ scale: renderScale, rotation: effRotation(pdfPage) });
  const c = document.createElement('canvas');
  c.width = Math.floor(vp.width);
  c.height = Math.floor(vp.height);
  const ctx = c.getContext('2d');
  await pdfPage.render({ canvasContext: ctx, viewport: vp }).promise;
  return c;
}

// Host bridge — invoked by PdfJsHost.StartRegionCopyAsync when the user
// clicks the chrome button. Toggling on/off via a single message keeps the
// host-side state minimal (no need to track "am I in region mode" in C#).
window.HB_startRegionCopy = function () {
  try {
    if (regionState.active) exitRegionMode();
    else enterRegionMode('image');
  } catch (e) { postDebug('HB_startRegionCopy: ' + (e?.message || e)); }
};

// Same as above but the drag captures the TEXT inside the rect (great for
// extracting one column out of a multi-column page where the regular text
// selection would grab across-the-line and pick up the other column too).
window.HB_startRegionCopyText = function () {
  try {
    if (regionState.active) exitRegionMode();
    else enterRegionMode('text');
  } catch (e) { postDebug('HB_startRegionCopyText: ' + (e?.message || e)); }
};

bindEvents();
showSidebarTab('thumbs');

// Tell the host we're ready to receive HB_loadPdf calls. The webMessage is what the
// WPF host actually waits on (NavigationCompleted fires before <script type="module">
// finishes executing); the DOM event is for any other in-page listeners.
window.dispatchEvent(new CustomEvent('hb-viewer-ready'));
try {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage('hb-viewer-ready');
  }
} catch (e) { /* host gone */ }
