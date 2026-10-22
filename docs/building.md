# Dotori 프로젝트 빌드 방법

dotori 자체(CLI, 서버, 워커 등)를 소스에서 빌드하는 방법을 설명합니다.

## 사전 요구사항

- .NET 10 SDK 이상
- bash (Linux / macOS / WSL)

## 빌드 대상

| 대상 이름 | 설명 | 출력 경로 |
|-----------|------|-----------|
| `cli` | dotori CLI (NativeAOT 단일 실행 파일, Language Server 포함) | `build/cli/` |
| `build_server` | 분산 빌드 코디네이터 | `build/build_server/` |
| `worker` | 분산 빌드 워커 | `build/worker/` |
| `registry` | npm 호환 레지스트리 배포 번들 (Compose/Kubernetes) | `build/registry/` |

CI는 Windows x64/ARM64, macOS ARM64, Linux x64/ARM64에서 전체 .NET 테스트와
NativeAOT publish를 수행합니다. 별도 C++ 스모크 매트릭스는 Linux Clang/GCC,
Apple Clang, MSVC로 실제 `.dotori` 프로젝트를 빌드하고 `type = test` 타깃을
실행해 toolchain별 회귀를 검증합니다.

---

## 스크립트 대응표

`build.sh` (Linux/macOS)와 `build.ps1` (Windows)는 동일한 기능과 옵션 구조를 가집니다.

| 기능 | build.sh | build.ps1 |
|------|----------|-----------|
| 전체 빌드 | `./build.sh` | `.\build.ps1` |
| 대상 지정 | `--only cli,worker` | `-Only cli,worker` |
| 대상 제외 | `--skip registry` | `-Skip registry` |
| 빌드 구성 | `--config Debug` | `-Config Debug` |
| RID 지정 | `--rid linux-x64` | `-Rid win-x64` |
| 버전 지정 | `--version v1.2.3` | `-Version v1.2.3` |
| 빌드 후 설치 | `--install` | `-Install` |
| 제거 | — | `-Uninstall` |

---

## build.sh 사용법

```bash
./build.sh [옵션]
```

### 옵션

| 옵션 | 기본값 | 설명 |
|------|--------|------|
| `--only <targets>` | 전체 | 빌드할 대상을 쉼표로 지정 |
| `--skip <targets>` | 없음 | 건너뛸 대상을 쉼표로 지정 |
| `--config <cfg>` | `Release` | 빌드 구성 (`Release` / `Debug`) |
| `--rid <rid>` | — | .NET Runtime Identifier. 지정 시 self-contained로 퍼블리시 |
| `--version <ver>` | — | 어셈블리 버전 (예: `v1.2.3`) |
| `--install` | — | CLI 빌드 후 시스템에 설치 (manpage 포함) |
| `--help` | — | 도움말 출력 |

### 예시

```bash
# 전체 빌드 (Release)
./build.sh

# CLI를 linux-x64 self-contained 단일 바이너리로 빌드
./build.sh --only cli --rid linux-x64

# CLI + 서버 + 워커를 Windows x64용으로 빌드, 버전 태그 포함
./build.sh --only cli,build_server,worker --rid win-x64 --version v1.0.0

# macOS Apple Silicon용 CLI 빌드
./build.sh --only cli --rid osx-arm64

# Debug 구성으로 전체 빌드
./build.sh --config Debug

# registry를 제외하고 빌드
./build.sh --skip registry

# 레지스트리 배포 파일만 build/registry에 준비
./build.sh --only registry

# CLI 빌드 후 시스템에 바로 설치
./build.sh --only cli --install

# linux-x64 단일 바이너리로 빌드 후 설치
./build.sh --only cli --rid linux-x64 --install
```

---

## CLI 설치 (Linux / macOS)

`--install` 플래그를 사용하면 빌드 직후 CLI를 시스템에 설치합니다.

**root 권한이 있는 경우** — 확인 없이 바로 설치합니다.

```
설치 위치: /usr/local/bin/dotori
manpage:   /usr/local/share/man/man1/dotori.1
```

**일반 사용자인 경우** — 설치 여부를 한 번 확인합니다.

```
[install] root 권한이 없습니다.
         ~/.local/bin/dotori 에 설치하시겠습니까? [Y/n]
```

`Y` (또는 Enter) → `~/.local/bin/dotori` 및 `~/.local/share/man/man1/dotori.1` 에 설치됩니다.

`~/.local/bin`이 PATH에 없으면 추가 방법을 안내합니다.

```bash
# ~/.bashrc 또는 ~/.zshrc 에 추가
export PATH="$HOME/.local/bin:$PATH"
```

---

## CLI 설치 (Windows)

Windows에서는 `build.ps1` PowerShell 스크립트를 사용합니다. `build.sh`와 동일한 옵션 구조를 가집니다.

### 기본 설치

```powershell
# CLI만 빌드 후 설치
.\build.ps1 -Only cli -Install

# win-x64 단일 바이너리로 빌드 후 설치, 버전 태그 포함
.\build.ps1 -Only cli -Rid win-x64 -Version v1.0.0 -Install

# 이미 빌드된 바이너리를 설치만 수행 (build/ 폴더에 있어야 함)
.\build.ps1 -Only cli -Skip cli -Install
```

**관리자 권한이 있는 경우** — 설치 경로를 선택할 수 있습니다.

```
[install] 관리자 권한이 있습니다. 'C:\Program Files\Dotori' 에 설치하시겠습니까? [Y/n]
```

- `Y` → `C:\Program Files\Dotori\dotori.exe`, 시스템 PATH(`Machine` 스코프)에 등록
- `N` → `%LOCALAPPDATA%\Dotori\dotori.exe`, 사용자 PATH(`User` 스코프)에 등록

**관리자 권한이 없는 경우** — 확인 없이 사용자 경로에 설치합니다.

```
설치 위치: %LOCALAPPDATA%\Dotori\dotori.exe
PATH 스코프: User
```

### 제거

```powershell
.\build.ps1 -Uninstall
```

설치된 위치(Program Files 또는 LOCALAPPDATA)를 자동으로 감지하여 실행 파일을 삭제하고 PATH 항목을 제거합니다.

---

## dotnet CLI로 직접 빌드

`build.sh`를 사용하지 않고 `dotnet` 명령으로 개별 컴포넌트를 빌드할 수 있습니다.

```bash
# 빌드 (출력: src/<프로젝트>/bin/)
dotnet build src/Dotori.Cli/Dotori.Cli.csproj

# 퍼블리시 — 현재 플랫폼
dotnet publish src/Dotori.Cli/Dotori.Cli.csproj -c Release -o build/cli

# 퍼블리시 — 크로스 컴파일 (linux-x64)
dotnet publish src/Dotori.Cli/Dotori.Cli.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -o build/cli-linux-x64

# 실행 (개발 중)
dotnet run --project src/Dotori.Cli/Dotori.Cli.csproj -- build --help

# 테스트
dotnet test

# 코드 포매팅
dotnet csharpier format .
# (최초 실행 전: dotnet tool install csharpier)
```

---

## 지원 Runtime Identifier (RID)

| RID | 대상 |
|-----|------|
| `linux-x64` | Linux amd64 |
| `linux-arm64` | Linux arm64 |
| `win-x64` | Windows amd64 |
| `win-arm64` | Windows arm64 |
| `osx-arm64` | macOS Apple Silicon |
| `osx-x64` | macOS Intel |

RID를 지정하지 않으면 빌드 호스트 플랫폼에 맞는 바이너리가 생성됩니다.

---

## 솔루션 구조

```
Dotori.slnx
├── src/
│   ├── Dotori.Cli/              ← CLI 진입점 (NativeAOT)
│   ├── Dotori.Core/             ← 핵심 빌드 시스템 로직
│   ├── Dotori.Grpc/             ← gRPC 스텁, 원격 실행기 및 인증
│   ├── Dotori.PackageManager/   ← 패키지 매니저
│   ├── Dotori.BuildServer/      ← 분산 빌드 코디네이터
│   ├── Dotori.Worker/           ← 분산 빌드 워커
│   └── Dotori.LanguageServer/   ← LSP 서버
├── deploy/registry/             ← npm 호환 레지스트리 배포 구성
└── tests/
    ├── Dotori.Tests.Build/
    ├── Dotori.Tests.Graph/
    ├── Dotori.Tests.Parsing/
    ├── Dotori.Tests.PackageManager/
    ├── Dotori.Tests.LanguageServer/
```
