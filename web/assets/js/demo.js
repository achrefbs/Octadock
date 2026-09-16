
(function(){
"use strict";
var $ = function(s,c){ return (c||document).querySelector(s); };
var $$ = function(s,c){ return Array.prototype.slice.call((c||document).querySelectorAll(s)); };
var finePointer = window.matchMedia("(pointer:fine)").matches;
var canHover = window.matchMedia("(hover:hover)").matches;
var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
var ICONS = {
  "scan-line":'<path d="M3 7V5a2 2 0 0 1 2-2h2"/><path d="M17 3h2a2 2 0 0 1 2 2v2"/><path d="M21 17v2a2 2 0 0 1-2 2h-2"/><path d="M7 21H5a2 2 0 0 1-2-2v-2"/><path d="M7 12h10"/>',
  "fullscreen":'<path d="M3 7V5a2 2 0 0 1 2-2h2"/><path d="M17 3h2a2 2 0 0 1 2 2v2"/><path d="M21 17v2a2 2 0 0 1-2 2h-2"/><path d="M7 21H5a2 2 0 0 1-2-2v-2"/><rect x="7" y="8" width="10" height="8" rx="1"/>',
  "app-window":'<rect x="2" y="4" width="20" height="16" rx="2.5"/><path d="M2 9h20"/><path d="M6 6.5h.01"/><path d="M9 6.5h.01"/>',
  "ellipsis":'<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
  "square-pen":'<path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.4 2.6a2.1 2.1 0 0 1 3 3L12.4 14.6a2 2 0 0 1-.9.5l-2.9.8a.5.5 0 0 1-.6-.6l.8-2.9a2 2 0 0 1 .5-.9z"/>',
  "pen":'<path d="M21.2 6.8a1 1 0 0 0-4-4L3.8 16.2a2 2 0 0 0-.5.8l-1.3 4.4a.5.5 0 0 0 .6.6l4.4-1.3a2 2 0 0 0 .8-.5z"/>',
  "type":'<path d="M4 7V4h16v3"/><path d="M9 20h6"/><path d="M12 4v16"/>',
  "mic":'<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/>',
  "eye":'<path d="M2.1 12.3a1 1 0 0 1 0-.7 10.8 10.8 0 0 1 19.9 0 1 1 0 0 1 0 .7 10.8 10.8 0 0 1-19.9 0"/><circle cx="12" cy="12" r="3"/>',
  "history":'<path d="M3 12a9 9 0 1 0 9-9 9.8 9.8 0 0 0-6.7 2.7L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>',
  "square":'<rect x="3" y="3" width="18" height="18" rx="2.5"/>',
  "counter":'<circle cx="12" cy="12" r="9"/><path d="M10.5 9.5 12.5 8v8"/>',
  "highlighter":'<path d="m9 11-6 6v3h9l3-3"/><path d="m22 12-4.6 4.6a2 2 0 0 1-2.8 0l-5.2-5.2a2 2 0 0 1 0-2.8L14 4"/>',
  "droplet":'<path d="M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7z"/>',
  "crop":'<path d="M6 2v14a2 2 0 0 0 2 2h14"/><path d="M18 22V8a2 2 0 0 0-2-2H2"/>',
  "arrow-up-right":'<path d="M7 7h10v10"/><path d="M7 17 17 7"/>',
  "chevron-right":'<path d="m9 18 6-6-6-6"/>',
  "play":'<path d="M6 4.5v15a.5.5 0 0 0 .8.4l12-7.5a.5.5 0 0 0 0-.8l-12-7.5a.5.5 0 0 0-.8.4z"/>',
  "check":'<path d="M20 6 9 17l-5-5"/>',
  "sparkle":'<path d="M12 3l1.9 5.6L19.5 10.5 13.9 12.4 12 18l-1.9-5.6L4.5 10.5l5.6-1.9z"/>',
  "move-vertical":'<path d="M12 2v20"/><path d="m8 18 4 4 4-4"/><path d="m8 6 4-4 4 4"/>',
  "layers":'<path d="M12.8 2.2a2 2 0 0 0-1.7 0L2.6 6.1a1 1 0 0 0 0 1.8l8.6 3.9a2 2 0 0 0 1.7 0l8.6-3.9a1 1 0 0 0 0-1.8z"/><path d="M2 12a1 1 0 0 0 .6.9l8.6 3.9a2 2 0 0 0 1.7 0l8.6-3.9A1 1 0 0 0 22 12"/><path d="M2 17a1 1 0 0 0 .6.9l8.6 3.9a2 2 0 0 0 1.7 0l8.6-3.9A1 1 0 0 0 22 17"/>',
  "volume-2":'<path d="M11 4.7a.7.7 0 0 0-1.2-.5L6.4 7.6A1.4 1.4 0 0 1 5.4 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.4a1.4 1.4 0 0 1 1 .4l3.4 3.4a.7.7 0 0 0 1.2-.5z"/><path d="M16 9a5 5 0 0 1 0 6"/><path d="M19.4 18.4a9 9 0 0 0 0-12.7"/>',
  "link":'<path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/>',
  "search":'<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  "info":'<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
  "shield":'<path d="M20 13c0 5-3.5 7.5-7.7 9a1 1 0 0 1-.6 0C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.2-2.7a1.2 1.2 0 0 1 1.5 0C14.5 3.8 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>',
  "cpu":'<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M15 2v2"/><path d="M15 20v2"/><path d="M2 15h2"/><path d="M2 9h2"/><path d="M20 15h2"/><path d="M20 9h2"/><path d="M9 2v2"/><path d="M9 20v2"/>',
  "folder":'<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9L9.6 3.9A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>'
};
function icon(name){ return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' + (ICONS[name] || ICONS["scan-line"]) + '</svg>'; }
function paint(root){ $$("[data-icon]", root).forEach(function(el){ if (!el.firstElementChild) el.innerHTML = icon(el.getAttribute("data-icon")); }); }
paint(document);

if (reduceMotion || !("IntersectionObserver" in window)) $$("[data-reveal]").forEach(function(el){ el.classList.add("revealed"); });
else { var io = new IntersectionObserver(function(entries){ entries.forEach(function(en){ if (en.isIntersecting){ en.target.classList.add("revealed"); io.unobserve(en.target); } }); }, { threshold: 0.1 }); $$("[data-reveal]").forEach(function(el){ io.observe(el); }); }

/* ---------- the demo: a desktop you can capture ---------- */
var demo = $("#demo"), stage = $("#stage"), xLine = $("#xLine"), yLine = $("#yLine"), sel = $("#sel"), selChip = $("#selChip"), flash = $("#flash"),
    countdown = $("#countdown"), pill = $("#pillStatus"), pillText = $("#pillText"), hint = $("#hint"), shelf = $("#shelf"), shelfList = $("#shelfList"),
    dock = $("#dock"), dockMenu = $("#dockMenu"), moreBtn = $("#moreBtn"), dictated = $("#dictated");
var mode = "area", busy = false, shooting = false, sx = 0, sy = 0, captures = 0, hintTimer = 0, lastArea = null, collapseTimer = 0;
var HINT = { area: canHover ? "Try it: drag across this preview" : "Try it: drag across this preview", ocr: "OCR: drag over text to copy it" };
function xy(e){ var r = demo.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }
function say(text, done){ hint.innerHTML = "<i></i><span>" + text + "</span>"; hint.classList.toggle("done", !!done); clearTimeout(hintTimer); if (done) hintTimer = setTimeout(function(){ say(captures ? "Again? Go ahead, it is free" : HINT[mode] || HINT.area); }, 3200); }
function wait(ms){ return new Promise(function(r){ setTimeout(r, ms); }); }
function animate(el, frames, opts){ if (reduceMotion || typeof el.animate !== "function"){ var last = frames[frames.length - 1]; Object.keys(last).forEach(function(k){ el.style[k] = last[k]; }); return Promise.resolve(); } var a = el.animate(frames, opts); return a.finished.catch(function(){}).then(function(){ var end = frames[frames.length - 1]; Object.keys(end).forEach(function(k){ el.style[k] = end[k]; }); a.cancel(); }); }
function showSel(r, chip, tight){ sel.style.display = "block"; sel.style.opacity = "1"; sel.style.transition = ""; sel.style.left = r.x + "px"; sel.style.top = r.y + "px"; sel.style.width = r.w + "px"; sel.style.height = r.h + "px"; sel.classList.toggle("tight", !!tight); selChip.textContent = chip; }
function hideSel(){ sel.style.transition = "opacity .3s"; sel.style.opacity = "0"; setTimeout(function(){ sel.style.display = "none"; }, 320); }
function flashNow(){ flash.classList.remove("go"); void flash.offsetWidth; flash.classList.add("go"); }
function press(b){ if (!b) return; b.classList.add("pressing"); setTimeout(function(){ b.classList.remove("pressing"); }, 520); }
function showPill(text, mic){ pillText.textContent = text; pill.classList.toggle("mic", !!mic); pill.classList.add("on"); }
function hidePill(){ pill.classList.remove("on"); }
function tileForRect(r){
  var sr = stage.getBoundingClientRect(), dr = demo.getBoundingClientRect(); var ox = sr.left - dr.left, oy = sr.top - dr.top;
  var tileW = shelfList.clientWidth || 170; var k = tileW / Math.max(1, r.w); var th = Math.max(56, Math.min(150, Math.round(r.h * k)));
  var clone = stage.cloneNode(true); clone.removeAttribute("id"); clone.classList.add("stage--clone"); clone.setAttribute("aria-hidden", "true"); clone.setAttribute("inert", "");
  $$("[id]", clone).forEach(function(el){ el.removeAttribute("id"); });
  clone.style.width = sr.width + "px"; clone.style.height = sr.height + "px"; clone.style.background = "#1a5fd2";
  clone.style.transform = "scale(" + k + ") translate(" + (-(r.x - ox)) + "px," + (-(r.y - oy)) + "px)";
  var view = document.createElement("div"); view.className = "tile-view"; view.style.setProperty("--th", th + "px"); view.appendChild(clone); return view;
}
function addTile(content, label){
  captures++; var li = document.createElement("li"); li.className = "tile"; li.setAttribute("title", label); li.appendChild(content);
  shelfList.prepend(li); while (shelfList.children.length > 4) shelfList.lastElementChild.remove();
  shelf.classList.add("on"); shelf.classList.remove("hidden-by-user"); return li;
}
function finishImage(r, label){ flashNow(); var view = tileForRect(r); hideSel(); addTile(view, label); say(label + " captured in this demo. Find it on the Shelf", true); }
function rectOf(el){ var r = el.getBoundingClientRect(), h = demo.getBoundingClientRect(); return { x: r.left - h.left, y: r.top - h.top, w: r.width, h: r.height }; }
function fullRect(){ return { x: 0, y: 0, w: demo.clientWidth, h: demo.clientHeight }; }
function nearestWindow(p){
  var wins = $$(".win", stage).filter(function(w){ return getComputedStyle(w).display !== "none"; }); var best = null, bestD = Infinity;
  wins.forEach(function(w){ var r = rectOf(w), dx = Math.max(r.x - p.x, 0, p.x - (r.x + r.w)), dy = Math.max(r.y - p.y, 0, p.y - (r.y + r.h)), d = dx * dx + dy * dy; if (d < bestD){ bestD = d; best = r; } });
  return best || { x: 40, y: 40, w: demo.clientWidth - 80, h: demo.clientHeight - 120 };
}
function growSel(r, chip, ms, tight){ showSel({ x: r.x, y: r.y, w: 0, h: 0 }, chip, tight); return animate(sel, [{ width: "0px", height: "0px" }, { width: r.w + "px", height: r.h + "px" }], { duration: ms, easing: "cubic-bezier(.2,.7,.2,1)", fill: "forwards" }).then(function(){ selChip.textContent = chip + " · " + Math.round(r.w * 2) + " × " + Math.round(r.h * 2); }); }
function placeDictation(){
  if (!dictated.classList.contains("on")){ demo.classList.remove("dictating"); return; }
  var copy = $(".hero-copy", stage), bounds = demo.getBoundingClientRect();
  if (!copy) return;
  var top = copy.getBoundingClientRect().bottom - bounds.top + 12;
  dictated.style.top = top + "px";
  var reserve = parseFloat(getComputedStyle(hint).bottom) + hint.offsetHeight + 16;
  demo.style.setProperty("--dictation-height", Math.ceil(top + dictated.offsetHeight + reserve) + "px");
  demo.classList.add("dictating");
}
if ("ResizeObserver" in window){
  var dictationLayout = new ResizeObserver(placeDictation);
  [$(".hero-copy", stage), dictated, hint].forEach(function(el){ if (el) dictationLayout.observe(el); });
}
window.addEventListener("resize", placeDictation);
function typeInto(el, text){ el.textContent = ""; el.classList.add("on"); placeDictation(); if (reduceMotion){ el.textContent = text; placeDictation(); return Promise.resolve(); } return new Promise(function(resolve){ var i = 0; var t = setInterval(function(){ i++; el.textContent = text.slice(0, i); if (i >= text.length){ clearInterval(t); resolve(); } }, 28); }); }
function run(act, at){
  if (busy) return; busy = true; var p = at || { x: demo.clientWidth * .75, y: demo.clientHeight * .5 }; var done = function(){ busy = false; };
  if (act === "window"){ var wr = nearestWindow(p); var pad2 = { x: wr.x - 1, y: wr.y - 1, w: wr.w + 2, h: wr.h + 2 }; growSel(pad2, "Window", 420).then(function(){ return wait(320); }).then(function(){ finishImage(pad2, "Window"); }).then(done, done); }
  else if (act === "screen" || act === "monitors"){ var fr = fullRect(); var label = act === "monitors" ? "All monitors" : "Full screen"; growSel(fr, label, 380, true).then(function(){ return wait(320); }).then(function(){ finishImage(fr, label); }).then(done, done); }
  else if (act === "previous"){ if (!lastArea){ say("Previous area: drag an area first", true); done(); return; } growSel(lastArea, "Previous area", 300).then(function(){ return wait(260); }).then(function(){ finishImage(lastArea, "Previous area"); }).then(done, done); }
  else if (act === "timer"){ var n = 3; countdown.textContent = "3"; countdown.classList.add("on"); say("Timer: get the screen ready"); var t = setInterval(function(){ n--; if (n > 0){ countdown.textContent = String(n); countdown.classList.remove("on"); void countdown.offsetWidth; countdown.classList.add("on"); return; } clearInterval(t); countdown.classList.remove("on"); var fr2 = fullRect(); showSel(fr2, "Full screen · " + Math.round(fr2.w * 2) + " × " + Math.round(fr2.h * 2), true); setTimeout(function(){ finishImage(fr2, "Timed full screen"); done(); }, 260); }, 900); }
  else if (act === "scroll"){ var base = nearestWindow(p); var r1 = { x: base.x - 1, y: base.y - 1, w: base.w + 2, h: base.h + 2 }; say("Scrolling page: viewport 1 of 3"); growSel(r1, "Scrolling · viewport 1", 380).then(function(){ selChip.textContent = "Scrolling · viewport 2 of 3"; say("Scrolling page: viewport 2 of 3"); return wait(520); }).then(function(){ selChip.textContent = "Scrolling · viewport 3 of 3"; say("Scrolling page: stitching"); return wait(520); }).then(function(){ flashNow(); hideSel(); var view = tileForRect(r1); view.style.setProperty("--th", "150px"); addTile(view, "Scrolling page"); say("Stitched from 3 viewports. Beta, and honest about it", true); }).then(done, done); }
  else if (act === "record"){ var s = 0; showPill("Recording 00:00"); say("Recording the screen. Beta"); var rt = setInterval(function(){ s++; pillText.textContent = "Recording 00:0" + s; }, 1000); setTimeout(function(){ clearInterval(rt); hidePill(); var v = document.createElement("div"); v.className = "tile-view"; v.style.setProperty("--th", "96px"); v.innerHTML = '<div class="tile-rec"><span class="i" data-icon="play"></span>recording · 00:03</div>'; paint(v); addTile(v, "Recording"); say("Recording preview complete. No real video was recorded", true); done(); }, 3200); }
  else if (act === "dictate"){ showPill("Listening · local model", true); say("Dictating, live partials at the cursor"); wait(600).then(function(){ return typeInto(dictated, "Send the lake shot to Sam before Friday, and keep the reflection."); }).then(function(){ hidePill(); say("Inserted at the cursor. Nothing left this PC", true); return wait(2600); }).then(function(){ dictated.classList.remove("on"); placeDictation(); }).then(done, done); }
  else done();
}
if (demo){
  if (finePointer){
    demo.addEventListener("pointerenter", function(){ demo.classList.add("armed"); });
    demo.addEventListener("pointerleave", function(){ if (!shooting) demo.classList.remove("armed"); });
    demo.addEventListener("pointermove", function(e){ var p = xy(e); xLine.style.left = p.x + "px"; yLine.style.top = p.y + "px"; demo.classList.toggle("over-ui", !!(e.target.closest && e.target.closest(".dock, .dock-menu, .shelf"))); if (shooting){ var w = Math.abs(p.x - sx), h = Math.abs(p.y - sy); sel.style.left = Math.min(p.x, sx) + "px"; sel.style.top = Math.min(p.y, sy) + "px"; sel.style.width = w + "px"; sel.style.height = h + "px"; selChip.textContent = (mode === "ocr" ? "OCR" : "Area") + " · " + Math.round(w * 2) + " × " + Math.round(h * 2); } });
  }
  $$("img", demo).forEach(function(img){ img.setAttribute("draggable", "false"); });
  demo.addEventListener("dragstart", function(e){ e.preventDefault(); });
  demo.addEventListener("pointerdown", function(e){ if (e.button !== 0 || busy) return; if (e.target.closest("a, button, input, .dock, .dock-menu, .shelf")) return; e.preventDefault(); closeMenu(); var p = xy(e); shooting = true; sx = p.x; sy = p.y; demo.classList.add("shooting"); showSel({ x: sx, y: sy, w: 0, h: 0 }, mode === "ocr" ? "OCR" : "Area"); try { demo.setPointerCapture(e.pointerId); } catch(_){} });
  demo.addEventListener("pointerup", function(e){ if (!shooting) return; shooting = false; demo.classList.remove("shooting"); var p = xy(e), w = Math.abs(p.x - sx), h = Math.abs(p.y - sy); var r = { x: Math.min(p.x, sx), y: Math.min(p.y, sy), w: w, h: h }; if (w > 12 && h > 12){ if (mode === "ocr"){ flashNow(); hideSel(); mode = "area"; var lines = Math.max(1, Math.round(h / 24)); var v = document.createElement("div"); v.className = "tile-view"; v.style.setProperty("--th", "84px"); v.innerHTML = '<div class="tile-text">' + lines + ' line' + (lines === 1 ? "" : "s") + ' of text\ncopied to the clipboard\non-device OCR</div>'; addTile(v, "OCR"); say(lines + " line" + (lines === 1 ? "" : "s") + " of sample text captured in this demo", true); } else { lastArea = r; finishImage(r, "Area"); } } else hideSel(); });
  demo.addEventListener("pointercancel", function(){ shooting = false; demo.classList.remove("shooting"); hideSel(); });
}
function openDock(){ clearTimeout(collapseTimer); dock.classList.add("open"); }
function foldDock(){ clearTimeout(collapseTimer); collapseTimer = setTimeout(function(){ if (!dockMenu.classList.contains("open") && !dock.matches(":focus-visible") && !dock.querySelector(":focus-visible")) dock.classList.remove("open"); }, 420); }
if (canHover){ dock.addEventListener("pointerenter", openDock); dock.addEventListener("pointerleave", foldDock); dockMenu.addEventListener("pointerenter", openDock); dockMenu.addEventListener("pointerleave", foldDock); }
dock.addEventListener("click", openDock); dock.addEventListener("focusin", openDock); dock.addEventListener("focusout", function(){ if (!dock.contains(document.activeElement)) foldDock(); });
document.addEventListener("pointerdown", function(e){ if (!dock.contains(e.target) && !dockMenu.contains(e.target)){ closeMenu(); foldDock(); } });
function placeMenu(){ var b = moreBtn.getBoundingClientRect(), h = demo.getBoundingClientRect(); var left = b.left - h.left, bottom = h.bottom - b.top + 8; var maxLeft = h.width - dockMenu.offsetWidth - 12; dockMenu.style.left = Math.max(12, Math.min(left, maxLeft)) + "px"; dockMenu.style.bottom = bottom + "px"; }
function closeMenu(){ dockMenu.classList.remove("open"); moreBtn.setAttribute("aria-expanded", "false"); moreBtn.classList.remove("on"); }
$$("[data-act]", dock).forEach(function(b){ b.addEventListener("click", function(){ var a = b.getAttribute("data-act"); closeMenu(); press(b);
  if (a === "area"){ mode = "area"; say("Area: drag anywhere on this desktop"); return; }
  if (a === "shelf"){ if (!captures){ say("The Shelf appears with your first capture"); return; } var hidden = shelf.classList.toggle("hidden-by-user"); say(hidden ? "Shelf hidden" : "Shelf shown"); return; }
  if (a === "history"){ say("History opens the Library: every capture on this PC, searchable"); return; }
  if (!busy) run(a); }); });
moreBtn.addEventListener("click", function(){ var open = !dockMenu.classList.contains("open"); if (open){ openDock(); dockMenu.classList.add("open"); placeMenu(); } else dockMenu.classList.remove("open"); moreBtn.setAttribute("aria-expanded", String(open)); moreBtn.classList.toggle("on", open); });
window.addEventListener("resize", function(){ if (dockMenu.classList.contains("open")) placeMenu(); });
$$("[data-act]", dockMenu).forEach(function(b){ b.addEventListener("click", function(){ var a = b.getAttribute("data-act"); closeMenu(); foldDock();
  if (a === "ocr"){ mode = "ocr"; say(HINT.ocr); return; }
  if (a === "clipboard"){ say("Clipboard history: optional, searchable, on this PC"); return; }
  if (a === "context"){ say("Context bundles captures, notes and files into a folder you choose"); return; }
  if (a === "exit"){ say("Exit closes the tray app. Here, nothing happens"); return; }
  if (!busy) run(a); }); });
document.addEventListener("keydown", function(e){ if (e.key === "Escape"){ closeMenu(); foldDock(); } });
say(HINT.area);
})();
