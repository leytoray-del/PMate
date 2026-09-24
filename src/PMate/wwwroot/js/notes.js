// Work notes: list with full-text search on the left, editor on the right. The editor saves while you type.
import { $, esc, fmtMD, today } from './util.js';
import { db, note, proj, saveNote, searchNotes } from './store.js';
import { ui } from './state.js';
import { projOptions, toast } from './components.js';

export const noteTitle = n => n.title || n.body.split('\n').map(l => l.trim()).find(Boolean) || 'Untitled note';
const markSnippet = s => esc(s).replace(/\u0001/g, '<mark>').replace(/\u0002/g, '</mark>');

export function notesView() {
  const n = note(ui.notes.selectedId);
  return `<div class="head"><h1>Notes</h1><button class="btn primary" data-act="new-note">+ New note</button></div>
  <div class="notes">
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
    <input class="ntitle" name="title" value="${esc(n.title)}" placeholder="Title (if empty, the first line is used)" autocomplete="off">
    <div class="nprops">
      <input type="date" name="noteDate" value="${esc(n.noteDate)}" title="Date">
      <select name="projectId">${projOptions(n.projectId)}</select>
      <input name="tags" value="${esc(n.tags)}" placeholder="Tags, separated by commas" style="flex:1;min-width:160px">
      <label class="small"><input type="checkbox" name="pinned" ${n.pinned ? 'checked' : ''}> Pin to top</label>
    </div>
    <textarea class="nbody" name="body" placeholder="Write your notes here…">${esc(n.body)}</textarea>
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

/* ---------- autosave ---------- */
let pending = null;

function formData(f) {
  return {projectId: f.projectId.value, title: f.title.value, body: f.body.value, tags: f.tags.value, noteDate: f.noteDate.value, pinned: f.pinned.checked};
}

async function saveNow(f) {
  const status = $('#note-status');
  try {
    await saveNote(f.dataset.id, formData(f));
    if (status && f.isConnected) status.textContent = 'Saved';
    const list = $('#note-list');
    if (list) list.innerHTML = noteListHtml();
  } catch (err) {
    if (status && f.isConnected) status.textContent = 'Not saved';
    toast(err.message, {error: true});
  }
}

/** Called on every keystroke in the editor. Saves 0.7 s after typing stops. */
export function noteEdited(f, immediate = false) {
  clearTimeout(pending?.timer);
  const status = $('#note-status');
  if (status) status.textContent = 'Saving…';
  if (immediate) { pending = null; return saveNow(f); }
  pending = {form: f, timer: setTimeout(() => { pending = null; saveNow(f); }, 700)};
}

/** Saves a pending change right away, for example before switching notes or pages. */
export async function flushNote() {
  if (!pending) return;
  clearTimeout(pending.timer);
  const f = pending.form;
  pending = null;
  await saveNow(f);
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

/** The selected text in the editor, or the line under the cursor. List markers like "- " or "1. " are removed. */
export function editorSelection() {
  const ta = $('#note-editor [name=body]');
  if (!ta) return '';
  const {value, selectionStart: s, selectionEnd: e} = ta;
  let text;
  if (s !== e) text = value.slice(s, e);
  else {
    const start = value.lastIndexOf('\n', s - 1) + 1;
    const end = value.indexOf('\n', s);
    text = value.slice(start, end < 0 ? value.length : end);
  }
  return text.trim().replace(/^([-*•]|\d+[.)]|\[[ xX]?\])\s+/, '').replace(/\s+/g, ' ').slice(0, 300);
}
