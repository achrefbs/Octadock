import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const port = Number.parseInt(process.env.PORT ?? '4173', 10);
const host = '127.0.0.1';

const contentTypes = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.glb', 'model/gltf-binary'],
  ['.html', 'text/html; charset=utf-8'],
  ['.ico', 'image/x-icon'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.png', 'image/png'],
  ['.svg', 'image/svg+xml'],
  ['.wasm', 'application/wasm'],
  ['.woff2', 'font/woff2'],
]);

function resolveRequest(requestUrl) {
  const parsed = new URL(requestUrl ?? '/', 'http://' + host + ':' + port);
  const pathname = decodeURIComponent(parsed.pathname);
  const relative = pathname === '/' ? 'index.html' : '.' + pathname;
  const candidate = path.resolve(webRoot, relative);
  const isInsideRoot = candidate === webRoot || candidate.startsWith(webRoot + path.sep);
  return isInsideRoot ? candidate : null;
}

const server = createServer((request, response) => {
  const candidate = resolveRequest(request.url);
  if (!candidate) {
    response.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
    response.end('Forbidden');
    return;
  }

  let file = candidate;
  if (existsSync(file) && statSync(file).isDirectory()) {
    file = path.join(file, 'index.html');
  }

  if (!existsSync(file) || !statSync(file).isFile()) {
    response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    response.end('Not found');
    return;
  }

  response.writeHead(200, {
    'Cache-Control': 'no-store',
    'Content-Type': contentTypes.get(path.extname(file).toLowerCase()) ?? 'application/octet-stream',
  });

  if (request.method === 'HEAD') {
    response.end();
    return;
  }

  createReadStream(file).pipe(response);
});

server.listen(port, host, () => {
  console.log('Octadock website test server: http://' + host + ':' + port);
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
