// Entry point: rendering, click/form/keyboard handling, and start-up.
import { $, esc, today, addDays, weekStart, plural, sleep } from './util.js';
import { api } from './api.js';
import * as store from './store.js';
import { db, task, isDone, isOpen, firstOpen, firstDone } from './store.js';
import { ui, app, isAdmin } from './state.js';
import { views, boardBody, daySummary, exportCsv } from './views.js';
import { openTask, newTask, closeDrawer, renderDrawer, refreshTimeline, autosaveTask, syncDrawerStatus } from './drawer.js';
import { quickLogModal, editLogModal, commitModal, projectModal, milestoneModal, statusEditor } from './modals.js';
import { toast, hideToast, closeModal, msOptions, taskOptions } from './components.js';
import { notesView, noteListHtml, noteEdited, flushNote, openNote, createNote, noteSearchChanged, editorSelection } from './notes.js';
import { raidView, raidModal, raidTypeChanged } from './raid.js';
import { buildSample } from './sample.js';
import { usersView, userModal, resetPasswordModal, accountModal, generatePassword } from './users.js';

views.notes = notesView;
views.raid = raidView;
views.users = usersView;

async function loadUsers() {
  try { ui.users = await api.get('/api/users'); } catch (err) { ui.users = []; toast(err.message, {error: true}); }
}

/** A dialog the user must finish, for example choosing a new password. Esc and clicking outside do not close it. */
function openForcedAccount() {
  ui.forcedModal = true;
  accountModal(true);
}
function closeAnyModal() { if (!ui.forcedModal) closeModal(); }

/** Shows "… deleted" with an Undo button. */
const deleted = r => toast(`${r.label} deleted`, {undoId: r.undoId});

async function loadTrash() {
  try { ui.trash = await store.listTrash(); } catch { ui.trash = []; }
}

/* ---------- render ---------- */
function render() {
  if (ui.view === 'users' && !isAdmin()) ui.view = 'today';
  document.querySelectorAll('.nav').forEach(n => n.classList.toggle('on', n.dataset.view === (ui.view === 'project' ? 'projects' : ui.view)));
  $('#nav-users').hidden = !isAdmin();
  $('#main').innerHTML = updateBanner() + views[ui.view]();
  const td = today(), n = db.tasks.filter(t => isOpen(t) && t.due && t.due <= td).length;
  const badge = $('#n-today');
  badge.hidden = !n;
  badge.textContent = n;
  renderSideFoot();
}
Object.assign(app, {render, renderDrawer, refreshTimeline});

function renderSideFoot() {
  const sys = ui.system, me = ui.me;
  $('#side-foot').innerHTML = (me ? `<div class="me">${esc(me.displayName)}</div>
      <div class="small muted">${me.role === 'admin' ? 'Admin' : 'Member'}</div>
      <div class="me-actions"><button class="btn sm" data-act="account">My account</button><button class="btn sm" data-act="logout">Sign out</button></div>` : '') +
    (sys ? `<div style="margin-top:8px">v${esc(sys.version)}${sys.commit ? ` · ${esc(sys.commit)}` : ''}</div>` : '');
}

function updateBanner() {
  const s = ui.system;
  if (!s?.behind || !isAdmin()) return '';
  return `<div class="banner info">A new version is available (${plural(s.behind, 'change')}).<span class="sp"></span>
    <button class="btn sm primary" data-act="do-update">Update and restart</button></div>`;
}

async function loadSystem() {
  try { ui.system = await api.get('/api/system/info'); } catch { /* not critical */ }
}

/* ---------- forms ---------- */
const forms = {
  async task(f, d) {
    if (f.dataset.id) { document.activeElement?.blur(); return; } // existing tasks save field by field
    const t = await store.createTask({title: d.get('title'), projectId: d.get('projectId'), milestoneId: d.get('milestoneId'), owner: d.get('owner'),
      due: d.get('due'), status: d.get('status'), priority: d.get('priority'), description: d.get('description')});
    openTask(t.id);
    toast('Task created');
  },
  async 'quick-task'(f, d) {
    if (!d.get('title').trim()) return;
    await store.createTask({title: d.get('title'), projectId: d.get('projectId'), owner: d.get('owner'), due: d.get('due'), status: firstOpen()?.id, priority: 'medium'});
    render();
    toast('Task added');
    setTimeout(() => $('[data-form="quick-task"] [name=title]')?.focus());
  },
  async log(f, d) {
    const type = d.get('type');
    await store.addUpdate({taskId: f.dataset.task, date: d.get('date'), type, text: d.get('text'), hours: +d.get('hours') || 0,
      who: type === 'commit' ? d.get('who') : '', commitDue: type === 'commit' ? d.get('commitDue') : ''});
    f.text.value = '';
    f.hours.value = '';
    refreshTimeline();
    render();
    toast('Update added');
  },
  async 'quick-log'(f, d) {
    await store.addUpdate({taskId: d.get('taskId'), projectId: d.get('projectId'), date: d.get('date'), type: d.get('type'),
      text: d.get('text'), hours: +d.get('hours') || 0});
    ui.tsDay = d.get('date');
    closeModal();
    render();
    refreshTimeline();
    toast('Time logged');
  },
  async project(f, d) {
    const id = f.dataset.id;
    const p = await store.saveProject(id, Object.fromEntries(['name', 'code', 'client', 'start', 'end', 'status', 'description'].map(k => [k, d.get(k)])));
    if (!id) { ui.projectId = p.id; ui.view = 'project'; }
    closeModal();
    render();
    toast('Project saved');
  },
  async milestone(f, d) {
    await store.saveMilestone(f.dataset.id, {projectId: f.dataset.project, ...Object.fromEntries(['name', 'planDate', 'actualDate', 'status', 'note'].map(k => [k, d.get(k)]))});
    closeModal();
    render();
  },
  /* users and account */
  async 'new-user'(f, d) {
    await api.post('/api/users', {userName: d.get('userName'), displayName: d.get('displayName'), role: d.get('role'), password: d.get('password')});
    await loadUsers();
    closeModal();
    render();
    toast(`User added. Give them the user name "${d.get('userName')}" and the temporary password.`);
  },
  async 'edit-user'(f, d) {
    await api.put(`/api/users/${f.dataset.id}`, {displayName: d.get('displayName'), role: d.get('role'), isActive: d.get('isActive') === 'on'});
    await loadUsers();
    closeModal();
    render();
    toast('User saved');
  },
  async 'reset-password'(f, d) {
    await api.post(`/api/users/${f.dataset.id}/reset-password`, {newPassword: d.get('password')});
    await loadUsers();
    closeModal();
    render();
    toast('Password reset. Give the user the temporary password.');
  },
  async profile(f, d) {
    ui.me = await api.put('/api/auth/profile', {displayName: d.get('displayName')});
    closeModal();
    render();
    toast('Name saved');
  },
  async 'change-password'(f, d) {
    if (d.get('newPassword') !== d.get('confirm')) throw new Error('The two new passwords do not match.');
    ui.me = await api.post('/api/auth/change-password', {currentPassword: d.get('currentPassword'), newPassword: d.get('newPassword')});
    const wasForced = ui.forcedModal;
    ui.forcedModal = false;
    closeModal();
    toast('Password changed');
    if (wasForced) await loadApp(); else render();
  },

  async settings(f, d) {
    await store.saveSettings({hoursPerDay: +d.get('hoursPerDay') || 8, backupDir: d.get('backupDir'),
      remindersEnabled: d.get('remindersEnabled') === 'on', morningReminder: d.get('morningReminder'), eveningReminder: d.get('eveningReminder')});
    await loadSystem();
    render();
    toast('Settings saved');
  },
  // Notes save while typing; Enter in the title just moves on.
  async note() { document.activeElement?.blur(); },
  async 'edit-log'(f, d) {
    const type = d.get('type');
    await store.editUpdate(f.dataset.id, {projectId: d.get('projectId') ?? undefined, date: d.get('date'), type, text: d.get('text'),
      hours: +d.get('hours') || 0, who: type === 'commit' ? d.get('who') : '', commitDue: type === 'commit' ? d.get('commitDue') : ''});
    closeModal();
    refreshTimeline();
    render();
    toast('Record updated');
  },
  async 'quick-commit'(f, d) {
    await store.addUpdate({taskId: d.get('taskId'), projectId: d.get('projectId'), type: 'commit', text: d.get('text'),
      who: d.get('who'), commitDue: d.get('commitDue'), hours: 0});
    closeModal();
    toast('Commitment added. It shows on the Today page until it is kept.');
  },
  async raid(f, d) {
    const fields = ['projectId', 'type', 'title', 'description', 'owner', 'impact', 'likelihood', 'status', 'response', 'dueDate'];
    const r = await store.saveRaid(f.dataset.id, Object.fromEntries(fields.map(k => [k, d.get(k) ?? ''])));
    closeModal();
    render();
    toast(f.dataset.id ? 'RAID item saved' : `Added ${r.type[0].toUpperCase()}-${r.number}`);
  },
};

/* ---------- click actions ---------- */
async function toggleDone(t) {
  const saved = await store.setStatus(t, isDone(t) ? firstOpen().id : firstDone().id);
  syncDrawerStatus(saved);
  return saved;
}

const actions = {
  nav: async a => {
    await flushNote();
    ui.view = a.dataset.view;
    closeModal();
    if (ui.view === 'settings') ui.trash = null;
    if (ui.view === 'users') ui.users = null;
    render();
    window.scrollTo(0, 0);
    if (ui.view === 'settings') { await Promise.all([loadSystem(), loadTrash()]); render(); }
    if (ui.view === 'users') { await loadUsers(); render(); }
  },
  account: () => accountModal(),
  logout: async () => {
    await flushNote();
    await api.post('/api/auth/logout');
    location.href = '/login.html';
  },
  'new-user': () => userModal(),
  'edit-user': a => userModal(a.dataset.id),
  'reset-password': a => resetPasswordModal(a.dataset.id),
  'gen-password': a => { a.closest('form').password.value = generatePassword(); },
  undo: async a => {
    hideToast();
    await store.restore(a.dataset.id);
    if (ui.view === 'settings') await loadTrash();
    render();
    if (ui.taskId) renderDrawer();
    toast('Restored');
  },
  restore: async a => {
    await store.restore(a.dataset.id);
    await loadTrash();
    render();
    toast('Restored');
  },
  close: () => closeModal(),
  'close-drawer': () => closeDrawer(),
  'new-task': a => newTask({projectId: a.dataset.project}),
  'open-task': a => a.dataset.id && openTask(a.dataset.id),
  'toggle-done': async a => {
    const t = await toggleDone(task(a.dataset.id));
    render();
    if (isDone(t)) toast('Done: ' + t.title);
  },
  'toggle-commit': async a => {
    const u = db.updates.find(x => x.id === a.dataset.id);
    await store.setCommitDone(u.id, !u.commitDone);
    refreshTimeline();
    render();
  },
  // Small deletes happen right away: the toast offers Undo.
  'del-log': async a => {
    const r = await store.deleteUpdate(a.dataset.id);
    closeModal();
    refreshTimeline();
    render();
    deleted(r);
  },
  'edit-log': a => editLogModal(a.dataset.id),
  'log-to-raid': a => {
    const u = db.updates.find(x => x.id === a.dataset.id);
    raidModal(null, {projectId: u.projectId, type: 'risk', title: u.text.split('\n')[0].slice(0, 200), description: u.text});
  },
  'tl-filter': a => { ui.tlFilter = a.dataset.f; refreshTimeline(); },
  'del-task': async a => {
    const r = await store.deleteTask(a.dataset.id);
    closeDrawer();
    render();
    deleted(r);
  },
  'new-project': () => projectModal(),
  'edit-project': a => projectModal(a.dataset.id),
  'open-project': a => { ui.projectId = a.dataset.id; ui.view = 'project'; render(); window.scrollTo(0, 0); },
  'del-project': async a => {
    const p = store.proj(a.dataset.id);
    const nt = db.tasks.filter(t => t.projectId === p.id).length, nl = db.updates.filter(u => u.projectId === p.id && u.type !== 'status').length;
    if (!confirm(`Delete project "${p.name}"?\nThis also deletes ${plural(nt, 'task')}, ${plural(nl, 'record')}, and its notes and RAID items.\nYou can restore it from Settings for 30 days.\nIf the project has ended, set its status to Closed instead.`)) return;
    const r = await store.deleteProject(p.id);
    if (ui.taskId && !task(ui.taskId)) closeDrawer();
    closeModal();
    ui.view = 'projects';
    render();
    deleted(r);
  },
  'toggle-closed': () => { ui.showClosed = !ui.showClosed; render(); },
  'new-milestone': a => milestoneModal(null, a.dataset.project),
  'edit-milestone': a => milestoneModal(a.dataset.id),
  'del-milestone': async a => {
    const r = await store.deleteMilestone(a.dataset.id);
    closeModal();
    render();
    deleted(r);
  },

  /* notes */
  'new-note': async a => {
    await createNote(a.dataset.project);
    render();
    setTimeout(() => $('#note-editor [name=body]')?.focus());
  },
  'open-note': async a => { await openNote(a.dataset.id); render(); },
  'notes-for-project': a => { ui.notes.project = a.dataset.id; ui.notes.q = ''; ui.notes.hits = null; ui.view = 'notes'; render(); },
  'del-note': async a => {
    await flushNote();
    const r = await store.deleteNote(a.dataset.id);
    ui.notes.selectedId = null;
    render();
    deleted(r);
  },
  'note-to-task': async () => {
    const text = editorSelection();
    if (!text) return toast('Select some text or put the cursor on a line first.', {error: true});
    await flushNote();
    newTask({projectId: store.note(ui.notes.selectedId)?.projectId || '', title: text});
  },
  'note-to-commit': async () => {
    const text = editorSelection();
    if (!text) return toast('Select some text or put the cursor on a line first.', {error: true});
    await flushNote();
    commitModal(text, store.note(ui.notes.selectedId)?.projectId);
  },
  'note-to-raid': async () => {
    const text = editorSelection();
    if (!text) return toast('Select some text or put the cursor on a line first.', {error: true});
    await flushNote();
    raidModal(null, {projectId: store.note(ui.notes.selectedId)?.projectId, title: text});
  },

  /* RAID */
  'new-raid': a => raidModal(null, {projectId: a.dataset.project}),
  'edit-raid': a => raidModal(a.dataset.id),
  'raid-filter': a => { ui.raid[a.dataset.k] = a.dataset.v; render(); },
  'raid-for-project': a => { Object.assign(ui.raid, {project: a.dataset.id, type: '', status: 'active'}); ui.view = 'raid'; render(); window.scrollTo(0, 0); },
  'del-raid': async a => {
    const r = await store.deleteRaid(a.dataset.id);
    closeModal();
    render();
    deleted(r);
  },

  'test-reminder': async () => {
    await api.post('/api/system/test-reminder');
    toast('Test reminder sent. Look near the clock.');
  },
  'board-mode': a => { ui.board.mode = a.dataset.mode; render(); },
  'board-filter': a => { Object.assign(ui.board, {project: '', owner: '', q: '', due: a.dataset.due}); ui.view = 'board'; render(); },
  'edit-statuses': () => statusEditor.start(),
  's-move': a => statusEditor.move(+a.dataset.i, +a.dataset.d),
  's-del': a => statusEditor.remove(+a.dataset.i),
  's-add': () => statusEditor.add(),
  's-save': async () => {
    const err = statusEditor.validate();
    if (err) return toast(err, true);
    await store.saveStatuses(statusEditor.draft);
    closeModal();
    render();
    if (ui.taskId) renderDrawer();
    toast('Columns saved');
  },
  'quick-log': a => quickLogModal(a.dataset.d),
  'ts-week': a => {
    const n = +a.dataset.n;
    ui.tsWeek = n ? addDays(ui.tsWeek, n) : weekStart(today());
    ui.tsDay = n ? addDays(ui.tsDay, n) : today();
    render();
  },
  'ts-day': a => { ui.tsDay = a.dataset.d; render(); },
  'copy-summary': async () => {
    const txt = $('#summary').value;
    try { await navigator.clipboard.writeText(txt); } catch { $('#summary').select(); document.execCommand('copy'); }
    toast('Copied to clipboard');
  },
  'export-csv': () => exportCsv(),
  'import-json': () => $('#import-input').click(),
  'backup-now': async () => {
    const r = await api.post('/api/system/backup');
    await loadSystem();
    render();
    toast(r.error ? 'Backup finished with a problem: ' + r.error : `Backup saved (${plural(r.files.length, 'file')})`, !!r.error);
  },
  'load-sample': async () => {
    await store.importData(buildSample(ui.me.displayName), 'merge');
    ui.view = 'today';
    render();
    toast('Sample data loaded. You can remove it in Settings.');
  },
  'remove-sample': async () => {
    if (!confirm('Remove all sample projects, tasks and records? Your own data is kept.')) return;
    await store.removeSample();
    if (ui.taskId && !task(ui.taskId)) closeDrawer();
    render();
  },
  'clear-all': async () => {
    if (!confirm('Delete all projects, tasks and records? This cannot be undone. Export a backup first.')) return;
    if (prompt('Type DELETE to confirm') !== 'DELETE') return;
    await store.clearAll();
    closeDrawer();
    ui.view = 'today';
    render();
  },
  'check-updates': async () => {
    ui.system = await api.post('/api/system/check-updates');
    render();
    toast(ui.system.updateError ? 'Could not check for updates' : ui.system.behind ? 'A new version is available' : 'You have the latest version', !!ui.system.updateError);
  },
  'do-update': async () => {
    if (!confirm('Download the new version and restart? This takes about 30 seconds.')) return;
    const oldCommit = ui.system?.commit;
    await api.post('/api/system/update');
    $('#overlay').hidden = false;
    await sleep(3000);
    for (let i = 0; i < 120; i++) {
      try {
        const info = await api.get('/api/system/info');
        if (info.commit !== oldCommit || i > 30) { location.reload(); return; }
      } catch { /* still restarting */ }
      await sleep(2000);
    }
    $('#overlay').hidden = true;
    toast('The app did not come back. Start it again from the desktop shortcut.', true);
  },
};

/* ---------- events ---------- */
const run = async fn => { try { await fn(); } catch (err) { toast(err.message, true); } };

document.addEventListener('click', e => {
  if (e.target.id === 'modal') return closeAnyModal();
  const a = e.target.closest('[data-act]');
  if (!a || !actions[a.dataset.act]) return;
  if (a.tagName === 'A') e.preventDefault();
  run(() => actions[a.dataset.act](a, e));
});

document.addEventListener('submit', e => {
  const f = e.target;
  if (!forms[f.dataset.form]) return;
  e.preventDefault();
  const btn = f.querySelector('button:not([type=button])');
  if (btn) btn.disabled = true;
  run(() => forms[f.dataset.form](f, new FormData(f))).finally(() => { if (btn) btn.disabled = false; });
});

document.addEventListener('input', e => {
  const el = e.target;
  if (el.dataset.filter) {
    ui.board[el.dataset.filter] = el.type === 'checkbox' ? el.checked : el.value;
    $('#board-body').innerHTML = boardBody();
  }
  if (el.dataset.sfield === 'name') statusEditor.set(+el.dataset.i, 'name', el.value);
  if (el.hasAttribute('data-note-search')) noteSearchChanged(el.value);
  if (el.form?.dataset.form === 'note' && ['title', 'body', 'tags'].includes(el.name)) noteEdited(el.form);
});

document.addEventListener('change', e => {
  const el = e.target, form = el.form;
  if (el.dataset.filter !== undefined && el.tagName === 'SELECT') { ui.board[el.dataset.filter] = el.value; $('#board-body').innerHTML = boardBody(); }
  if (el.dataset.sfield === 'color') statusEditor.set(+el.dataset.i, 'color', el.value);
  if (el.dataset.sfield === 'isDone') statusEditor.set(+el.dataset.i, 'isDone', el.checked);
  if (el.hasAttribute('data-task-target')) form.taskId.innerHTML = taskOptions(el.value, '');
  if (form?.dataset.form === 'task') {
    if (el.name === 'projectId') form.milestoneId.innerHTML = msOptions(el.value, '');
    if (form.dataset.id) run(() => autosaveTask(el));
  }
  if ((form?.dataset.form === 'log' || form?.dataset.form === 'edit-log') && el.name === 'type') {
    form.querySelectorAll('.cf').forEach(x => { x.hidden = el.value !== 'commit'; });
  }
  if (form?.dataset.form === 'note' && ['projectId', 'noteDate', 'pinned'].includes(el.name)) noteEdited(form, true);
  if (el.hasAttribute('data-note-project')) {
    ui.notes.project = el.value;
    if (ui.notes.q.trim()) noteSearchChanged(ui.notes.q); else $('#note-list').innerHTML = noteListHtml();
  }
  if (el.dataset.raidFilter) { ui.raid[el.dataset.raidFilter] = el.value; render(); }
  if (el.hasAttribute('data-raid-type')) raidTypeChanged(el);
});

$('#import-input').addEventListener('change', e => run(async () => {
  const file = e.target.files[0];
  e.target.value = '';
  if (!file) return;
  let data;
  try { data = JSON.parse(await file.text()); } catch { throw new Error('This file is not valid JSON.'); }
  const count = k => (data[k] || []).length;
  if (!confirm(`Replace ALL current data with this backup?\nThe backup has ${plural(count('projects'), 'project')}, ${plural(count('tasks'), 'task')} and ${plural(count('updates') + count('logs'), 'record')}.`)) return;
  const r = await store.importData(data, 'replace');
  closeDrawer();
  render();
  toast(`Restored ${plural(r.tasks, 'task')} and ${plural(r.updates, 'record')}`);
}));

// Drag a card to another board column
document.addEventListener('dragstart', e => { const c = e.target.closest?.('[data-drag]'); if (c) e.dataTransfer.setData('text/plain', c.dataset.drag); });
document.addEventListener('dragover', e => { const c = e.target.closest?.('[data-drop]'); if (c) { e.preventDefault(); c.classList.add('over'); } });
document.addEventListener('dragleave', e => { const c = e.target.closest?.('[data-drop]'); if (c && !c.contains(e.relatedTarget)) c.classList.remove('over'); });
document.addEventListener('drop', e => {
  const c = e.target.closest?.('[data-drop]');
  if (!c) return;
  e.preventDefault();
  const t = task(e.dataTransfer.getData('text/plain'));
  if (!t) return;
  run(async () => {
    syncDrawerStatus(await store.setStatus(t, c.dataset.drop));
    $('#board-body').innerHTML = boardBody();
  });
});

document.addEventListener('keydown', e => {
  const tag = (e.target.tagName || '').toLowerCase(), typing = ['input', 'textarea', 'select'].includes(tag);
  if (e.key === 'Escape') {
    if (!$('#modal').hidden) return closeAnyModal();
    if (typing && e.target.closest('#note-editor')) return e.target.blur();
    if (typing && e.target.closest('#drawer')) return e.target.blur();
    if (!$('#drawer').hidden) return closeDrawer();
  }
  if (typing || e.ctrlKey || e.metaKey || e.altKey || !$('#modal').hidden) return;
  if (e.key === 'n' || e.key === 'N') { e.preventDefault(); newTask(); }
  if (e.key === 'l' || e.key === 'L') { e.preventDefault(); quickLogModal(ui.view === 'timesheet' ? ui.tsDay : today()); }
});

/* ---------- start ---------- */
let lastDay = today();
setInterval(() => { if (today() !== lastDay) { lastDay = today(); render(); refreshTimeline(); } }, 60_000);
setInterval(() => loadSystem().then(() => {
  if (!ui.forcedModal && (!document.activeElement || document.activeElement === document.body)) render();
}), 30 * 60_000);
// Save a note that is still waiting for its autosave when the window closes.
window.addEventListener('pagehide', () => { flushNote(); });

async function loadApp() {
  await Promise.all([store.load(), loadSystem()]);
  render();
}

run(async () => {
  // Not signed in: api.js sends the browser to the login page.
  ui.me = await api.get('/api/auth/me');
  if (ui.me.mustChangePassword) {
    $('#main').innerHTML = '';
    renderSideFoot();
    openForcedAccount();
    return;
  }
  await loadApp();
});
