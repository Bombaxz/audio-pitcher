// Idioma elegido a mano: se recuerda y la detección automática ya no lo pisa
document.querySelectorAll('[data-lang]').forEach((a) => {
  a.addEventListener('click', () => { try { localStorage.setItem('lang', a.dataset.lang); } catch (e) {} });
});

// Aparición suave al entrar en pantalla
const io = new IntersectionObserver((entries) => {
  for (const e of entries) {
    if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
  }
}, { rootMargin: '0px 0px -10% 0px' });
document.querySelectorAll('.reveal').forEach((el) => io.observe(el));

// Lema: cada palabra se enciende al cruzar la mitad de la pantalla
const tagline = document.querySelector('.words');
if (tagline) {
  tagline.innerHTML = tagline.textContent.trim().split(/\s+/)
    .map((w) => `<span>${w}</span> `).join('');
  const wordIo = new IntersectionObserver((entries) => {
    for (const e of entries) e.target.classList.toggle('on', e.boundingClientRect.top < window.innerHeight * 0.6);
  }, { rootMargin: '0px 0px -40% 0px', threshold: 0 });
  tagline.querySelectorAll('span').forEach((s) => wordIo.observe(s));
}

// Ventana de instalación: se abre al descargar (la descarga sigue sola)
const install = document.getElementById('install');
if (install) {
  install.querySelector('.install-close').addEventListener('click', () => install.close());
  install.addEventListener('click', (e) => { if (e.target === install) install.close(); });
}

// Conteo de descargas (se activa cuando esté cargado Google Analytics)
document.querySelectorAll('[data-download]').forEach((a) => {
  a.addEventListener('click', () => {
    document.querySelectorAll('[data-download]').forEach((b) => b.classList.add('done'));
    if (install && !install.open) install.showModal();
    if (typeof window.gtag === 'function') {
      window.gtag('event', 'file_download', { file_name: 'AudioPitcher-Setup.exe', link_location: a.dataset.download });
    }
  });
});
