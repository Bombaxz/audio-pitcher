// Servidor local de la página con recarga automática al guardar.
// Uso: node tools/web-dev.js
const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', 'web');
const port = 5178;
const types = {
  '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript',
  '.png': 'image/png', '.svg': 'image/svg+xml', '.xml': 'application/xml', '.txt': 'text/plain',
};
const clients = new Set();
const reloadSnippet = `<script>new EventSource('/__reload').onmessage=()=>location.reload()</script>`;

http.createServer((req, res) => {
  if (req.url === '/__reload') {
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
    clients.add(res);
    req.on('close', () => clients.delete(res));
    return;
  }
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p.endsWith('/')) p += 'index.html';
  let file = path.join(root, p);
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  else if (!path.extname(file) && fs.existsSync(file + '.html')) file += '.html';
  let status = 200;
  if (!file.startsWith(root) || !fs.existsSync(file)) { file = path.join(root, '404.html'); status = 404; }
  const ext = path.extname(file);
  let body = fs.readFileSync(file);
  if (ext === '.html' && !req.url.includes('noreload')) body = body.toString().replace('</body>', reloadSnippet + '</body>');
  res.writeHead(status, { 'content-type': types[ext] || 'application/octet-stream', 'cache-control': 'no-store' });
  res.end(body);
}).listen(port, () => {
  console.log(`Página en http://localhost:${port}`);
});

let timer;
fs.watch(root, { recursive: true }, () => {
  clearTimeout(timer);
  timer = setTimeout(() => clients.forEach((c) => c.write('data: reload\n\n')), 150);
});
