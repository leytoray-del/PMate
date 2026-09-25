// Thin wrapper around fetch. All server calls go through here.
// To move to a cloud server later, only the base URL and sign-in handling change.

const BASE = '';
const EMPTY_TO_NULL = new Set(['due', 'start', 'end', 'planDate', 'actualDate', 'commitDue', 'date', 'noteDate', 'dueDate']);

// The UI uses '' for "no value". The API wants null for empty dates and ids.
function clean(v) {
  if (Array.isArray(v)) return v.map(clean);
  if (v && typeof v === 'object') {
    const o = {};
    for (const [k, x] of Object.entries(v)) o[k] = x === '' && (EMPTY_TO_NULL.has(k) || k.endsWith('Id')) ? null : clean(x);
    return o;
  }
  return v;
}

async function request(method, url, body, {raw = false} = {}) {
  const init = {method, headers: {'X-PM-Client': '1'}};
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(raw ? body : clean(body));
  }
  let res;
  try { res = await fetch(BASE + url, init); }
  catch { throw new Error('Cannot reach the PMate service. Is it running?'); }
  // Signed out, or the sign-in ended (password changed, account disabled): go to the login page.
  if (res.status === 401 && !url.startsWith('/api/auth/')) {
    location.href = '/login.html';
    throw new Error('Please sign in again.');
  }
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try { const j = await res.json(); msg = j.error || j.title || msg; } catch {}
    throw new Error(msg);
  }
  if (res.status === 204 || res.status === 202) return null;
  return (res.headers.get('content-type') || '').includes('json') ? res.json() : res.text();
}

export const api = {
  get: url => request('GET', url),
  post: (url, body = {}, opts) => request('POST', url, body, opts),
  put: (url, body) => request('PUT', url, body),
  del: url => request('DELETE', url),
};
