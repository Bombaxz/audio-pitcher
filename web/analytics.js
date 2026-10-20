// Google Analytics con Consent Mode: en Europa sin cookies hasta que la persona acepte.
(function () {
  var ID = 'G-Z9EXZ0W0NS';
  var EEA = ['AT','BE','BG','HR','CY','CZ','DK','EE','FI','FR','DE','GR','HU','IS','IE','IT','LV','LI','LT','LU',
             'MT','NL','NO','PL','PT','RO','SK','SI','ES','SE','GB','CH'];
  window.dataLayer = window.dataLayer || [];
  window.gtag = function () { window.dataLayer.push(arguments); };
  var denied = { analytics_storage: 'denied', ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied' };
  gtag('consent', 'default', Object.assign({ region: EEA }, denied));
  gtag('consent', 'default', { analytics_storage: 'granted', ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied' });

  var choice = null;
  try { choice = localStorage.getItem('consent'); } catch (e) {}
  if (choice === 'granted') gtag('consent', 'update', { analytics_storage: 'granted' });

  gtag('js', new Date());
  gtag('config', ID);
  var s = document.createElement('script');
  s.async = true;
  s.src = 'https://www.googletagmanager.com/gtag/js?id=' + ID;
  document.head.appendChild(s);

  // Microsoft Clarity (mapas de calor y grabaciones): en Europa solo con consentimiento
  function loadClarity() {
    (function (c, l, a, r, i, t, y) {
      c[a] = c[a] || function () { (c[a].q = c[a].q || []).push(arguments); };
      t = l.createElement(r); t.async = 1; t.src = 'https://www.clarity.ms/tag/' + i;
      y = l.getElementsByTagName(r)[0]; y.parentNode.insertBefore(t, y);
    })(window, document, 'clarity', 'script', 'z0sx32zr4o');
  }

  // Aviso solo para quien está en Europa (por zona horaria) y todavía no eligió
  var tz = '';
  try { tz = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) {}
  var europe = tz.indexOf('Europe/') === 0;
  if (!europe || choice === 'granted') loadClarity();
  if (choice || !europe) return;

  document.addEventListener('DOMContentLoaded', function () {
    var es = (document.documentElement.lang || '').indexOf('es') === 0;
    var bar = document.createElement('div');
    bar.className = 'consent';
    bar.setAttribute('role', 'region');
    bar.setAttribute('aria-label', es ? 'Cookies' : 'Cookies');
    bar.innerHTML =
      '<p>' + (es ? 'Usamos cookies de análisis para saber cuánta gente visita la página. ' : 'We use analytics cookies to count visits to this page. ') +
      '<a href="' + (es ? '/es/privacidad' : '/privacy') + '">' + (es ? 'Más información' : 'Learn more') + '</a></p>' +
      '<div><button type="button" data-c="denied">' + (es ? 'Rechazar' : 'Reject') + '</button>' +
      '<button type="button" data-c="granted" class="primary">' + (es ? 'Aceptar' : 'Accept') + '</button></div>';
    bar.addEventListener('click', function (e) {
      var c = e.target.getAttribute && e.target.getAttribute('data-c');
      if (!c) return;
      try { localStorage.setItem('consent', c); } catch (err) {}
      if (c === 'granted') { gtag('consent', 'update', { analytics_storage: 'granted' }); loadClarity(); }
      bar.remove();
    });
    document.body.appendChild(bar);
  });
})();
