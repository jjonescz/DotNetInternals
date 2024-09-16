import { MonacoLanguageClient } from 'monaco-languageclient';
import { CloseAction, ErrorAction, MessageTransports } from 'vscode-languageclient';

export function enableRoslynLsp(editorId) {
    /** @type {import('monaco-editor')} */
    const editor = window.blazorMonaco.editors.find((e) => e.id === editorId).editor;

    const languageClient = new MonacoLanguageClient({
        name: 'Roslyn Language Client',
        clientOptions: {
            documentSelector: ['csharp'],
        },
        connectionProvider: {
            get: async () => {
                
            },
        },
    })
}
