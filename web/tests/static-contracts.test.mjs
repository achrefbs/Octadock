import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { parse } from 'parse5';

const webRoot = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const contentRoots = [webRoot, path.join(webRoot, 'concepts')];
const htmlFiles = contentRoots.flatMap(directory => readdirSync(directory)
  .filter((name) => name.endsWith('.html'))
  .map((name) => path.join(directory, name)))
  .sort((left, right) => left.localeCompare(right));
const cssFiles = contentRoots.flatMap(directory => readdirSync(directory)
  .filter((name) => name.endsWith('.css'))
  .map((name) => path.join(directory, name)))
  .sort((left, right) => left.localeCompare(right));

function visit(node, callback) {
  callback(node);
  for (const child of node.childNodes ?? []) visit(child, callback);
  if (node.content) visit(node.content, callback);
}

function attributes(node) {
  return new Map((node.attrs ?? []).map(({ name, value }) => [name, value]));
}

function textContent(node) {
  let text = node.value ?? '';
  for (const child of node.childNodes ?? []) text += textContent(child);
  return text;
}

function displayPath(file) {
  return path.relative(webRoot, file).replaceAll('\\', '/');
}

function sourceLine(node) {
  return node.sourceCodeLocation?.startLine ?? '?';
}

function parseLocalReference(reference, sourceFile) {
  const value = reference.trim();
  if (!value) return { kind: 'empty' };
  if (/^javascript:/i.test(value)) return { kind: 'unsafe' };
  if (/^(?:https?:|mailto:|tel:|data:|blob:)/i.test(value)) return { kind: 'external' };

  const hashIndex = value.indexOf('#');
  const fragment = hashIndex >= 0 ? decodeURIComponent(value.slice(hashIndex + 1)) : '';
  const withoutFragment = hashIndex >= 0 ? value.slice(0, hashIndex) : value;
  const decodedPath = decodeURIComponent(withoutFragment.split('?')[0]);
  const file = decodedPath.length === 0
    ? sourceFile
    : path.resolve(
      decodedPath.startsWith('/') ? webRoot : path.dirname(sourceFile),
      decodedPath.replace(/^\//, ''),
    );

  return { kind: 'local', file, fragment };
}

function referenceExists(reference, sourceFile, idsByFile) {
  const parsed = parseLocalReference(reference, sourceFile);
  if (parsed.kind !== 'local') return parsed.kind !== 'unsafe' && parsed.kind !== 'empty';
  if (parsed.file !== webRoot && !parsed.file.startsWith(webRoot + path.sep)) return false;

  let target = parsed.file;
  try {
    if (statSync(target).isDirectory()) target = path.join(target, 'index.html');
  } catch {
    return false;
  }

  if (parsed.fragment) return idsByFile.get(target)?.has(parsed.fragment) ?? false;
  return true;
}

function collectJavaScript(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const candidate = path.join(directory, entry.name);
    if (entry.isDirectory()) return collectJavaScript(candidate);
    return entry.isFile() && entry.name.endsWith('.js') ? [candidate] : [];
  });
}

const documents = new Map();
const idsByFile = new Map();
const parseErrorsByFile = new Map();
for (const file of htmlFiles) {
  const parseErrors = [];
  const document = parse(readFileSync(file, 'utf8'), {
    onParseError: (error) => parseErrors.push(error),
    sourceCodeLocationInfo: true,
  });
  documents.set(file, document);
  parseErrorsByFile.set(file, parseErrors);
  const ids = new Set();
  visit(document, (node) => {
    const id = attributes(node).get('id');
    if (id) ids.add(id);
  });
  idsByFile.set(file, ids);
}

test('every HTML page has a semantic, addressable static document', () => {
  const failures = [];

  for (const [file, document] of documents) {
    const seenIds = new Set();
    let title = '';
    let lang = '';
    let mains = 0;
    let headings = 0;
    let viewports = 0;

    for (const error of parseErrorsByFile.get(file)) {
      failures.push(
        displayPath(file) + ':' + error.startLine + ' HTML parse error ' + error.code,
      );
    }

    visit(document, (node) => {
      const attrs = attributes(node);
      if (node.tagName === 'html') lang = attrs.get('lang') ?? '';
      if (node.tagName === 'title') title = textContent(node).trim();
      if (node.tagName === 'main') mains += 1;
      if (node.tagName === 'h1') headings += 1;
      if (node.tagName === 'meta' && attrs.get('name')?.toLowerCase() === 'viewport') viewports += 1;

      const id = attrs.get('id');
      if (id && seenIds.has(id)) {
        failures.push(displayPath(file) + ':' + sourceLine(node) + ' duplicate id #' + id);
      }
      if (id) seenIds.add(id);

      if (node.tagName === 'script' && ['application/ld+json', 'importmap'].includes(attrs.get('type'))) {
        try {
          JSON.parse(textContent(node));
        } catch (error) {
          failures.push(
            displayPath(file) + ':' + sourceLine(node) + ' invalid ' + attrs.get('type') + ' JSON: ' + error.message,
          );
        }
      }
    });

    if (lang !== 'en') failures.push(displayPath(file) + ' must declare lang=\"en\"');
    if (!title) failures.push(displayPath(file) + ' must have a non-empty title');
    if (mains !== 1) failures.push(displayPath(file) + ' must contain exactly one main landmark (found ' + mains + ')');
    if (headings !== 1) failures.push(displayPath(file) + ' must contain exactly one h1 (found ' + headings + ')');
    if (viewports !== 1) failures.push(displayPath(file) + ' must contain exactly one viewport meta tag (found ' + viewports + ')');
  }

  assert.deepEqual(failures, [], failures.join('\n'));
});

test('all HTML links, fragments, and runtime assets resolve locally', () => {
  const failures = [];
  const resourceAttributes = new Map([
    ['audio', ['src']],
    ['iframe', ['src']],
    ['img', ['src', 'srcset']],
    ['link', ['href']],
    ['object', ['data']],
    ['script', ['src']],
    ['source', ['src', 'srcset']],
    ['video', ['src', 'poster']],
  ]);

  for (const [file, document] of documents) {
    visit(document, (node) => {
      const attrs = attributes(node);
      const references = [];
      if (node.tagName === 'a' && attrs.has('href')) {
        references.push(['href', attrs.get('href'), true]);
      }
      for (const name of resourceAttributes.get(node.tagName) ?? []) {
        if (!attrs.has(name)) continue;
        const values = name === 'srcset'
          ? attrs.get(name).split(',').map((part) => part.trim().split(/\s+/)[0])
          : [attrs.get(name)];
        for (const value of values) references.push([name, value, false]);
      }

      for (const [name, reference, allowExternal] of references) {
        const parsed = parseLocalReference(reference, file);
        if (parsed.kind === 'external' && !allowExternal) {
          failures.push(
            displayPath(file) + ':' + sourceLine(node) + ' external runtime ' + name + '=' + JSON.stringify(reference),
          );
          continue;
        }
        if (!referenceExists(reference, file, idsByFile)) {
          failures.push(
            displayPath(file) + ':' + sourceLine(node) + ' unresolved ' + name + '=' + JSON.stringify(reference),
          );
        }
      }

      if (node.tagName === 'script' && attrs.get('type') === 'importmap') {
        const importMap = JSON.parse(textContent(node));
        for (const [specifier, reference] of Object.entries(importMap.imports ?? {})) {
          if (!referenceExists(reference, file, idsByFile)) {
            failures.push(
              displayPath(file) + ':' + sourceLine(node) + ' unresolved import map ' + specifier + ' -> ' + reference,
            );
          }
        }
      }
    });
  }

  assert.deepEqual(failures, [], failures.join('\n'));
});

test('CSS font and image references resolve without external runtime dependencies', () => {
  const failures = [];
  const urlPattern = /url\(\s*(?:(["'])(.*?)\1|([^\s)'";]+))\s*\)/gu;

  for (const file of cssFiles) {
    const css = readFileSync(file, 'utf8');
    for (const match of css.matchAll(urlPattern)) {
      const reference = match[2] ?? match[3];
      if (reference.startsWith('#') || reference.startsWith('data:')) continue;
      const parsed = parseLocalReference(reference, file);
      if (parsed.kind === 'external') {
        failures.push(displayPath(file) + ' external CSS resource ' + reference);
      } else if (!referenceExists(reference, file, idsByFile)) {
        failures.push(displayPath(file) + ' unresolved CSS resource ' + reference);
      }
    }
  }

  assert.deepEqual(failures, [], failures.join('\n'));
});

test('the first-party module and model resource graph resolves', () => {
  const failures = [];
  const importMaps = [];
  for (const [file, document] of documents) {
    visit(document, (node) => {
      if (node.tagName === 'script' && attributes(node).get('type') === 'importmap') {
        importMaps.push({ file, imports: JSON.parse(textContent(node)).imports ?? {} });
      }
    });
  }
  const importPattern = /(?:\bimport\s+(?:[^'";]*?\s+from\s+)?|\bexport\s+[^'";]*?\s+from\s+)["']([^"']+)["']/gu;
  const dynamicImportPattern = /\bimport\(\s*["']([^"']+)["']\s*\)/gu;
  const moduleResourcePattern = /new\s+URL\(\s*["']([^"']+)["']\s*,\s*import\.meta\.url\s*\)/gu;
  const fetchPattern = /\bfetch\(\s*["']([^"']+)["']/gu;

  const runtimeFiles = ['assets', 'concepts'].flatMap(directory => collectJavaScript(path.join(webRoot, directory)));
  for (const file of runtimeFiles) {
    const source = readFileSync(file, 'utf8');
    const imports = [
      ...source.matchAll(importPattern),
      ...source.matchAll(dynamicImportPattern),
    ].map((match) => match[1]);

    for (const specifier of imports) {
      if (specifier.startsWith('.') || specifier.startsWith('/')) {
        if (!referenceExists(specifier, file, idsByFile)) {
          failures.push(displayPath(file) + ' unresolved module import ' + specifier);
        }
        continue;
      }

      const mappings = importMaps.map(map => ({
        file: map.file,
        entry: Object.entries(map.imports)
          .sort(([left], [right]) => right.length - left.length)
          .find(([key]) => specifier === key || (key.endsWith('/') && specifier.startsWith(key))),
      })).filter(map => map.entry);
      if (mappings.length === 0) {
        failures.push(displayPath(file) + ' bare import has no import-map entry: ' + specifier);
        continue;
      }

      // Import-map URLs are relative to their declaring HTML document. The
      // shared modules are consumed from both / and /concepts/.
      for (const mapping of mappings) {
        const [key, mappedBase] = mapping.entry;
        const mappedReference = mappedBase + specifier.slice(key.length);
        if (!referenceExists(mappedReference, mapping.file, idsByFile)) {
          failures.push(
            displayPath(file) + ' unresolved mapped import ' + specifier + ' from ' + displayPath(mapping.file) + ' -> ' + mappedReference,
          );
        }
      }
    }

    for (const match of source.matchAll(moduleResourcePattern)) {
      if (!referenceExists(match[1], file, idsByFile)) {
        failures.push(displayPath(file) + ' unresolved module resource ' + match[1]);
      }
    }

    for (const match of source.matchAll(fetchPattern)) {
      const documentFile = file.startsWith(path.join(webRoot, 'concepts') + path.sep)
        ? path.join(webRoot, 'concepts', 'index.html') : path.join(webRoot, 'index.html');
      if (!referenceExists(match[1], documentFile, idsByFile)) {
        failures.push(displayPath(file) + ' unresolved fetch resource ' + match[1]);
      }
    }
  }

  assert.deepEqual(failures, [], failures.join('\n'));
});

test('the web manifest and its install assets resolve', () => {
  const manifestFile = path.join(webRoot, 'site.webmanifest');
  const manifest = JSON.parse(readFileSync(manifestFile, 'utf8'));
  const failures = [];

  for (const reference of [manifest.start_url, ...(manifest.icons ?? []).map((icon) => icon.src)]) {
    if (!referenceExists(reference, manifestFile, idsByFile)) {
      failures.push('site.webmanifest unresolved resource ' + reference);
    }
  }

  assert.equal(manifest.name, 'Octadock');
  assert.deepEqual(failures, [], failures.join('\n'));
});

test('the JavaScript-disabled landing contract remains in normal document flow', () => {
  const indexFile = path.join(webRoot, 'index.html');
  const document = documents.get(indexFile);
  const requiredIds = ['main', 'top', 'capture', 'tools', 'library', 'local', 'windows', 'download'];
  const mainText = [];
  let insideMain = false;

  function collectMain(node) {
    if (node.tagName === 'main') insideMain = true;
    if (insideMain && node.nodeName === '#text') mainText.push(node.value);
    for (const child of node.childNodes ?? []) collectMain(child);
    if (node.tagName === 'main') insideMain = false;
  }
  collectMain(document);

  for (const id of requiredIds) {
    assert.ok(idsByFile.get(indexFile).has(id), 'index.html is missing #' + id);
  }
  const content = mainText.join(' ');
  assert.match(content, /Grab anything on your screen\.\s*It stays yours\./);
  assert.match(content, /Everything runs on your PC\./);
  assert.match(content, /0\.3\.0-alpha\.2/);
  assert.match(content, /unsigned 0\.3\.0-alpha\.2 preview/i);
});
