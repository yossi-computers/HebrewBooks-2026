// dtSearch hit overlay for the vendored pdf.js viewer.
//
// Strategy:
//   1) Parse dtSearch's MakePdfWebHighlightFile XML — we get one (page, pos, len) per hit
//      where `pos` is a char offset in dtSearch's text extraction (units=characters).
//   2) On each pagerendered, walk pdfjs's getTextContent.items[]:
//        a) Build a list of all *rendered* occurrences of any matched term on the page.
//           These are stable anchors — they're the actual rendered words pdfjs displays.
//        b) For each dtSearch hit, snap to the nearest term occurrence (by min |charDelta|).
//           dtSearch and pdfjs disagree by a few chars on whitespace/EOL/ligatures, so the
//           raw `pos` drifts, but the relative ordering of matches stays consistent — so the
//           nearest-occurrence is reliably the right one.
//        c) Fall back to the raw (pos, len) substring slice when no terms are known or no
//           occurrence is within reach.
//   3) Draw rect-per-substring on a canvas overlay sized to match pdfjs's page canvas.
//
// Contract with the WPF host:
//   window.HB_setHighlightXml(xml, terms)
//      — replace the active hit set + matched-term list. Empty xml clears highlights.

(function () {
    'use strict';

    if (window.__hbOverlayInstalled) return;
    window.__hbOverlayInstalled = true;

    var hitsByPage = new Map();   // 1-based pageNumber → [{pos, len}, ...]
    var matchedTerms = [];        // ["ברכת", "הברכת", ...]

    // ---- XML parsing ------------------------------------------------------

    function parseHighlightXml(xml) {
        var map = new Map();
        if (!xml || typeof xml !== 'string') return map;
        var re = /<loc\b([^>]*)>/gi;
        var m;
        while ((m = re.exec(xml)) !== null) {
            var attrs = m[1];
            var pg = readIntAttr(attrs, 'pg');
            var pos = readIntAttr(attrs, 'pos');
            var len = readIntAttr(attrs, 'len');
            if (pg === null || pos === null || len === null) continue;
            var page = pg + 1;
            var arr = map.get(page);
            if (!arr) { arr = []; map.set(page, arr); }
            arr.push({ pos: pos, len: len });
        }
        return map;
    }

    function readIntAttr(attrs, name) {
        // dtSearch's XML uses unquoted values: pg=10 pos=512. Tolerate both forms.
        var re = new RegExp('\\b' + name + '\\s*=\\s*"?([#\\w-]+)"?', 'i');
        var m = re.exec(attrs);
        if (!m) return null;
        var n = parseInt(m[1], 10);
        return isNaN(n) ? null : n;
    }

    // ---- pdfjs DOM accessors ---------------------------------------------

    function pageDivFor(pageNumber) {
        return document.querySelector('.page[data-page-number="' + pageNumber + '"]');
    }

    function pageViewFor(pageNumber) {
        var app = window.PDFViewerApplication;
        if (!app || !app.pdfViewer) return null;
        return app.pdfViewer.getPageView(pageNumber - 1);
    }

    // ---- highlight rendering ---------------------------------------------

    function ensureCanvasFor(pageDiv, pageNumber, pdfCanvas, viewport) {
        var canvas = pageDiv.querySelector('.hb-highlight-layer[data-page="' + pageNumber + '"]');
        if (!canvas) {
            canvas = document.createElement('canvas');
            canvas.className = 'hb-highlight-layer';
            canvas.setAttribute('data-page', String(pageNumber));
            canvas.style.position = 'absolute';
            canvas.style.left = '0';
            canvas.style.top = '0';
            canvas.style.pointerEvents = 'none';
            // Just below textLayer so text selection still works on top of highlights.
            canvas.style.zIndex = '2';
            pageDiv.appendChild(canvas);
        }
        if (pdfCanvas) {
            canvas.width = pdfCanvas.width;
            canvas.height = pdfCanvas.height;
            canvas.style.width = pdfCanvas.style.width || (pdfCanvas.clientWidth + 'px');
            canvas.style.height = pdfCanvas.style.height || (pdfCanvas.clientHeight + 'px');
        } else {
            canvas.width = viewport.width;
            canvas.height = viewport.height;
        }
        return canvas;
    }

    /**
     * Compute the device-pixel ratio between the canvas's pixel buffer and its CSS box,
     * so rects expressed in CSS pixels (from viewport.convertToViewportPoint) draw on the
     * correct device pixels.
     */
    function dprOf(canvas) {
        var cssWidth = parseFloat(canvas.style.width);
        if (!cssWidth || isNaN(cssWidth)) return 1;
        return canvas.width / cssWidth;
    }

    /**
     * Compute the (x, y, w, h) viewport rect (CSS px) for the substring [relStart, relEnd)
     * inside `item` from getTextContent. Width is approximated by `item.width * fracOfText` —
     * accurate for monospaced runs, slight error for proportional fonts but stable enough
     * for highlighting.
     */
    function rectForItemSubstring(item, relStart, relEnd, viewport) {
        var text = item.str || '';
        if (text.length === 0) return null;
        var tx = item.transform;
        var fontHeight = Math.hypot(tx[2], tx[3]);
        if (!fontHeight) fontHeight = Math.hypot(tx[0], tx[1]);
        var p = viewport.convertToViewportPoint(tx[4], tx[5]);
        var x = p[0], y = p[1];

        var totalW = item.width * viewport.scale;
        var fracStart = relStart / text.length;
        var fracEnd = relEnd / text.length;

        var hx, hw;
        if (item.dir === 'rtl') {
            // RTL: tx[4] is the right edge of the run; advance leftward.
            hx = x - totalW * fracEnd;
            hw = totalW * (fracEnd - fracStart);
        } else {
            hx = x + totalW * fracStart;
            hw = totalW * (fracEnd - fracStart);
        }
        var hh = fontHeight * viewport.scale;
        var hy = y - hh;
        return { x: hx, y: hy, w: hw, h: hh };
    }

    function drawRect(ctx, dpr, r) {
        if (!r || r.w <= 0 || r.h <= 0) return;
        // Bright, saturated yellow with a thin darker border so the highlight reads on
        // both white and lightly-tinted backgrounds.
        ctx.fillStyle = 'rgba(255, 213, 0, 0.55)';
        ctx.fillRect(r.x * dpr, r.y * dpr, r.w * dpr, r.h * dpr);
        ctx.strokeStyle = 'rgba(180, 120, 0, 0.85)';
        ctx.lineWidth = Math.max(1, dpr);
        ctx.strokeRect(r.x * dpr, r.y * dpr, r.w * dpr, r.h * dpr);
    }

    /**
     * Find every occurrence of any term in `terms` inside the concatenated page text.
     * Returns one entry per occurrence, paired with the item-relative positions so we can
     * draw the highlight on the right glyph run.
     *
     * If a term spans multiple items (rare — only if pdfjs split a single visual word),
     * we currently take the first item only, which is good-enough — the user will see a
     * highlight on the start of the word.
     */
    function findTermOccurrences(items, terms) {
        var occurrences = [];
        if (!terms || terms.length === 0) return occurrences;

        // Normalize terms once (drop empty / whitespace-only).
        var ts = [];
        for (var i = 0; i < terms.length; i++) {
            var t = terms[i];
            if (t && t.length > 0) ts.push(t);
        }
        if (ts.length === 0) return occurrences;

        var charIndex = 0;
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var text = item.str || '';
            if (text.length > 0) {
                for (var j = 0; j < ts.length; j++) {
                    var term = ts[j];
                    var from = 0;
                    while (from < text.length) {
                        var pos = text.indexOf(term, from);
                        if (pos === -1) break;
                        occurrences.push({
                            charIndex: charIndex + pos,
                            length: term.length,
                            item: item,
                            relStart: pos,
                            relEnd: pos + term.length,
                        });
                        from = pos + 1;
                    }
                }
            }
            charIndex += text.length;
        }
        // Order by char position so the snap step is O(N + H log N).
        occurrences.sort(function (a, b) { return a.charIndex - b.charIndex; });
        return occurrences;
    }

    /**
     * Greedy snap: each hit picks the closest unclaimed term occurrence. When the dtSearch
     * char-pos drifts a little, this still pairs hits with the right rendered words because
     * relative ordering is consistent.
     */
    function snapHitsToOccurrences(hits, occurrences) {
        var assignments = []; // { hit, occurrence | null }
        var used = new Array(occurrences.length);
        for (var i = 0; i < hits.length; i++) {
            var h = hits[i];
            var bestIdx = -1, bestDist = Infinity;
            for (var k = 0; k < occurrences.length; k++) {
                if (used[k]) continue;
                var d = Math.abs(occurrences[k].charIndex - h.pos);
                if (d < bestDist) { bestDist = d; bestIdx = k; }
            }
            if (bestIdx >= 0) {
                used[bestIdx] = true;
                assignments.push({ hit: h, occurrence: occurrences[bestIdx] });
            } else {
                assignments.push({ hit: h, occurrence: null });
            }
        }
        return assignments;
    }

    function drawSubstringFallback(items, hit, viewport, ctx, dpr) {
        // Walk items with running charIndex; draw rect for every item-substring that
        // overlaps [hit.pos, hit.pos+hit.len). Used when we can't snap to a term.
        var charIndex = 0;
        var hlEnd = hit.pos + hit.len;
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var text = item.str || '';
            var itemStart = charIndex;
            var itemEnd = charIndex + text.length;
            charIndex = itemEnd;
            if (hlEnd <= itemStart || hit.pos >= itemEnd) continue;
            var relStart = Math.max(hit.pos - itemStart, 0);
            var relEnd = Math.min(hlEnd - itemStart, text.length);
            if (relStart >= relEnd) continue;
            drawRect(ctx, dpr, rectForItemSubstring(item, relStart, relEnd, viewport));
        }
    }

    function renderPageHighlights(pageNumber) {
        var hits = hitsByPage.get(pageNumber);
        if (!hits || hits.length === 0) {
            var existing = document.querySelector('.hb-highlight-layer[data-page="' + pageNumber + '"]');
            if (existing) existing.remove();
            return;
        }

        var pageDiv = pageDivFor(pageNumber);
        if (!pageDiv) return;
        var view = pageViewFor(pageNumber);
        if (!view || !view.viewport || !view.pdfPage) return;
        var viewport = view.viewport;

        var pdfCanvas = pageDiv.querySelector('.canvasWrapper > canvas');
        var canvas = ensureCanvasFor(pageDiv, pageNumber, pdfCanvas, viewport);
        var ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        var dpr = dprOf(canvas);

        view.pdfPage.getTextContent().then(function (tc) {
            var items = tc.items;
            var occurrences = findTermOccurrences(items, matchedTerms);

            if (occurrences.length > 0) {
                var assignments = snapHitsToOccurrences(hits, occurrences);
                for (var i = 0; i < assignments.length; i++) {
                    var a = assignments[i];
                    if (a.occurrence) {
                        drawRect(ctx, dpr, rectForItemSubstring(
                            a.occurrence.item, a.occurrence.relStart, a.occurrence.relEnd, viewport));
                    } else {
                        drawSubstringFallback(items, a.hit, viewport, ctx, dpr);
                    }
                }
            } else {
                // No matched-terms info → use raw char positions.
                for (var h = 0; h < hits.length; h++) {
                    drawSubstringFallback(items, hits[h], viewport, ctx, dpr);
                }
            }
        }).catch(function (e) {
            console.error('HB highlight render failed for page ' + pageNumber, e);
        });
    }

    // ---- public API + event wiring ---------------------------------------

    function clearAllOverlays() {
        var nodes = document.querySelectorAll('.hb-highlight-layer');
        for (var i = 0; i < nodes.length; i++) nodes[i].remove();
    }

    function reapplyAllRendered() {
        var nodes = document.querySelectorAll('.page[data-loaded="true"]');
        for (var i = 0; i < nodes.length; i++) {
            var n = parseInt(nodes[i].getAttribute('data-page-number'), 10);
            if (!isNaN(n)) renderPageHighlights(n);
        }
    }

    window.HB_setHighlightXml = function (xml, terms) {
        clearAllOverlays();
        hitsByPage = parseHighlightXml(xml);
        matchedTerms = Array.isArray(terms) ? terms.slice() : [];
        reapplyAllRendered();
    };

    function bindEvents() {
        var app = window.PDFViewerApplication;
        if (!app || !app.eventBus) return false;
        // pdfjs renders pages lazily as they scroll into view. Re-draw our overlay each
        // time pdfjs finishes rendering a page so highlights track zoom/scroll.
        app.eventBus.on('pagerendered', function (ev) {
            try { renderPageHighlights(ev.pageNumber); } catch (e) { /* swallow */ }
        });
        app.eventBus.on('documentdestroyed', clearAllOverlays);
        return true;
    }

    function waitForApp() {
        if (bindEvents()) return;
        var attempts = 0;
        var t = setInterval(function () {
            attempts++;
            if (bindEvents() || attempts > 200) clearInterval(t);
        }, 50);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', waitForApp);
    } else {
        waitForApp();
    }
})();
