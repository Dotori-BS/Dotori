"""Exercise the actual dotori lsp process over Content-Length framed stdio."""

import argparse
import json
import subprocess
import tempfile
from pathlib import Path


def frame(message):
    body = json.dumps(message, ensure_ascii=False).encode("utf-8")
    return f"Content-Length: {len(body)}\r\n\r\n".encode("ascii") + body


def read_messages(output):
    messages = []
    while output:
        headers, separator, remainder = output.partition(b"\r\n\r\n")
        assert separator, output[:200]
        header_map = dict(line.split(b":", 1) for line in headers.split(b"\r\n"))
        length = int(header_map[b"Content-Length"].strip())
        body, output = remainder[:length], remainder[length:]
        assert len(body) == length
        try:
            messages.append(json.loads(body))
        except (ValueError, UnicodeError) as error:
            raise AssertionError(f"Invalid JSON-RPC response: {body[:400]!r}") from error
    return messages


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, default=Path(__file__).resolve().parents[1]
                        / "src/Dotori.Cli/bin/Release/net10.0/dotori.dll")
    cli = parser.parse_args().cli.resolve(strict=True)
    command = ["dotnet", str(cli)] if cli.suffix == ".dll" else [str(cli)]
    with tempfile.TemporaryDirectory(prefix="dotori-lsp-") as temporary:
        root = Path(temporary) / "프로젝트 space"
        root.mkdir()
        dependency = root / "lib"
        dependency.mkdir()
        (dependency / ".dotori").write_text("project Lib { type = header-only }", encoding="utf-8")
        text = '\n'.join([
            'project App {',
            '    type = executable',
            '    option simd {',
            '        default = false',
            '    }',
            '    [simd] {',
            '        defines { "USE_SIMD" }',
            '    }',
            '    dependencies {',
            '        lib = {',
            '            path = "./lib"',
            '        }',
            '    }',
            '}',
            '# 한글 문서 🚀',
        ])
        document = root / ".dotori"
        document.write_text(text, encoding="utf-8")
        uri = document.as_uri()
        identifier = {"uri": uri}
        messages = []

        def send(method, params=None, request_id=None):
            message = {"jsonrpc": "2.0", "method": method}
            if params is not None:
                message["params"] = params
            if request_id is not None:
                message["id"] = request_id
            messages.append(frame(message))

        send("initialize", {"processId": None, "rootUri": root.as_uri(), "capabilities": {}}, 1)
        send("initialized", {})
        send("textDocument/didOpen", {"textDocument": {
            "uri": uri, "languageId": "dotori", "version": 1, "text": text,
        }})
        send("textDocument/completion", {"textDocument": identifier,
             "position": {"line": 1, "character": 11}}, 2)
        escaped_id = 'hover"\\한글'
        send("textDocument/hover", {"textDocument": identifier,
             "position": {"line": 1, "character": 6}}, escaped_id)
        send("textDocument/definition", {"textDocument": identifier,
             "position": {"line": 10, "character": 23}}, 4)
        symbol = {"textDocument": identifier, "position": {"line": 2, "character": 12}}
        send("textDocument/references", {**symbol, "context": {"includeDeclaration": True}}, 5)
        send("textDocument/prepareRename", symbol, 6)
        send("textDocument/rename", {**symbol, "newName": "vectorized"}, 7)
        send("textDocument/formatting", {"textDocument": identifier,
             "options": {"tabSize": 4, "insertSpaces": True}}, 8)
        send("dotori/unknown", {}, 9)
        for version, content in [(2, 'project App { description = "unfinished'), (3, text)]:
            send("textDocument/didChange", {"textDocument": {"uri": uri, "version": version},
                 "contentChanges": [{"text": content}]})
            if version == 2:
                send("textDocument/formatting", {"textDocument": identifier,
                     "options": {"tabSize": 4, "insertSpaces": True}}, "invalid-format")
        # A notification larger than an OS pipe buffer must finish before the next
        # response and shutdown; otherwise headers and JSON bodies can interleave.
        large_text = 'project App {\n    type = executable\n    dependencies {\n' + '\n'.join(
            f'    missing{index} = {{ path = "./missing{index}" }}' for index in range(500)
        ) + '\n} }'
        send("textDocument/didChange", {"textDocument": {"uri": uri, "version": 4},
             "contentChanges": [{"text": large_text}]})
        send("textDocument/hover", {"textDocument": identifier,
             "position": {"line": 1, "character": 6}}, "after-large")
        send("textDocument/didClose", {"textDocument": identifier})
        send("textDocument/hover", {"textDocument": identifier,
             "position": {"line": 1, "character": 6}}, 10)
        send("shutdown", request_id=11)
        send("exit")

        result = subprocess.run([*command, "lsp"], input=b"".join(messages), cwd=root,
                                capture_output=True, timeout=30)
        assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
        received = read_messages(result.stdout)
        responses = {message["id"]: message for message in received if "id" in message}
        assert len(responses) == 13, received
        capabilities = responses[1]["result"]["capabilities"]
        assert capabilities["textDocumentSync"] == 1
        assert capabilities["renameProvider"]["prepareProvider"]
        labels = {item["label"] for item in responses[2]["result"]["items"]}
        assert "executable" in labels and "static-library" in labels
        assert "type" in responses[escaped_id]["result"]["contents"]["value"]
        assert responses[4]["result"]["uri"] == (dependency / ".dotori").as_uri()
        assert len(responses[5]["result"]) == 2
        assert responses[6]["result"]["start"]["line"] == 2
        edits = responses[7]["result"]["changes"][uri]
        assert len(edits) == 2 and all(edit["newText"] == "vectorized" for edit in edits)
        assert len(responses[8]["result"]) == 1
        assert "project App" in responses[8]["result"][0]["newText"]
        assert responses["invalid-format"]["result"] == []
        assert responses[9]["error"]["code"] == -32601
        assert responses[10]["result"] is None
        assert responses[11]["result"] is None
        assert "type" in responses["after-large"]["result"]["contents"]["value"]
        diagnostics = [message["params"]["diagnostics"] for message in received
                       if message.get("method") == "textDocument/publishDiagnostics"]
        assert len(diagnostics) == 5, received
        assert diagnostics[0] == []
        assert diagnostics[1] and diagnostics[1][0]["severity"] == 1
        assert diagnostics[2] == [] and diagnostics[4] == []
        assert len(diagnostics[3]) == 500
        assert len(json.dumps(diagnostics[3]).encode("utf-8")) > 65536
        result = subprocess.run([*command, "lsp"], input=frame({"jsonrpc": "2.0", "method": "exit"}),
                                cwd=root, capture_output=True, timeout=10)
        assert result.returncode == 1
    print("LSP smoke passed: lifecycle, diagnostics, completion, hover, definition, references, rename, formatting.")


if __name__ == "__main__":
    main()
