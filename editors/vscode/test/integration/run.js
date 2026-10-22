'use strict';

const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const { downloadAndUnzipVSCode, runTests } = require('@vscode/test-electron');

async function main() {
  const repo = path.resolve(__dirname, '../../../..');
  const root = path.join(repo, 'build/editor-integration', `vscode-${Date.now()}`);
  const workspace = path.join(root, 'project with spaces');
  const extensions = path.join(root, 'extensions');
  const userData = path.join(root, 'user-data');
  const driver = path.join(root, 'driver');
  const cli = path.join(repo, 'src/Dotori.Cli/bin/Release/net10.0/dotori.dll');
  if (!fs.existsSync(cli)) throw new Error('Build the Release CLI before running integration tests.');
  for (const dir of [workspace, extensions, path.join(userData, 'User'), driver, path.join(workspace, 'lib')])
    fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(workspace, '.env'), '');
  fs.writeFileSync(path.join(workspace, '.dotori'), [
    'project App {', '    type = executable', '    option simd {',
    '        default = false', '    }', '    [simd] {', '        defines { "USE_SIMD" }',
    '    }', '    dependencies {', '        lib = {', '            path = "./lib"',
    '        }', '    }', '    sources { include "hello.cpp" }', '}', ''
  ].join('\n'));
  fs.writeFileSync(path.join(workspace, 'lib/.dotori'), 'project Lib { type = header-only }\n');
  fs.copyFileSync(path.join(repo, 'tests/platform-smoke/hello.cpp'), path.join(workspace, 'hello.cpp'));
  fs.writeFileSync(path.join(userData, 'User/settings.json'), JSON.stringify({
    'dotori.path': 'dotnet', 'dotori.arguments': [cli],
    'dotori.languageServer.logFile': path.join(root, 'lsp.log'),
    'telemetry.telemetryLevel': 'off', 'extensions.autoUpdate': false,
    'workbench.startupEditor': 'none', 'security.workspace.trust.enabled': false,
    'files.eol': '\n', 'editor.formatOnSave': false,
    'task.showCommandCenter': false
  }, null, 2));
  fs.writeFileSync(path.join(driver, 'package.json'), JSON.stringify({
    name: 'dotori-integration-driver', publisher: 'dotori-tests', version: '1.0.0',
    engines: { vscode: '^1.95.0' }, extensionDependencies: ['dotori.dotori']
  }));
  const executable = await downloadAndUnzipVSCode({
    version: process.env.VSCODE_VERSION || 'stable',
    cachePath: path.join(repo, 'build/editor-integration/vscode-download')
  });
  const base = path.dirname(executable);
  const candidates = [base, ...fs.readdirSync(base, { withFileTypes: true })
    .filter(entry => entry.isDirectory()).map(entry => path.join(base, entry.name))];
  const cliScript = candidates.map(dir => path.join(dir, 'resources/app/out/cli.js'))
    .find(file => fs.existsSync(file));
  // On macOS the executable lives inside Electron.app/Contents/MacOS.
  const script = process.platform === 'darwin'
    ? path.resolve(path.dirname(executable), '../Resources/app/out/cli.js') : cliScript;
  const install = cp.spawnSync(executable, [script, '--user-data-dir', userData,
    '--extensions-dir', extensions, '--install-extension', path.join(repo, 'editors/vscode/dotori-0.1.0.vsix'), '--force'], {
    env: { ...process.env, ELECTRON_RUN_AS_NODE: '1' }, encoding: 'utf8', timeout: 120000
  });
  process.stdout.write(install.stdout || '');
  process.stderr.write(install.stderr || '');
  if (install.status !== 0) throw new Error(`VSIX installation failed: ${install.error || install.status}`);
  console.log(`Integration artifacts: ${root}`);
  await runTests({
    vscodeExecutablePath: executable, extensionDevelopmentPath: driver,
    extensionTestsPath: path.join(__dirname, 'suite.js'),
    extensionTestsEnv: { DOTORI_INTEGRATION_ROOT: root },
    launchArgs: [workspace, '--user-data-dir', userData, '--extensions-dir', extensions,
      '--skip-welcome', '--skip-release-notes', '--disable-workspace-trust', '--disable-updates']
  });
}

main().catch(error => { console.error(error); process.exitCode = 1; });
