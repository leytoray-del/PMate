// The main pages. Each view returns an HTML string for <main>.
import { esc, today, parse, addDays, dayDiff, fmtMD, fmtDate, fmtFull, fmtH, round2, plural, by, download, WD, LOGTYPE, LOGCOLOR, PSTATUS, MSTATUS } from './util.js';
import { db, proj, task, st, statuses, tag, tags, projColor, isDone, isOpen, isActivity, recentDone, firstOpen, projLabel, owners, openCommitCount, person, team } from './store.js';
import { ui, isAdmin } from './state.js';
import { statusChip, tagChip, projChipC, avatar, taskSnippet, dueInfo, taskMeta, taskList, sortTasks, commitList, logList, projOptions, opts, ownerList } from './components.js';
import { raidTable, raidAttention, sortRaid } from './raid.js';
import { noteTitle } from './notes.js';

export const views = {};

/* ---------- Today ---------- */
views.today = () => {
  if (!db.projects.length && !db.tasks.length) return welcome();
  const td = today(), open = db.tasks.filter(isOpen);
  const overdue = sortTasks(open.filter(t => t.due && t.due < td));
  const dueToday = sortTasks(open.filter(t => t.due === td));
  const soon = sortTasks(open.filter(t => t.due > td && t.due <= addDays(td, 7)));
  const active = sortTasks(open.filter(t => t.status !== firstOpen()?.id && !(t.due && t.due <= addDays(td, 7))));
  const commits = db.updates.filter(u => u.type === 'commit' && !u.commitDone).sort(by(u => u.commitDue || '9999'));
  const todayLogs = db.updates.filter(u => u.date === td && isActivity(u) && u.createdById === ui.me?.id);
  const hours = todayLogs.reduce((s, u) => s + (+u.hours || 0), 0);
  return `
  <div class="head"><h1>Today · ${fmtFull(td)}</h1><button class="btn primary" data-act="new-task">+ New task</button></div>
  <div class="stats">
    <div class="stat danger" data-act="board-filter" data-due="overdue"><b>${overdue.length}</b><span>Overdue</span></div>
    <div class="stat warn" data-act="board-filter" data-due="today"><b>${dueToday.length}</b><span>Due today</span></div>
    <div class="stat" data-act="board-filter" data-due="week"><b>${soon.length}</b><span>Due in next 7 days</span></div>
    <div class="stat ${commits.some(c => c.commitDue && c.commitDue < td) ? 'danger' : ''}"><b>${commits.length}</b><span>Open commitments</span></div>
    <div class="stat" data-act="nav" data-view="timesheet"><b>${fmtH(hours)}</b><span>Logged today / ${db.settings.hoursPerDay}h</span></div>
  </div>
  <form class="card bar" data-form="quick-task">
    <input name="title" placeholder="Quick add a task, press Enter" style="flex:1;min-width:220px" autocomplete="off">
    <select name="projectId">${projOptions(ui.board.project === '_none' ? '' : ui.board.project)}</select>
    <input name="owner" placeholder="Owner" list="owners" value="${esc(ui.me?.displayName)}" style="width:120px">
    <input type="date" name="due" title="Due date">
    <button class="btn primary">Add</button>${ownerList()}
  </form>
  <div class="grid g2">
    <div>
      <div class="card"><h2>Needs action now <span class="muted small">Overdue and due today</span></h2>${taskList([...overdue, ...dueToday], true, 'Nothing overdue or due today')}</div>
      <div class="card"><h2>Due in the next 7 days</h2>${taskList(soon, true, 'Nothing due in the next 7 days')}</div>
      <div class="card"><h2>Other active tasks</h2>${taskList(active, true, 'None')}</div>
    </div>
    <div>
      <div class="card"><h2>Open commitments <span class="muted small">Who promised what</span></h2>${commitList(commits)}</div>
      ${raidCard()}
      <div class="card"><h2>Logged today <span class="r"><button class="btn sm" data-act="quick-log">+ Log time</button></span></h2>${logList(todayLogs)}</div>
    </div>
  </div>`;
};

function raidCard() {
  const list = raidAttention();
  if (!list.length) return '';
  return `<div class="card"><h2>RAID items to watch <span class="muted small">High rating or due within 7 days</span>
    <span class="r"><button class="btn sm" data-act="nav" data-view="raid">Open RAID log</button></span></h2>${raidTable(list.slice(0, 8))}</div>`;
}

function welcome() {
  return `<div class="head"><h1>Welcome to PMate</h1></div>
  <div class="card welcome">
    <p>All data is saved in a database on this computer. Nothing is sent to any server.</p>
    <ol>
      ${isAdmin() ? '<li>In <b>Users</b>, add your team members. In <b>Settings &amp; Data</b>, you can set a second backup folder, for example in OneDrive.</li>' : ''}
      <li>In <b>Projects</b>, create a project. Use the same project code as your timesheet.</li>
      <li>Add tasks to the project. Each time you work on a task, add a <b>Work log</b> with hours.</li>
      <li>Open <b>Timesheet</b> to see daily and weekly hours. Copy each project's hours and content into your timesheet.</li>
    </ol>
    <p>Used the single-file version before? Export a backup there, then use <b>Settings &amp; Data → Restore from backup</b> here.</p>
    <div class="actions" style="justify-content:flex-start">
      <button class="btn primary" data-act="new-project">Create your first project</button>
      ${isAdmin() ? '<button class="btn" data-act="load-sample">Load sample data</button>' : ''}
    </div>
    <p class="small muted">Shortcuts: <span class="kbd">N</span> new task, <span class="kbd">L</span> log time, <span class="kbd">Esc</span> close panel.</p>
  </div>`;
}

/* ---------- Task board ---------- */
views.board = () => {
  const b = ui.board;
  return `<div class="head"><h1>Task Board</h1>
    <div class="seg"><button class="${b.mode === 'kanban' ? 'on' : ''}" data-act="board-mode" data-mode="kanban">Board</button><button class="${b.mode === 'list' ? 'on' : ''}" data-act="board-mode" data-mode="list">List</button></div>
    ${isAdmin() ? '<button class="btn" data-act="edit-statuses">Customize columns</button><button class="btn" data-act="edit-tags">Customize tags</button>' : ''}
    <button class="btn primary" data-act="new-task">+ New task</button></div>
  <div class="bar">
    <input data-filter="q" placeholder="Search tasks and records" value="${esc(b.q)}" style="width:220px">
    <select data-filter="project"><option value="">All projects</option><option value="_none" ${b.project === '_none' ? 'selected' : ''}>No project</option>${projOptions(b.project, {none: '', closed: true})}</select>
    <select data-filter="owner" title="Whose tasks">${ownerFilterOptions(b.owner)}</select>
    <select data-filter="tag" title="Kind of work"><option value="">All tags</option><option value="_none" ${b.tag === '_none' ? 'selected' : ''}>No tag</option>
      ${tags().map(g => `<option value="${g.id}" ${g.id === b.tag ? 'selected' : ''}>${esc(g.name)}</option>`).join('')}</select>
    <select data-filter="due">${opts({'': 'Any due date', overdue: 'Overdue', today: 'Due today', week: 'Due within 7 days', nodue: 'No due date'}, b.due)}</select>
    ${b.mode === 'kanban' ? `<select data-filter="group" title="Group the cards in each column">${opts({project: 'Group by project', tag: 'Group by tag', '': 'No groups'}, b.group)}</select>` : ''}
    <label class="small"><input type="checkbox" data-filter="showDone" ${b.showDone ? 'checked' : ''}> Show all done tasks</label>
  </div>
  <div id="board-body">${boardBody()}</div>`;
};

/** "My tasks" first (the default), then all owners, no owner, and everyone else by name. */
function ownerFilterOptions(sel) {
  const me = ui.me?.displayName || '';
  const opt = (v, label) => `<option value="${esc(v)}" ${v === sel ? 'selected' : ''}>${esc(label)}</option>`;
  // People you can see, plus anyone who owns a task (for example a vendor). Not commitment names.
  const names = [...new Set([...db.people.filter(p => p.isActive).map(p => p.displayName), ...db.tasks.map(t => t.owner)].filter(Boolean))].sort();
  return opt(me, `My tasks (${me})`) + opt('', 'All owners') + opt('_none', 'No owner') +
    `<optgroup label="Owner">${names.filter(o => o !== me).map(o => opt(o, o)).join('')}</optgroup>`;
}

function boardTasks() {
  const b = ui.board, td = today(), q = b.q.trim().toLowerCase();
  return db.tasks.filter(t => {
    if (b.project === '_none' ? t.projectId : b.project && t.projectId !== b.project) return false;
    if (b.owner === '_none' ? t.owner : b.owner && t.owner !== b.owner) return false;
    if (b.tag === '_none' ? tag(t.tagId) : b.tag && t.tagId !== b.tag) return false;
    if (b.due === 'overdue' && !(isOpen(t) && t.due && t.due < td)) return false;
    if (b.due === 'today' && !(isOpen(t) && t.due === td)) return false;
    if (b.due === 'week' && !(isOpen(t) && t.due && t.due <= addDays(td, 7))) return false;
    if (b.due === 'nodue' && t.due) return false;
    if (!b.showDone && isDone(t) && !recentDone(t)) return false;
    if (q) {
      const hay = [t.title, t.description, t.owner, ...db.updates.filter(u => u.taskId === t.id).map(u => u.text)].join(' ').toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });
}

export function boardBody() {
  const list = sortTasks(boardTasks());
  if (ui.board.mode === 'list') {
    if (!list.length) return '<div class="card empty">No tasks match these filters</div>';
    return `<div class="card"><table><thead><tr><th></th><th>Task</th><th>Tag</th><th>Project</th><th>Owner</th><th>Status</th><th>Due</th><th class="num">Hours</th></tr></thead><tbody>
      ${list.map(t => {
        const d = dueInfo(t), p = proj(t.projectId), c = openCommitCount(t.id);
        const h = db.updates.filter(u => u.taskId === t.id).reduce((s, u) => s + (+u.hours || 0), 0);
        return `<tr class="click ${ui.taskId === t.id ? 'sel' : ''}" data-act="open-task" data-id="${t.id}">
          <td><input type="checkbox" data-act="toggle-done" data-id="${t.id}" ${isDone(t) ? 'checked' : ''}></td>
          <td>${esc(t.title)} ${c ? `<span class="chip warn">${plural(c, 'commitment')}</span>` : ''}</td>
          <td>${tagChip(t.tagId)}</td><td>${p ? projChipC(p) : '-'}</td><td>${esc(t.owner || '-')}</td>
          <td>${statusChip(t.status)}</td>
          <td class="due ${d.cls}">${esc(d.txt)}</td><td class="num">${h ? fmtH(h) : ''}</td></tr>`;
      }).join('')}
      </tbody></table><div class="small muted" style="margin-top:8px">${plural(list.length, 'task')}</div></div>`;
  }
  const cols = statuses();
  const groupOf = boardGroup(ui.board.group);
  // Group headers only help when the cards on the board belong to more than one group.
  const grouped = !!groupOf && new Set(list.map(t => groupOf(t).key)).size > 1;
  return `<div class="kanban" style="grid-template-columns:repeat(${cols.length},minmax(230px,1fr))">${cols.map(s => {
    const col = list.filter(t => st(t.status).id === s.id);
    return `<div class="col" data-drop="${s.id}"><h3><span class="sdot c-${s.color}"></span>${esc(s.name)} <span class="muted">${col.length}</span></h3>
      ${grouped ? groupCards(col, groupOf).map(g => `<div class="kgroup"><span class="sdot c-${g.color}"></span>${esc(g.label)} <span class="muted">${g.items.length}</span></div>
        ${g.items.map(t => kcard(t, ui.board.group)).join('')}`).join('') : col.map(t => kcard(t, '')).join('')}
    </div>`;
  }).join('')}</div>
    <p class="small muted">Tip: drag a card to change its status. The colored bar on the left of a card shows its project. Done columns show tasks finished in the last 14 days only.</p>`;
}

/** Returns a function that gives a task's group ({key, label, color, order}), or null for no groups. */
function boardGroup(field) {
  if (field === 'project') return t => {
    const p = proj(t.projectId);
    return p ? {key: p.id, label: projLabel(p), color: projColor(p), order: '0' + projLabel(p).toLowerCase()} : {key: '', label: 'No project', color: 'gray', order: '1'};
  };
  if (field === 'tag') return t => {
    const g = tag(t.tagId);
    return g ? {key: g.id, label: g.name, color: g.color, order: String(g.sortOrder).padStart(4, '0')} : {key: '', label: 'No tag', color: 'gray', order: '9999'};
  };
  return null;
}

/** Splits a column's cards into groups. Cards keep their order inside a group. */
function groupCards(list, groupOf) {
  const groups = new Map();
  for (const t of list) {
    const g = groupOf(t);
    if (!groups.has(g.key)) groups.set(g.key, {...g, items: []});
    groups.get(g.key).items.push(t);
  }
  return [...groups.values()].sort(by(g => g.order));
}

/**
 * A board card. Left bar = project color. Tag and title on the first line, then the latest update,
 * then project, owner and due date. The group header already shows the project or tag, so the card leaves it out.
 */
function kcard(t, groupedBy) {
  const d = dueInfo(t), p = proj(t.projectId), c = openCommitCount(t.id), snip = taskSnippet(t);
  return `<div class="kcard c-${p ? projColor(p) : 'gray'} ${isDone(t) ? 'done' : ''} ${ui.taskId === t.id ? 'sel' : ''}" draggable="true" data-drag="${t.id}" data-act="open-task" data-id="${t.id}">
    <div class="ttitle">${groupedBy === 'tag' ? '' : tagChip(t.tagId)} ${esc(t.title)}</div>
    ${snip ? `<div class="ksnip" title="${esc(snip)}">${esc(snip)}</div>` : ''}
    <div class="kfoot">${groupedBy === 'project' ? '' : projChipC(p)}${avatar(t.owner)}
      ${t.priority === 'high' && !isDone(t) ? '<span class="hi" title="High priority">High</span>' : ''}
      ${c ? `<span class="chip warn">${plural(c, 'commitment')}</span>` : ''}
      ${t.due ? `<span class="due ${d.cls}">${esc(d.txt)}</span>` : ''}</div>
  </div>`;
}

/* ---------- Projects ---------- */
const projChip = p => `<span class="chip ${p.status === 'active' ? 'ok' : p.status === 'hold' ? 'warn' : ''}">${PSTATUS[p.status]}</span>`;

views.projects = () => {
  const order = ['active', 'hold', 'closed'];
  const list = db.projects.filter(p => ui.showClosed || p.status !== 'closed')
    .sort((a, b) => order.indexOf(a.status) - order.indexOf(b.status) || a.name.localeCompare(b.name));
  const td = today();
  return `<div class="head"><h1>Projects</h1>
    <label class="small"><input type="checkbox" data-act="toggle-closed" ${ui.showClosed ? 'checked' : ''}> Show closed projects</label>
    <button class="btn primary" data-act="new-project">+ New project</button></div>
  ${list.length ? `<div class="grid g3">${list.map(p => {
    const ts = db.tasks.filter(t => t.projectId === p.id), done = ts.filter(isDone).length;
    const late = ts.filter(t => isOpen(t) && t.due && t.due < td).length;
    const next = db.milestones.filter(m => m.projectId === p.id && m.status !== 'done').sort(by(m => m.planDate || '9999'))[0];
    const pct = ts.length ? Math.round(done / ts.length * 100) : 0;
    return `<div class="card pcard" data-act="open-project" data-id="${p.id}">
      <div style="display:flex;gap:8px;align-items:center"><span class="sdot c-${projColor(p)}"></span><h3>${esc(p.name)}</h3><span style="margin-left:auto">${projChip(p)}</span></div>
      <div class="small muted">${esc(p.code || 'No project code')}${p.client ? ' · ' + esc(p.client) : ''}${p.end ? ' · Ends ' + fmtMD(p.end) : ''}</div>
      <div class="prog"><i style="width:${pct}%"></i></div>
      <div class="small">${done}/${ts.length} tasks done ${late ? `<span class="chip danger">${late} overdue</span>` : ''}</div>
      <div class="small muted" style="margin-top:4px">Next milestone: ${next ? `${esc(next.name)} (${next.planDate ? fmtMD(next.planDate) : 'no date'})${next.planDate && next.planDate < td ? ' <span class="chip danger">Late</span>' : ''}` : 'none'}</div>
    </div>`;
  }).join('')}</div>` : '<div class="card empty">No projects yet. Click "New project" to start.</div>'}`;
};

views.project = () => {
  const p = proj(ui.projectId);
  if (!p) { ui.view = 'projects'; return views.projects(); }
  const td = today();
  const ts = db.tasks.filter(t => t.projectId === p.id);
  const mss = db.milestones.filter(m => m.projectId === p.id).sort(by(m => m.planDate || '9999'));
  const logs = db.updates.filter(u => u.projectId === p.id && isActivity(u)).sort((a, b) => b.date.localeCompare(a.date) || (b.createdAt || 0) - (a.createdAt || 0));
  const hours = logs.reduce((s, u) => s + (+u.hours || 0), 0);
  const commits = logs.filter(u => u.type === 'commit' && !u.commitDone).sort(by(u => u.commitDue || '9999'));
  const raidItems = sortRaid(db.raidItems.filter(r => r.projectId === p.id && r.status !== 'closed'));
  const closedRaid = db.raidItems.filter(r => r.projectId === p.id && r.status === 'closed').length;
  const notes = db.notes.filter(n => n.projectId === p.id).sort((a, b) => b.noteDate.localeCompare(a.noteDate) || b.updatedAt - a.updatedAt);
  const left = p.end ? dayDiff(p.end, td) : null;
  return `<div class="head"><button class="btn" data-act="nav" data-view="projects">← Projects</button>
    <h1><span class="sdot c-${projColor(p)}" style="vertical-align:middle"></span> ${esc(p.name)} ${projChip(p)}</h1>
    <button class="btn" data-act="edit-project" data-id="${p.id}">Edit project</button>
    <button class="btn primary" data-act="new-task" data-project="${p.id}">+ New task</button></div>
  <div class="card small">
    <b>Code:</b> ${esc(p.code || '-')} &nbsp; <b>Client / Dept:</b> ${esc(p.client || '-')} &nbsp;
    <b>Period:</b> ${p.start ? fmtMD(p.start) : '?'} – ${p.end ? fmtFull(p.end) : '?'}${left !== null && p.status !== 'closed' ? ` (${left >= 0 ? `${plural(left, 'day')} left` : `${plural(-left, 'day')} over`})` : ''} &nbsp;
    <b>Total hours:</b> ${fmtH(hours)}
    ${p.description ? `<div class="muted" style="margin-top:6px;white-space:pre-wrap">${esc(p.description)}</div>` : ''}
    <div style="margin-top:8px;display:flex;gap:6px;align-items:center;flex-wrap:wrap"><b>Team:</b>
      ${team(p.id).map(x => `<span class="chip ${x.id === ui.me?.id ? 'cc c-blue' : ''}">${esc(x.displayName)}${x.isActive ? '' : ' (disabled)'}</span>`).join('') || '<span class="muted">Nobody yet. Only admins can see this project.</span>'}
      ${isAdmin() ? `<button class="btn sm" data-act="edit-team" data-id="${p.id}">Manage team</button>` : ''}</div>
  </div>
  <div class="card"><h2>Plan · Milestones <span class="r"><button class="btn sm" data-act="new-milestone" data-project="${p.id}">+ Milestone</button></span></h2>
    ${mss.length ? `<table><thead><tr><th>Milestone</th><th>Planned</th><th>Actual</th><th>Status</th><th>Variance</th><th class="num">Tasks</th></tr></thead><tbody>
    ${mss.map(m => {
      const mt = ts.filter(t => t.milestoneId === m.id), md = mt.filter(isDone).length;
      let dev = '';
      if (m.status === 'done' && m.actualDate && m.planDate) { const d = dayDiff(m.actualDate, m.planDate); dev = d > 0 ? `<span class="chip danger">${plural(d, 'day')} late</span>` : `<span class="chip ok">On time</span>`; }
      else if (m.status !== 'done' && m.planDate && m.planDate < td) dev = `<span class="chip danger">Late by ${plural(dayDiff(td, m.planDate), 'day')}</span>`;
      else if (m.status !== 'done' && m.planDate && dayDiff(m.planDate, td) <= 7) dev = `<span class="chip warn">Due in ${plural(dayDiff(m.planDate, td), 'day')}</span>`;
      return `<tr class="click" data-act="edit-milestone" data-id="${m.id}"><td>${esc(m.name)}${m.note ? `<div class="small muted">${esc(m.note)}</div>` : ''}</td>
        <td>${m.planDate ? fmtMD(m.planDate) : '-'}</td><td>${m.actualDate ? fmtMD(m.actualDate) : '-'}</td>
        <td><span class="chip cc c-${m.status === 'done' ? 'green' : m.status === 'doing' ? 'blue' : 'gray'}">${MSTATUS[m.status]}</span></td>
        <td>${dev}</td><td class="num">${mt.length ? `${md}/${mt.length}` : '-'}</td></tr>`;
    }).join('')}
    </tbody></table>` : '<div class="empty">No milestones yet. Add the key dates first, for example: requirements sign-off, build complete, UAT, go-live.</div>'}
  </div>
  <div class="card"><h2>RAID log <span class="muted small">${raidItems.length} open${closedRaid ? `, ${closedRaid} closed` : ''}</span>
    <span class="r"><button class="btn sm" data-act="raid-for-project" data-id="${p.id}">View all</button><button class="btn sm" data-act="new-raid" data-project="${p.id}">+ Add</button></span></h2>
    ${raidTable(raidItems, false, 'No open risks, assumptions, issues or dependencies.')}
  </div>
  <div class="grid g2">
    <div class="card"><h2>Tasks <span class="muted small">${ts.filter(isOpen).length} open</span></h2>${taskList(sortTasks(ts.filter(t => isOpen(t) || recentDone(t))), false, 'No tasks yet')}</div>
    <div>
      <div class="card"><h2>Open commitments</h2>${commitList(commits)}</div>
      <div class="card"><h2>Notes <span class="r"><button class="btn sm" data-act="new-note" data-project="${p.id}">+ New note</button></span></h2>
        ${notes.slice(0, 6).map(n => `<div class="trow" data-act="open-note" data-id="${n.id}"><div class="tmain"><div class="ttitle">${n.pinned ? '★ ' : ''}${esc(noteTitle(n))}</div>
          <div class="tmeta">${n.tags ? esc(n.tags) : ''}</div></div><div class="due">${fmtMD(n.noteDate)}</div></div>`).join('') || '<div class="empty">No notes for this project yet.</div>'}
        ${notes.length > 6 ? `<div class="small"><a href="#" data-act="notes-for-project" data-id="${p.id}">All ${notes.length} notes</a></div>` : ''}</div>
      <div class="card"><h2>Recent records</h2>${logs.slice(0, 12).map(u => {
        const t = task(u.taskId);
        return `<div class="log"><div class="d">${fmtMD(u.date)}<br><span class="chip cc c-${LOGCOLOR[u.type]}">${LOGTYPE[u.type]}</span></div><div class="c">${t ? `<div class="small muted">${esc(t.title)}</div>` : ''}${esc(u.text)}</div><div class="h">${u.hours ? fmtH(+u.hours) : ''}</div></div>`;
      }).join('') || '<div class="empty">No records</div>'}</div>
    </div>
  </div>`;
};

/* ---------- Timesheet ---------- */
const weekDays = () => [...Array(7)].map((_, i) => addDays(ui.tsWeek, i));
// The timesheet shows one person's records: yourself, or (admins only) someone else.
const tsUserId = () => (isAdmin() && ui.tsUser) || ui.me?.id;
const ofTsUser = u => u.createdById === tsUserId();

views.timesheet = () => {
  const days = weekDays();
  if (!days.includes(ui.tsDay)) ui.tsDay = days.includes(today()) ? today() : days[0];
  const rows = {};
  for (const u of db.updates.filter(u => +u.hours > 0 && u.date >= days[0] && u.date <= days[6] && ofTsUser(u))) {
    const k = u.projectId || '';
    rows[k] = rows[k] || {};
    rows[k][u.date] = (rows[k][u.date] || 0) + +u.hours;
  }
  const keys = Object.keys(rows).sort((a, b) => projLabel(proj(a)).localeCompare(projLabel(proj(b))));
  const colTotal = d => keys.reduce((s, k) => s + (rows[k][d] || 0), 0);
  const weekTotal = days.reduce((s, d) => s + colTotal(d), 0);
  const std = +db.settings.hoursPerDay || 8;
  const dayLogs = db.updates.filter(u => u.date === ui.tsDay && isActivity(u) && ofTsUser(u));
  const other = tsUserId() !== ui.me?.id;
  return `<div class="head"><h1>Timesheet${other ? ` · ${esc(person(tsUserId())?.displayName || '')}` : ''}</h1>
    ${isAdmin() ? `<select data-act-change="ts-user" title="Whose timesheet">${db.people.map(p =>
      `<option value="${p.id}" ${p.id === tsUserId() ? 'selected' : ''}>${esc(p.displayName)}${p.id === ui.me?.id ? ' (me)' : ''}</option>`).join('')}</select>` : ''}
    <button class="btn" data-act="ts-week" data-n="-7">◀ Previous</button>
    <button class="btn" data-act="ts-week" data-n="0">This week</button>
    <button class="btn" data-act="ts-week" data-n="7">Next ▶</button>
    <button class="btn" data-act="export-csv">Export week (CSV)</button></div>
  <div class="card ts"><h2>${fmtMD(days[0])} – ${fmtFull(days[6])} <span class="muted small">Click a day to see its details</span><span class="r small">Week total <b>${fmtH(weekTotal)}</b></span></h2>
    <table><thead><tr><th>Project</th>${days.map(d => `<th class="num day ${d === ui.tsDay ? 'on' : ''}" data-act="ts-day" data-d="${d}">${WD[parse(d).getDay()]}<br>${fmtMD(d)}</th>`).join('')}<th class="num">Total</th></tr></thead>
    <tbody>${keys.length ? keys.map(k => `<tr><td>${esc(projLabel(proj(k)))}</td>${days.map(d => `<td class="num day ${d === ui.tsDay ? 'on' : ''}" data-act="ts-day" data-d="${d}">${rows[k][d] ? round2(rows[k][d]) : ''}</td>`).join('')}<td class="num"><b>${round2(days.reduce((s, d) => s + (rows[k][d] || 0), 0))}</b></td></tr>`).join('')
      : `<tr><td colspan="9" class="empty">No hours logged this week</td></tr>`}</tbody>
    <tfoot><tr><td>Daily total</td>${days.map(d => {
      const t = colTotal(d), wd = parse(d).getDay();
      const low = t < std && wd > 0 && wd < 6 && d <= today();
      return `<td class="num ${low ? 'low' : ''}">${t ? round2(t) : low ? '0' : ''}</td>`;
    }).join('')}<td class="num">${round2(weekTotal)}</td></tr></tfoot></table>
    <div class="small muted" style="margin-top:6px">Workdays with less than ${std}h are shown in orange.</div>
  </div>
  ${dayDetail(ui.tsDay, other)}`;
};

// Records of one day, grouped by project, in the shape of a GTS entry: project, hours, content.
function dayGroups(date) {
  const logs = db.updates.filter(u => u.date === date && isActivity(u) && ofTsUser(u)).sort(by(u => u.createdAt || 0));
  const groups = {};
  for (const u of logs) (groups[u.projectId || ''] = groups[u.projectId || ''] || []).push(u);
  return Object.entries(groups).map(([pid, list]) => ({pid, list, hours: list.reduce((s, u) => s + (+u.hours || 0), 0)}))
    .sort((a, b) => !a.pid - !b.pid || projLabel(proj(a.pid)).localeCompare(projLabel(proj(b.pid))));
}

// One line of content per record, ready to paste into the GTS content box.
const recordLine = u => {
  const t = task(u.taskId);
  return `${u.type === 'work' ? '' : `[${LOGTYPE[u.type]}] `}${t ? t.title + ': ' : ''}${u.text.replace(/\n+/g, ' ')}`;
};
export const groupContent = (date, pid) => (dayGroups(date).find(g => g.pid === pid)?.list || []).map(recordLine).join('\n');

function dayDetail(date, other) {
  const groups = dayGroups(date);
  const total = groups.reduce((s, g) => s + g.hours, 0);
  return `<div class="card"><h2>${fmtFull(date)}${total ? ` <span class="chip">${fmtH(total)}</span>` : ''}
    <span class="r">${other ? '' : `<button class="btn sm" data-act="quick-log" data-d="${date}">+ Log time</button>`}
    ${groups.length ? `<button class="btn sm primary" data-act="copy-summary">Copy whole day</button>` : ''}</span></h2>
    ${groups.length ? groups.map(g => {
      const p = proj(g.pid);
      return `<div class="gts">
        <div class="gts-head">${p ? projChipC(p) : '<span class="chip">No project</span>'}<b>${esc(p ? p.name : 'No project')}</b>
          ${g.hours ? `<button class="btn sm gts-h" data-act="copy-text" data-text="${round2(g.hours)}" title="Copy hours">${fmtH(g.hours)}</button>` : ''}
          <button class="btn sm primary" data-act="copy-group" data-d="${date}" data-p="${g.pid}" title="Copy the content of all records below">Copy content</button></div>
        ${g.list.map(u => {
          const t = task(u.taskId);
          return `<div class="gts-rec">
            <div class="c">${u.type === 'work' ? '' : `<span class="chip cc c-${LOGCOLOR[u.type]}">${LOGTYPE[u.type]}</span> `}${t ? `<a href="#" data-act="open-task" data-id="${t.id}">${esc(t.title)}</a>: ` : ''}${esc(u.text)}</div>
            <div class="h">${u.hours ? fmtH(+u.hours) : ''}</div>
            ${other ? '' : `<span class="acts"><button class="btn link small" data-act="edit-log" data-id="${u.id}">Edit</button><button class="btn link small" data-act="del-log" data-id="${u.id}">Delete</button></span>`}</div>`;
        }).join('')}</div>`;
    }).join('') : '<div class="empty">No records on this day</div>'}
  </div>`;
}

export function daySummary(date) {
  const groups = dayGroups(date);
  if (!groups.length) return `${fmtFull(date)}\nNo records.`;
  let out = `${fmtFull(date)} - Total ${fmtH(groups.reduce((s, g) => s + g.hours, 0))}\n`;
  for (const g of groups) {
    out += `\n[${g.pid ? projLabel(proj(g.pid)) : 'No project'}] ${g.hours ? fmtH(g.hours) : ''}\n`;
    for (const u of g.list) out += `- ${recordLine(u)}${u.hours ? ` (${fmtH(+u.hours)})` : ''}\n`;
  }
  return out.trim();
}

export function exportCsv() {
  const days = weekDays();
  const logs = db.updates.filter(u => u.date >= days[0] && u.date <= days[6] && isActivity(u) && ofTsUser(u)).sort(by(u => u.date));
  const q = v => `"${String(v ?? '').replace(/"/g, '""')}"`;
  const lines = [['Date', 'Weekday', 'Project Code', 'Project', 'Task', 'Type', 'Hours', 'Content'].join(',')];
  for (const u of logs) {
    const p = proj(u.projectId), t = task(u.taskId);
    lines.push([u.date, WD[parse(u.date).getDay()], p?.code, p?.name, t?.title, LOGTYPE[u.type], u.hours || '', u.text].map(q).join(','));
  }
  const who = (person(tsUserId())?.displayName || 'me').replace(/[^\w.-]+/g, '-');
  download(`timesheet-${who}-${days[0]}.csv`, '\ufeff' + lines.join('\r\n'), 'text/csv');
}

/* ---------- Settings ---------- */
views.settings = () => {
  const s = db.settings, sys = ui.system || {}, admin = isAdmin();
  const hasSample = [...db.projects, ...db.tasks].some(x => x.isSample);
  const when = v => v ? new Date(v).toLocaleString() : 'never';
  return `<div class="head"><h1>Settings &amp; Data</h1></div>
  <div class="card"><h2>My account</h2>
    <div class="kv"><span>Name</span><span>${esc(ui.me.displayName)}</span><span>User name</span><code>${esc(ui.me.userName)}</code>
      <span>Role</span><span>${admin ? 'Admin' : 'Member'}</span></div>
    <div class="actions" style="justify-content:flex-start"><button class="btn" data-act="account">Change name or password</button></div>
  </div>
  ${admin ? `<form class="card" data-form="settings"><h2>General</h2>
    <div class="form">
      <label>Standard hours per day<input type="number" name="hoursPerDay" step="0.5" min="0" max="24" value="${esc(s.hoursPerDay)}"></label>
      <span></span>
      <label class="full">Extra backup folder (optional, for example a OneDrive folder)
        <input name="backupDir" value="${esc(s.backupDir)}" placeholder="C:\\Users\\you\\OneDrive - Company\\PMate Backup"></label>
    </div>
    <h2 style="margin-top:18px">Reminders</h2>
    <p class="small muted">Windows notifications on workdays, from the PMate icon near the clock.
      ${sys.notificationsAvailable === false ? '<span class="chip warn">Notifications are not available in this session</span>' : ''}</p>
    <div class="form">
      <label class="full" style="flex-direction:row;align-items:center;gap:8px"><input type="checkbox" name="remindersEnabled" ${s.remindersEnabled !== false ? 'checked' : ''}> Turn on reminders</label>
      <label>Morning: tasks and commitments due today<input type="time" name="morningReminder" value="${esc(s.morningReminder || '09:00')}"></label>
      <label>Evening: if today's hours are below ${esc(s.hoursPerDay)}h<input type="time" name="eveningReminder" value="${esc(s.eveningReminder || '17:30')}"></label>
    </div>
    <div class="actions"><button type="button" class="btn l" data-act="test-reminder">Send a test reminder</button><button class="btn primary">Save</button></div>
  </form>` : ''}
  <div class="card"><h2>Recently deleted <span class="muted small">Kept for 30 days</span></h2>
    ${!ui.trash ? '<div class="empty">Loading…</div>' : !ui.trash.length ? '<div class="empty">Nothing was deleted in the last 30 days.</div>'
      : `<table><tbody>${ui.trash.map(b => `<tr><td>${esc(b.label)}${b.itemCount > 1 ? ` <span class="muted small">(${b.itemCount} rows)</span>` : ''}</td>
          <td class="muted small">${new Date(b.deletedAt).toLocaleString()}</td>
          <td class="num"><button class="btn sm" data-act="restore" data-id="${b.id}">Restore</button></td></tr>`).join('')}</tbody></table>`}
  </div>
  <div class="card"><h2>Board columns</h2>
    <p style="display:flex;gap:6px;flex-wrap:wrap;align-items:center">${statuses().map(x => `<span class="chip cc c-${x.color}">${esc(x.name)}${x.isDone ? ' ✓' : ''}</span>`).join('')}</p>
    ${admin ? '<button class="btn" data-act="edit-statuses">Customize columns</button>' : '<p class="small muted">Only an admin can change the columns.</p>'}
  </div>
  <div class="card"><h2>Task tags <span class="muted small">The kind of work, shown on board cards</span></h2>
    <p style="display:flex;gap:6px;flex-wrap:wrap;align-items:center">${tags().map(g => tagChip(g.id)).join('') || '<span class="muted small">No tags</span>'}</p>
    ${admin ? '<button class="btn" data-act="edit-tags">Customize tags</button>' : '<p class="small muted">Only an admin can change the tags.</p>'}
  </div>
  <div class="card"><h2>Data and backups</h2>
    ${admin ? `<div class="kv">
      <span>Database</span><code>${esc(sys.databaseFile || '')}</code>
      <span>Daily backups</span><code>${esc(sys.backupDir || '')}${sys.extraBackupDir ? '<br>' + esc(sys.extraBackupDir) : ''}</code>
      <span>Last backup</span><span>${when(sys.lastBackup)}${sys.backupError ? ` <span class="chip danger">${esc(sys.backupError)}</span>` : ''}</span>
    </div>
    <p class="small muted">A backup is made automatically once a day. The last 14 are kept.
      Keep the database itself on the local disk. Only put backups in OneDrive.</p>` : '<p class="small muted">Backups are made automatically every day.</p>'}
    <div class="actions" style="justify-content:flex-start">
      ${admin ? '<button class="btn" data-act="backup-now">Back up now</button>' : ''}
      <a class="btn" href="/api/export" download>Export backup (JSON)</a>
      ${admin ? `<button class="btn" data-act="import-json">Restore from backup</button>
      ${hasSample ? '<button class="btn" data-act="remove-sample">Remove sample data</button>' : '<button class="btn" data-act="load-sample">Load sample data</button>'}
      <button class="btn danger" data-act="clear-all">Delete all data</button>` : ''}
    </div>
    ${admin ? '<p class="small muted">"Restore from backup" also accepts backups from the old single-file version.</p>' : ''}
  </div>
  <div class="card"><h2>Version and updates</h2>
    <div class="kv">
      <span>Version</span><span>${esc(sys.version || '?')}${sys.commit ? ` <code>${esc(sys.commit)}</code>` : ''}</span>
      ${admin ? `<span>Updates</span><span>${!sys.updatesEnabled ? 'Off (not running from a git clone)'
        : sys.updateError ? `<span class="chip danger">${esc(sys.updateError)}</span>`
        : sys.behind ? `<span class="chip warn">${plural(sys.behind, 'new change')} available</span>` : 'Up to date'}
        ${sys.lastChecked ? `<span class="muted small">· checked ${when(sys.lastChecked)}</span>` : ''}</span>` : ''}
    </div>
    ${admin ? `<div class="actions" style="justify-content:flex-start">
      <button class="btn" data-act="check-updates" ${sys.updatesEnabled ? '' : 'disabled'}>Check for updates</button>
      ${sys.behind ? '<button class="btn primary" data-act="do-update">Update and restart</button>' : ''}
    </div>` : ''}
    <p class="small muted">Shortcuts: <span class="kbd">N</span> new task, <span class="kbd">L</span> log time, <span class="kbd">Esc</span> close panel.</p>
  </div>`;
};
