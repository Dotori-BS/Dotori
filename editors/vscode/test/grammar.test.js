'use strict';

const { test, before } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { Registry, parseRawGrammar, INITIAL } = require('vscode-textmate');
const { loadWASM, OnigScanner, OnigString } = require('vscode-oniguruma');

let grammar;
before(async () => {
  await loadWASM(fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')));
  const registry = new Registry({
    onigLib: Promise.resolve({
      createOnigScanner: patterns => new OnigScanner(patterns),
      createOnigString: text => new OnigString(text)
    }),
    loadGrammar: async () => parseRawGrammar(fs.readFileSync(
      path.join(__dirname, '../syntaxes/dotori.tmLanguage.json'), 'utf8'), 'dotori.json')
  });
  grammar = await registry.loadGrammar('source.dotori');
});

function tokens(line, state = INITIAL) {
  return grammar.tokenizeLine(line, state);
}

test('project declarations, properties, standards and condition scopes', () => {
  const line = 'project Demo { [windows.debug] { std = c++23 } }';
  const result = tokens(line).tokens;
  for (const [word, scope] of [
    ['project', 'keyword.declaration.dotori'], ['windows', 'keyword.control.platform.dotori'],
    ['debug', 'keyword.control.config.dotori'], ['std', 'support.type.property-name.dotori'],
    ['c++23', 'constant.language.std.dotori']
  ]) {
    const offset = line.indexOf(word);
    const token = result.find(t => t.startIndex <= offset && t.endIndex >= offset + word.length);
    assert.ok(token?.scopes.includes(scope), `${word}: ${JSON.stringify(token)}`);
  }
});

test('enum values ending in plus signs and keyword-like identifiers are whole tokens', () => {
  for (const word of ['libc++', 'libstdc++']) {
    const token = tokens(word).tokens[0];
    assert.ok(token.scopes.includes('constant.language.dotori'), JSON.stringify(token));
    assert.equal(token.endIndex, word.length);
  }
  for (const word of ['project-helper', 'type-extra', 'c++23-extra']) {
    const token = tokens(word).tokens[0];
    assert.ok(token.scopes.includes('variable.other.dotori'), JSON.stringify(token));
    assert.equal(token.endIndex, word.length);
  }
});

test('strings protect URLs and support escaped quotes and environment interpolation', () => {
  const result = tokens('description = "https://host/#name \\"quoted\\" ${HOME}"').tokens;
  assert.ok(result.some(t => t.scopes.includes('variable.other.env.dotori')));
  assert.ok(result.some(t => t.scopes.includes('constant.character.escape.dotori')));
  assert.ok(!result.some(t => t.scopes.some(scope => scope.startsWith('comment.'))));
});

test('multiline block comments continue until the closing delimiter', () => {
  const start = tokens('(* project ignored');
  const continuation = tokens('still a comment', start.ruleStack);
  assert.ok(continuation.tokens.every(t => t.scopes.includes('comment.block.dotori')));
  const end = tokens('*) project Demo {}', continuation.ruleStack);
  assert.ok(end.tokens.some(t => t.scopes.includes('keyword.declaration.dotori')));
});
