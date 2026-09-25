// Block editor for notes, in the style of Confluence pages. Built on Tiptap (js/vendor/tiptap.js, see tools/editor-lib).
// Type "/" to add a block. Select text to get the formatting bar.
// Markdown shortcuts also work while typing: "## " heading, "- " list, "1. " numbered list, "[] " action item, "```" code block.
import { Editor, Node, Extension, mergeAttributes, StarterKit, CodeBlockLowlight, TableKit, TaskList, TaskItem, Placeholder,
  BubbleMenu, Suggestion, createLowlight, languages } from './vendor/tiptap.js';
import { esc } from './util.js';

const lowlight = createLowlight(languages);

/** Languages offered in a code block. Keys are highlight.js names. '' lets the editor guess the colors. */
const LANGUAGES = {'': 'Auto detect', bash: 'Bash / Shell', csharp: 'C#', css: 'CSS', diff: 'Diff', go: 'Go', java: 'Java',
  javascript: 'JavaScript', json: 'JSON', markdown: 'Markdown', powershell: 'PowerShell', python: 'Python', sql: 'SQL',
  typescript: 'TypeScript', xml: 'HTML / XML', yaml: 'YAML'};

const PANELS = {info: 'Info', note: 'Note', success: 'Success', warning: 'Warning', error: 'Error'};

const el = (tag, cls, text) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text) e.textContent = text;
  return e;
};
const button = (cls, text, title) => {
  const b = el('button', cls, text);
  b.type = 'button'; // the editor sits in a <form>; a plain button would submit it
  if (title) b.title = title;
  return b;
};

/* ---------- code block: language list and Copy button above the code ---------- */
const CodeBlock = CodeBlockLowlight.extend({
  addNodeView() {
    return ({node, editor, getPos}) => {
      let current = node;
      const dom = el('div', 'code-block');
      const head = el('div', 'code-head');
      head.contentEditable = 'false';
      const select = el('select');
      select.title = 'Language';
      const copy = button('btn sm', 'Copy', 'Copy the code');
      head.append(select, copy);
      const pre = el('pre'), code = el('code');
      pre.spellcheck = false;
      pre.append(code);
      dom.append(head, pre);

      const fillLanguages = () => {
        const lang = current.attrs.language || '';
        const known = lang in LANGUAGES ? LANGUAGES : {...LANGUAGES, [lang]: lang}; // for example "js" typed after ```
        select.innerHTML = Object.entries(known).map(([k, v]) => `<option value="${esc(k)}">${esc(v)}</option>`).join('');
        select.value = lang;
      };
      fillLanguages();

      select.addEventListener('change', () => {
        const pos = getPos();
        if (typeof pos === 'number') editor.view.dispatch(editor.state.tr.setNodeAttribute(pos, 'language', select.value || null));
      });
      copy.addEventListener('click', async () => {
        try {
          await navigator.clipboard.writeText(current.textContent);
          copy.textContent = 'Copied';
        } catch { copy.textContent = 'Copy failed'; }
        setTimeout(() => { copy.textContent = 'Copy'; }, 1500);
      });

      return {
        dom, contentDOM: code,
        update(n) {
          if (n.type !== current.type) return false;
          const langChanged = n.attrs.language !== current.attrs.language;
          current = n;
          if (langChanged) fillLanguages();
          return true;
        },
        stopEvent: e => head.contains(e.target),
        ignoreMutation: m => head.contains(m.target),
      };
    };
  },
});

/* ---------- panel: a colored box (info, note, success, warning, error) that holds other blocks ---------- */
const Panel = Node.create({
  name: 'panel',
  group: 'block',
  content: 'block+',
  defining: true,
  addAttributes() {
    return {kind: {default: 'info', parseHTML: e => e.getAttribute('data-panel') || 'info', renderHTML: a => ({'data-panel': a.kind})}};
  },
  parseHTML() { return [{tag: 'div[data-panel]'}]; },
  renderHTML({HTMLAttributes}) { return ['div', mergeAttributes(HTMLAttributes, {class: 'panel'}), 0]; },
  addCommands() {
    return {setPanel: kind => ({commands}) => commands.wrapIn(this.name, {kind})};
  },
  addNodeView() {
    return ({node, editor, getPos}) => {
      let current = node;
      const dom = el('div', 'panel');
      const icon = el('span', 'panel-icon');
      const tools = el('div', 'panel-tools');
      icon.contentEditable = tools.contentEditable = 'false';
      for (const [k, label] of Object.entries(PANELS)) {
        const b = button(`panel-dot`, '', `${label} panel`);
        b.dataset.kind = k;
        tools.append(b);
      }
      const remove = button('panel-remove', '×', 'Remove the panel (keep the text)');
      tools.append(remove);
      const content = el('div', 'panel-body');
      dom.append(icon, tools, content);
      const show = () => { dom.dataset.panel = current.attrs.kind; };
      show();

      tools.addEventListener('mousedown', e => e.preventDefault()); // keep the cursor in the text
      tools.addEventListener('click', e => {
        const pos = getPos();
        if (typeof pos !== 'number') return;
        const b = e.target.closest('button');
        if (b?.dataset.kind) editor.view.dispatch(editor.state.tr.setNodeAttribute(pos, 'kind', b.dataset.kind));
        else if (b === remove) editor.view.dispatch(editor.state.tr.replaceWith(pos, pos + current.nodeSize, current.content));
      });

      return {
        dom, contentDOM: content,
        update(n) {
          if (n.type !== current.type) return false;
          current = n;
          show();
          return true;
        },
        stopEvent: e => tools.contains(e.target),
        ignoreMutation: m => tools.contains(m.target) || m.target === dom && m.type === 'attributes',
      };
    };
  },
});

/* ---------- "/" menu ---------- */
const BLOCKS = [
  {icon: 'T', title: 'Text', hint: 'Plain paragraph', run: c => c.setParagraph()},
  {icon: 'H1', title: 'Heading 1', hint: 'Big section heading', keys: 'h1 title', run: c => c.setHeading({level: 1})},
  {icon: 'H2', title: 'Heading 2', hint: 'Medium section heading', keys: 'h2', run: c => c.setHeading({level: 2})},
  {icon: 'H3', title: 'Heading 3', hint: 'Small section heading', keys: 'h3', run: c => c.setHeading({level: 3})},
  {icon: '•', title: 'Bullet list', hint: 'Simple list', keys: 'ul unordered', run: c => c.toggleBulletList()},
  {icon: '1.', title: 'Numbered list', hint: 'List with numbers', keys: 'ol ordered', run: c => c.toggleOrderedList()},
  {icon: '☑', title: 'Action items', hint: 'Checklist of things to do', keys: 'task todo checkbox', run: c => c.toggleTaskList()},
  {icon: '</>', title: 'Code block', hint: 'Code with colors and a Copy button', keys: 'snippet pre', run: c => c.setCodeBlock()},
  {icon: '❝', title: 'Quote', hint: 'Quoted text', keys: 'blockquote', run: c => c.setBlockquote()},
  {icon: '—', title: 'Divider', hint: 'Horizontal line', keys: 'hr line rule', run: c => c.setHorizontalRule()},
  {icon: '▦', title: 'Table', hint: '3 × 3 table with a header row', keys: 'grid', run: c => c.insertTable({rows: 3, cols: 3, withHeaderRow: true})},
  ...Object.entries(PANELS).map(([k, label]) => ({icon: '', panel: k, title: `${label} panel`, hint: 'Colored box to make text stand out',
    keys: 'panel callout', run: c => c.setPanel(k)})),
];

function findBlocks(query) {
  const q = query.toLowerCase();
  return BLOCKS.filter(b => `${b.title} ${b.keys || ''}`.toLowerCase().includes(q));
}

function slashRenderer() {
  let box, unmount, items = [], sel = 0, pick;
  const draw = () => {
    box.innerHTML = items.length
      ? items.map((b, i) => `<div class="slash-item${i === sel ? ' on' : ''}" data-i="${i}">
          <span class="slash-icon"${b.panel ? ` data-panel="${b.panel}"` : ''}>${esc(b.icon)}</span>
          <span><span class="slash-title">${esc(b.title)}</span><span class="slash-hint">${esc(b.hint)}</span></span></div>`).join('')
      : '<div class="slash-empty">No matching block</div>';
    box.querySelector('.on')?.scrollIntoView({block: 'nearest'});
  };
  const set = p => { items = p.items; pick = p.command; sel = 0; draw(); };
  return {
    onStart(p) {
      box = el('div', 'slash-menu');
      box.addEventListener('mousedown', e => {
        e.preventDefault();
        const row = e.target.closest('[data-i]');
        if (row) pick(items[+row.dataset.i]);
      });
      set(p);
      unmount = p.mount(box);
    },
    onUpdate: set,
    onKeyDown({event}) {
      if (!items.length) return false;
      if (event.key === 'ArrowDown') { sel = (sel + 1) % items.length; draw(); return true; }
      if (event.key === 'ArrowUp') { sel = (sel + items.length - 1) % items.length; draw(); return true; }
      if (event.key === 'Enter' || event.key === 'Tab') { pick(items[sel]); return true; }
      return false;
    },
    onExit() { unmount?.(); unmount = box = null; },
  };
}

const SlashMenu = Extension.create({
  name: 'slashMenu',
  addProseMirrorPlugins() {
    return [Suggestion({
      editor: this.editor,
      char: '/',
      allow: ({state}) => !state.selection.$from.parent.type.spec.code, // "/" in code is just a slash
      items: ({query}) => findBlocks(query),
      command: ({editor, range, props}) => props.run(editor.chain().focus().deleteRange(range)).run(),
      render: slashRenderer,
    })];
  },
});

/* ---------- floating bars: text formatting, and table tools ---------- */
function formatBar() {
  const bar = el('div', 'float-bar');
  bar.innerHTML = `
    <button type="button" data-cmd="bold" title="Bold (Ctrl+B)"><b>B</b></button>
    <button type="button" data-cmd="italic" title="Italic (Ctrl+I)"><i>I</i></button>
    <button type="button" data-cmd="underline" title="Underline (Ctrl+U)"><u>U</u></button>
    <button type="button" data-cmd="strike" title="Strikethrough"><s>S</s></button>
    <button type="button" data-cmd="code" title="Inline code">&lt;/&gt;</button>
    <button type="button" data-cmd="link" title="Link">Link</button>`;
  return bar;
}

function tableBar() {
  const bar = el('div', 'float-bar');
  bar.innerHTML = `
    <button type="button" data-cmd="addRowAfter" title="Add a row below">+ Row</button>
    <button type="button" data-cmd="addColumnAfter" title="Add a column to the right">+ Column</button>
    <button type="button" data-cmd="deleteRow" title="Delete this row">− Row</button>
    <button type="button" data-cmd="deleteColumn" title="Delete this column">− Column</button>
    <button type="button" data-cmd="toggleHeaderRow" title="Turn the header row on or off">Header</button>
    <button type="button" data-cmd="deleteTable" title="Delete the table" class="danger">Delete table</button>`;
  return bar;
}

function runBarCommand(editor, cmd) {
  const c = editor.chain().focus();
  if (cmd === 'link') {
    const old = editor.getAttributes('link').href || '';
    const url = prompt('Link address (leave empty to remove the link):', old || 'https://');
    if (url === null) return;
    if (!url.trim() || url.trim() === 'https://') c.extendMarkRange('link').unsetLink().run();
    else c.extendMarkRange('link').setLink({href: url.trim()}).run();
    return;
  }
  const toggles = {bold: 'toggleBold', italic: 'toggleItalic', underline: 'toggleUnderline', strike: 'toggleStrike', code: 'toggleCode'};
  c[toggles[cmd] || cmd]().run();
}

function wireBar(editor, bar) {
  bar.addEventListener('mousedown', e => e.preventDefault()); // keep the text selected
  bar.addEventListener('click', e => {
    const b = e.target.closest('[data-cmd]');
    if (b) runBarCommand(editor, b.dataset.cmd);
  });
}

function markActive(editor, bar) {
  bar.querySelectorAll('[data-cmd]').forEach(b => b.classList.toggle('on', editor.isActive(b.dataset.cmd)));
}

/* ---------- public ---------- */

/** Turns a note written before the block editor (plain text) into a page: one paragraph per line. */
export function textToDoc(text) {
  const lines = (text || '').split('\n');
  return {type: 'doc', content: lines.map(l => l ? {type: 'paragraph', content: [{type: 'text', text: l}]} : {type: 'paragraph'})};
}

/**
 * Creates the editor inside `element`.
 * content: the saved page JSON (string), or '' to start from `body` (plain text of an older note).
 * onChange(editor) is called after every change.
 */
export function createNoteEditor({element, content, body, onChange}) {
  let doc;
  try { doc = content ? JSON.parse(content) : textToDoc(body); } catch { doc = textToDoc(body); }

  const fmt = formatBar(), tbl = tableBar();
  let editor = null;
  editor = new Editor({
    element,
    content: doc,
    extensions: [
      StarterKit.configure({
        codeBlock: false,
        heading: {levels: [1, 2, 3]},
        link: {openOnClick: false, autolink: true, defaultProtocol: 'https'},
      }),
      CodeBlock.configure({lowlight, defaultLanguage: null, enableTabIndentation: true, tabSize: 2}),
      TableKit.configure({table: {resizable: false}}),
      TaskList,
      TaskItem.configure({nested: true}),
      Panel,
      SlashMenu,
      Placeholder.configure({
        placeholder: ({node}) => node.type.name === 'heading' ? 'Heading' : 'Type / to add a block, or just start writing',
        includeChildren: false,
      }),
      BubbleMenu.extend({name: 'formatBar'}).configure({
        pluginKey: 'formatBar',
        element: fmt,
        shouldShow: ({editor: e, state, from, to}) =>
          from !== to && e.isEditable && !e.isActive('codeBlock') && !state.selection.node && !state.selection.$anchorCell,
      }),
      BubbleMenu.extend({name: 'tableBar'}).configure({
        pluginKey: 'tableBar',
        element: tbl,
        options: {placement: 'top-start'},
        getReferencedVirtualElement: () => {
          if (!editor) return null;
          const {view, state} = editor;
          const tableEl = view.domAtPos(state.selection.from).node;
          const table = (tableEl.nodeType === 1 ? tableEl : tableEl.parentElement)?.closest('table');
          return table ? {getBoundingClientRect: () => table.getBoundingClientRect()} : null;
        },
        shouldShow: ({editor: e, from, to}) => e.isEditable && e.isActive('table') && (from === to || !!e.state.selection.$anchorCell),
      }),
    ],
    editorProps: {
      attributes: {class: 'doc', spellcheck: 'true'},
      // Ctrl+click (Cmd+click on a Mac) opens a link in a new tab, like Confluence.
      handleClick(view, pos, event) {
        const a = event.target.closest?.('a[href]');
        if (!a || !(event.ctrlKey || event.metaKey)) return false;
        window.open(a.href, '_blank', 'noopener');
        return true;
      },
    },
    onUpdate: ({editor: e}) => onChange?.(e),
    onSelectionUpdate: ({editor: e}) => markActive(e, fmt),
  });
  wireBar(editor, fmt);
  wireBar(editor, tbl);
  return editor;
}

/** The selected text, or the text of the block under the cursor. */
export function selectedText(editor) {
  const {state} = editor, {from, to, $from} = state.selection;
  return from !== to ? state.doc.textBetween(from, to, '\n', ' ') : $from.parent.textContent;
}
