'use strict';

const vscode = require('vscode');
const { LanguageClient } = require('vscode-languageclient/node');

let client;

function configuration() {
  return vscode.workspace.getConfiguration('dotori');
}

function executablePath() {
  return configuration().get('path', 'dotori');
}

function quoteForTerminal(value) {
  if (!/[\s"']/.test(value)) return value;
  return `"${value.replace(/"/g, '\\"')}"`;
}

function runInTerminal(name, args) {
  const terminal = vscode.window.createTerminal({ name: `Dotori: ${name}` });
  terminal.show();
  terminal.sendText([executablePath(), ...args].map(quoteForTerminal).join(' '));
}

async function startLanguageServer(context) {
  const args = ['lsp'];
  const logFile = configuration().get('languageServer.logFile', '');
  if (logFile) args.push('--log-file', logFile);

  client = new LanguageClient(
    'dotoriLanguageServer',
    'Dotori Language Server',
    { command: executablePath(), args },
    {
      documentSelector: [
        { scheme: 'file', language: 'dotori' },
        { scheme: 'untitled', language: 'dotori' }
      ],
      synchronize: {
        fileEvents: vscode.workspace.createFileSystemWatcher('**/.dotori')
      }
    }
  );

  context.subscriptions.push(client);
  await client.start();
}

async function activate(context) {
  const registerTerminalCommand = (id, name, args) =>
    context.subscriptions.push(vscode.commands.registerCommand(id, () => runInTerminal(name, args)));

  registerTerminalCommand('dotori.build', 'Build', ['build']);
  registerTerminalCommand('dotori.test', 'Test', ['test']);
  registerTerminalCommand('dotori.run', 'Run', ['run']);
  registerTerminalCommand('dotori.setupIde', 'IDE Setup', ['setup', 'ide', '--ide', 'vscode']);

  context.subscriptions.push(vscode.commands.registerCommand('dotori.formatFile', async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'dotori' || editor.document.isUntitled) return;
    await editor.document.save();
    runInTerminal('Format', ['format', editor.document.uri.fsPath]);
  }));

  context.subscriptions.push(vscode.commands.registerCommand('dotori.restartLanguageServer', async () => {
    if (client) await client.restart();
  }));

  await startLanguageServer(context);
}

async function deactivate() {
  if (client) await client.stop();
}

module.exports = { activate, deactivate };
