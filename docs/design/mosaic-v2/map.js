/* Prototype only: draws the House vote map. Needs d3 and topojson-client. Data: data/cd119.json, data/roll308.json. */
(function () {
  'use strict';
  var ST = {AL:'Alabama',AK:'Alaska',AZ:'Arizona',AR:'Arkansas',CA:'California',CO:'Colorado',CT:'Connecticut',DE:'Delaware',FL:'Florida',GA:'Georgia',HI:'Hawaii',ID:'Idaho',IL:'Illinois',IN:'Indiana',IA:'Iowa',KS:'Kansas',KY:'Kentucky',LA:'Louisiana',ME:'Maine',MD:'Maryland',MA:'Massachusetts',MI:'Michigan',MN:'Minnesota',MS:'Mississippi',MO:'Missouri',MT:'Montana',NE:'Nebraska',NV:'Nevada',NH:'New Hampshire',NJ:'New Jersey',NM:'New Mexico',NY:'New York',NC:'North Carolina',ND:'North Dakota',OH:'Ohio',OK:'Oklahoma',OR:'Oregon',PA:'Pennsylvania',RI:'Rhode Island',SC:'South Carolina',SD:'South Dakota',TN:'Tennessee',TX:'Texas',UT:'Utah',VT:'Vermont',VA:'Virginia',WA:'Washington',WV:'West Virginia',WI:'Wisconsin',WY:'Wyoming',DC:'District of Columbia',PR:'Puerto Rico',GU:'Guam',VI:'U.S. Virgin Islands',AS:'American Samoa',MP:'Northern Mariana Islands'};
  var PN = {R:'Republican', D:'Democrat', I:'Independent'};
  var PK = {R:'rep', D:'dem', I:'ind'};
  var CK = {Y:'yes', N:'against', NV:'mute'};
  var CW = {Y:'Voted Yes', N:'Voted No', NV:'Did not vote', V:'Vacant seat', X:'Cannot vote on passage'};
  var GLYPH = {Y:'✓', N:'✕', NV:'-'};
  var CLS = {Y:'yes', N:'no', NV:'nv'};
  var NS = 'http://www.w3.org/2000/svg';
  var cache;

  function el(n, a, p) {
    var e = document.createElementNS(NS, n);
    for (var k in a) e.setAttribute(k, a[k]);
    if (p) p.appendChild(e);
    return e;
  }
  function ok(r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); }
  function load() {
    if (!cache) {
      cache = Promise.all([fetch('data/cd119.json').then(ok), fetch('data/roll308.json').then(ok)]).then(function (r) {
        var topo = r[0], fc = topojson.feature(topo, topo.objects[Object.keys(topo.objects)[0]]), by = {};
        r[1].seats.forEach(function (s) { by[s.k] = s; });
        fc.features.forEach(function (f) { f.seat = by[f.properties.k]; });
        return {fc: fc, seats: r[1].seats, delegates: r[1].delegates};
      });
    }
    return cache;
  }
  function seatName(s) { return ST[s.s] + (s.d ? ' district ' + s.d : ' at large'); }
  function short(s) { return s.s + '-' + (s.d || 'AL'); }

  function defs() {
    if (document.getElementById('cl-defs')) return;
    var s = el('svg', {id: 'cl-defs', width: 0, height: 0, 'aria-hidden': 'true', focusable: 'false'});
    s.style.cssText = 'position:absolute;width:0;height:0';
    var d = el('defs', {}, s);
    ['yes', 'against', 'mute', 'rep', 'dem', 'ind'].forEach(function (k) {
      var c = 'fill:var(--c-' + k + ')';
      var p = el('pattern', {id: 'p-n-' + k, width: 6, height: 6, patternUnits: 'userSpaceOnUse', patternTransform: 'rotate(45)'}, d);
      el('rect', {width: 6, height: 6, style: 'fill:var(--surface)'}, p);
      el('rect', {width: 3, height: 6, style: c}, p);
      var q = el('pattern', {id: 'p-nv-' + k, width: 5, height: 5, patternUnits: 'userSpaceOnUse'}, d);
      el('rect', {width: 5, height: 5, style: 'fill:var(--surface)'}, q);
      el('circle', {cx: 2.5, cy: 2.5, r: 1.4, style: c}, q);
    });
    document.body.insertBefore(s, document.body.firstChild);
  }

  /* paint for a seat: key is the color token, pattern says how it voted */
  function paint(s, color) {
    if (s.c === 'V') return {fill: 'var(--raised)', stroke: 'var(--line-strong)', dash: '3 2'};
    var k = color === 'party' ? PK[s.p] : CK[s.c];
    if (s.c === 'Y') return {fill: 'var(--c-' + k + ')', stroke: 'var(--bg)'};
    return {fill: 'url(#p-' + (s.c === 'N' ? 'n' : 'nv') + '-' + k + ')', stroke: 'var(--c-' + k + ')'};
  }
  /* a standalone swatch for the legend */
  function swatch(s, color) {
    var svg = el('svg', {viewBox: '0 0 18 18', 'aria-hidden': 'true', focusable: 'false'});
    var p = paint(s, color);
    el('rect', {x: 1, y: 1, width: 16, height: 16, rx: 3, style: 'fill:' + p.fill + ';stroke:' + (p.stroke === 'var(--bg)' ? 'var(--c-' + (color === 'party' ? PK[s.p] : CK[s.c]) + ')' : p.stroke) + ';stroke-width:1.5' + (p.dash ? ';stroke-dasharray:' + p.dash : '')}, svg);
    return svg;
  }
  function counts(data) {
    var c = {Y: 0, N: 0, NV: 0, V: 0, party: {R: {Y: 0, N: 0, NV: 0}, D: {Y: 0, N: 0, NV: 0}, I: {Y: 0, N: 0, NV: 0}}, vacant: []};
    data.seats.forEach(function (s) {
      c[s.c]++;
      if (s.c === 'V') c.vacant.push(s); else c.party[s.p][s.c]++;
    });
    return c;
  }
  function esc(t) { return String(t).replace(/[&<>"]/g, function (m) { return {'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[m]; }); }
  function who(s) {
    if (s.c === 'V') return '<b>Vacant seat</b><br>' + seatName(s);
    return '<b>' + esc(s.n) + '</b> (' + s.p + ')<br>' + seatName(s);
  }
  function castPill(c) {
    if (c === 'V') return '<span class="cast nv">Vacant</span>';
    if (c === 'X') return '<span class="cast nv">Cannot vote on passage</span>';
    return '<span class="cast ' + CLS[c] + '"><span aria-hidden="true">' + GLYPH[c] + '</span>' + {Y: 'Yes', N: 'No', NV: 'Not voting'}[c] + '</span>';
  }
  function label(s) {
    if (s.c === 'V') return 'Vacant seat, ' + seatName(s) + '. No member in the Clerk record.';
    if (s.c === 'X') return s.n + ', ' + PN[s.p] + ', delegate for ' + ST[s.s] + '. Cannot vote on passage.';
    return s.n + ', ' + PN[s.p] + ', ' + seatName(s) + '. ' + CW[s.c] + '.';
  }

  function mount(o) {
    var svg = o.svg, wrap = svg.parentNode, W = 0, items = [], data, view = o.view || 'geo', color = o.color || 'vote';
    var tip = o.tip, readout = o.readout, live = o.interactive !== false;
    svg.classList.add('cl-map');

    function seatPath(parent, s, d, cx, cy, extra) {
      var p = el('path', {d: d, 'class': 'seat'}, parent), pt = paint(s, color);
      p.style.fill = pt.fill;
      p.setAttribute('stroke', pt.stroke);
      p.setAttribute('stroke-width', view === 'geo' ? (s.c === 'Y' ? 0.5 : 0.9) : 1.2);
      if (pt.dash) p.setAttribute('stroke-dasharray', pt.dash);
      if (live) {
        p.setAttribute('tabindex', '-1');
        p.setAttribute('role', s.k === 'MA07' ? 'link' : 'button');
        p.setAttribute('aria-label', label(s));
      }
      p.__s = s;
      p.__it = {el: p, x: cx, y: cy};
      items.push(p.__it);
      return p;
    }
    function circleD(cx, cy, r) { return 'M' + (cx - r) + ',' + cy + 'a' + r + ',' + r + ' 0 1,0 ' + 2 * r + ',0a' + r + ',' + r + ' 0 1,0 ' + (-2 * r) + ',0'; }

    function delegates(g, y) {
      if (o.delegates === false) return y;
      el('text', {x: 0, y: y + 11, 'class': 'gl-lbl'}, g).textContent = 'Delegates (circles) cannot vote on passage.';
      var r = 16, x = r + 2, cy = y + 22 + r;
      data.delegates.forEach(function (d) {
        var s = {k: 'D-' + d.s, s: d.s, d: 0, n: d.n, p: d.p, c: 'X'};
        var col = color === 'party' ? 'var(--c-' + PK[d.p] + ')' : 'var(--line-strong)';
        var p = el('path', {d: circleD(x, cy, r), 'class': 'seat', 'stroke-dasharray': '3 2', 'stroke-width': 2, stroke: col}, g);
        p.style.fill = 'var(--raised)';
        if (live) {
          p.setAttribute('tabindex', '-1'); p.setAttribute('role', 'button'); p.setAttribute('aria-label', label(s));
        }
        p.__s = s;
        el('text', {x: x, y: cy + 4, 'class': 'dlbl', 'aria-hidden': 'true'}, g).textContent = d.s;
        p.__it = {el: p, x: x, y: cy};
        items.push(p.__it);
        x += 2 * r + 8;
      });
      return cy + r + 4;
    }

    function drawGeo(g, zipg) {
      var proj = d3.geoIdentity().reflectY(true).fitWidth(W, data.fc), path = d3.geoPath(proj);
      var h = Math.ceil(path.bounds(data.fc)[1][1]) + 2;
      data.fc.features.forEach(function (f) {
        var c = path.centroid(f), p = seatPath(g, f.seat, path(f), c[0], c[1]);
        if (o.zip && o.zip.indexOf(f.seat.k) >= 0) { el('path', {d: p.getAttribute('d'), 'class': 'youb'}, zipg); el('path', {d: p.getAttribute('d'), 'class': 'you'}, zipg); }
      });
      return h;
    }
    function drawTiles(g, zipg) {
      var S = W < 520 ? 22 : 26, gap = 3, by = {}, order = [], x = 0, y = 0, rowH = 0, gapG = 16, lblH = 15;
      data.seats.forEach(function (s) { (by[s.s] = by[s.s] || []).push(s); });
      order = Object.keys(by).sort(function (a, b) { return ST[a] < ST[b] ? -1 : 1; });
      order.forEach(function (st) {
        var list = by[st], n = list.length, cols = Math.max(1, Math.ceil(Math.sqrt(n))), rows = Math.ceil(n / cols);
        var gw = cols * (S + gap) - gap, gh = rows * (S + gap) - gap + lblH;
        if (x > 0 && x + gw > W) { x = 0; y += rowH + gapG; rowH = 0; }
        el('text', {x: x, y: y + 11, 'class': 'gl-lbl'}, g).textContent = st;
        list.forEach(function (s, i) {
          var sx = x + (i % cols) * (S + gap), sy = y + lblH + Math.floor(i / cols) * (S + gap);
          var d = 'M' + sx + ',' + sy + 'h' + S + 'v' + S + 'h' + (-S) + 'z';
          var p = seatPath(g, s, d, sx + S / 2, sy + S / 2);
          if (o.zip && o.zip.indexOf(s.k) >= 0) { el('path', {d: d, 'class': 'youb'}, zipg); el('path', {d: d, 'class': 'you'}, zipg); }
        });
        x += gw + gapG; rowH = Math.max(rowH, gh);
      });
      return y + rowH + 2;
    }

    function render() {
      W = Math.max(280, Math.floor(wrap.clientWidth));
      items = [];
      while (svg.firstChild) svg.removeChild(svg.firstChild);
      var g = el('g', {}, svg), zipg = el('g', {}, svg), dg = el('g', {}, svg), hlg = el('g', {}, svg);
      var h = view === 'geo' ? drawGeo(g, zipg) : drawTiles(g, zipg);
      h = delegates(dg, h + 14);
      svg.setAttribute('viewBox', '0 0 ' + W + ' ' + Math.ceil(h));
      svg.setAttribute('width', W); svg.setAttribute('height', Math.ceil(h));
      svg.style.height = 'auto';
      o.hlb = el('path', {'class': 'hlb', d: ''}, hlg); o.hl = el('path', {'class': 'hl', d: ''}, hlg);
      if (live && items.length) {
        var start = items.filter(function (i) { return o.zip && i.el.__s.k === 'MA07'; })[0] || items[0];
        start.el.setAttribute('tabindex', '0');
      }
    }

    function showAt(p, ev) {
      var s = p.__s;
      o.hl.setAttribute('d', p.getAttribute('d')); o.hlb.setAttribute('d', p.getAttribute('d'));
      if (readout) readout.innerHTML = who(s) + '. ' + castPill(s.c);
      if (!tip) return;
      tip.innerHTML = who(s) + '<br>' + castPill(s.c);
      tip.hidden = false;
      var wr = wrap.getBoundingClientRect(), x, y;
      if (ev && ev.clientX !== undefined && ev.type.indexOf('pointer') === 0) { x = ev.clientX - wr.left; y = ev.clientY - wr.top; }
      else { var b = p.getBoundingClientRect(); x = b.left + b.width / 2 - wr.left; y = b.top + b.height / 2 - wr.top; }
      var tw = tip.offsetWidth, th = tip.offsetHeight;
      tip.style.left = Math.max(0, Math.min(wr.width - tw, x - tw / 2)) + 'px';
      tip.style.top = (y - th - 14 < 0 ? y + 16 : y - th - 14) + 'px';
    }
    function hide() { if (tip) tip.hidden = true; o.hl.setAttribute('d', ''); o.hlb.setAttribute('d', ''); }
    function activate(p) {
      var s = p.__s;
      if (s.k === 'MA07') { location.href = 'official.html'; return; }
      if (readout) readout.innerHTML = who(s) + '. ' + castPill(s.c) + ' <span class="example">example</span> In the built site this opens the page for this ' + (s.c === 'X' ? 'delegate' : 'member') + '. The prototype has one member page, for Ayanna Pressley.';
    }
    function nearest(cur, dir) {
      var c = cur.__it, best = null, bs = 1e9;
      items.forEach(function (i) {
        if (i === c) return;
        var dx = i.x - c.x, dy = i.y - c.y, along = dx * dir[0] + dy * dir[1], perp = Math.abs(dx * dir[1] - dy * dir[0]);
        if (along <= 1) return;
        var sc = along + 2.5 * perp;
        if (sc < bs) { bs = sc; best = i; }
      });
      return best;
    }

    function bind() {
      svg.addEventListener('pointerover', function (ev) { var p = ev.target.closest('.seat'); if (p) showAt(p, ev); });
      svg.addEventListener('pointermove', function (ev) { var p = ev.target.closest('.seat'); if (p) showAt(p, ev); });
      svg.addEventListener('pointerleave', hide);
      svg.addEventListener('focusin', function (ev) {
        var p = ev.target.closest('.seat'); if (!p) return;
        items.forEach(function (i) { i.el.setAttribute('tabindex', '-1'); });
        p.setAttribute('tabindex', '0'); showAt(p, ev);
      });
      svg.addEventListener('focusout', hide);
      svg.addEventListener('click', function (ev) { var p = ev.target.closest('.seat'); if (p) activate(p); });
      svg.addEventListener('keydown', function (ev) {
        var p = ev.target.closest && ev.target.closest('.seat'); if (!p) return;
        var dir = {ArrowRight: [1, 0], ArrowLeft: [-1, 0], ArrowDown: [0, 1], ArrowUp: [0, -1]}[ev.key];
        if (dir) { ev.preventDefault(); var n = nearest(p, dir); if (n) n.el.focus(); }
        else if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); activate(p); }
      });
    }
    return load().then(function (d) {
      data = d; defs(); render();
      if (live) bind();
      var raf;
      if (window.ResizeObserver) new ResizeObserver(function () {
        cancelAnimationFrame(raf);
        raf = requestAnimationFrame(function () { if (Math.abs(wrap.clientWidth - W) > 1) render(); });
      }).observe(wrap);
      return {
        data: d,
        set: function (v, c) { if (v) view = v; if (c) color = c; render(); },
        view: function () { return view; }
      };
    });
  }

  window.CLMap = {mount: mount, load: load, counts: counts, swatch: swatch, castPill: castPill, seatName: seatName, short: short, ST: ST, PN: PN, esc: esc};
})();
