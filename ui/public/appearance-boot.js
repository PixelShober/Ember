// Applies the stored appearance before the first paint (no flash of the wrong colours).
// A plain script file, because the page's Content-Security-Policy allows no inline scripts.
(function () {
  try {
    var a = JSON.parse(localStorage.getItem('ember-appearance') || 'null') || {};
    var root = document.documentElement;
    if (a.scheme === 'light' || a.scheme === 'dark') root.classList.add(a.scheme);
    root.dataset.design = a.design === 'glut' ? 'glut' : 'efeu';
  } catch (e) {
    document.documentElement.dataset.design = 'efeu';
  }
})();
