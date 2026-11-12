// Applies the stored appearance before the first paint (no flash of the wrong colours).
// A plain script file, because the page's Content-Security-Policy allows no inline scripts.
(function () {
  try {
    var a = JSON.parse(localStorage.getItem('ember-appearance') || 'null') || {};
    var root = document.documentElement;
    var dark = a.scheme === 'dark' || (a.scheme !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches);
    root.classList.add(dark ? 'dark' : 'light');
    root.dataset.design = a.design === 'glut' ? 'glut' : 'efeu';
  } catch (e) {
    document.documentElement.dataset.design = 'efeu';
  }
})();
