// RAID log (Risks, Assumptions, Issues, Dependencies): page, table and edit dialog.
import { esc, today, addDays, dayDiff, fmtMD, RAIDTYPE, RAIDTYPES, RAIDCOLOR, RAIDSTATUS, RAIDRESPONSE, LEVEL } from './util.js';
import { db, proj, raid } from './store.js';
import { ui } from './state.js';
import { openModal, projOptions, opts, ownerList, raidRef, raidRating, raidRatingChip } from './components.js';

const RATING_ORDER = {high: 0, medium: 1, low: 2};
const isActive = r => r.status !== 'closed';

export function sortRaid(list) {
  return list.slice().sort((a, b) =>
    isActive(b) - isActive(a) || RATING_ORDER[raidRating(a)] - RATING_ORDER[raidRating(b)] ||
    (a.dueDate || '9999').localeCompare(b.dueDate || '9999') || a.number - b.number);
}

function matchesStatus(r, status) {
  return status === 'all' || (status === 'closed' ? !isActive(r) : isActive(r));
}

export function raidView() {
  const f = ui.raid;
  const base = db.raidItems.filter(r => (!f.project || r.projectId === f.project) && matchesStatus(r, f.status));
  const list = sortRaid(base.filter(r => !f.type || r.type === f.type));
  const count = type => base.filter(r => r.type === type).length;
  return `<div class="head"><h1>RAID Log</h1><button class="btn primary" data-act="new-raid">+ New item</button></div>
  <div class="bar">
    <div class="tabs">
      <button class="${!f.type ? 'on' : ''}" data-act="raid-filter" data-k="type" data-v="">All<span class="muted">${base.length}</span></button>
      ${Object.entries(RAIDTYPES).map(([k, v]) => `<button class="${f.type === k ? 'on' : ''}" data-act="raid-filter" data-k="type" data-v="${k}">${v}<span class="muted">${count(k)}</span></button>`).join('')}
    </div>
    <span style="flex:1"></span>
    <select data-raid-filter="project"><option value="">All projects</option>${projOptions(f.project, {none: '', closed: true})}</select>
    <select data-raid-filter="status">${opts({active: 'Open and monitoring', closed: 'Closed', all: 'All statuses'}, f.status)}</select>
  </div>
  <div class="card">${raidTable(list, !f.project, 'No RAID items match these filters.')}</div>
  <p class="small muted">Rating: for risks, impact × likelihood. For the other types, impact only. Refs like R-2 count per project and type.</p>`;
}

export function raidTable(list, showProject = true, emptyText = 'No items') {
  if (!list.length) return `<div class="empty">${emptyText}</div>`;
  const td = today();
  return `<table><thead><tr><th>Ref</th><th>Title</th>${showProject ? '<th>Project</th>' : ''}<th>Owner</th><th>Rating</th><th>Status</th><th>Due</th></tr></thead><tbody>
    ${list.map(r => {
      const p = proj(r.projectId), late = isActive(r) && r.dueDate && r.dueDate < td;
      return `<tr class="click" data-act="edit-raid" data-id="${r.id}">
        <td><span class="chip cc c-${RAIDCOLOR[r.type]}">${raidRef(r)}</span></td>
        <td>${esc(r.title)}${r.response ? `<div class="small muted">${esc(RAIDRESPONSE[r.type])}: ${esc(r.response.slice(0, 120))}</div>` : ''}</td>
        ${showProject ? `<td>${esc(p ? p.code || p.name : '-')}</td>` : ''}
        <td>${esc(r.owner || '-')}</td>
        <td>${raidRatingChip(r)}</td>
        <td><span class="chip ${r.status === 'closed' ? 'ok' : r.status === 'monitoring' ? '' : 'warn'}">${RAIDSTATUS[r.status]}</span></td>
        <td class="due ${late ? 'danger' : ''}">${r.dueDate ? (late ? `Overdue ${dayDiff(td, r.dueDate)}d` : fmtMD(r.dueDate)) : ''}</td></tr>`;
    }).join('')}
  </tbody></table>`;
}

/** Open items that need attention: high rating, or due within 7 days. */
export function raidAttention() {
  const soon = addDays(today(), 7);
  return sortRaid(db.raidItems.filter(r => isActive(r) && (raidRating(r) === 'high' || (r.dueDate && r.dueDate <= soon))));
}

export function raidModal(id, preset = {}) {
  const firstProject = db.projects.find(p => p.status !== 'closed') || db.projects[0];
  const r = id ? raid(id) : {
    id: '', projectId: preset.projectId || ui.raid.project || (ui.view === 'project' ? ui.projectId : '') || firstProject?.id || '',
    type: preset.type || ui.raid.type || 'risk', title: preset.title || '', description: preset.description || '', owner: '',
    impact: 'medium', likelihood: 'medium', status: 'open', response: '', dueDate: '',
  };
  if (!db.projects.length) throw new Error('Create a project first. Every RAID item belongs to a project.');
  openModal(`<h2>${id ? `Edit ${esc(RAIDTYPE[r.type].toLowerCase())} ${raidRef(r)}` : 'New RAID item'}<button class="btn link x" data-act="close">Close ✕</button></h2>
  <form data-form="raid" data-id="${r.id}"><div class="form">
    <label>Type<select name="type" data-raid-type>${opts(RAIDTYPE, r.type)}</select></label>
    <label>Project<select name="projectId" required>${projOptions(r.projectId, {none: ''})}</select></label>
    <label class="full">Title<input name="title" required value="${esc(r.title)}" ${id ? '' : 'autofocus'} placeholder="Short description"></label>
    <label class="full">Details<textarea name="description" rows="2">${esc(r.description)}</textarea></label>
    <label>Owner<input name="owner" list="owners" value="${esc(r.owner)}"></label>
    <label>Due / review date<input type="date" name="dueDate" value="${esc(r.dueDate)}"></label>
    <label>Impact<select name="impact">${opts(LEVEL, r.impact)}</select></label>
    <label data-likelihood ${r.type === 'risk' ? '' : 'hidden'}>Likelihood<select name="likelihood">${opts(LEVEL, r.likelihood || 'medium')}</select></label>
    <label>Status<select name="status">${opts(RAIDSTATUS, r.status)}</select></label>
    <label class="full"><span data-response-label>${RAIDRESPONSE[r.type]}</span><textarea name="response" rows="3">${esc(r.response)}</textarea></label>
  </div>${ownerList()}
  <div class="actions">${id ? `<button type="button" class="btn danger l" data-act="del-raid" data-id="${r.id}">Delete</button>` : ''}
    <button type="button" class="btn" data-act="close">Cancel</button><button class="btn primary">Save</button></div></form>`);
}

/** Shows or hides the Likelihood field and renames the Response field when the type changes. */
export function raidTypeChanged(select) {
  const f = select.form;
  f.querySelector('[data-likelihood]').hidden = select.value !== 'risk';
  f.querySelector('[data-response-label]').textContent = RAIDRESPONSE[select.value];
}
