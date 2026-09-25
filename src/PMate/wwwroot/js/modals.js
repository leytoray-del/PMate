// Centered dialogs: log time, edit record, commitment, project, milestone, board columns.
import { esc, today, plural, COLORS, LOGTYPE, PSTATUS, MSTATUS } from './util.js';
import { db, proj, ms, task, statuses, team } from './store.js';
import { ui, isAdmin } from './state.js';
import { openModal, projOptions, taskOptions, opts, ownerList } from './components.js';

/** Edit an existing timeline record. The task it belongs to cannot change. */
export function editLogModal(id) {
  const u = db.updates.find(x => x.id === id);
  if (!u) return;
  const t = task(u.taskId), isCommit = u.type === 'commit';
  openModal(`<h2>Edit record<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="edit-log" data-id="${u.id}"><div class="form">
    ${t ? `<label class="full">Task<input value="${esc(t.title)}" disabled></label>`
      : `<label class="full">Project<select name="projectId">${projOptions(u.projectId)}</select></label>`}
    <label>Type<select name="type">${opts(LOGTYPE, u.type)}</select></label>
    <label>Date<input type="date" name="date" value="${esc(u.date)}" required></label>
    <label>Hours<input type="number" name="hours" step="0.25" min="0" max="24" value="${u.hours || ''}"></label>
    <span></span>
    <label class="full">Text<textarea name="text" rows="3" required autofocus>${esc(u.text)}</textarea></label>
    <label class="cf" ${isCommit ? '' : 'hidden'}>Promised by<input name="who" list="owners" value="${esc(u.who)}"></label>
    <label class="cf" ${isCommit ? '' : 'hidden'}>By date<input type="date" name="commitDue" value="${esc(u.commitDue)}"></label>
  </div>${ownerList()}
  <div class="actions"><button type="button" class="btn danger l" data-act="del-log" data-id="${u.id}">Delete</button>
    <button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

/** Add a commitment that is not (yet) linked to a task, for example from meeting notes. */
export function commitModal(text, projectId) {
  openModal(`<h2>New commitment<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="quick-commit"><div class="form">
    <label class="full">What was promised<textarea name="text" rows="2" required>${esc(text)}</textarea></label>
    <label>Promised by<input name="who" list="owners" required autofocus></label>
    <label>By date<input type="date" name="commitDue"></label>
    <label>Project<select name="projectId" data-task-target>${projOptions(projectId || '')}</select></label>
    <label>Task (optional)<select name="taskId">${taskOptions(projectId || '', '')}</select></label>
  </div>${ownerList()}
  <div class="actions"><button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

export function quickLogModal(date) {
  const pid = ui.view === 'project' ? ui.projectId : '';
  openModal(`<h2>Log time<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="quick-log"><div class="form">
    <label>Date<input type="date" name="date" value="${date || today()}" required></label>
    <label>Hours<input type="number" name="hours" step="0.25" min="0" max="24" required autofocus></label>
    <label>Project<select name="projectId" data-task-target>${projOptions(pid)}</select></label>
    <label>Task (optional)<select name="taskId">${taskOptions(pid, '')}</select></label>
    <label>Type<select name="type">${opts(LOGTYPE, 'work')}</select></label>
    <label class="full">What did you do?<textarea name="text" rows="3" required placeholder="Example: weekly status meeting, follow up UAT issues"></textarea></label>
  </div><div class="actions"><button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

export function projectModal(id) {
  const p = id ? proj(id) : {id: '', name: '', code: '', client: '', status: 'active', start: today(), end: '', description: ''};
  openModal(`<h2>${id ? 'Edit project' : 'New project'}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="project" data-id="${p.id}"><div class="form">
    <label class="full">Project name<input name="name" required value="${esc(p.name)}" autofocus></label>
    <label>Project code (for timesheet)<input name="code" value="${esc(p.code)}" placeholder="e.g. PRJ-001"></label>
    <label>Client / Department<input name="client" value="${esc(p.client)}"></label>
    <label>Start date<input type="date" name="start" value="${esc(p.start)}"></label>
    <label>Planned end date<input type="date" name="end" value="${esc(p.end)}"></label>
    <label>Status<select name="status">${opts(PSTATUS, p.status)}</select></label>
    <label class="full">Description (goals, scope, stakeholders)<textarea name="description" rows="3">${esc(p.description)}</textarea></label>
  </div><div class="actions">${id && isAdmin() ? `<button type="button" class="btn danger l" data-act="del-project" data-id="${p.id}">Delete project</button>` : ''}
    <button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

export function milestoneModal(id, projectId) {
  const m = id ? ms(id) : {id: '', projectId, name: '', planDate: '', actualDate: '', status: 'planned', note: ''};
  openModal(`<h2>${id ? 'Edit milestone' : 'New milestone'}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="milestone" data-id="${m.id}" data-project="${m.projectId}"><div class="form">
    <label class="full">Name<input name="name" required value="${esc(m.name)}" autofocus placeholder="e.g. UAT complete"></label>
    <label>Planned date<input type="date" name="planDate" value="${esc(m.planDate)}"></label>
    <label>Actual date<input type="date" name="actualDate" value="${esc(m.actualDate)}"></label>
    <label>Status<select name="status">${opts(MSTATUS, m.status)}</select></label>
    <label class="full">Note<textarea name="note" rows="2">${esc(m.note)}</textarea></label>
  </div><div class="actions">${id ? `<button type="button" class="btn danger l" data-act="del-milestone" data-id="${m.id}">Delete</button>` : ''}
    <button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

/** Admins choose who is on the project team. Only team members (and admins) see the project. */
export function teamModal(projectId) {
  const p = proj(projectId), current = new Set(team(projectId).map(x => x.id));
  const people = db.people.filter(x => x.isActive || current.has(x.id));
  openModal(`<h2>Team for ${esc(p.name)}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="team" data-id="${p.id}">
    <p class="small muted">Team members see this project, with all its tasks, milestones, notes and RAID items. Admins always see all projects.</p>
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:6px 14px">
      ${people.map(x => `<label style="display:flex;gap:8px;align-items:center"><input type="checkbox" name="userId" value="${x.id}" ${current.has(x.id) ? 'checked' : ''}>
        ${esc(x.displayName)}${x.isActive ? '' : ' <span class="muted small">(disabled)</span>'}</label>`).join('')}
    </div>
    <div class="actions"><button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save team</button></div>
  </form>`);
}

/* ---------- board columns editor ---------- */
export const statusEditor = {
  draft: null,
  start() { this.draft = statuses().map(({id, name, color, isDone}) => ({id, name, color, isDone})); this.render(); },
  render() {
    const draft = this.draft;
    const count = id => db.tasks.filter(t => t.status === id).length;
    const kept = new Set(draft.map(s => s.id));
    const lost = statuses().filter(s => !kept.has(s.id) && count(s.id));
    const fallback = draft.find(s => !s.isDone);
    openModal(`<h2>Customize board columns<button class="btn link x" data-act="close">Close ✕</button></h2>
    <p class="small muted">Rename, reorder, add or remove columns. Tick "Done" for columns that mean the task is finished. You need at least one Done column and one open column.</p>
    <div>${draft.map((s, i) => `<div class="srow">
      <select data-sfield="color" data-i="${i}" class="c-${s.color}">${COLORS.map(c => `<option value="${c}" ${c === s.color ? 'selected' : ''}>● ${c}</option>`).join('')}</select>
      <input data-sfield="name" data-i="${i}" value="${esc(s.name)}" placeholder="Column name">
      <label class="small"><input type="checkbox" data-sfield="isDone" data-i="${i}" ${s.isDone ? 'checked' : ''}> Done</label>
      <span class="small muted">${s.id ? plural(count(s.id), 'task') : 'new'}</span>
      <span class="ctl"><button class="btn sm" data-act="s-move" data-i="${i}" data-d="-1" ${i ? '' : 'disabled'}>↑</button><button class="btn sm" data-act="s-move" data-i="${i}" data-d="1" ${i < draft.length - 1 ? '' : 'disabled'}>↓</button>
        <button class="btn sm danger" data-act="s-del" data-i="${i}">Remove</button></span>
    </div>`).join('')}</div>
    <div style="margin-top:10px"><button class="btn sm" data-act="s-add">+ Add column</button></div>
    ${lost.length && fallback ? `<p class="small" style="color:var(--warn)">Tasks in removed columns (${lost.map(s => esc(s.name)).join(', ')}) will move to "${esc(fallback.name)}".</p>` : ''}
    <div class="actions"><button class="btn" data-act="close">Cancel</button><button class="btn primary" data-act="s-save">Save</button></div>`);
  },
  move(i, d) { const j = i + d; [this.draft[i], this.draft[j]] = [this.draft[j], this.draft[i]]; this.render(); },
  remove(i) { this.draft.splice(i, 1); this.render(); },
  add() {
    const firstDone = this.draft.findIndex(s => s.isDone);
    this.draft.splice(firstDone < 0 ? this.draft.length : firstDone, 0, {id: '', name: 'New column', color: 'teal', isDone: false});
    this.render();
    setTimeout(() => { const n = [...document.querySelectorAll('#modal [data-sfield=name]')].find(i => i.value === 'New column'); n?.focus(); n?.select(); });
  },
  set(i, field, value) { this.draft[i][field] = value; if (field === 'color') this.render(); },
  validate() {
    const d = this.draft;
    if (d.some(s => !s.name.trim())) return 'Every column needs a name.';
    if (!d.some(s => s.isDone)) return 'Mark at least one column as Done.';
    if (!d.some(s => !s.isDone)) return 'You need at least one column that is not Done.';
    return null;
  },
};
