'use strict';

const vscode = require('vscode');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function eventually(check, label, timeout = 30000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const result = await check();
    if (result) return result;
    await delay(100);
  }
  throw new Error(`Timed out: ${label}`);
}

exports.run = async () => {
  const root = process.env.DOTORI_INTEGRATION_ROOT;
  const workspace = vscode.workspace.workspaceFolders[0].uri;
  const results = [];
  async function check(name, action) {
    await action();
    results.push(name);
    console.log(`PASS ${name}`);
    fs.writeFileSync(path.join(root, 'results.json'), JSON.stringify({ vscode: vscode.version, passed: results }, null, 2));
  }
  const uri = vscode.Uri.joinPath(workspace, '.dotori');
  const doc = await vscode.workspace.openTextDocument(uri);
  await vscode.window.showTextDocument(doc);
  const extension = vscode.extensions.getExtension('dotori.dotori');
  assert.ok(extension, 'Packaged extension must be installed');
  assert.ok(extension.extensionPath.startsWith(path.join(root, 'extensions')), extension.extensionPath);
  await extension.activate();
  await check('installed VSIX activates and recognizes .dotori', async () => {
    assert.equal(doc.languageId, 'dotori');
    assert.ok(extension.isActive);
  });
  await check('completion from real language server', async () => {
    const completions = await eventually(async () => {
      const list = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', uri, new vscode.Position(1, 11));
      return list?.items.some(item => (item.label.label || item.label) === 'executable') && list;
    }, 'completion provider');
    assert.ok(completions.items.some(item => (item.label.label || item.label) === 'static-library'));
  });
  await check('hover', async () => {
    const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', uri, new vscode.Position(1, 6));
    assert.ok(hovers.some(hover => hover.contents.some(content => /type/.test(content.value))));
  });
  await check('definition into dependency dotfile', async () => {
    const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', uri, new vscode.Position(10, 23));
    console.log('Definition targets:', JSON.stringify(definitions), 'Expected:', vscode.Uri.joinPath(workspace, 'lib/.dotori').fsPath);
    assert.ok(definitions.some(definition => (definition.uri || definition.targetUri).fsPath === vscode.Uri.joinPath(workspace, 'lib/.dotori').fsPath));
  });
  await check('option references and rename edits', async () => {
    const position = new vscode.Position(2, 12);
    const refs = await vscode.commands.executeCommand('vscode.executeReferenceProvider', uri, position);
    assert.equal(refs.length, 2);
    const edit = await vscode.commands.executeCommand('vscode.executeDocumentRenameProvider', uri, position, 'vectorized');
    assert.equal(edit.get(uri).length, 2);
    assert.ok(edit.get(uri).every(change => change.newText === 'vectorized'));
  });
  const named = vscode.Uri.joinPath(workspace, 'named.dotori');
  fs.writeFileSync(named.fsPath, 'project Broken { type = ??? }');
  const bad = await vscode.workspace.openTextDocument(named);
  await vscode.window.showTextDocument(bad);
  await check('named.dotori association and live error diagnostics', async () => {
    assert.equal(bad.languageId, 'dotori');
    const diagnostics = await eventually(() => vscode.languages.getDiagnostics(named).find(d => d.severity === vscode.DiagnosticSeverity.Error), 'syntax error diagnostics');
    assert.ok(diagnostics.message);
  });
  await check('diagnostics clear after an unsaved edit', async () => {
    const edit = new vscode.WorkspaceEdit();
    edit.replace(named, new vscode.Range(bad.positionAt(0), bad.positionAt(bad.getText().length)), 'project Fixed { type = header-only }');
    assert.ok(await vscode.workspace.applyEdit(edit));
    await eventually(() => vscode.languages.getDiagnostics(named).length === 0, 'diagnostics clearing');
    assert.ok(bad.isDirty);
  });
  await check('format command preserves unsaved buffer and undo', async () => {
    const before = bad.getText();
    await vscode.commands.executeCommand('dotori.formatFile');
    await eventually(() => bad.getText() !== before, 'format command edits');
    assert.ok(bad.isDirty);
    assert.equal(fs.readFileSync(named.fsPath, 'utf8'), 'project Broken { type = ??? }');
    await vscode.commands.executeCommand('undo');
    assert.equal(bad.getText(), before);
  });
  const autoFormatUri = vscode.Uri.joinPath(workspace, 'auto-format.dotori');
  fs.writeFileSync(autoFormatUri.fsPath, 'project AutoFormat { type = header-only }\n');
  const autoFormatDoc = await vscode.workspace.openTextDocument(autoFormatUri);
  await vscode.window.showTextDocument(autoFormatDoc);
  const pendingEdit = new vscode.WorkspaceEdit();
  pendingEdit.insert(autoFormatUri, autoFormatDoc.positionAt(autoFormatDoc.getText().length), ' ');
  await vscode.workspace.applyEdit(pendingEdit);
  await check('format on save uses the default Dotori LSP formatter', async () => {
    assert.equal(vscode.workspace.getConfiguration('dotori', autoFormatUri).get('formatOnSave'), true);
    await eventually(async () => {
      const edits = await vscode.commands.executeCommand('vscode.executeFormatDocumentProvider', autoFormatUri, {
        tabSize: 4, insertSpaces: true
      });
      return edits?.length > 0;
    }, 'formatting provider');
    await autoFormatDoc.save();
    await eventually(() => fs.readFileSync(autoFormatUri.fsPath, 'utf8') ===
      'project AutoFormat {\n    type = header-only\n}\n', 'formatted file saved');
    assert.equal(autoFormatDoc.isDirty, false);
  });
  await check('untitled Dotori buffers are formatted', async () => {
    const untitled = await vscode.workspace.openTextDocument({ language: 'dotori', content: 'project Untitled { type = header-only }' });
    await vscode.window.showTextDocument(untitled);
    await vscode.commands.executeCommand('dotori.formatFile');
    await eventually(() => untitled.getText().includes('\n'), 'untitled formatting');
    assert.ok(untitled.isUntitled);
    await vscode.commands.executeCommand('workbench.action.revertAndCloseActiveEditor');
  });
  await vscode.window.showTextDocument(doc);
  async function task(command) {
    const exit = new Promise((resolve, reject) => {
      const timer = setTimeout(() => { subscription.dispose(); reject(new Error(`Task timeout: ${command}`)); }, 120000);
      const subscription = vscode.tasks.onDidEndTaskProcess(event => {
        if (event.execution.task.source !== 'Dotori') return;
        clearTimeout(timer); subscription.dispose(); resolve(event.exitCode);
      });
    });
    await vscode.commands.executeCommand(`dotori.${command}`);
    assert.equal(await exit, 0, `${command} exit code`);
  }
  for (const command of ['build', 'run', 'test', 'setupIde'])
    await check(`real ${command} process task`, () => task(command));
  await check('IDE refresh writes C++ integration files', async () => {
    for (const file of ['compile_commands.json', '.vscode/tasks.json'])
      assert.ok(fs.existsSync(path.join(workspace.fsPath, file)), file);
  });
  await check('language server restart reconnects completion', async () => {
    await vscode.commands.executeCommand('dotori.restartLanguageServer');
    await eventually(async () => {
      const list = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', uri, new vscode.Position(1, 11));
      return list?.items.length > 0;
    }, 'completion after restart');
  });
  console.log(`PASS ${results.length} real VS Code integration checks`);
};
