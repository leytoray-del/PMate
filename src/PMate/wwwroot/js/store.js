// Client copy of the data, loaded from the server, and all changes to it.
// Every change goes to the API first. The local copy is updated from the server's answer.
import { api } from './api.js';
import { by } from './util.js';
import { ui } from './state.js';

export const db = {settings: {hoursPerDay: 8, backupDir: ''}, statuses: [], projects: [], milestones: [], tasks: [], updates: [], notes: [], raidItems: [],
  projectMembers: [], people: []};

const TIME_KEYS = new Set(['createdAt', 'updatedAt', 'doneAt', 'exportedAt']);

// Server → UI: null becomes '', timestamps become milliseconds.
function norm(o) {
  const r = {};
  for (const [k, v] of Object.entries(o || {})) r[k] = TIME_KEYS.has(k) ? (v ? Date.parse(v) : 0) : v ?? '';
  return r;
}
const upsert = (list, item) => {
  const i = list.findIndex(x => x.id === item.id);
  if (i >= 0) list[i] = item; else list.push(item);
  return item;
};

export async function load() {
  const s = await api.get('/api/snapshot');
  db.settings = norm(s.settings);
  db.statuses = s.statuses.map(norm).sort(by(x => x.sortOrder));
  db.projects = s.projects.map(norm);
  db.milestones = s.milestones.map(norm);
  db.tasks = s.tasks.map(norm);
  db.updates = s.updates.map(norm);
  db.notes = (s.notes || []).map(norm);
  db.raidItems = (s.raidItems || []).map(norm);
  db.projectMembers = s.projectMembers || [];
  db.people = s.people || [];
}

/* ---------- lookups ---------- */
export const proj = id => db.projects.find(p => p.id === id);
export const task = id => db.tasks.find(t => t.id === id);
export const ms = id => db.milestones.find(m => m.id === id);
export const note = id => db.notes.find(n => n.id === id);
export const raid = id => db.raidItems.find(r => r.id === id);
export const statuses = () => db.statuses;
export const st = id => db.statuses.find(s => s.id === id) || db.statuses[0];
export const firstOpen = () => db.statuses.find(s => !s.isDone) || db.statuses[0];
export const firstDone = () => db.statuses.find(s => s.isDone) || db.statuses[db.statuses.length - 1];
export const isDone = t => !!st(t.status)?.isDone;
export const isOpen = t => !isDone(t);
export const isActivity = u => u.type !== 'status';
export const recentDone = t => isDone(t) && t.doneAt && Date.now() - t.doneAt < 14 * 864e5;
export const projLabel = p => p ? (p.code ? `${p.code} ${p.name}` : p.name) : 'No project';
export const openCommitCount = id => db.updates.filter(u => u.taskId === id && u.type === 'commit' && !u.commitDone).length;
/** Names to offer as task owner: active users you can see, plus names already used (for example vendors). */
export const owners = () => [...new Set([ui.me?.displayName, ...db.people.filter(p => p.isActive).map(p => p.displayName),
  ...db.tasks.map(t => t.owner), ...db.updates.map(u => u.who)].filter(Boolean))].sort();
export const person = id => db.people.find(p => p.id === id);
export const team = projectId => db.projectMembers.filter(m => m.projectId === projectId).map(m => person(m.userId)).filter(Boolean);

/* ---------- tasks ---------- */
const taskBody = t => ({title: t.title, projectId: t.projectId, milestoneId: t.milestoneId, owner: t.owner,
  due: t.due, status: t.status, priority: t.priority, description: t.description});

export async function createTask(input) {
  return upsert(db.tasks, norm(await api.post('/api/tasks', taskBody(input))));
}
export async function saveTask(t, changes) {
  const r = await api.put(`/api/tasks/${t.id}`, taskBody({...t, ...changes}));
  const saved = upsert(db.tasks, norm(r.task));
  if (r.statusChange) db.updates.push(norm(r.statusChange));
  for (const u of db.updates) if (u.taskId === saved.id) u.projectId = saved.projectId;
  return saved;
}
export const setStatus = (t, status) => t.status === status ? t : saveTask(t, {status});
// Every delete returns {undoId, label}. Pass undoId to restore() to undo it.
export async function deleteTask(id) {
  const r = await api.del(`/api/tasks/${id}`);
  db.tasks = db.tasks.filter(t => t.id !== id);
  db.updates = db.updates.filter(u => u.taskId !== id);
  return r;
}

/* ---------- timeline records ---------- */
export async function addUpdate(input) {
  const u = norm(await api.post('/api/updates', input));
  db.updates.push(u);
  return u;
}
export async function setCommitDone(id, done) {
  upsert(db.updates, norm(await api.put(`/api/updates/${id}/commit-done`, {done})));
}
export async function editUpdate(id, input) {
  return upsert(db.updates, norm(await api.put(`/api/updates/${id}`, input)));
}
export async function deleteUpdate(id) {
  const r = await api.del(`/api/updates/${id}`);
  db.updates = db.updates.filter(u => u.id !== id);
  return r;
}

/* ---------- projects and milestones ---------- */
export async function saveProject(id, input) {
  return upsert(db.projects, norm(id ? await api.put(`/api/projects/${id}`, input) : await api.post('/api/projects', input)));
}
export async function deleteProject(id) {
  const r = await api.del(`/api/projects/${id}`);
  await load();
  return r;
}
/** Replaces the project team (admins only). */
export async function setTeam(projectId, userIds) {
  const members = await api.put(`/api/projects/${projectId}/members`, userIds);
  db.projectMembers = db.projectMembers.filter(m => m.projectId !== projectId).concat(members);
}
export async function saveMilestone(id, input) {
  return upsert(db.milestones, norm(id ? await api.put(`/api/milestones/${id}`, input) : await api.post('/api/milestones', input)));
}
export async function deleteMilestone(id) {
  const r = await api.del(`/api/milestones/${id}`);
  db.milestones = db.milestones.filter(m => m.id !== id);
  return r;
}

/* ---------- notes ---------- */
// content: the page as block editor JSON (a string). version: the version the editor started from.
// The server refuses a save based on an old version, so nobody overwrites someone else's newer changes.
const noteBody = n => ({projectId: n.projectId, title: n.title, body: n.body, tags: n.tags, noteDate: n.noteDate, pinned: !!n.pinned,
  content: n.content || null, version: n.version ?? null});
export async function saveNote(id, input) {
  return upsert(db.notes, norm(id ? await api.put(`/api/notes/${id}`, noteBody(input)) : await api.post('/api/notes', noteBody(input))));
}
export async function deleteNote(id) {
  const r = await api.del(`/api/notes/${id}`);
  db.notes = db.notes.filter(n => n.id !== id);
  return r;
}
export const searchNotes = (q, projectId) =>
  api.get(`/api/notes/search?q=${encodeURIComponent(q)}${projectId ? `&projectId=${encodeURIComponent(projectId)}` : ''}`);

/* ---------- RAID ---------- */
export async function saveRaid(id, input) {
  return upsert(db.raidItems, norm(id ? await api.put(`/api/raid/${id}`, input) : await api.post('/api/raid', input)));
}
export async function deleteRaid(id) {
  const r = await api.del(`/api/raid/${id}`);
  db.raidItems = db.raidItems.filter(x => x.id !== id);
  return r;
}

/* ---------- trash ---------- */
export const listTrash = () => api.get('/api/trash');
export async function restore(undoId) {
  await api.post(`/api/trash/${undoId}/restore`);
  await load();
}

/* ---------- settings, columns, data ---------- */
export async function saveSettings(s) { db.settings = norm(await api.put('/api/settings', s)); }
export async function saveStatuses(list) {
  await api.put('/api/statuses', list.map(({id, name, color, isDone}) => ({id, name, color, isDone})));
  await load();
}
export async function importData(json, mode) {
  const r = await api.post(`/api/import?mode=${mode}`, json, {raw: true});
  await load();
  return r;
}
export async function removeSample() { await api.del('/api/sample'); await load(); }
export async function clearAll() { await api.del('/api/data?confirm=DELETE'); await load(); }
