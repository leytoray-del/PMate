// Demo data in the backup format. Loaded with "merge", so real data is never replaced.
import { today, addDays } from './util.js';

export function buildSample(me) {
  const td = today(), now = Date.now();
  let seq = 0;
  const id = () => crypto.randomUUID().replaceAll('-', '');
  const iso = ms => new Date(ms).toISOString();
  const names = {todo: 'To Do', doing: 'In Progress', waiting: 'Waiting', done: 'Done'};
  const data = {format: 'pmate', version: 4, exportedAt: iso(now), settings: null, statuses: [], projects: [], milestones: [], tasks: [], updates: [], notes: [], raidItems: []};

  const P = (name, code, client, s, e, description) => {
    const p = {id: id(), name, code, client, status: 'active', start: addDays(td, s), end: addDays(td, e), description, isSample: true, createdAt: iso(now), updatedAt: iso(now)};
    data.projects.push(p); return p;
  };
  const M = (p, name, plan, status, actual) => {
    const m = {id: id(), projectId: p.id, name, planDate: addDays(td, plan), actualDate: actual != null ? addDays(td, actual) : null, status, createdAt: iso(now), updatedAt: iso(now)};
    data.milestones.push(m); return m;
  };
  const T = (p, m, title, owner, due, status, priority, age) => {
    const t = {id: id(), projectId: p.id, milestoneId: m?.id ?? null, title, owner, due: due == null ? null : addDays(td, due), status, priority,
      isSample: true, createdAt: iso(now - age * 864e5), updatedAt: iso(now), doneAt: status === 'done' ? iso(now - 20 * 864e5) : null};
    data.tasks.push(t); return t;
  };
  const L = (t, p, day, type, text, hours, extra = {}) => data.updates.push({id: id(), taskId: t?.id ?? null, projectId: p.id, date: addDays(td, day),
    type, text, hours, isSample: true, createdAt: iso(now + day * 864e5 + seq++), ...extra});
  const S = (t, p, day, from, to) => L(t, p, day, 'status', '', 0, {fromStatus: from, toStatus: to, fromName: names[from], toName: names[to]});

  const p1 = P('Sample: CRM Upgrade', 'PRJ-CRM', 'Sales Dept', -40, 50, 'Upgrade the old CRM to the new version. Scope: customers, quotations, reports.');
  const p2 = P('Sample: Office Move', 'PRJ-OFF', 'Admin', -15, 45, 'Move the Singapore office to the new address, including network and meeting room equipment.');
  const m1 = M(p1, 'Requirements sign-off', -25, 'done', -22), m2 = M(p1, 'Build complete', 8, 'doing'), m3 = M(p1, 'UAT complete', 30, 'planned');
  M(p1, 'Go-live', 48, 'planned');
  const m5 = M(p2, 'New site confirmed', -3, 'doing');
  M(p2, 'Fit-out complete', 30, 'planned');

  const t1 = T(p1, m1, 'Send requirements document for business sign-off', me, -20, 'done', 'high', 30);
  const t2 = T(p1, m2, 'Follow up quotation module progress', 'Alice', 2, 'doing', 'high', 9);
  const t3 = T(p1, m2, 'Confirm data migration approach', 'Ben', -2, 'waiting', 'high', 7);
  T(p1, m3, 'Prepare UAT test cases', 'Chen Wei', 12, 'todo', 'medium', 3);
  const t5 = T(p1, null, 'Update weekly status report', me, 0, 'todo', 'medium', 2);
  const t6 = T(p2, m5, 'Confirm lease terms with landlord', me, 1, 'doing', 'high', 10);
  const t7 = T(p2, null, 'Collect seating needs from each team', 'Priya', 5, 'todo', 'low', 4);
  T(p2, null, 'Compare IT cabling quotes', 'Ben', 9, 'todo', 'medium', 1);

  S(t1, p1, -24, 'todo', 'doing'); L(t1, p1, -24, 'work', 'Drafted requirements document.', 3);
  S(t1, p1, -21, 'doing', 'done'); L(t1, p1, -21, 'decision', 'Business agreed: reports module moves to phase 2.', 0);
  S(t2, p1, -8, 'todo', 'doing');
  L(t2, p1, -8, 'work', 'Kick-off with Alice. Agreed on build plan for the quotation module.', 1);
  L(t2, p1, -4, 'work', 'Checked progress: about 40% done.', 0.5);
  L(t2, p1, -4, 'risk', 'Pricing rules are more complex than planned. May need 3 more days.', 0);
  L(t2, p1, -1, 'work', 'Meeting with Alice on quotation module. About 60% done.', 1);
  L(t2, p1, 0, 'work', 'Reviewed test results for the quotation module. Found 3 issues.', 1.5);
  L(t2, p1, 0, 'commit', 'Send the issue list to Alice', 0, {who: me, commitDue: addDays(td, 2)});
  S(t3, p1, -5, 'todo', 'doing'); S(t3, p1, -3, 'doing', 'waiting');
  L(t3, p1, -3, 'work', 'Discussed options with Ben and the vendor.', 1);
  L(t3, p1, -1, 'commit', 'Provide first draft of the migration approach', 0, {who: 'Ben', commitDue: addDays(td, 1)});
  L(t3, p1, -1, 'risk', 'Old system data quality is poor. Migration may be delayed.', 0.5);
  S(t6, p2, -6, 'todo', 'doing');
  L(t6, p2, -1, 'work', 'Reviewed the lease. Listed 5 questions.', 2);
  L(t6, p2, -1, 'commit', 'Reply with comments on lease changes by Friday', 0, {who: 'Landlord', commitDue: addDays(td, 3)});
  L(t6, p2, 0, 'work', 'Call with landlord about lease questions.', 1);
  L(null, p1, -1, 'work', 'Weekly project meeting', 1.5);
  L(t7, p2, -1, 'work', 'Emailed all teams to collect seating needs.', 0.5);
  L(t5, p1, 0, 'work', "Collected this week's progress. Started the report.", 1);

  const N = (p, day, title, body, tags, pinned = false) => data.notes.push({id: id(), projectId: p?.id ?? null, title, body, tags,
    noteDate: addDays(td, day), pinned, isSample: true, createdAt: iso(now + day * 864e5), updatedAt: iso(now + day * 864e5)});
  N(p1, -1, 'Weekly project meeting', [
    'Attendees: Alice, Ben, Chen Wei, vendor PM',
    '',
    'Progress',
    '- Quotation module about 60% done',
    '- Data migration approach still open',
    '',
    'Actions',
    '- Ben to send the first draft of the migration approach by Thursday',
    '- Chen Wei to start the UAT test plan',
    '- Ask finance to confirm the tax rules for quotations',
  ].join('\n'), 'meeting, weekly', true);
  N(p2, -3, 'Site visit with landlord', 'Checked the new office on level 12.\n- Network room is smaller than expected\n- Two meeting rooms need extra power points\n- Landlord will reply on the lease changes', 'site visit');
  N(null, -6, 'Ideas for next quarter', 'Automate the weekly report.\nReview vendor contracts before renewal.', 'ideas');

  const nums = {};
  const R = (p, type, title, owner, impact, likelihood, status, response, due) => {
    const key = p.id + type;
    nums[key] = (nums[key] || 0) + 1;
    data.raidItems.push({id: id(), projectId: p.id, type, number: nums[key], title, owner, impact, likelihood: type === 'risk' ? likelihood : null, status,
      response, dueDate: due == null ? null : addDays(td, due), isSample: true, createdAt: iso(now), updatedAt: iso(now)});
  };
  R(p1, 'risk', 'Old system data quality may delay the migration', 'Ben', 'high', 'high', 'open', 'Run a data quality check on 1,000 sample records first.', 5);
  R(p1, 'risk', 'Pricing rules more complex than planned', 'Alice', 'medium', 'medium', 'monitoring', 'Weekly check with the vendor.', 7);
  R(p1, 'assumption', 'Sales team is available for UAT in October', 'Me', 'high', null, 'open', 'Confirm with the sales manager.', 3);
  R(p1, 'issue', 'Test environment is down twice a week', 'Chen Wei', 'medium', null, 'open', 'IT is replacing the server.', -1);
  R(p1, 'dependency', 'Finance must confirm tax rules for quotations', 'Me', 'medium', null, 'open', 'Follow up in the weekly finance call.', 6);
  R(p2, 'risk', 'Fit-out contractor not available in time', 'Priya', 'high', 'medium', 'open', 'Get quotes from two contractors.', 10);
  return data;
}
