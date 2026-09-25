// Work notes: list with full-text search on the left, editor on the right. The editor saves while you type.
// The page itself is a block editor (editor.js), loaded only when the Notes page opens.
import { $, esc, fmtMD, today } from './util.js';
import { db, note, proj, saveNote, searchNotes, load } from './store.js';
import { ui } from './state.js';
import { projOptions, toast } from './components.js';

export const noteTitle = n => n.title || n.body.split('\n').map(l => l.trim()).find(Boolean) || 'Untitled note';
const markSnippet = s => esc(s).replace(/\u0001/g, '<mark>').replace(/\u0002/g, '</mark>');

export function notesView() {
  const n = note(ui.notes.selectedId);
  return `<div class="head"><h1>Notes</h1><button class="btn primary" data-act="new-note">+ New note</button></div>
  <div class="notes${n ? ' has-sel' : ''}">
    <div class="card nlist">
      <div class="bar"><input data-note-search placeholder="Search all notes" value="${esc(ui.notes.q)}" autocomplete="off"></div>
      <div class="bar"><select data-note-project><option value="">All projects</option>${projOptions(ui.notes.project, {none: '', closed: true})}</select></div>
      <div id="note-list">${noteListHtml()}</div>
    </div>
    <div class="card neditor" id="note-editor">${editorHtml(n)}</div>
  </div>`;
}

export function noteListHtml() {
  const {q, hits, project} = ui.notes;
  const list = q.trim() && hits
    ? hits.map(h => ({n: note(h.id), snippet: h.snippet})).filter(x => x.n)
    : db.notes.filter(n => !project || n.projectId === project)
        .sort((a, b) => b.pinned - a.pinned || b.noteDate.localeCompare(a.noteDate) || b.updatedAt - a.updatedAt)
        .map(n => ({n, snippet: null}));
  if (!list.length) return `<div class="empty">${q.trim() ? 'No notes match your search.' : 'No notes yet.'}</div>`;
  return list.map(({n, snippet}) => {
    const p = proj(n.projectId);
    return `<div class="nitem ${n.id === ui.notes.selectedId ? 'sel' : ''}" data-act="open-note" data-id="${n.id}">
      <div class="nt">${n.pinned ? '★ ' : ''}${esc(noteTitle(n))}</div>
      <div class="nm">${fmtMD(n.noteDate)}${p ? ` <span class="chip">${esc(p.code || p.name)}</span>` : ''}${n.tags ? ` <span>${esc(n.tags)}</span>` : ''}</div>
      <div class="ns">${snippet ? markSnippet(snippet) : esc(n.body.slice(0, 200))}</div></div>`;
  }).join('');
}

function editorHtml(n) {
  if (!n) return `<div class="empty">Select a note on the left, or create a new one.<br><br>
    Tip: write meeting minutes here. Then select a line and turn it into a task, a commitment, or a RAID item.</div>`;
  return `<form data-form="note" data-id="${n.id}">
    <button type="button" class="btn sm note-back" data-act="close-note">← All notes</button>
    <input class="ntitle" name="title" value="${esc(n.title)}" placeholder="Title (if empty, the first line is used)" autocomplete="off">
    <div class="nprops">
      <input type="date" name="noteDate" value="${esc(n.noteDate)}" title="Date">
      <select name="projectId">${projOptions(n.projectId)}</select>
      <input name="tags" value="${esc(n.tags)}" placeholder="Tags, separated by commas" style="flex:1;min-width:160px">
      <label class="small"><input type="checkbox" name="pinned" ${n.pinned ? 'checked' : ''}> Pin to top</label>
    </div>
    <div class="banner" id="note-conflict"${conflict.has(n.id) ? '' : ' hidden'}>Someone else changed this note after you opened it. Your latest changes are not saved.
      <span class="sp"></span><button type="button" class="btn sm" data-act="reload-notes">Load the latest version</button></div>
    <div class="nbody" id="note-doc"><div class="empty">Loading the editor…</div></div>
    <div class="ntools">
      <span class="small muted">Selected text or current line →</span>
      <button type="button" class="btn sm" data-act="note-to-task">Task</button>
      <button type="button" class="btn sm" data-act="note-to-commit">Commitment</button>
      <button type="button" class="btn sm" data-act="note-to-raid">RAID item</button>
      <span class="sp"></span>
      <span class="small muted" id="note-status">Saved</span>
      <button type="button" class="btn sm danger" data-act="del-note" data-id="${n.id}">Delete</button>
    </div>
  </form>`;
}

/* ---------- block editor ---------- */
let editorMod = null; // editor.js, once loaded
let ed = null;        // the editor on screen
let edNoteId = null;  // the note it shows
let mountSeq = 0;

/** Puts the block editor into the note form. main.js calls this after each render of the Notes page. */
export async function mountNoteEditor() {
  const seq = ++mountSeq;
  destroyNoteEditor();
  const host = $('#note-doc'), n = note(ui.notes.selectedId);
  if (!host || !n) return;
  try { editorMod ??= await import('./editor.js'); }
  catch (err) { host.innerHTML = `<div class="empty">The editor could not load: ${esc(err.message)}</div>`; return; }
  if (seq !== mountSeq || !host.isConnected) return; // another render came first
  // Changes that are typed but not saved yet win over the stored copy.
  const content = latest?.id === n.id ? latest.data.content : n.content;
  host.innerHTML = '';
  ed = editorMod.createNoteEditor({element: host, content, body: n.body, onChange: () => {
    const f = host.closest('form');
    if (f) noteEdited(f);
  }});
  edNoteId = n.id;
}

export function destroyNoteEditor() {
  ed?.destroy();
  ed = edNoteId = null;
}

/* ---------- autosave ---------- */
// Saves run one after another, so each one sends the version returned by the one before.
let queue = Promise.resolve();
let pending = null;          // {snap, timer}: a save waiting for typing to stop
let latest = null;           // the newest snapshot that is not saved yet
const conflict = new Set();  // notes whose save was refused because someone else saved first

function snapshot(f) {
  const n = note(f.dataset.id);
  const data = {projectId: f.projectId.value, title: f.title.value, tags: f.tags.value, noteDate: f.noteDate.value, pinned: f.pinned.checked,
    body: n?.body ?? '', content: n?.content || null};
  if (ed && edNoteId === f.dataset.id) data.content = JSON.stringify(ed.getJSON());
  return {id: f.dataset.id, data};
}

function setStatus(id, text) {
  const status = $('#note-status');
  if (status && status.closest('form')?.dataset.id === id) status.textContent = text;
}

function save(snap) {
  queue = queue.then(async () => {
    const n = note(snap.id);
    if (!n || conflict.has(snap.id)) return;
    try {
      await saveNote(snap.id, {...snap.data, version: n.version});
      if (latest === snap) latest = null;
      setStatus(snap.id, latest ? 'Saving…' : 'Saved');
      const list = $('#note-list');
      if (list) list.innerHTML = noteListHtml();
    } catch (err) {
      setStatus(snap.id, 'Not saved');
      if (err.status !== 409) return toast(err.message, {error: true});
      conflict.add(snap.id);
      const banner = $('#note-conflict');
      if (banner && banner.closest('form')?.dataset.id === snap.id) banner.hidden = false;
    }
  });
  return queue;
}

/** Called on every change in the note. Saves 0.7 s after typing stops. */
export function noteEdited(f, immediate = false) {
  const snap = snapshot(f);
  latest = snap;
  clearTimeout(pending?.timer);
  setStatus(snap.id, conflict.has(snap.id) ? 'Not saved' : 'Saving…');
  if (immediate) { pending = null; return save(snap); }
  pending = {snap, timer: setTimeout(() => { pending = null; save(snap); }, 700)};
}

/** Saves a pending change right away, for example before switching notes or pages. Also waits for saves already running. */
export function flushNote() {
  if (pending) {
    clearTimeout(pending.timer);
    const {snap} = pending;
    pending = null;
    save(snap);
  }
  return queue;
}

/** After a refused save: drops the unsaved changes and loads the newest data from the server. */
export async function reloadNotes() {
  clearTimeout(pending?.timer);
  pending = latest = null;
  await queue;
  conflict.clear();
  await load();
}

/* ---------- actions ---------- */
export async function openNote(id) {
  await flushNote();
  ui.notes.selectedId = id;
  ui.view = 'notes';
}

export async function createNote(projectId) {
  await flushNote();
  const n = await saveNote(null, {projectId: projectId ?? ui.notes.project ?? '', title: '', body: '', tags: '', noteDate: today(), pinned: false});
  ui.notes.selectedId = n.id;
  ui.notes.q = '';
  ui.notes.hits = null;
  ui.view = 'notes';
  return n;
}

let searchTimer;
export function noteSearchChanged(q) {
  ui.notes.q = q;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(async () => {
    try { ui.notes.hits = q.trim() ? await searchNotes(q, ui.notes.project) : null; }
    catch (err) { toast(err.message, {error: true}); }
    const list = $('#note-list');
    if (list) list.innerHTML = noteListHtml();
  }, 250);
}

/** The selected text in the editor, or the block under the cursor. List markers like "- " or "1. " are removed. */
export function editorSelection() {
  if (!ed) return '';
  return editorMod.selectedText(ed).trim().replace(/^([-*•]|\d+[.)]|\[[ xX]?\])\s+/, '').replace(/\s+/g, ' ').slice(0, 300);
}
