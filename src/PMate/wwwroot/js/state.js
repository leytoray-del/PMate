// UI state that is not saved on the server.
import { today, weekStart } from './util.js';

export const ui = {
  view: 'today',
  board: {project: '', owner: '', tag: '', q: '', due: '', mode: 'kanban', group: 'project', showDone: false},
  projectId: null,
  showClosed: false,
  tsWeek: weekStart(today()),
  tsDay: today(),
  tsUser: null,       // whose timesheet (admins can pick); null = me
  taskId: null,       // task open in the drawer
  newTask: null,      // preset for the "new task" drawer
  tlFilter: 'all',    // timeline filter in the drawer
  system: null,       // /api/system/info
  notes: {selectedId: null, q: '', project: '', hits: null},
  raid: {project: '', type: '', status: 'active'},
  trash: null,        // /api/trash, loaded when Settings opens
  me: null,           // signed-in user: {id, userName, displayName, role, mustChangePassword}
  users: null,        // /api/users, loaded when the Users page opens (admins only)
};

export const isAdmin = () => ui.me?.role === 'admin';

// Render functions, set by main.js. Other modules call these to refresh the screen.
export const app = {
  render: () => {},
  renderDrawer: () => {},
  refreshTimeline: () => {},
};
