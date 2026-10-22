'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const plain = value => JSON.parse(JSON.stringify(value));
const tick = () => new Promise(resolve => setImmediate(resolve));

function harness() {
  const state = {
    config: {}, commands: new Map(), clients: [], tasks: [], errors: [], warnings: [],
    executed: [], watchers: [], saves: [], logs: [], saveResult: true,
    folders: [{ name: 'A', uri: { fsPath: '/project A', folder: 'A' } }]
  };
  const disposable = () => ({ dispose() {} });
  const vscode = {
    workspace: {
      get workspaceFolders() { return state.folders; },
      getConfiguration: () => ({ get: (name, fallback) => state.config[name] ?? fallback }),
      getWorkspaceFolder: uri => state.folders.find(folder => uri.folder === folder.name),
      saveAll: async untitled => { state.saves.push(untitled); return state.saveResult; },
      createFileSystemWatcher: glob => { state.watchers.push(glob); return disposable(); },
      onDidChangeConfiguration: callback => { state.configChanged = callback; return disposable(); }
    },
    window: {
      activeTextEditor: undefined,
      createOutputChannel: () => ({ appendLine: text => state.logs.push(text), ...disposable() }),
      showErrorMessage: async text => state.errors.push(text),
      showWarningMessage: async text => state.warnings.push(text),
      showWorkspaceFolderPick: async () => state.picked
    },
    commands: {
      registerCommand: (name, callback) => { state.commands.set(name, callback); return disposable(); },
      executeCommand: async name => state.executed.push(name)
    },
    Task: class { constructor(definition, scope, name, source, execution, problemMatchers) {
      Object.assign(this, { definition, scope, name, source, execution, problemMatchers });
    } },
    ProcessExecution: class { constructor(command, args, options) {
      Object.assign(this, { command, args, options });
    } },
    TaskGroup: { Build: 'build', Test: 'test' },
    TaskRevealKind: { Always: 1 },
    tasks: { executeTask: async task => state.tasks.push(task) }
  };
  class LanguageClient {
    constructor(id, name, server, options) {
      Object.assign(this, { id, name, server, options, disposed: false });
      state.clients.push(this);
    }
    async start() {
      if (state.startFailure) throw new Error(state.startFailure);
      if (state.startWait) await state.startWait;
    }
    async dispose() { this.disposed = true; }
  }
  const sandbox = {
    module: { exports: {} },
    require: name => {
      if (name === 'vscode') return vscode;
      if (name === 'vscode-languageclient/node') return { LanguageClient };
      throw new Error(`Unexpected dependency: ${name}`);
    }
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../extension.js'), 'utf8'), sandbox);
  const extension = sandbox.module.exports;
  const context = { subscriptions: [] };
  return { state, vscode, extension, activate: () => extension.activate(context),
    command: (id, ...args) => state.commands.get(`dotori.${id}`)(...args) };
}

test('starts stdio LSP with managed binary arguments, log path and dotfile watchers', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  h.state.config = { path: 'dotnet', arguments: ['/tools with spaces/dotori.dll'],
    'languageServer.logFile': '/logs with spaces/lsp.log' };
  await h.activate();
  assert.deepEqual(plain(h.state.clients[0].server), {
    command: 'dotnet', args: ['/tools with spaces/dotori.dll', 'lsp', '--log-file', '/logs with spaces/lsp.log']
  });
  assert.equal(h.state.clients[0].options.documentSelector.length, 2);
  assert.deepEqual(h.state.watchers, ['**/{.dotori,*.dotori}']);
});

test('build uses process arguments verbatim and the active workspace folder', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  const b = { name: 'B', uri: { fsPath: '/projects/B & $(literal)', folder: 'B' } };
  h.state.folders.push(b);
  h.state.config.path = 'C:/Tools with spaces & symbols/dotori.exe';
  h.vscode.window.activeTextEditor = { document: { uri: b.uri } };
  await h.activate();
  await h.command('build');
  const task = h.state.tasks[0];
  assert.equal(task.scope, b);
  assert.equal(task.execution.command, h.state.config.path);
  assert.deepEqual(plain(task.execution.args), ['build']);
  assert.equal(task.execution.options.cwd, b.uri.fsPath);
  assert.equal(task.group, 'build');
  assert.deepEqual(h.state.saves, [false]);
});

test('test, run and IDE refresh use the same command prefix', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  h.state.config = { path: 'dotnet', arguments: ['/tools/dotori.dll'] };
  await h.activate();
  await h.command('test');
  await h.command('run');
  await h.command('setupIde');
  assert.deepEqual(h.state.tasks.map(task => plain(task.execution.args)), [
    ['/tools/dotori.dll', 'test'], ['/tools/dotori.dll', 'run'],
    ['/tools/dotori.dll', 'setup', 'ide', '--ide', 'vscode']
  ]);
  assert.equal(h.state.tasks[0].group, 'test');
});

test('folder picker cancellation and failed saves do not execute tasks', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  h.state.folders.push({ name: 'B', uri: { fsPath: '/B' } });
  await h.activate();
  await h.command('build');
  assert.equal(h.state.tasks.length, 0);
  h.state.picked = h.state.folders[1];
  h.state.saveResult = false;
  await h.command('build');
  assert.equal(h.state.tasks.length, 0);
  h.state.saveResult = true;
  await h.command('build');
  assert.equal(h.state.tasks[0].scope, h.state.picked);
});

test('missing workspace gives a useful message', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  h.state.folders = [];
  await h.activate();
  await h.command('build');
  assert.equal(h.state.tasks.length, 0);
  assert.match(h.state.warnings[0], /Open a project folder/);
});

test('format delegates unsaved and untitled documents to the editor LSP formatter', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  await h.activate();
  h.vscode.window.activeTextEditor = { document: { languageId: 'dotori', isUntitled: true } };
  await h.command('formatFile');
  assert.deepEqual(h.state.executed, ['editor.action.formatDocument']);
  assert.equal(h.state.tasks.length, 0);
  assert.equal(h.state.saves.length, 0);
  h.vscode.window.activeTextEditor.document.languageId = 'cpp';
  await h.command('formatFile');
  assert.equal(h.state.executed.length, 1);
});

test('configuration changes replace the client with updated settings', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  await h.activate();
  h.state.config.path = '/new/dotori';
  h.state.configChanged({ affectsConfiguration: setting => setting === 'dotori.path' });
  await tick();
  assert.equal(h.state.clients.length, 2);
  assert.equal(h.state.clients[0].disposed, true);
  assert.equal(h.state.clients[1].server.command, '/new/dotori');
  h.state.configChanged({ affectsConfiguration: () => false });
  await tick();
  assert.equal(h.state.clients.length, 2);
  await Promise.all([h.command('restartLanguageServer'), h.command('restartLanguageServer')]);
  assert.equal(h.state.clients.length, 4);
  assert.ok(h.state.clients.slice(0, -1).every(client => client.disposed));
});

test('failed LSP startup leaves commands available and restart recovers', async t => {
  const h = harness();
  t.after(() => h.extension.deactivate());
  h.state.startFailure = 'spawn dotori ENOENT';
  await h.activate();
  assert.match(h.state.errors[0], /ENOENT/);
  await h.command('build');
  assert.equal(h.state.tasks.length, 1);
  h.state.startFailure = undefined;
  await h.command('restartLanguageServer');
  assert.equal(h.state.clients.length, 2);
  assert.equal(h.state.clients[0].disposed, true);
});

test('deactivation waits for startup and disposes the client', async () => {
  const h = harness();
  let release;
  h.state.startWait = new Promise(resolve => { release = resolve; });
  const activation = h.activate();
  await tick();
  const deactivation = h.extension.deactivate();
  release();
  await Promise.all([activation, deactivation]);
  assert.equal(h.state.clients.length, 1);
  assert.equal(h.state.clients[0].disposed, true);
});
