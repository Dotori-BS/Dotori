"""Compile and link with uploaded Clang/LLD while the worker has an empty tool PATH.

Build Dotori.slnx first. This test stages the local compiler/linker (large binaries),
starts local coordinator/worker processes, and checks content reuse and cache hits.
"""

import argparse
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path


def free_port():
    with socket.socket() as connection:
        connection.bind(("127.0.0.1", 0))
        return connection.getsockname()[1]


def main():
    repo = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, default=repo / "src/Dotori.Cli/bin/Debug/net10.0/dotori.dll")
    parser.add_argument("--server", type=Path, default=repo / "src/Dotori.BuildServer/bin/Debug/net10.0/dotori-server.dll")
    parser.add_argument("--worker", type=Path, default=repo / "src/Dotori.Worker/bin/Debug/net10.0/dotori-worker.dll")
    parser.add_argument("--compiler", default=shutil.which("clang++"))
    options = parser.parse_args()
    if sys.platform not in ("win32", "linux"):
        parser.error("This smoke fixture targets Windows or Linux; it does not configure an Apple SDK.")
    if not options.compiler:
        raise RuntimeError("Install Clang/LLD or supply --compiler.")
    dotnet = str(Path(shutil.which("dotnet")).resolve())

    def launch_command(path):
        path = path.resolve(strict=True)
        return [dotnet, str(path)] if path.suffix == ".dll" else [str(path)]

    with tempfile.TemporaryDirectory(prefix="dotori-shipped-tools-") as temporary:
        root = Path(temporary)
        project = root / "project space"
        project.mkdir()
        tools = root / "tools"
        tools.mkdir()
        compiler_name = "clang++.exe" if os.name == "nt" else "clang++"
        linker_name = "lld-link.exe" if os.name == "nt" else "ld.lld"
        linker = shutil.which(linker_name) or str(Path(options.compiler).parent / linker_name)
        shutil.copy2(options.compiler, tools / compiler_name, follow_symlinks=True)
        shutil.copy2(linker, tools / linker_name, follow_symlinks=True)
        library_environment = {}
        if os.name != "nt":
            libraries = tools / "lib"
            libraries.mkdir()
            # Ship compiler-specific shared libraries as well as its executable.
            # The OS loader, libc and other base runtime libraries remain host-provided.
            for executable in [options.compiler, linker]:
                dependencies = subprocess.run(["ldd", executable], capture_output=True, text=True)
                if dependencies.returncode and not any(message in dependencies.stdout + dependencies.stderr
                    for message in ["not a dynamic executable", "statically linked"]):
                    raise RuntimeError(dependencies.stdout + dependencies.stderr)
                for library in re.findall(r"=>\s+(/\S+)", dependencies.stdout):
                    name = Path(library).name
                    if name.startswith(("libc.so", "libm.so", "libdl.so", "libpthread.so", "librt.so")):
                        continue
                    shutil.copy2(library, libraries / name, follow_symlinks=True)
            library_environment["LD_LIBRARY_PATH"] = str(libraries)
        sdk = root / "sdk"
        sdk.mkdir()
        (sdk / "sdk.h").write_text("#define SDK_VALUE 23\n", encoding="utf-8")
        (project / "message.h").write_text("#define PROJECT_VALUE 19\n", encoding="utf-8")
        (project / "main.cpp").write_text(
            '#include "message.h"\n#include <sdk.h>\n'
            '#if defined(__linux__)\nextern "C" void _start() {\n'
            '  long code = SDK_VALUE + PROJECT_VALUE;\n'
            '  asm volatile("syscall" : : "a"(60L), "D"(code) : "rcx", "r11", "memory");\n'
            '  __builtin_unreachable();\n}\n#else\n'
            'extern "C" int main() { return SDK_VALUE + PROJECT_VALUE; }\n#endif\n',
            encoding="utf-8",
        )
        target = "x86_64-pc-windows-msvc" if os.name == "nt" else "x86_64-unknown-linux-gnu"
        profile = project / "compiler.json"
        profile.write_text(json.dumps({"kind": "clang", "compiler": str(tools / compiler_name),
            "linker": str(tools / compiler_name), "targetTriple": target}), encoding="utf-8")
        configuration = project / "remote-tools.json"
        configuration.write_text(json.dumps({"toolchainRoots": [str(tools), str(sdk)],
            "inputRoots": [str(project)], "workingDirectory": str(project),
            "environment": library_environment}), encoding="utf-8")
        link_flags = ["-nostdlib", "-Wl,/entry:main,/subsystem:console,/nodefaultlib"] if os.name == "nt" else ["-nostdlib", "-Wl,-e,_start"]
        if os.name == "nt":
            pdb_path = (project / ".dotori-cache/bin/windows-x64-debug/side.pdb").as_posix()
            link_flags.append(f'-Wl,/debug,/pdb:"{pdb_path}"')
        quoted_link_flags = " ".join(json.dumps(flag) for flag in link_flags)
        (project / ".dotori").write_text(
            'project UploadedTools { type = executable std = c++17 runtime-link = dynamic\n'
            'sources { include "main.cpp" }\n'
            f'compile-flags {{ "-I{sdk.as_posix()}" "-fno-stack-protector" }}\n'
            f'link-flags {{ {quoted_link_flags} }}\n}}', encoding="utf-8",
        )
        worker_port, server_port = free_port(), free_port()
        worker_address = f"http://127.0.0.1:{worker_port}"
        server_address = f"http://127.0.0.1:{server_port}"
        worker_cache = root / "worker-cache"
        coordinator_cache = root / "coordinator-cache"
        worker_environment = os.environ.copy()
        for key in ["INCLUDE", "LIB", "LIBPATH", "CC", "CXX", "DOTORI_WORKER_TOKEN", "DOTORI_WORKER_TOKEN_FILE"]:
            worker_environment.pop(key, None)
        worker_environment.update({"PATH": "", "ASPNETCORE_URLS": worker_address,
            "DOTORI_ALLOW_UPLOADED_TOOLCHAINS": "true", "DOTORI_SUPPORTED_TARGETS": "no-installed-toolchains",
            "DOTORI_CONTENT_CACHE_DIR": str(worker_cache), "DOTORI_WORKER_TOKEN": "smoke-worker-token"})
        server_environment = os.environ.copy()
        for key in ["DOTORI_WORKER_TOKEN", "DOTORI_WORKER_TOKEN_FILE", "DOTORI_SERVER_TOKEN", "DOTORI_SERVER_TOKEN_FILE"]:
            server_environment.pop(key, None)
        server_environment.update({"ASPNETCORE_URLS": server_address, "DOTORI_WORKERS": worker_address,
            "BUILD_CACHE_DIR": str(root / "object-cache"), "BUILD_CONTENT_CACHE_DIR": str(coordinator_cache),
            "DOTORI_WORKER_TOKEN": "smoke-worker-token", "DOTORI_SERVER_TOKEN": "smoke-server-token"})
        client_environment = os.environ.copy()
        client_environment.pop("DOTORI_SERVER_TOKEN_FILE", None)
        client_environment["DOTORI_SERVER_TOKEN"] = "smoke-server-token"
        creation_flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        processes = []
        try:
            with (root / "worker.log").open("wb") as worker_log, (root / "server.log").open("wb") as server_log:
                for command, environment, log in [
                    (launch_command(options.worker), worker_environment, worker_log),
                    (launch_command(options.server), server_environment, server_log),
                ]:
                    processes.append(subprocess.Popen(command, cwd=project, env=environment,
                        stdout=log, stderr=subprocess.STDOUT, creationflags=creation_flags))
                for address in [worker_address, server_address]:
                    # Only probe TCP: these endpoints intentionally accept HTTP/2 only.
                    for attempt in range(100):
                        if any(process.poll() is not None for process in processes):
                            raise AssertionError((root / "worker.log").read_text(errors="replace") + (root / "server.log").read_text(errors="replace"))
                        try:
                            with socket.create_connection(("127.0.0.1", int(address.rsplit(":", 1)[1])), timeout=1):
                                break
                        except OSError:
                            time.sleep(0.1)
                    else:
                        raise TimeoutError(address)
                build = [*launch_command(options.cli), "build", "--project", str(project), "--compiler", str(profile),
                    "--remote", server_address, "--remote-toolchain", str(configuration),
                    "--target", "windows-x64" if os.name == "nt" else "linux-x64"]

                def run_build():
                    result = subprocess.run(build, cwd=project, capture_output=True, timeout=300,
                        text=True, encoding="utf-8", errors="replace", env=client_environment)
                    if result.returncode:
                        raise AssertionError(result.stdout + result.stderr + (root / "worker.log").read_text(errors="replace") + (root / "server.log").read_text(errors="replace"))
                    assert "Falling back to local" not in result.stderr
                    return result

                unauthorized = subprocess.run(build, cwd=project, capture_output=True, timeout=300,
                    text=True, encoding="utf-8", errors="replace",
                    env={**client_environment, "DOTORI_SERVER_TOKEN": "wrong-token"})
                assert unauthorized.returncode != 0 and "Unauthenticated" in unauthorized.stderr, unauthorized.stdout + unauthorized.stderr
                run_build()
                assert list((worker_cache / "toolchains").rglob(compiler_name))
                assert list((worker_cache / "toolchains").rglob("sdk.h"))
                executable = next((project / ".dotori-cache/bin").rglob("*.exe")) if os.name == "nt" else next(
                    path for path in (project / ".dotori-cache/bin").rglob("*") if path.is_file())
                assert subprocess.run([str(executable)], timeout=10).returncode == 42
                if os.name == "nt":
                    assert list((project / ".dotori-cache/bin").rglob("side.pdb"))
                first_compile_cache_entries = len(list((coordinator_cache / "results").glob("*.compile")))
                assert first_compile_cache_entries == 1
                upload_marker = "Executing endpoint 'gRPC - /BuildWorker/UploadBlob'"
                worker_uploads = (root / "worker.log").read_text(errors="replace").count(upload_marker)
                assert worker_uploads > 0
                for path in (project / ".dotori-cache").rglob("*.obj"):
                    path.unlink()
                for path in (project / ".dotori-cache").rglob("*.o"):
                    path.unlink()
                run_build()
                assert len(list((coordinator_cache / "results").glob("*.compile"))) == 1
                assert worker_uploads == (root / "worker.log").read_text(errors="replace").count(upload_marker)
                (project / "message.h").write_text("#define PROJECT_VALUE 20\n", encoding="utf-8")
                run_build()
                assert len(list((coordinator_cache / "results").glob("*.compile"))) == 2
                assert subprocess.run([str(executable)], timeout=10).returncode == 43
        finally:
            for process in reversed(processes):
                process.terminate()
            for process in processes:
                process.wait(timeout=10)
    print("Uploaded-toolchain smoke passed: compiler/SDK/header upload, link, executable, CAS reuse and cache invalidation.")


if __name__ == "__main__":
    main()
