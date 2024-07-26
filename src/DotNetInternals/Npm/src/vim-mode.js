import { initVimMode } from 'monaco-vim';

export function enableVimMode(editorId, statusBarId) {
  const editor = window.blazorMonaco.editors.find((e) => e.id === editorId).editor;
    const statusBar = document.getElementById(statusBarId);

    return initVimMode(editor, statusBar);
}
