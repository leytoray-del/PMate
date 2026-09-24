// Small helpers and constants shared by all modules.

export const $ = (s, el = document) => el.querySelector(s);
export const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));
export const sleep = ms => new Promise(r => setTimeout(r, ms));

const pad = n => String(n).padStart(2, '0');
export const ymd = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
export const today = () => ymd(new Date());
export const parse = s => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
export const addDays = (s, n) => { const d = parse(s); d.setDate(d.getDate() + n); return ymd(d); };
export const weekStart = s => { const d = parse(s); d.setDate(d.getDate() - (d.getDay() + 6) % 7); return ymd(d); };
export const dayDiff = (a, b) => Math.round((parse(a) - parse(b)) / 864e5);

export const WD = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
export const fmtMD = s => s ? `${+s.slice(8, 10)} ${MON[+s.slice(5, 7) - 1]}` : '';
export const fmtDate = s => s ? `${WD[parse(s).getDay()]} ${fmtMD(s)}` : '';
export const fmtFull = s => s ? `${fmtDate(s)} ${s.slice(0, 4)}` : '';
export const relDay = s => {
  const d = dayDiff(today(), s);
  if (d === 0) return 'Today';
  if (d === 1) return 'Yesterday';
  if (d === -1) return 'Tomorrow';
  return fmtDate(s) + (s.slice(0, 4) !== today().slice(0, 4) ? ' ' + s.slice(0, 4) : '');
};
export const round2 = n => Math.round(n * 100) / 100;
export const fmtH = n => `${round2(n)}h`;
export const plural = (n, w) => `${n} ${w}${n === 1 ? '' : 's'}`;
export const by = (f, dir = 1) => (a, b) => { const x = f(a), y = f(b); return x < y ? -dir : x > y ? dir : 0; };

export function download(name, text, type) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], {type}));
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

export const COLORS = ['gray', 'blue', 'purple', 'amber', 'red', 'green', 'teal', 'pink'];
export const PRIO = {high: 'High', medium: 'Medium', low: 'Low'};
export const LOGTYPE = {work: 'Work log', note: 'Note', commit: 'Commitment', decision: 'Decision', risk: 'Risk / Issue'};
export const LOGCOLOR = {work: 'blue', note: 'gray', commit: 'amber', decision: 'green', risk: 'red', status: 'gray'};
export const PSTATUS = {active: 'Active', hold: 'On hold', closed: 'Closed'};
export const MSTATUS = {planned: 'Not started', doing: 'In progress', done: 'Done'};
export const RAIDTYPE = {risk: 'Risk', assumption: 'Assumption', issue: 'Issue', dependency: 'Dependency'};
export const RAIDTYPES = {risk: 'Risks', assumption: 'Assumptions', issue: 'Issues', dependency: 'Dependencies'};
export const RAIDCOLOR = {risk: 'red', assumption: 'purple', issue: 'amber', dependency: 'teal'};
export const RAIDSTATUS = {open: 'Open', monitoring: 'Monitoring', closed: 'Closed'};
export const LEVEL = {high: 'High', medium: 'Medium', low: 'Low'};
// What the "Response" field means for each RAID type.
export const RAIDRESPONSE = {risk: 'Mitigation plan', assumption: 'How to confirm', issue: 'Resolution', dependency: 'Follow-up'};
