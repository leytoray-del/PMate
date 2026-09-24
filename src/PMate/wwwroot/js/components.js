// Reusable pieces of HTML and small UI helpers.
import { $, esc, today, dayDiff, fmtMD, fmtDate, fmtH, plural, by, LOGTYPE, LOGCOLOR, PRIO } from './util.js';
import { db, proj, task, st, isDone, isOpen, recentDone, projLabel, owners, openCommitCount, statuses } from './store.js';
import { ui } from './state.js';

export const statusChip = id => { const s = st(id); return s ? `<span class="chip cc c-${s.color}">${esc(s.name)}</span>` : ''; };

export function dueInfo(t) {
  if (!t.due) return {cls: '', txt: 'No due date'};
  const diff = dayDiff(t.due, today());
  if (isDone(t)) return {cls: '', txt: fmtMD(t.due)};
  if (diff < 0) return {cls: 'danger', txt: `Overdue ${-diff}d`};
  if (diff === 0) return {cls: 'warn', txt: 'Due today'};
  if (diff === 1) return {cls: 'warn', txt: 'Due tomorrow'};
  if (diff <= 7) return {cls: '', txt: fmtDate(t.due)};
  return {cls: '', txt: fmtMD(t.due) + (t.due.slice(0, 4) !== today().slice(0, 4) ? ' ' + t.due.slice(0, 4) : '')};
}

export function taskMeta(t, showProject, showStatus = true) {
  const p = proj(t.projectId), c = openCommitCount(t.id);
  return `${showProject && p ? `<span class="chip">${esc(p.code || p.name)}</span>` : ''}
    ${showStatus ? statusChip(t.status) : ''}
    ${t.owner ? `<span>${esc(t.owner)}</span>` : ''}
    ${c ? `<span class="chip warn">${plural(c, 'commitment')}</span>` : ''}`;
}

export function taskRow(t, showProject = true) {
  const d = dueInfo(t);
  return `<div class="trow ${isDone(t) ? 'done' : ''} ${ui.taskId === t.id ? 'sel' : ''}" data-act="open-task" data-id="${t.id}">
    <input type="checkbox" data-act="toggle-done" data-id="${t.id}" ${isDone(t) ? 'checked' : ''} title="Mark as done">
    <span class="prio p-${t.priority}" title="Priority: ${PRIO[t.priority]}"></span>
    <div class="tmain"><div class="ttitle">${esc(t.title)}</div><div class="tmeta">${taskMeta(t, showProject)}</div></div>
    <div class="due ${d.cls}">${esc(d.txt)}</div></div>`;
}

export const taskList = (list, showProject = true, emptyText = 'No tasks') =>
  list.length ? list.map(t => taskRow(t, showProject)).join('') : `<div class="empty">${emptyText}</div>`;

const PRIO_ORDER = ['high', 'medium', 'low'];
export const sortTasks = list => list.slice().sort((a, b) =>
  isDone(a) - isDone(b) || (a.due || '9999').localeCompare(b.due || '9999') ||
  PRIO_ORDER.indexOf(a.priority) - PRIO_ORDER.indexOf(b.priority));

export function commitList(list) {
  if (!list.length) return '<div class="empty">No open commitments</div>';
  const td = today();
  return list.map(u => {
    const t = task(u.taskId), late = u.commitDue && u.commitDue < td;
    return `<div class="trow ${t && ui.taskId === t.id ? 'sel' : ''}" ${t ? `data-act="open-task" data-id="${t.id}"` : ''}>
      <input type="checkbox" data-act="toggle-commit" data-id="${u.id}" title="Mark as kept">
      <div class="tmain"><div class="ttitle">${esc(u.text)}</div>
      <div class="tmeta">${u.who ? `<span>By ${esc(u.who)}</span>` : ''}${t ? `<span>· ${esc(t.title)}</span>` : ''}</div></div>
      <div class="due ${late ? 'danger' : ''}">${u.commitDue ? (late ? `Overdue ${dayDiff(td, u.commitDue)}d` : fmtDate(u.commitDue)) : ''}</div></div>`;
  }).join('');
}

export function logList(list) {
  if (!list.length) return '<div class="empty">Nothing logged yet</div>';
  return list.slice().sort(by(u => u.createdAt || 0)).map(u => {
    const t = task(u.taskId), p = proj(u.projectId);
    return `<div class="log"><div class="d"><span class="chip cc c-${LOGCOLOR[u.type]}">${LOGTYPE[u.type]}</span></div>
      <div class="c"><div class="small muted">${esc(p ? (p.code || p.name) : 'No project')}${t ? ` · <a href="#" data-act="open-task" data-id="${t.id}">${esc(t.title)}</a>` : ''}</div>${esc(u.text)}</div>
      <div class="h">${u.hours ? fmtH(+u.hours) : ''}</div>
      <button class="btn link small" data-act="edit-log" data-id="${u.id}">Edit</button>
      <button class="btn link small" data-act="del-log" data-id="${u.id}">Delete</button></div>`;
  }).join('');
}

/* ---------- <option> lists ---------- */
export function projOptions(sel, {none = 'No project', closed = false} = {}) {
  const list = db.projects.filter(p => closed || p.status !== 'closed' || p.id === sel).sort(by(p => projLabel(p).toLowerCase()));
  return (none ? `<option value="">${none}</option>` : '') +
    list.map(p => `<option value="${p.id}" ${p.id === sel ? 'selected' : ''}>${esc(projLabel(p))}</option>`).join('');
}
export function msOptions(projectId, sel) {
  return `<option value="">None</option>` + db.milestones.filter(m => m.projectId === projectId)
    .sort(by(m => m.planDate || '')).map(m => `<option value="${m.id}" ${m.id === sel ? 'selected' : ''}>${esc(m.name)}</option>`).join('');
}
export function taskOptions(projectId, sel) {
  return `<option value="">None</option>` + sortTasks(db.tasks.filter(t => (t.projectId || '') === projectId && (isOpen(t) || t.id === sel || recentDone(t))))
    .map(t => `<option value="${t.id}" ${t.id === sel ? 'selected' : ''}>${esc(t.title)}</option>`).join('');
}
export const opts = (map, sel) => Object.entries(map).map(([k, v]) => `<option value="${k}" ${k === sel ? 'selected' : ''}>${v}</option>`).join('');
export const statusOptions = sel => statuses().map(s => `<option value="${s.id}" ${s.id === sel ? 'selected' : ''}>${esc(s.name)}</option>`).join('');
export const ownerList = () => `<datalist id="owners">${owners().map(o => `<option value="${esc(o)}">`).join('')}</datalist>`;

/* ---------- RAID ---------- */
export const raidRef = r => `${r.type[0].toUpperCase()}-${r.number}`;
const SCORE = {high: 3, medium: 2, low: 1};
/** Rating: impact × likelihood for risks, impact alone for the other types. */
export function raidRating(r) {
  const score = r.type === 'risk' ? SCORE[r.impact] * SCORE[r.likelihood || 'medium'] : SCORE[r.impact] * 2;
  return score >= 6 ? 'high' : score >= 3 ? 'medium' : 'low';
}
export const raidRatingChip = r => {
  const level = raidRating(r);
  return `<span class="chip ${level === 'high' ? 'danger' : level === 'medium' ? 'warn' : ''}" title="${r.type === 'risk' ? `Impact ${r.impact}, likelihood ${r.likelihood}` : `Impact ${r.impact}`}">${level[0].toUpperCase() + level.slice(1)}</span>`;
};

/* ---------- toast and modal ---------- */
/**
 * Short message at the bottom of the screen.
 * Pass {undoId} to show an Undo button (for deletes), or {error: true} for errors.
 */
export function toast(msg, opts = {}) {
  if (typeof opts === 'boolean') opts = {error: opts};
  const t = $('#toast');
  t.innerHTML = esc(msg) + (opts.undoId ? ` <button class="btn sm" data-act="undo" data-id="${esc(opts.undoId)}">Undo</button>` : '');
  t.classList.toggle('error', !!opts.error);
  t.classList.toggle('action', !!opts.undoId);
  t.classList.add('show');
  clearTimeout(toast.t);
  toast.t = setTimeout(() => t.classList.remove('show', 'action'), opts.undoId ? 9000 : opts.error ? 5000 : 2200);
}
export function hideToast() { $('#toast').classList.remove('show', 'action'); }
export function openModal(html) {
  const m = $('#modal');
  m.innerHTML = `<div class="mbox" role="dialog">${html}</div>`;
  m.hidden = false;
  setTimeout(() => m.querySelector('[autofocus]')?.focus());
}
export function closeModal() { const m = $('#modal'); m.hidden = true; m.innerHTML = ''; }
