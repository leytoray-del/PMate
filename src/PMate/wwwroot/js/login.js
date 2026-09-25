// Login page: sign in, or create the first admin account on first start.
import { api } from './api.js';

const $ = s => document.querySelector(s);
const loginForm = $('#login-form');
const setupForm = $('#setup-form');

function showError(form, msg) {
  const el = form.querySelector('.lerror');
  el.textContent = msg;
  el.hidden = !msg;
}

async function submit(form, send) {
  const btn = form.querySelector('.lsubmit');
  btn.disabled = true;
  showError(form, '');
  try {
    await send(new FormData(form));
    // A real page change after success tells the browser the sign-in worked, so it offers to save the password.
    location.replace('/');
  } catch (err) {
    showError(form, err.message);
    btn.disabled = false;
  }
}

loginForm.addEventListener('submit', e => {
  e.preventDefault();
  submit(loginForm, d => api.post('/api/auth/login', {userName: d.get('userName'), password: d.get('password'), rememberMe: d.get('rememberMe') === 'on'}));
});

setupForm.addEventListener('submit', e => {
  e.preventDefault();
  submit(setupForm, d => {
    if (d.get('password') !== d.get('confirm')) throw new Error('The two passwords do not match.');
    return api.post('/api/auth/setup', {userName: d.get('userName'), displayName: d.get('displayName'), password: d.get('password')});
  });
});

document.addEventListener('click', e => {
  const b = e.target.closest('[data-show-password]');
  if (!b) return;
  const input = b.parentElement.querySelector('input');
  input.type = input.type === 'password' ? 'text' : 'password';
  b.textContent = input.type === 'password' ? 'Show' : 'Hide';
  input.focus();
});

try {
  const s = await api.get('/api/auth/status');
  if (s.signedIn) location.replace('/');
  const form = s.hasUsers ? loginForm : setupForm;
  form.hidden = false;
  form.querySelector('input')?.focus();
} catch (err) {
  loginForm.hidden = false;
  showError(loginForm, err.message);
}
