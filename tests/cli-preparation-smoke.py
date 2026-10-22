"""Exercise CLI project preparation and PCH reuse with a real Clang toolchain.

Run after dotnet build: python tests/cli-preparation-smoke.py
Pass --cli to test another managed DLL or a published native executable.
"""

import argparse
import json
import subprocess
import tempfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--cli", type=Path,
        default=Path(__file__).resolve().parents[1]
        / "src/Dotori.Cli/bin/Debug/net10.0/dotori.dll",
    )
    parser.add_argument("--compiler", default="clang")
    options = parser.parse_args()
    cli = options.cli.resolve(strict=True)
    command = ["dotnet", str(cli)] if cli.suffix == ".dll" else [str(cli)]

    with tempfile.TemporaryDirectory(prefix="dotori-preparation-") as temporary:
        root = Path(temporary)

        def run(*args, success=True):
            result = subprocess.run(
                [*command, *args], cwd=root, capture_output=True,
                text=True, encoding="utf-8", timeout=120,
            )
            if (result.returncode == 0) != success:
                raise AssertionError(result.stdout + result.stderr)
            return result.stdout + result.stderr

        (root / ".dotori").write_text(
            'project Sample { type = executable toolchain = "'
            + options.compiler.replace("\\", "/")
            + '" sources { include "main.cpp" } pch { header = "pch.h" source = "pch.cpp" } }',
            encoding="utf-8",
        )
        (root / "pch.h").write_text("#define RESULT 0\n", encoding="utf-8")
        (root / "pch.cpp").write_text('#include "pch.h"\n', encoding="utf-8")
        source = root / "main.cpp"
        source.write_text('#include "pch.h"\nint main() { return RESULT; }\n', encoding="utf-8")
        build = ["build", "--project", str(root), "--no-link"]

        # Dry runs must not mark a PCH as built or create its output.
        output = run(*build, "--dry-run")
        assert "c++-header" in output or "/Yc" in output, output
        assert not list(root.rglob("*.pch"))

        run(*build, "--frozen")
        pch, = root.rglob("*.pch")
        original_pch_time = pch.stat().st_mtime_ns
        run(*build, "--explain")
        assert pch.stat().st_mtime_ns == original_pch_time

        # Single-file builds share PCH cache state with full builds.
        run(*build, "--file", str(source))
        assert pch.stat().st_mtime_ns == original_pch_time
        (root / "pch.h").write_text("#define RESULT (0)\n", encoding="utf-8")
        run(*build, "--file", str(source))
        changed_pch_time = pch.stat().st_mtime_ns
        assert changed_pch_time != original_pch_time
        run(*build)
        assert pch.stat().st_mtime_ns == changed_pch_time

        # Failed PCH compilation must stop both paths before compiling a source.
        for args in [[], ["--file", str(source)]]:
            (root / "pch.h").write_text("#error invalid PCH\n", encoding="utf-8")
            output = run(*build, *args, "--verbose", success=False)
            assert "invalid PCH" in output, output
            assert "-include-pch" not in output and "/Yu" not in output, output
        (root / "pch.h").write_text("#define RESULT 0\n", encoding="utf-8")

        run(*build, "--file", str(root / "missing.cpp"), success=False)
        run(*build, "--unknown-project-option", success=False)
        run("export", "ide", "--project", str(root))
        exported = json.loads((root / "compile_commands.json").read_text(encoding="utf-8"))
        run("generate-compile-commands", "--project", str(root))
        assert json.loads((root / "compile_commands.json").read_text(encoding="utf-8")) == exported

        # run/test use the same preparation and actual PCH-enabled build pipeline.
        run("run", "--project", str(root))
        run("test", "--project", str(root))

        # Offline policies must fail without fetching missing locked Git packages.
        lock = root / "dotori-lock.json"
        lock.write_text(json.dumps({"lockVersion": 2, "packages": {
            "missing@1.0.0": {"version": "1.0.0", "resolved": "git+https://example.invalid/missing#v1"},
        }}), encoding="utf-8")
        locked_contents = lock.read_bytes()
        for policy in ["--offline", "--frozen"]:
            output = run(*build, policy, success=False)
            assert "offline mode" in output, output
            assert lock.read_bytes() == locked_contents
        # Exports must not acquire the build commands' package-fetch behavior.
        run("export", "ide", "--project", str(root))

        # Check transitive graph traversal and cycle errors across build/export.
        for name, dependency in [("core", None), ("lib", "core"), ("app", "lib")]:
            directory = root / name
            directory.mkdir()
            dependencies = (
                f'dependencies {{ {dependency} = {{ path = "../{dependency}" }} }}'
                if dependency else ""
            )
            (directory / ".dotori").write_text(
                f'project {name} {{ type = static-library sources {{ include "unit.cpp" }} '
                + dependencies + ' }', encoding="utf-8",
            )
            (directory / "unit.cpp").write_text(f"int {name}_value() {{ return 0; }}\n", encoding="utf-8")
        graph_args = ["--project", str(root / "app"), "--compiler", options.compiler]
        output = run("build", *graph_args, "--dry-run", "--no-link")
        assert output.index("Building core") < output.index("Building lib") < output.index("Building app")
        run("export", "ide", *graph_args)
        entries = json.loads((root / "compile_commands.json").read_text(encoding="utf-8"))
        assert len(entries) == 3, entries

        # Build-system export must work without detecting a compiler.
        app_manifest = root / "app/.dotori"
        app_manifest.write_text(
            app_manifest.read_text(encoding="utf-8").replace(
                "type = static-library", 'toolchain = "missing-compiler" type = static-library'),
            encoding="utf-8",
        )
        run("export", "build-system", "--project", str(root / "app"), "--output", str(root / "cmake"))
        assert len(list((root / "cmake").rglob("CMakeLists.txt"))) == 3
        core_manifest = root / "core/.dotori"
        core_manifest.write_text(
            'project core { dependencies { app = { path = "../app" } } }', encoding="utf-8",
        )
        for args in [["build", "--dry-run"], ["export", "ide"]]:
            output = run(*args, *graph_args, success=False)
            assert "Circular dependency detected" in output, output

    print("CLI preparation smoke passed (PCH, run/test, exports, graph traversal, offline and cycle errors).")


if __name__ == "__main__":
    main()
