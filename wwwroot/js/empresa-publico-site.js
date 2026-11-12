'use strict';
(() => {
  const toggle = document.querySelector('#mobileNavToggle');
  const nav = document.querySelector('#mainNavigation');
  toggle?.addEventListener('click', () => {
    const open = nav.classList.toggle('is-open');
    toggle.setAttribute('aria-expanded', String(open));
  });
  document.querySelectorAll('#mainNavigation a').forEach(link => link.addEventListener('click', () => {
    nav?.classList.remove('is-open');
    toggle?.setAttribute('aria-expanded', 'false');
  }));
})();
