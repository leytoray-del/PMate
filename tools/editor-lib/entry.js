// Everything the notes editor (src/PMate/wwwroot/js/editor.js) needs from Tiptap, in one file.
// Build with "npm install" then "npm run build". The output is committed, so building the app does not need Node.js.
export { Editor, Node, Extension, mergeAttributes, wrappingInputRule } from '@tiptap/core';
export { TextSelection } from '@tiptap/pm/state';
export { StarterKit } from '@tiptap/starter-kit';
export { CodeBlockLowlight } from '@tiptap/extension-code-block-lowlight';
export { TableKit } from '@tiptap/extension-table';
export { TaskList, TaskItem } from '@tiptap/extension-list';
export { Placeholder } from '@tiptap/extensions';
export { BubbleMenu } from '@tiptap/extension-bubble-menu';
export { Suggestion } from '@tiptap/suggestion';
export { createLowlight } from 'lowlight';

// Languages for syntax colors in code blocks. Keep this list in step with LANGUAGES in editor.js.
import bash from 'highlight.js/lib/languages/bash';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import diff from 'highlight.js/lib/languages/diff';
import go from 'highlight.js/lib/languages/go';
import java from 'highlight.js/lib/languages/java';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import markdown from 'highlight.js/lib/languages/markdown';
import powershell from 'highlight.js/lib/languages/powershell';
import python from 'highlight.js/lib/languages/python';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';
export const languages = {bash, csharp, css, diff, go, java, javascript, json, markdown, powershell, python, sql, typescript, xml, yaml};
