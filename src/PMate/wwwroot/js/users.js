// Users page (admins) and the "My account" dialog (everyone).
import { esc, plural } from './util.js';
import { ui } from './state.js';
import { openModal, opts } from './components.js';

const ROLE = {admin: 'Admin', member: 'Member'};
const when = v => v ? new Date(v).toLocaleString() : 'never';

export function usersView() {
  const list = ui.users;
  return `<div class="head"><h1>Users</h1><button class="btn primary" data-act="new-user">+ Add user</button></div>
  <div class="card">
    ${!list ? '<div class="empty">Loading…</div>' : `<table><thead><tr><th>Name</th><th>User name</th><th>Role</th><th>Status</th><th>Last sign-in</th><th></th></tr></thead><tbody>
      ${list.map(u => `<tr>
        <td>${esc(u.displayName)}${u.id === ui.me.id ? ' <span class="chip">you</span>' : ''}</td>
        <td><code>${esc(u.userName)}</code></td>
        <td><span class="chip ${u.role === 'admin' ? 'cc c-blue' : ''}">${ROLE[u.role]}</span></td>
        <td>${!u.isActive ? '<span class="chip">Disabled</span>' : u.mustChangePassword ? '<span class="chip warn">Must set new password</span>' : '<span class="chip ok">Active</span>'}</td>
        <td class="small muted">${when(u.lastLoginAt)}</td>
        <td class="num"><button class="btn sm" data-act="edit-user" data-id="${u.id}">Edit</button>
          <button class="btn sm" data-act="reset-password" data-id="${u.id}">Reset password</button></td></tr>`).join('')}
    </tbody></table><div class="small muted" style="margin-top:8px">${plural(list.filter(u => u.isActive).length, 'active user')}</div>`}
  </div>
  <div class="card small muted">
    <b>Admin</b> can manage users, settings, board columns, backups and updates. <b>Member</b> can work with projects, tasks, notes and the RAID log.<br>
    New users and reset passwords are temporary: the user must choose a new password at the next sign-in.
    Users are disabled instead of deleted, so their name stays on the records they created.
  </div>`;
}

/** A random temporary password that is easy to read out: no 0/O or 1/l/I. */
export function generatePassword() {
  const chars = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const bytes = crypto.getRandomValues(new Uint8Array(12));
  return [...bytes].map(b => chars[b % chars.length]).join('').replace(/(.{4})(?!$)/g, '$1-');
}

const tempPasswordField = () => `<label class="full">Temporary password
  <span style="display:flex;gap:6px"><input name="password" required minlength="8" autocomplete="off" value="${generatePassword()}" style="flex:1;font-family:Consolas,monospace">
  <button type="button" class="btn" data-act="gen-password">New</button></span>
  <span class="small muted">Give this to the user. They must choose their own password when they sign in.</span></label>`;

export function userModal(id) {
  const u = id ? ui.users.find(x => x.id === id) : null;
  openModal(`<h2>${u ? 'Edit user' : 'Add user'}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="${u ? 'edit-user' : 'new-user'}" data-id="${u?.id || ''}" autocomplete="off"><div class="form">
    ${u ? `<label>User name<input value="${esc(u.userName)}" disabled></label>`
      : `<label>User name<input name="userName" required autofocus autocapitalize="none" spellcheck="false" placeholder="e.g. ben.lim"></label>`}
    <label>Display name<input name="displayName" required value="${esc(u?.displayName || '')}" placeholder="e.g. Ben Lim"></label>
    <label>Role<select name="role">${opts(ROLE, u?.role || 'member')}</select></label>
    ${u ? `<label style="flex-direction:row;align-items:center;gap:8px;margin-top:18px"><input type="checkbox" name="isActive" ${u.isActive ? 'checked' : ''}> Account is active</label>`
      : tempPasswordField()}
  </div>
  <div class="actions"><button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">${u ? 'Save' : 'Add user'}</button></div></form>`);
}

export function resetPasswordModal(id) {
  const u = ui.users.find(x => x.id === id);
  openModal(`<h2>Reset password for ${esc(u.displayName)}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="reset-password" data-id="${u.id}" autocomplete="off"><div class="form">${tempPasswordField()}</div>
  <p class="small muted">${esc(u.displayName)} will be signed out everywhere, and must choose a new password at the next sign-in.</p>
  <div class="actions"><button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Reset password</button></div></form>`);
}

/**
 * My account: display name and password.
 * With forced = true (after an admin reset), only the password form is shown and the dialog cannot be closed.
 */
export function accountModal(forced = false) {
  const me = ui.me;
  openModal(`<h2>${forced ? 'Choose a new password' : 'My account'}${forced ? '' : '<button class="btn link x" data-act="close">Close ✕</button>'}</h2>
  ${forced ? `<p>Your password was set by an admin. Please choose your own password to continue.</p>` : `
  <form data-form="profile"><div class="form">
    <label>User name<input value="${esc(me.userName)}" disabled></label>
    <label>Display name<input name="displayName" required value="${esc(me.displayName)}"></label>
  </div><div class="actions"><button class="btn">Save name</button></div></form>
  <h2 style="font-size:15px;margin-top:18px">Change password</h2>`}
  <form data-form="change-password" data-forced="${forced}">
    <input name="userName" autocomplete="username" value="${esc(me.userName)}" hidden>
    <div class="form">
      <label class="full">Current password<input name="currentPassword" type="password" autocomplete="current-password" required ${forced ? 'autofocus' : ''}></label>
      <label>New password<input name="newPassword" type="password" autocomplete="new-password" minlength="8" required placeholder="At least 8 characters"></label>
      <label>Repeat new password<input name="confirm" type="password" autocomplete="new-password" minlength="8" required></label>
    </div>
    <p class="small muted">After the change, you are signed out on other computers.</p>
    <div class="actions">${forced ? '<button type="button" class="btn l" data-act="logout">Sign out</button>' : ''}<button class="btn primary">Change password</button></div>
  </form>`);
}
