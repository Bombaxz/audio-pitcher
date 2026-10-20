// Genera las guías (web/guides y web/es/guias) desde este único archivo, en los dos idiomas.
// Uso: node tools/build-guides.js
const fs = require('fs');
const path = require('path');

const SITE = 'https://audiopitcher.app';
const DL = 'https://github.com/Bombaxz/audio-pitcher/releases/latest/download/AudioPitcher-Setup.exe';
const web = path.join(__dirname, '..', 'web');

const L = {
  en: {
    dir: 'guides', home: '/', other: 'es', otherLabel: 'ES', otherAria: 'Ver en español',
    guides: 'Guides', privacy: '/privacy', privacyLabel: 'Privacy', report: 'Report a problem',
    download: 'Download', downloadFull: 'Download Audio Pitcher', skip: 'Skip to content',
    license: 'Audio Pitcher is free software under the', indexTitle: 'Audio Pitcher guides',
    indexIntro: 'Step by step help to set up Audio Pitcher, fix common problems and get more out of it.',
    homeAria: 'Audio Pitcher, home',
  },
  es: {
    dir: 'es/guias', home: '/es/', other: 'en', otherLabel: 'EN', otherAria: 'View in English',
    guides: 'Guías', privacy: '/es/privacidad', privacyLabel: 'Privacidad', report: 'Reportar un problema',
    download: 'Descargar', downloadFull: 'Descargar Audio Pitcher', skip: 'Saltar al contenido',
    license: 'Audio Pitcher es software libre con licencia', indexTitle: 'Guías de Audio Pitcher',
    indexIntro: 'Ayuda paso a paso para configurar Audio Pitcher, resolver problemas comunes y sacarle más provecho.',
    homeAria: 'Audio Pitcher, inicio',
  },
};

const fig = (src, alt) => `<figure><img src="${src}" alt="${alt}" width="290" height="341" loading="lazy"><figcaption>${alt}</figcaption></figure>`;

// slug por idioma + contenido
const GUIDES = [
  {
    slug: { en: 'getting-started', es: 'primeros-pasos' },
    en: {
      title: 'Getting started with Audio Pitcher',
      desc: 'Install Audio Pitcher, pick an app, change its pitch, choose where you hear it, and use effects and recording.',
      body: `
<h2>1. Install</h2>
<ol>
  <li>Download <b>AudioPitcher-Setup.exe</b> and open it. If Windows shows "Windows protected your PC", click <b>More info</b> and then <b>Run anyway</b>.</li>
  <li>Keep <b>Install VB&#8209;CABLE</b> checked. It's the virtual audio device Audio Pitcher uses. Windows asks permission to install the driver: accept it.</li>
  <li>When the installer asks, <b>restart your PC</b>. It's only needed the first time, and without it you may hear nothing.</li>
</ol>
<h2>2. Pick the app</h2>
<p>Under <b>APP</b> you'll see the apps that are playing sound right now. Pick the one you want to change, for example Spotify.</p>
<ul>
  <li>If your app isn't listed, press play in it and open the list again: Windows only shows apps once they start playing.</li>
  <li><b>More options…</b> at the bottom of the list shows every app with audio.</li>
  <li>Click the star next to an app to keep it at the top.</li>
</ul>
${fig('/img/demo-apps.jpg', 'Picking the app that Audio Pitcher will process')}
<h2>3. Change the pitch</h2>
<ul>
  <li>Drag <b>PITCH</b> to raise or lower the tone up to 12 semitones. The speed doesn't change.</li>
  <li>You can also click the number and type an exact value, like 2.5.</li>
  <li>The <b>↺</b> button next to it brings it back to 0.</li>
  <li><b>FORMANT</b> changes the timbre of voices separately. Use it to keep voices natural when you move the pitch a lot.</li>
</ul>
<h2>4. Choose where you hear it</h2>
<p>Open settings (gear icon) and look at <b>AUDIO OUTPUT</b>. Pick your headphones or speakers. <b>Default device</b> follows whatever Windows is using. Never pick an option that says <b>CABLE</b> there, or you'll hear nothing.</p>
<h2>5. Process all your PC audio</h2>
<p>To change the pitch of everything at once, pick <b>System (VB&#8209;Cable)</b> under APP and set <b>CABLE Input</b> as the output in Windows (speaker icon on the taskbar). When you close Audio Pitcher, it switches Windows back to your headphones or speakers.</p>
<h2>6. Effects</h2>
<p>Turn on <b>FX</b> and open the panel with the small arrow to add reverb, delay or chorus. Each effect has a <b>WET</b> knob that controls how much of it you hear.</p>
<h2>7. Record</h2>
<ol>
  <li>Click the record button at the top. It starts recording what plays on your PC.</li>
  <li>Stop it with <b>⏹</b>. Drag the red edges of the waveform to trim the start and end.</li>
  <li>Press <b>▶</b> to listen to exactly what will be saved. <b>NORM</b> evens out the volume.</li>
  <li><b>Save WAV</b> exports the file.</li>
</ol>
<h2>8. Window options</h2>
<ul>
  <li>The pin icon keeps Audio Pitcher above other windows.</li>
  <li><b>—</b> minimizes it. In settings you can choose whether it goes to the taskbar or the tray next to the clock.</li>
  <li>In settings you can also change the language (English or Spanish), the theme, the colors and the text contrast.</li>
</ul>`,
    },
    es: {
      title: 'Primeros pasos con Audio Pitcher',
      desc: 'Instala Audio Pitcher, elige una app, cámbiale el tono, decide dónde escucharlo y usa los efectos y la grabación.',
      body: `
<h2>1. Instalar</h2>
<ol>
  <li>Descarga <b>AudioPitcher-Setup.exe</b> y ábrelo. Si Windows muestra "Windows protegió tu PC", haz clic en <b>Más información</b> y después en <b>Ejecutar de todas formas</b>.</li>
  <li>Deja marcado <b>Instalar VB&#8209;CABLE</b>. Es el dispositivo de audio virtual que usa Audio Pitcher. Windows pide permiso para instalar el driver: acéptalo.</li>
  <li>Cuando el instalador lo pregunte, <b>reinicia la PC</b>. Solo hace falta la primera vez, y sin reiniciar puede que no se escuche nada.</li>
</ol>
<h2>2. Elegir la app</h2>
<p>En <b>APP</b> aparecen las apps que están sonando en este momento. Elige la que quieres cambiar, por ejemplo Spotify.</p>
<ul>
  <li>Si tu app no aparece, dale play y vuelve a abrir la lista: Windows recién la muestra cuando empieza a sonar.</li>
  <li><b>Más opciones…</b>, al final de la lista, muestra todas las apps con audio.</li>
  <li>Con la estrella al lado de una app la dejas siempre arriba.</li>
</ul>
${fig('/img/demo-apps.jpg', 'Eligiendo la app que va a procesar Audio Pitcher')}
<h2>3. Cambiar el tono</h2>
<ul>
  <li>Arrastra <b>PITCH</b> para subir o bajar el tono hasta 12 semitonos. La velocidad no cambia.</li>
  <li>También puedes hacer clic en el número y escribir un valor exacto, como 2.5.</li>
  <li>El botón <b>↺</b> de al lado lo vuelve a 0.</li>
  <li><b>FORMANT</b> cambia el timbre de las voces por separado. Sirve para que las voces suenen naturales cuando mueves mucho el tono.</li>
</ul>
<h2>4. Elegir dónde se escucha</h2>
<p>Abre la configuración (ícono del engranaje) y mira <b>SALIDA DE AUDIO</b>. Elige tus auriculares o parlantes. <b>Dispositivo predeterminado</b> usa lo mismo que Windows. Nunca elijas ahí una opción que diga <b>CABLE</b>, porque no se escucharía nada.</p>
<h2>5. Procesar todo el audio de la PC</h2>
<p>Para cambiarle el tono a todo a la vez, elige <b>Sistema (VB&#8209;Cable)</b> en APP y pon <b>CABLE Input</b> como salida en Windows (ícono del parlante en la barra de tareas). Al cerrar Audio Pitcher, Windows vuelve solo a tus auriculares o parlantes.</p>
<h2>6. Efectos</h2>
<p>Activa <b>FX</b> y abre el panel con la flechita para sumar reverb, delay o chorus. Cada efecto tiene una perilla <b>WET</b> que controla cuánto se escucha.</p>
<h2>7. Grabar</h2>
<ol>
  <li>Haz clic en el botón de grabar, arriba. Empieza a grabar lo que suena en la PC.</li>
  <li>Detén con <b>⏹</b>. Arrastra los bordes rojos de la onda para recortar el principio y el final.</li>
  <li>Toca <b>▶</b> para escuchar exactamente lo que se va a guardar. <b>NORM</b> empareja el volumen.</li>
  <li><b>Guardar WAV</b> exporta el archivo.</li>
</ol>
<h2>8. Opciones de la ventana</h2>
<ul>
  <li>El ícono del pin deja Audio Pitcher por encima de las demás ventanas.</li>
  <li><b>—</b> lo minimiza. En la configuración eliges si va a la barra de tareas o a la bandeja, junto al reloj.</li>
  <li>En la configuración también cambias el idioma (español o inglés), el tema, los colores y el contraste del texto.</li>
</ul>`,
    },
  },
  {
    slug: { en: 'troubleshooting', es: 'solucion-de-problemas' },
    en: {
      title: 'Troubleshooting: no sound, double sound and VB-CABLE',
      desc: 'Fix the most common Audio Pitcher problems: hearing nothing, hearing the song twice, VB-CABLE not detected and Discord streams.',
      body: `
<h2>I hear nothing after installing</h2>
<p>Restart your PC. VB&#8209;CABLE is a driver and Windows only activates it after a restart. If Audio Pitcher says <b>"VB-CABLE is installed, but Windows hasn't activated it yet"</b>, that's exactly what's missing.</p>
<h2>I hear nothing after closing Audio Pitcher</h2>
<p>Windows may have left VB&#8209;CABLE as your main output. Click the speaker icon on the taskbar, open the device list and pick your headphones or speakers. Since version 1.0.3, Audio Pitcher does this for you when it closes.</p>
<p>If only one app stays silent, open <b>Settings › System › Sound › Volume mixer</b>, find that app and set its output to <b>Default</b>.</p>
<h2>I hear the song twice</h2>
<ul>
  <li>Pick the app again in the APP list so Audio Pitcher reconnects it.</li>
  <li>In the Windows volume mixer, while Audio Pitcher is open, the app you're processing should be playing to <b>CABLE</b>. If it isn't, close and reopen Audio Pitcher.</li>
  <li>In <b>System</b> mode, Windows' output must be <b>CABLE Input</b>.</li>
</ul>
<h2>Audio Pitcher says VB-CABLE is not installed</h2>
<ul>
  <li>If you just installed it, restart your PC.</li>
  <li>If it says it's <b>disabled</b>, open the Windows sound control panel, and in the Playback and Recording tabs right click <b>CABLE</b> and choose <b>Enable</b>.</li>
  <li>If it really isn't installed, run the Audio Pitcher installer again and keep VB&#8209;CABLE checked.</li>
</ul>
<h2>My app doesn't show up in the list</h2>
<p>Press play in it and open the list again. Windows only creates the app's audio once it starts playing. Also check <b>More options…</b> at the bottom of the list.</p>
<h2>I hear echo or feedback</h2>
<p>In Audio Pitcher's settings, <b>AUDIO OUTPUT</b> must be your headphones or speakers, never an option that says CABLE.</p>
<h2>On Discord, people hear the song twice when I share my whole screen</h2>
<p>When you share the entire screen, Discord captures the original audio of every app directly, even if it plays through VB&#8209;CABLE, plus what Audio Pitcher plays. Audio Pitcher can't hide the original from Discord. Sharing a single window instead of the whole screen avoids the doubled sound, but then Audio Pitcher's audio isn't included in the stream either.</p>
<h2>Still not working?</h2>
<p>In Audio Pitcher's settings, at the very bottom, click <b>Report a problem</b>. It saves a report on your Desktop with the app and audio device details (nothing personal). Attach it to a new issue on <a href="https://github.com/Bombaxz/audio-pitcher/issues" rel="noopener">GitHub</a>.</p>`,
    },
    es: {
      title: 'Solución de problemas: no se escucha, se escucha doble y VB-CABLE',
      desc: 'Resuelve los problemas más comunes de Audio Pitcher: no se escucha nada, la canción suena doble, VB-CABLE no detectado y transmisiones en Discord.',
      body: `
<h2>No se escucha nada después de instalar</h2>
<p>Reinicia la PC. VB&#8209;CABLE es un driver y Windows recién lo activa después de reiniciar. Si Audio Pitcher dice <b>"VB-CABLE está instalado, pero Windows todavía no lo activó"</b>, es exactamente eso lo que falta.</p>
<h2>No se escucha nada después de cerrar Audio Pitcher</h2>
<p>Puede que Windows haya dejado VB&#8209;CABLE como salida principal. Haz clic en el ícono del parlante de la barra de tareas, abre la lista de dispositivos y elige tus auriculares o parlantes. Desde la versión 1.0.3, Audio Pitcher lo hace solo al cerrarse.</p>
<p>Si solo una app quedó muda, abre <b>Configuración › Sistema › Sonido › Mezclador de volumen</b>, busca esa app y pon su salida en <b>Predeterminado</b>.</p>
<h2>La canción se escucha doble</h2>
<ul>
  <li>Vuelve a elegir la app en la lista APP para que Audio Pitcher la reconecte.</li>
  <li>En el mezclador de volumen de Windows, con Audio Pitcher abierto, la app que estás procesando tiene que estar sonando por <b>CABLE</b>. Si no, cierra y vuelve a abrir Audio Pitcher.</li>
  <li>En el modo <b>Sistema</b>, la salida de Windows tiene que ser <b>CABLE Input</b>.</li>
</ul>
<h2>Audio Pitcher dice que VB-CABLE no está instalado</h2>
<ul>
  <li>Si recién lo instalaste, reinicia la PC.</li>
  <li>Si dice que está <b>desactivado</b>, abre el panel de sonido de Windows y, en las pestañas Reproducción y Grabación, haz clic derecho en <b>CABLE</b> y elige <b>Habilitar</b>.</li>
  <li>Si realmente no está instalado, vuelve a ejecutar el instalador de Audio Pitcher y deja marcado VB&#8209;CABLE.</li>
</ul>
<h2>Mi app no aparece en la lista</h2>
<p>Dale play y vuelve a abrir la lista. Windows recién crea el audio de la app cuando empieza a sonar. Revisa también <b>Más opciones…</b>, al final de la lista.</p>
<h2>Se escucha eco o acople</h2>
<p>En la configuración de Audio Pitcher, la <b>salida de audio</b> tiene que ser tus auriculares o parlantes, nunca una opción que diga CABLE.</p>
<h2>En Discord escuchan la canción doble cuando comparto la pantalla entera</h2>
<p>Al compartir la pantalla entera, Discord captura directamente el audio original de cada app, aunque suene por VB&#8209;CABLE, y además lo que reproduce Audio Pitcher. Audio Pitcher no puede ocultarle el original a Discord. Si compartes una sola ventana en lugar de la pantalla entera no se duplica, pero en ese caso el audio de Audio Pitcher tampoco entra en la transmisión.</p>
<h2>¿Sigue sin funcionar?</h2>
<p>En la configuración de Audio Pitcher, abajo de todo, haz clic en <b>Reportar un problema</b>. Guarda un reporte en tu Escritorio con datos del programa y de tus dispositivos de audio (nada personal). Adjúntalo en un tema nuevo en <a href="https://github.com/Bombaxz/audio-pitcher/issues" rel="noopener">GitHub</a>.</p>`,
    },
  },
  {
    slug: { en: 'nightcore-spotify', es: 'nightcore-en-spotify' },
    en: {
      title: 'How to listen to Spotify in nightcore style on PC',
      desc: 'Raise the pitch of any Spotify song in real time on Windows for a nightcore sound, without downloading or editing the track.',
      body: `
<p>Nightcore versions raise the pitch of a song so it sounds brighter and more energetic. With Audio Pitcher you can get that sound on any Spotify song, live, without downloading or editing anything.</p>
<h2>Steps</h2>
<ol>
  <li>Open Spotify and press play.</li>
  <li>In Audio Pitcher, pick <b>Spotify</b> under APP.</li>
  <li>Raise <b>PITCH</b> between <b>+3 and +5</b> semitones. Around +4 is the classic nightcore range.</li>
  <li>Optional: turn on <b>FX</b> and add a little reverb (WET around 15%) for a fuller sound.</li>
</ol>
${fig('/img/demo-pitch.jpg', 'Raising the pitch of the song in Audio Pitcher')}
<div class="note"><p>Classic nightcore also speeds the song up. Audio Pitcher only changes the pitch and keeps the original speed, so the song lasts the same.</p></div>
<h2>Keep voices from sounding squeaky</h2>
<p>If vocals sound too thin at +4 or more, move <b>FORMANT</b> down a little (for example −2). The tone stays high but the voice sounds more natural.</p>
<h2>Save a clip</h2>
<p>Use the record button to capture a fragment, trim it and save it as WAV. Keep it for personal use: songs are copyrighted.</p>`,
    },
    es: {
      title: 'Cómo escuchar Spotify en versión nightcore en la PC',
      desc: 'Sube el tono de cualquier canción de Spotify en tiempo real en Windows para lograr un sonido nightcore, sin descargar ni editar el tema.',
      body: `
<p>Las versiones nightcore suben el tono de una canción para que suene más brillante y con más energía. Con Audio Pitcher logras ese sonido con cualquier canción de Spotify, en vivo, sin descargar ni editar nada.</p>
<h2>Pasos</h2>
<ol>
  <li>Abre Spotify y dale play.</li>
  <li>En Audio Pitcher, elige <b>Spotify</b> en APP.</li>
  <li>Sube <b>PITCH</b> entre <b>+3 y +5</b> semitonos. Alrededor de +4 es el nightcore clásico.</li>
  <li>Opcional: activa <b>FX</b> y suma un poco de reverb (WET cerca de 15%) para un sonido más lleno.</li>
</ol>
${fig('/img/demo-pitch.jpg', 'Subiendo el tono de la canción en Audio Pitcher')}
<div class="note"><p>El nightcore clásico además acelera la canción. Audio Pitcher solo cambia el tono y mantiene la velocidad original, así que el tema dura lo mismo.</p></div>
<h2>Que las voces no suenen chillonas</h2>
<p>Si las voces quedan muy finas en +4 o más, baja un poco <b>FORMANT</b> (por ejemplo −2). El tono sigue alto pero la voz suena más natural.</p>
<h2>Guardar un fragmento</h2>
<p>Usa el botón de grabar para capturar un fragmento, recórtalo y guárdalo en WAV. Úsalo solo para ti: las canciones tienen derechos de autor.</p>`,
    },
  },
  {
    slug: { en: 'practice-in-another-key', es: 'practicar-en-otra-tonalidad' },
    en: {
      title: 'How to practice a song in another key',
      desc: 'Transpose any song playing on your PC to the key of your instrument or your voice, in real time, to practice guitar, bass or singing.',
      body: `
<p>If a song is too high to sing, or your guitar is tuned differently, you can move it to another key while it plays, from Spotify, YouTube or any player, without changing its speed.</p>
<h2>Steps</h2>
<ol>
  <li>Play the song in your app. YouTube in a browser works too (the change applies to the whole browser).</li>
  <li>In Audio Pitcher, pick that app under APP.</li>
  <li>Move <b>PITCH</b> the number of semitones you need, using the table below.</li>
</ol>
<h2>How many semitones?</h2>
<table>
  <tr><th>Situation</th><th>PITCH</th></tr>
  <tr><td>Guitar tuned half a step down (Eb)</td><td>−1</td></tr>
  <tr><td>Guitar in drop D, song in standard tuning</td><td>−2</td></tr>
  <tr><td>Song too high to sing</td><td>−2 to −4</td></tr>
  <tr><td>Song too low to sing</td><td>+2 to +4</td></tr>
  <tr><td>Going from E to D, A to G, etc.</td><td>−2</td></tr>
  <tr><td>Going one step up (C to D)</td><td>+2</td></tr>
</table>
<p>Every semitone is one fret on a guitar. One full step is 2 semitones.</p>
<h2>Tips</h2>
<ul>
  <li>Small changes (±1 or ±2) sound almost natural. Bigger ones are noticeable on vocals: lower <b>FORMANT</b> a bit when raising the pitch, or raise it when lowering.</li>
  <li>Record your practice with the record button and listen back with <b>▶</b> before saving.</li>
</ul>`,
    },
    es: {
      title: 'Cómo practicar una canción en otra tonalidad',
      desc: 'Transporta cualquier canción que suene en tu PC a la tonalidad de tu instrumento o de tu voz, en tiempo real, para practicar guitarra, bajo o canto.',
      body: `
<p>Si una canción te queda muy alta para cantar, o tu guitarra está afinada distinto, puedes pasarla a otra tonalidad mientras suena, desde Spotify, YouTube o cualquier reproductor, sin cambiar su velocidad.</p>
<h2>Pasos</h2>
<ol>
  <li>Reproduce la canción en tu app. YouTube en el navegador también funciona (el cambio se aplica a todo el navegador).</li>
  <li>En Audio Pitcher, elige esa app en APP.</li>
  <li>Mueve <b>PITCH</b> los semitonos que necesites, según la tabla de abajo.</li>
</ol>
<h2>¿Cuántos semitonos?</h2>
<table>
  <tr><th>Situación</th><th>PITCH</th></tr>
  <tr><td>Guitarra afinada medio tono abajo (Eb)</td><td>−1</td></tr>
  <tr><td>Guitarra en drop D y canción en afinación estándar</td><td>−2</td></tr>
  <tr><td>Canción muy alta para cantar</td><td>−2 a −4</td></tr>
  <tr><td>Canción muy baja para cantar</td><td>+2 a +4</td></tr>
  <tr><td>Pasar de Mi a Re, de La a Sol, etc.</td><td>−2</td></tr>
  <tr><td>Subir un tono (de Do a Re)</td><td>+2</td></tr>
</table>
<p>Cada semitono es un traste de la guitarra. Un tono completo son 2 semitonos.</p>
<h2>Consejos</h2>
<ul>
  <li>Los cambios chicos (±1 o ±2) suenan casi naturales. Los más grandes se notan en las voces: baja un poco <b>FORMANT</b> cuando subes el tono, o súbelo cuando lo bajas.</li>
  <li>Graba tu práctica con el botón de grabar y escúchala con <b>▶</b> antes de guardarla.</li>
</ul>`,
    },
  },
];

function page({ lang, url, altUrl, title, desc, body, crumbs }) {
  const t = L[lang];
  const o = L[t.other];
  return `<!doctype html>
<html lang="${lang}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title} · Audio Pitcher</title>
  <meta name="description" content="${desc}">
  <link rel="canonical" href="${SITE}${url}">
  <link rel="alternate" hreflang="${lang}" href="${SITE}${url}">
  <link rel="alternate" hreflang="${t.other}" href="${SITE}${altUrl}">
  <meta name="theme-color" content="#000000">
  <meta property="og:type" content="article">
  <meta property="og:site_name" content="Audio Pitcher">
  <meta property="og:title" content="${title}">
  <meta property="og:description" content="${desc}">
  <meta property="og:url" content="${SITE}${url}">
  <meta property="og:image" content="${SITE}/img/og.png">
  <link rel="icon" href="/favicon.ico" sizes="48x48">
  <link rel="icon" type="image/png" sizes="96x96" href="/favicon-96.png">
  <link rel="icon" type="image/png" sizes="192x192" href="/favicon-192.png">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600;700&display=swap" rel="stylesheet">
  <script src="https://unpkg.com/@phosphor-icons/web@2.1.1" defer></script>
  <link rel="stylesheet" href="/styles.css">
  <script src="/analytics.js"></script>
</head>
<body>
  <a class="skip" href="#main">${t.skip}</a>
  <header class="topbar">
    <div class="topbar-inner">
      <a class="brand" href="${t.home}" aria-label="${t.homeAria}">
        <img src="/img/logo.svg" alt="" width="32" height="32">
        <span>Audio Pitcher</span>
      </a>
      <nav aria-label="Main">
        <a class="lang" href="${altUrl}" hreflang="${t.other}" lang="${t.other}" data-lang="${t.other}" aria-label="${t.otherAria}">${t.otherLabel}</a>
        <a class="btn btn-sm" href="${DL}" data-download="nav">${t.download}</a>
      </nav>
    </div>
  </header>

  <main id="main" class="section article">
    ${crumbs ? `<p class="crumbs"><a href="/${t.dir}/">${t.guides}</a></p>` : ''}
    <h1>${title}</h1>
    <p class="intro">${desc}</p>
    ${body}
    <div class="cta">
      <a class="btn" href="${DL}" data-download="guide"><i class="ph ph-windows-logo"></i> ${t.downloadFull}</a>
    </div>
  </main>

  <footer class="footer">
    <p>${t.license} <a href="https://github.com/Bombaxz/audio-pitcher/blob/main/LICENSE" rel="noopener">GPL&nbsp;3.0</a>.</p>
    <nav aria-label="Footer">
      <a href="/${t.dir}/">${t.guides}</a>
      <a href="https://github.com/Bombaxz/audio-pitcher" rel="noopener">GitHub</a>
      <a href="https://github.com/Bombaxz/audio-pitcher/issues" rel="noopener">${t.report}</a>
      <a href="${t.privacy}">${t.privacyLabel}</a>
    </nav>
  </footer>
  <script src="/script.js" defer></script>
</body>
</html>
`;
}

const write = (rel, html) => {
  const file = path.join(web, rel);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, html);
  console.log('  ' + rel);
};

const urls = [];
for (const lang of ['en', 'es']) {
  const t = L[lang], o = L[t.other];
  // índice
  const list = GUIDES.map((g) => `<li><a href="/${t.dir}/${g.slug[lang]}"><h2>${g[lang].title}</h2><p>${g[lang].desc}</p></a></li>`).join('\n      ');
  write(`${t.dir}/index.html`, page({
    lang, url: `/${t.dir}/`, altUrl: `/${o.dir}/`, title: t.indexTitle, desc: t.indexIntro,
    body: `<ul class="guide-list">\n      ${list}\n    </ul>`, crumbs: false,
  }));
  urls.push([`/${t.dir}/`, `/${o.dir}/`, lang]);
  for (const g of GUIDES) {
    const url = `/${t.dir}/${g.slug[lang]}`, alt = `/${o.dir}/${g.slug[t.other]}`;
    write(`${t.dir}/${g.slug[lang]}.html`, page({ lang, url, altUrl: alt, ...g[lang], crumbs: true }));
    urls.push([url, alt, lang]);
  }
}

// sitemap completo (páginas principales + guías)
const today = new Date().toISOString().slice(0, 10);
const pairs = [['/', '/es/'], ['/privacy', '/es/privacidad']];
let xml = `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n`;
const entry = (loc, en, es) => `  <url>\n    <loc>${SITE}${loc}</loc>\n    <xhtml:link rel="alternate" hreflang="en" href="${SITE}${en}"/>\n    <xhtml:link rel="alternate" hreflang="es" href="${SITE}${es}"/>\n    <lastmod>${today}</lastmod>\n  </url>\n`;
for (const [en, es] of pairs) xml += entry(en, en, es) + entry(es, en, es);
for (const [url, alt, lang] of urls) xml += lang === 'en' ? entry(url, url, alt) : entry(url, alt, url);
xml += '</urlset>\n';
fs.writeFileSync(path.join(web, 'sitemap.xml'), xml);
console.log('  sitemap.xml');
