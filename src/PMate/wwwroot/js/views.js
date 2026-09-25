// The main pages. Each view returns an HTML string for <main>.
import { esc, today, parse, addDays, dayDiff, fmtMD, fmtDate, fmtFull, fmtH, round2, plural, by, download, WD, LOGTYPE, LOGCOLOR, PSTATUS, MSTATUS } from './util.js';
import { db, proj, task, st, statuses, isDone, isOpen, isActivity, recentDone, firstOpen, projLabel, owners, openCommitCount } from './store.js';
import { ui, isAdmin } from './state.js';
import { statusChip, dueInfo, taskMeta, taskList, sortTasks, commitList, logList, projOptions, opts, ownerList } from './components.js';
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
  const todayLogs = db.updates.filter(u => u.date === td && isActivity(u));
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
      <li>Open <b>Timesheet</b> to see daily and weekly hours. Copy the daily summary into your timesheet.</li>
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
    ${isAdmin() ? '<button class="btn" data-act="edit-statuses">Customize columns</button>' : ''}
    <button class="btn primary" data-act="new-task">+ New task</button></div>
  <div class="bar">
    <input data-filter="q" placeholder="Search tasks and records" value="${esc(b.q)}" style="width:220px">
    <select data-filter="project"><option value="">All projects</option><option value="_none" ${b.project === '_none' ? 'selected' : ''}>No project</option>${projOptions(b.project, {none: '', closed: true})}</select>
    <select data-filter="owner"><option value="">All owners</option>${owners().map(o => `<option ${o === b.owner ? 'selected' : ''}>${esc(o)}</option>`).join('')}</select>
    <select data-filter="due">${opts({'': 'Any due date', overdue: 'Overdue', today: 'Due today', week: 'Due within 7 days', nodue: 'No due date'}, b.due)}</select>
    <label class="small"><input type="checkbox" data-filter="showDone" ${b.showDone ? 'checked' : ''}> Show all done tasks</label>
  </div>
  <div id="board-body">${boardBody()}</div>`;
};

function boardTasks() {
  const b = ui.board, td = today(), q = b.q.trim().toLowerCase();
  return db.tasks.filter(t => {
    if (b.project === '_none' ? t.projectId : b.project && t.projectId !== b.project) return false;
    if (b.owner && t.owner !== b.owner) return false;
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
    return `<div class="card"><table><thead><tr><th></th><th>Task</th><th>Project</th><th>Owner</th><th>Status</th><th>Due</th><th class="num">Hours</th></tr></thead><tbody>
      ${list.map(t => {
        const d = dueInfo(t), p = proj(t.projectId), c = openCommitCount(t.id);
        const h = db.updates.filter(u => u.taskId === t.id).reduce((s, u) => s + (+u.hours || 0), 0);
        return `<tr class="click ${ui.taskId === t.id ? 'sel' : ''}" data-act="open-task" data-id="${t.id}">
          <td><input type="checkbox" data-act="toggle-done" data-id="${t.id}" ${isDone(t) ? 'checked' : ''}></td>
          <td>${esc(t.title)} ${c ? `<span class="chip warn">${plural(c, 'commitment')}</span>` : ''}</td>
          <td>${esc(p ? (p.code || p.name) : '-')}</td><td>${esc(t.owner || '-')}</td>
          <td>${statusChip(t.status)}</td>
          <td class="due ${d.cls}">${esc(d.txt)}</td><td class="num">${h ? fmtH(h) : ''}</td></tr>`;
      }).join('')}
      </tbody></table><div class="small muted" style="margin-top:8px">${plural(list.length, 'task')}</div></div>`;
  }
  const cols = statuses();
  return `<div class="kanban" style="grid-template-columns:repeat(${cols.length},minmax(230px,1fr))">${cols.map(s => {
    const col = list.filter(t => st(t.status).id === s.id);
    return `<div class="col" data-drop="${s.id}"><h3><span class="sdot c-${s.color}"></span>${esc(s.name)} <span class="muted">${col.length}</span></h3>
      ${col.map(t => {
        const d = dueInfo(t);
        return `<div class="kcard p-${t.priority} ${ui.taskId === t.id ? 'sel' : ''}" draggable="true" data-drag="${t.id}" data-act="open-task" data-id="${t.id}">
          <div class="ttitle">${esc(t.title)}</div>
          <div class="tmeta">${taskMeta(t, true, false)}</div>
          <div class="due ${d.cls}" style="margin-top:4px">${esc(d.txt)}</div></div>`;
      }).join('')}
    </div>`;
  }).join('')}</div>
    <p class="small muted">Tip: drag a card to change its status. Done columns show tasks finished in the last 14 days only.</p>`;
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
      <div style="display:flex;gap:8px;align-items:center"><h3>${esc(p.name)}</h3><span style="margin-left:auto">${projChip(p)}</span></div>
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
    <h1>${esc(p.name)} ${projChip(p)}</h1>
    <button class="btn" data-act="edit-project" data-id="${p.id}">Edit project</button>
    <button class="btn primary" data-act="new-task" data-project="${p.id}">+ New task</button></div>
  <div class="card small">
    <b>Code:</b> ${esc(p.code || '-')} &nbsp; <b>Client / Dept:</b> ${esc(p.client || '-')} &nbsp;
    <b>Period:</b> ${p.start ? fmtMD(p.start) : '?'} – ${p.end ? fmtFull(p.end) : '?'}${left !== null && p.status !== 'closed' ? ` (${left >= 0 ? `${plural(left, 'day')} left` : `${plural(-left, 'day')} over`})` : ''} &nbsp;
    <b>Total hours:</b> ${fmtH(hours)}
    ${p.description ? `<div class="muted" style="margin-top:6px;white-space:pre-wrap">${esc(p.description)}</div>` : ''}
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

views.timesheet = () => {
  const days = weekDays();
  if (!days.includes(ui.tsDay)) ui.tsDay = days.includes(today()) ? today() : days[0];
  const rows = {};
  for (const u of db.updates.filter(u => +u.hours > 0 && u.date >= days[0] && u.date <= days[6])) {
    const k = u.projectId || '';
    rows[k] = rows[k] || {};
    rows[k][u.date] = (rows[k][u.date] || 0) + +u.hours;
  }
  const keys = Object.keys(rows).sort((a, b) => projLabel(proj(a)).localeCompare(projLabel(proj(b))));
  const colTotal = d => keys.reduce((s, k) => s + (rows[k][d] || 0), 0);
  const weekTotal = days.reduce((s, d) => s + colTotal(d), 0);
  const std = +db.settings.hoursPerDay || 8;
  const dayLogs = db.updates.filter(u => u.date === ui.tsDay && isActivity(u));
  return `<div class="head"><h1>Timesheet</h1>
    <button class="btn" data-act="ts-week" data-n="-7">◀ Previous</button>
    <button class="btn" data-act="ts-week" data-n="0">This week</button>
    <button class="btn" data-act="ts-week" data-n="7">Next ▶</button>
    <button class="btn" data-act="export-csv">Export week (CSV)</button></div>
  <div class="card ts"><h2>${fmtMD(days[0])} – ${fmtFull(days[6])} <span class="muted small">Click a day to see its details</span><span class="r small">Week total <b>${fmtH(weekTotal)}</b></span></h2>
    <table><thead><tr><th>Project</th>${days.map(d => `<th class="num day ${d === ui.tsDay ? 'on' : ''}" data-act="ts-day" data-d="${d}">${WD[parse(d).getDay()]}<br>${fmtMD(d)}</th>`).join('')}<th class="num">Total</th></tr></thead>
    <tbody>${keys.length ? keys.map(k => `<tr><td>${esc(projLabel(proj(k)))}</td>${days.map(d => `<td class="num">${rows[k][d] ? round2(rows[k][d]) : ''}</td>`).join('')}<td class="num"><b>${round2(days.reduce((s, d) => s + (rows[k][d] || 0), 0))}</b></td></tr>`).join('')
      : `<tr><td colspan="9" class="empty">No hours logged this week</td></tr>`}</tbody>
    <tfoot><tr><td>Daily total</td>${days.map(d => {
      const t = colTotal(d), wd = parse(d).getDay();
      const low = t < std && wd > 0 && wd < 6 && d <= today();
      return `<td class="num ${low ? 'low' : ''}">${t ? round2(t) : low ? '0' : ''}</td>`;
    }).join('')}<td class="num">${round2(weekTotal)}</td></tr></tfoot></table>
    <div class="small muted" style="margin-top:6px">Workdays with less than ${std}h are shown in orange.</div>
  </div>
  <div class="grid g2">
    <div class="card"><h2>Records for ${fmtDate(ui.tsDay)} <span class="r"><button class="btn sm primary" data-act="quick-log" data-d="${ui.tsDay}">+ Log time</button></span></h2>${logList(dayLogs)}</div>
    <div class="card"><h2>Daily summary <span class="r"><button class="btn sm primary" data-act="copy-summary">Copy</button></span></h2>
      <textarea class="summary" id="summary">${esc(daySummary(ui.tsDay))}</textarea>
      <div class="small muted">You can edit the text before copying.</div></div>
  </div>`;
};

export function daySummary(date) {
  const logs = db.updates.filter(u => u.date === date && isActivity(u)).sort(by(u => u.createdAt || 0));
  if (!logs.length) return `${fmtFull(date)}\nNo records.`;
  const total = logs.reduce((s, u) => s + (+u.hours || 0), 0);
  const groups = {};
  for (const u of logs) (groups[u.projectId || ''] = groups[u.projectId || ''] || []).push(u);
  let out = `${fmtFull(date)} - Total ${fmtH(total)}\n`;
  for (const [pid, list] of Object.entries(groups)) {
    const h = list.reduce((s, u) => s + (+u.hours || 0), 0);
    out += `\n[${pid ? projLabel(proj(pid)) : 'General'}] ${h ? fmtH(h) : ''}\n`;
    for (const u of list) {
      const t = task(u.taskId);
      const tag = u.type === 'work' ? '' : `[${LOGTYPE[u.type]}] `;
      out += `- ${tag}${t ? t.title + ': ' : ''}${u.text.replace(/\n+/g, ' ')}${u.hours ? ` (${fmtH(+u.hours)})` : ''}\n`;
    }
  }
  return out.trim();
}

export function exportCsv() {
  const days = weekDays();
  const logs = db.updates.filter(u => u.date >= days[0] && u.date <= days[6] && isActivity(u)).sort(by(u => u.date));
  const q = v => `"${String(v ?? '').replace(/"/g, '""')}"`;
  const lines = [['Date', 'Weekday', 'Project Code', 'Project', 'Task', 'Type', 'Hours', 'Content'].join(',')];
  for (const u of logs) {
    const p = proj(u.projectId), t = task(u.taskId);
    lines.push([u.date, WD[parse(u.date).getDay()], p?.code, p?.name, t?.title, LOGTYPE[u.type], u.hours || '', u.text].map(q).join(','));
  }
  download(`timesheet-${days[0]}.csv`, '\ufeff' + lines.join('\r\n'), 'text/csv');
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
