'use strict';

const vscode = require('vscode');
const { LanguageClient } = require('vscode-languageclient/node');

let client;
let output;
let watcher;
let lifecycle = Promise.resolve();
let shuttingDown = false;

function configuration() {
  return vscode.workspace.getConfiguration('dotori');
}

function executable() {
  const config = configuration();
  return { command: config.get('path', 'dotori'), args: config.get('arguments', []) };
}

function reportError(error) {
  const message = error instanceof Error ? error.message : String(error);
  output.appendLine(message);
  void vscode.window.showErrorMessage(`Dotori: ${message}`);
}

async function runTask(name, args, uri) {
  const folders = vscode.workspace.workspaceFolders || [];
  const resource = uri || vscode.window.activeTextEditor?.document.uri;
  let folder = resource && vscode.workspace.getWorkspaceFolder(resource);
  if (!folder) {
    folder = folders.length === 1 ? folders[0]
      : folders.length > 1 ? await vscode.window.showWorkspaceFolderPick({ placeHolder: 'Select a Dotori project' })
      : undefined;
  }
  if (!folder) {
    if (folders.length === 0) void vscode.window.showWarningMessage('Dotori: Open a project folder to run this command.');
    return;
  }
  if (!await vscode.workspace.saveAll(false)) return;
  const program = executable();
  const task = new vscode.Task(
    { type: 'process', command: program.command, args: [...program.args, ...args] }, folder, name, 'Dotori',
    new vscode.ProcessExecution(program.command, [...program.args, ...args], { cwd: folder.uri.fsPath }),
    ['$gcc', '$msCompile']
  );
  if (name === 'Build') task.group = vscode.TaskGroup.Build;
  if (name === 'Test') task.group = vscode.TaskGroup.Test;
  task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, clear: true };
  return vscode.tasks.executeTask(task);
}

function restartLanguageServer() {
  const restart = lifecycle.then(async () => {
    if (client) {
      const previous = client;
      client = undefined;
      await previous.dispose();
    }
    if (shuttingDown) return;
    const program = executable();
    const args = [...program.args, 'lsp', '--watch-ide'];
    const logFile = configuration().get('languageServer.logFile', '');
    if (logFile) args.push('--log-file', logFile);
    client = new LanguageClient(
      'dotoriLanguageServer', 'Dotori Language Server',
      { command: program.command, args },
      {
        documentSelector: [
          { scheme: 'file', language: 'dotori' },
          { scheme: 'untitled', language: 'dotori' }
        ],
        outputChannel: output,
        synchronize: { fileEvents: watcher }
      }
    );
    await client.start();
  });
  lifecycle = restart.catch(reportError);
  return lifecycle;
}

async function activate(context) {
  shuttingDown = false;
  output = vscode.window.createOutputChannel('Dotori');
  watcher = vscode.workspace.createFileSystemWatcher('**/{.dotori,*.dotori}');
  context.subscriptions.push(output, watcher);
  function command(id, handler) {
    context.subscriptions.push(vscode.commands.registerCommand(id, async (...args) => {
      try { return await handler(...args); }
      catch (error) { reportError(error); }
    }));
  }
  command('dotori.build', uri => runTask('Build', ['build'], uri));
  command('dotori.test', uri => runTask('Test', ['test'], uri));
  command('dotori.run', uri => runTask('Run', ['run'], uri));
  command('dotori.setupIde', uri => runTask('IDE Setup', ['setup', 'ide', '--ide', 'vscode'], uri));
  command('dotori.formatFile', async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'dotori') return;
    // LSP formatting preserves unsaved buffers and undo history.
    await lifecycle;
    await vscode.commands.executeCommand('editor.action.formatDocument');
  });
  command('dotori.restartLanguageServer', restartLanguageServer);
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(event => {
    if (['dotori.path', 'dotori.arguments', 'dotori.languageServer.logFile']
      .some(setting => event.affectsConfiguration(setting))) void restartLanguageServer();
  }));
  await restartLanguageServer();
}

async function deactivate() {
  shuttingDown = true;
  await lifecycle;
  if (client) {
    const previous = client;
    client = undefined;
    await previous.dispose();
  }
}

module.exports = { activate, deactivate };
