// Task panel docked on the right: task fields, "add an update" form, and the timeline.
import { $, esc, today, ymd, dayDiff, fmtDate, fmtH, relDay, plural, LOGTYPE, LOGCOLOR, PRIO } from './util.js';
import { db, task, proj, statuses, isDone, isActivity, firstOpen, saveTask } from './store.js';
import { ui, app } from './state.js';
import { projOptions, msOptions, opts, statusOptions, ownerList } from './components.js';

export function openTask(id) {
  ui.taskId = id;
  ui.newTask = null;
  ui.tlFilter = 'all';
  renderDrawer();
  app.render();
}

export function newTask(preset = {}) {
  ui.taskId = null;
  ui.newTask = {
    projectId: preset.projectId ?? (ui.view === 'project' ? ui.projectId : ui.board.project === '_none' ? '' : ui.board.project),
    title: preset.title || '',
  };
  renderDrawer();
  app.render();
  setTimeout(() => { const i = $('#drawer [name=title]'); i?.focus(); i?.select(); });
}

export function closeDrawer() {
  if (!ui.taskId && !ui.newTask) return;
  ui.taskId = null;
  ui.newTask = null;
  $('#drawer').hidden = true;
  $('#drawer').innerHTML = '';
  document.body.classList.remove('drawer-open');
  app.render();
}

export function renderDrawer() {
  const d = $('#drawer'), isNew = !ui.taskId;
  if (!isNew && !task(ui.taskId)) return closeDrawer();
  if (isNew && !ui.newTask) return;
  const t = isNew
    ? {id: '', title: ui.newTask.title || '', projectId: ui.newTask.projectId || '', milestoneId: '', owner: ui.me?.displayName || '', due: '', priority: 'medium', status: firstOpen()?.id, description: ''}
    : task(ui.taskId);
  const p = proj(t.projectId);
  d.hidden = false;
  document.body.classList.add('drawer-open');
  d.innerHTML = `
  <div class="dhead">
    <span class="small muted">${isNew ? 'New task' : 'Task'}</span>
    ${!isNew ? `<span class="chip" id="d-proj" ${p ? '' : 'hidden'}>${esc(p ? p.code || p.name : '')}</span>` : ''}
    <span class="sp"></span>
    ${!isNew ? `<button class="btn sm danger" data-act="del-task" data-id="${t.id}">Delete</button>` : ''}
    <button class="btn icon" data-act="close-drawer" title="Close (Esc)">✕</button>
  </div>
  <div class="dscroll">
    <form data-form="task" data-id="${t.id}">
      <input class="dtitle" name="title" value="${esc(t.title)}" placeholder="Task name" required autocomplete="off">
      <div class="props">
        <span>Status</span><select name="status">${statusOptions(t.status)}</select>
        <span>Priority</span><select name="priority">${opts(PRIO, t.priority)}</select>
        <span>Project</span><select name="projectId">${projOptions(t.projectId)}</select>
        <span>Milestone</span><select name="milestoneId">${msOptions(t.projectId, t.milestoneId)}</select>
        <span>Owner</span><input name="owner" list="owners" value="${esc(t.owner)}">
        <span>Due date</span><input type="date" name="due" value="${esc(t.due)}">
      </div>
      <textarea name="description" rows="3" placeholder="Description, scope, links…">${esc(t.description)}</textarea>
      ${ownerList()}
      ${isNew ? `<div class="actions"><button type="button" class="btn" data-act="close-drawer">Cancel</button><button class="btn primary">Create task</button></div>`
        : `<div class="small muted" style="margin-top:4px">Changes save automatically.</div>`}
    </form>
    ${isNew ? '' : `
    <div class="sect">Add an update</div>
    <form class="composer" data-form="log" data-task="${t.id}">
      <div class="pills">${Object.entries(LOGTYPE).map(([k, v]) => `<label class="c-${LOGCOLOR[k]}"><input type="radio" name="type" value="${k}" ${k === 'work' ? 'checked' : ''}><span><i class="sdot"></i>${v}</span></label>`).join('')}</div>
      <textarea name="text" rows="2" required placeholder="What did you do? What was decided? Who promised what?"></textarea>
      <div class="crow cf" hidden>
        <span class="small muted">Promised by</span><input name="who" list="owners" value="${esc(t.owner)}" style="width:130px">
        <span class="small muted">by date</span><input type="date" name="commitDue">
      </div>
      <div class="crow">
        <input type="date" name="date" value="${today()}" required title="Date">
        <input type="number" name="hours" step="0.25" min="0" max="24" placeholder="Hours" style="width:90px" title="Hours spent">
        <span class="sp"></span><button class="btn primary">Add update</button>
      </div>
    </form>
    <div class="sect">Activity</div>
    <div id="timeline"></div>`}
  </div>`;
  if (!isNew) refreshTimeline();
}

export function refreshTimeline() {
  const el = $('#timeline'), t = task(ui.taskId);
  if (!el || !t) return;
  const all = db.updates.filter(u => u.taskId === t.id);
  const f = ui.tlFilter;
  const acts = all.filter(isActivity);
  const hours = all.reduce((s, u) => s + (+u.hours || 0), 0);
  const openC = all.filter(u => u.type === 'commit' && !u.commitDone).length;
  const created = ymd(new Date(t.createdAt || Date.now()));
  const age = dayDiff(isDone(t) && t.doneAt ? ymd(new Date(t.doneAt)) : today(), created);
  const count = type => all.filter(u => u.type === type).length;
  const filters = [['all', 'All'], ...Object.entries(LOGTYPE).filter(([k]) => count(k)), ...(count('status') ? [['status', 'Status changes']] : [])];

  const items = all.filter(u => f === 'all' || u.type === f);
  if (f === 'all') items.push({type: 'created', date: created, createdAt: (t.createdAt || 0) - 1});
  items.sort((a, b) => b.date.localeCompare(a.date) || (b.createdAt || 0) - (a.createdAt || 0));
  const groups = [];
  for (const it of items) {
    const g = groups[groups.length - 1];
    if (g && g.date === it.date) g.items.push(it); else groups.push({date: it.date, items: [it]});
  }

  const td = today();
  const colName = (id, saved) => statuses().find(s => s.id === id)?.name || saved || id;
  const item = u => {
    if (u.type === 'created') return `<div class="tl-sys c-gray">Task created</div>`;
    if (u.type === 'status') {
      const color = statuses().find(s => s.id === u.toStatus)?.color || 'gray';
      return `<div class="tl-sys c-${color}">Status: ${esc(colName(u.fromStatus, u.fromName))} → <b>${esc(colName(u.toStatus, u.toName))}</b>
        <button class="btn link small del" data-act="del-log" data-id="${u.id}">Delete</button></div>`;
    }
    const late = u.type === 'commit' && !u.commitDone && u.commitDue && u.commitDue < td;
    return `<div class="tl-item c-${LOGCOLOR[u.type]}">
      <div class="th"><b>${LOGTYPE[u.type]}</b>${u.hours ? `<span class="chip">${fmtH(+u.hours)}</span>` : ''}<span class="sp"></span>
        ${u.type === 'risk' ? `<button class="btn link small del" data-act="log-to-raid" data-id="${u.id}" title="Copy to the project's RAID log">To RAID log</button>` : ''}
        <button class="btn link small del" data-act="edit-log" data-id="${u.id}">Edit</button>
        <button class="btn link small del" data-act="del-log" data-id="${u.id}">Delete</button></div>
      <div class="tx">${esc(u.text)}</div>
      ${u.type === 'commit' ? `<div class="cm">
        <label><input type="checkbox" data-act="toggle-commit" data-id="${u.id}" ${u.commitDone ? 'checked' : ''}> Kept</label>
        ${u.who ? `<span class="muted">By ${esc(u.who)}</span>` : ''}
        ${u.commitDue ? `<span class="${late ? 'chip danger' : 'muted'}">${late ? 'Overdue · ' : 'Due '}${fmtDate(u.commitDue)}</span>` : ''}</div>` : ''}
    </div>`;
  };

  el.innerHTML = `
    <div class="tlstats"><span><b>${fmtH(hours)}</b> logged</span><span><b>${acts.length}</b> ${acts.length === 1 ? 'update' : 'updates'}</span>
      <span><b>${openC}</b> open ${openC === 1 ? 'commitment' : 'commitments'}</span><span>${isDone(t) ? 'Finished after' : 'Open for'} <b>${plural(age, 'day')}</b></span></div>
    ${filters.length > 2 ? `<div class="tlf">${filters.map(([k, v]) => `<button data-act="tl-filter" data-f="${k}" class="${f === k ? 'on' : ''}">${v}</button>`).join('')}</div>` : ''}
    <div class="tl">${groups.map(g => {
      const gh = g.items.reduce((s, u) => s + (+u.hours || 0), 0);
      const sub = g.date === td || dayDiff(td, g.date) === 1 ? ` <span class="muted">${fmtDate(g.date)}</span>` : '';
      return `<div class="tl-day">${relDay(g.date)}${sub}${gh ? ` <span class="muted">· ${fmtH(gh)}</span>` : ''}</div>` + g.items.map(item).join('');
    }).join('') || '<div class="empty">No updates yet.</div>'}</div>`;
}

/** Saves one changed field of the open task. */
export async function autosaveTask(el) {
  const t = task(el.form.dataset.id);
  if (!t) return;
  const k = el.name, v = el.value;
  if (!['title', 'status', 'priority', 'projectId', 'milestoneId', 'owner', 'due', 'description'].includes(k)) return;
  if (k === 'title' && !v.trim()) { el.value = t.title; return; }
  const changes = {[k]: v};
  if (k === 'projectId') changes.milestoneId = '';
  const saved = await saveTask(t, changes);
  if (k === 'projectId') {
    const p = proj(saved.projectId), chip = $('#d-proj');
    if (chip) { chip.hidden = !p; chip.textContent = p ? p.code || p.name : ''; }
  }
  if (k === 'status') refreshTimeline();
  app.render();
}

/** Keeps the drawer in sync when a task's status changes somewhere else (board drag, checkbox). */
export function syncDrawerStatus(t) {
  if (ui.taskId !== t.id) return;
  const sel = $('#drawer [name=status]');
  if (sel) sel.value = t.status;
  refreshTimeline();
}
