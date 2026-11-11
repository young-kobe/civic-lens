/* Prototype only: shows one page state at a time, and picks a skin. Use the buttons or add #name to the address. */
(function () {
  var SKINS = [['current', 'Current'], ['poster', 'Poster'], ['newsroom', 'Newsroom'], ['mosaic', 'Mosaic'], ['mosaic2', 'Mosaic v2']];
  var root = document.documentElement;
  var def = document.body.getAttribute('data-states');
  var states = def ? def.split('|').map(function (s) { var p = s.split(':'); return { id: p[0], label: p[1], zip: p[2] || '' }; }) : [];
  var bar = document.createElement('div');
  bar.className = 'state-bar';
  var wrap = document.createElement('div');
  wrap.className = 'wrap';
  bar.appendChild(wrap);

  function group(label, visible) {
    var g = document.createElement('div');
    g.className = 'state-group';
    g.setAttribute('role', 'group');
    g.setAttribute('aria-label', label);
    if (visible) g.innerHTML = '<span class="lbl">' + label + '</span>';
    wrap.appendChild(g);
    return g;
  }
  function button(g, attr, id, text) {
    var b = document.createElement('button');
    b.type = 'button'; b.textContent = text; b.setAttribute(attr, id); b.setAttribute('aria-pressed', 'false');
    g.appendChild(b);
  }

  var stateGroup = states.length ? group('Prototype: show a page state', true) : null;
  states.forEach(function (s) { button(stateGroup, 'data-st', s.id, s.label); });
  var skinGroup = group('Prototype skin', false);
  skinGroup.classList.add('skin-group');
  SKINS.forEach(function (s) { button(skinGroup, 'data-skin-pick', s[0], s[1]); });

  var anchor = document.querySelector('.hero') || document.getElementById('main') || document.querySelector('main');
  anchor.parentNode.insertBefore(bar, anchor);

  function showSkin(id) {
    if (id === 'current') root.removeAttribute('data-skin'); else root.setAttribute('data-skin', id);
    skinGroup.querySelectorAll('[data-skin-pick]').forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-skin-pick') === id)); });
  }
  showSkin(root.getAttribute('data-skin') || 'current');
  skinGroup.addEventListener('click', function (ev) {
    var b = ev.target.closest('[data-skin-pick]');
    if (!b) return;
    var id = b.getAttribute('data-skin-pick');
    showSkin(id);
    try { localStorage.setItem('cl-skin', id); } catch (x) {}
  });

  if (!states.length) return;
  function inList(attr, id) { return attr.split(' ').indexOf(id) >= 0; }
  function apply(id) {
    document.querySelectorAll('[data-show],[data-hide]').forEach(function (el) {
      var s = el.getAttribute('data-show'), h = el.getAttribute('data-hide');
      el.classList.toggle('st-off', (s !== null && !inList(s, id)) || (h !== null && inList(h, id)));
    });
    states.forEach(function (s) {
      stateGroup.querySelector('[data-st="' + s.id + '"]').setAttribute('aria-pressed', String(s.id === id));
      if (s.id === id) { var z = document.getElementById('zip'); if (z) z.value = s.zip; }
    });
  }
  function fromUrl() {
    var m = location.hash.match(/^#(?:state=)?([a-z0-9-]+)$/);
    return m && states.some(function (s) { return s.id === m[1]; }) ? m[1] : null;
  }
  stateGroup.addEventListener('click', function (ev) {
    var b = ev.target.closest('[data-st]');
    if (!b) return;
    history.replaceState(null, '', '#' + b.getAttribute('data-st'));
    apply(b.getAttribute('data-st'));
  });
  window.addEventListener('hashchange', function () { var s = fromUrl(); if (s) apply(s); });
  apply(fromUrl() || states[0].id);
})();
