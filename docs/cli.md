# CLI 사용법

dotori CLI의 모든 명령어와 옵션을 설명합니다.

## 전역 옵션

```
dotori --version    # 버전 출력
dotori --help       # 도움말 출력
```

---

## build — 빌드

프로젝트를 빌드합니다.

```bash
dotori build [옵션]
```

### 프로젝트 선택 옵션

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | `.dotori` 파일 또는 해당 파일이 있는 디렉토리 경로를 직접 지정 |
| `--all` | 하위 디렉토리에서 발견된 모든 프로젝트를 프롬프트 없이 빌드 |

`--project`를 생략하면 다음 순서로 프로젝트를 탐색합니다.

1. 현재 디렉토리의 `.dotori`
2. 상위 디렉토리를 git root까지 재귀 탐색
3. 하위 디렉토리 탐색 후 목록 수집
4. 하나면 자동 선택, 둘 이상이면 대화형 선택 프롬프트

### 빌드 구성 옵션

| 옵션 | 기본값 | 설명 |
|------|--------|------|
| `--release` | Debug | Release 구성으로 빌드 |
| `--target <타겟>` | 호스트 자동 감지 | 빌드 타겟 지정 (예: `macos-arm64`, `linux-x64`) |
| `--compiler <값>` | 자동 감지 | 컴파일러 선택 (아래 표 참조) |
| `--runtime-link <static\|dynamic>` | static | 런타임 링크 방식 |
| `--libc <glibc\|musl>` | glibc | C 런타임 라이브러리 (Linux 전용) |
| `--stdlib <libc++\|libstdc++>` | 플랫폼별 기본값 | C++ 표준 라이브러리 |
| `--jobs <N>` | CPU 코어 수 | 병렬 빌드 작업 수 |
| `--no-modules` | — | C++ Modules 지원 비활성화 |
| `--locked` | — | 의존성 선언과 lock 파일이 다르면 실패하고 lock 파일을 변경하지 않음 |
| `--offline` | — | 네트워크 접근 없이 설치된 패키지, lock 파일, 전역 tarball 캐시만 사용 |
| `--frozen` | — | `--locked --offline`과 동일 |
| `--dry-run` | — | 외부 빌드 명령과 스크립트를 실행하지 않고 예정된 컴파일·링크 명령 출력 |
| `--explain` | — | 소스 재컴파일과 재링크가 필요한 캐시 판단 사유 출력 |
| `--verbose` | — | 실행되는 compiler와 linker 명령을 출력 |
| `--timings` | — | 총 빌드 시간, 프로젝트 성공 수, 컴파일 수와 증분 재사용률 출력 |
| `--trace <경로>` | — | 버전이 명시된 JSON 빌드 보고서 저장 |

`--trace` 보고서는 target/configuration과 시작·종료 시각, 프로젝트별 소요 시간,
성공 여부, 전체·실행·재사용 컴파일 수, 링크 실행/재사용 상태를 포함합니다.
CI에서 빌드 성능 회귀와 캐시 효율을 수집할 때 사용할 수 있습니다.

```bash
dotori build --timings
dotori build --trace artifacts/dotori-build.json --timings
```

#### `--compiler` 값 목록

| 값 | 설명 |
|----|------|
| `msvc` | 최신 MSVC (vswhere로 자동 탐색) |
| `msvc-2022` | Visual Studio 2022 (버전 17.x) |
| `msvc-2019` | Visual Studio 2019 (버전 16.x) |
| `msvc-2017` | Visual Studio 2017 (버전 15.x) |
| `clang` | PATH의 clang++ |
| `apple-clang` | xcrun으로 얻는 Apple Clang (macOS 전용) |
| `brew-llvm` | Homebrew LLVM (`/opt/homebrew/opt/llvm/bin`) (macOS 전용) |
| `mise-llvm` | mise 관리 LLVM (`~/.local/share/mise/installs/llvm/`) |
| `clang++-18` | PATH에서 특정 버전 바이너리 이름 |
| `/usr/bin/clang++` | 절대 경로 |

우선순위: `--compiler` 플래그 > `.dotori` 파일의 `toolchain` 속성 > `CXX`/`CC` 환경 변수 > 자동 감지

`.dotori`의 `toolchain` 또는 `--compiler` 값이 `.json`으로 끝나면 compiler, linker, target triple, sysroot를 함께 지정하는 toolchain profile로 로드합니다. DSL에서 지정한 상대 profile 경로는 프로젝트 디렉토리를 기준으로 해석됩니다.

### 단일 파일 빌드 옵션

| 옵션 | 설명 |
|------|------|
| `--file <경로>` | 지정 파일만 컴파일 후 전체 링크 |
| `--file <경로> --no-link` | 컴파일만 수행 (링크 없음) |
| `--file <경로> --no-unity` | Unity Build가 켜진 경우에도 해당 파일을 단독으로 직접 컴파일 |

**Unity Build 환경에서의 `--file` 동작:**

- `--file src/foo.cpp` → `foo.cpp`가 포함된 unity batch 파일을 빌드 후 전체 링크
- `--file src/foo.cpp --no-link` → unity batch 파일만 컴파일 (`.obj` 생성)
- `--file src/foo.cpp --no-unity` → unity build를 무시하고 `foo.cpp` 단독 컴파일

**C++ Modules 파일 (`.cppm`, `.ixx`) 지정 시:**

- BMI만 재생성 (`--no-link` 암묵적 적용)
- 의존 BMI가 없으면 선행 BMI도 자동으로 함께 빌드

### 프로젝트 옵션 플래그

`.dotori` 파일에서 `option` 블록으로 선언된 옵션을 켜거나 끌 수 있습니다.

| 패턴 | 설명 |
|------|------|
| `--<옵션명>` | 해당 옵션을 활성화 (`default = false`인 옵션을 켤 때) |
| `--no-<옵션명>` | 해당 옵션을 비활성화 (`default = true`인 옵션을 끌 때) |

선언되지 않은 옵션 이름을 지정하면 오류가 출력됩니다.

```bash
# simd 옵션 활성화 (default=false 인 경우)
dotori build --simd

# simd 옵션 비활성화 (default=true 인 경우)
dotori build --no-simd

# 여러 옵션 동시 지정
dotori build --simd --experimental
```

### 분산 빌드 옵션

| 옵션 | 설명 |
|------|------|
| `--remote <URL>` | 분산 빌드 서버 주소 (예: `http://build-server:5100`) |

### 예시

```bash
# 현재 디렉토리 Debug 빌드
dotori build

# Release 빌드
dotori build --release

# iOS 크로스 컴파일 (macOS에서)
dotori build --target ios-arm64

# Android 크로스 컴파일
dotori build --target android-arm64

# Linux musl static 빌드 (완전 정적 바이너리)
dotori build --target linux-x64 --libc musl --runtime-link static

# WASM Emscripten 빌드
dotori build --target wasm32-emscripten

# 특정 소스 파일만 빌드
dotori build --file src/renderer.cpp

# 프로젝트 옵션 사용
dotori build --simd
dotori build --no-experimental

# 분산 빌드 서버 사용
dotori build --remote http://build-server:5100 --jobs 32
```

---

## run — 실행

프로젝트를 빌드하고 실행합니다. `type = executable` 프로젝트에서만 동작합니다.
`build` 명령과 동일한 증분 빌드 파이프라인을 사용하므로 custom build rule,
PCH, C++ modules, resource/assembler 입력, copy 블록과 pre/post-build 명령도
동일하게 적용됩니다. Git 패키지 의존성과 프로젝트 옵션 기본값도 실행 전에 해석합니다.

```bash
dotori run [옵션] [-- <실행 인수>]
```

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | 프로젝트 경로 지정 |
| `--release` | Release 빌드로 실행 |
| `--` | 이후 인수는 실행 파일에 전달 |

```bash
dotori run
dotori run --release
dotori run -- --config prod.json --verbose
```

---

## test — 테스트

테스트 대상 프로젝트를 빌드하고 실행합니다.
빌드 단계는 `build` 및 `run`과 동일한 공통 파이프라인을 사용합니다.
DAG에서 `type = test`인 프로젝트를 모두 찾아 순서대로 실행하며 하나라도
실패하면 즉시 해당 종료 코드를 반환합니다. test 프로젝트가 없는 기존
프로젝트에서는 하위 호환을 위해 executable 프로젝트를 대신 실행합니다.

```bash
dotori test [옵션]
```

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | 프로젝트 파일 또는 검색할 디렉터리 |
| `--all` | 디렉터리 아래의 모든 test 프로젝트를 검색하고 실행 |
| `--filter <패턴>` | 테스트 이름 필터 (글로브 패턴) |
| `--release` | Release 구성으로 테스트 |

```bash
dotori test
dotori test --filter "MyTest*"
```

---

## watch — 변경 감지 빌드

프로젝트 디렉터리를 재귀 감시하고 관련 파일이 바뀌면 증분 빌드를 다시 실행합니다.
여러 파일 이벤트는 debounce 구간 동안 하나로 합쳐지며 `.git`, `.dotori-cache`,
일반적인 빌드 출력 디렉터리와 오브젝트 파일 변경은 무시합니다.

```bash
dotori watch [watch 옵션] [build 옵션]
```

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | 감시할 프로젝트 파일 또는 디렉터리 |
| `--all` | 명시한 디렉터리 아래의 모든 프로젝트 감시 |
| `--debounce <ms>` | 연관 이벤트를 합칠 대기 시간 (기본값: `250`) |
| `--no-initial-build` | 시작 시 빌드하지 않고 첫 변경까지 대기 |

watch가 직접 처리하지 않는 옵션은 `build`에 그대로 전달합니다. 따라서 release,
target, compiler, remote, locked/offline, 프로젝트 선언 옵션 등을 함께 사용할 수 있습니다.

```bash
dotori watch --release --jobs 8
dotori watch --project workspace --all --target linux-x64
dotori watch --remote http://build-server:5100 --locked
```

Ctrl+C를 누르면 진행 중인 자식 빌드 프로세스까지 종료하고 감시를 끝냅니다.

---

## clean — 빌드 캐시 정리

```bash
dotori clean [옵션]
```

| 옵션 | 설명 |
|------|------|
| `--all` | 모든 프로젝트의 빌드 캐시 정리 |
| `--deps` | `deps/` 디렉토리(받아온 패키지)도 함께 삭제 |
| `--dry-run` | 삭제하지 않고 정리 대상만 출력 |

`.dotori-cache/` 디렉토리와 `output { }` 블록에서 지정한 경로의 복사본도 함께 삭제됩니다.
재귀 삭제는 프로젝트 내부의 출력 하위 디렉토리에만 허용됩니다. 프로젝트 루트 또는
프로젝트 외부 출력 경로는 안전을 위해 건너뛰고 경고를 출력합니다.
`--deps` 플래그를 추가하면 `deps/` 디렉토리도 삭제되며, 다음 빌드 시 패키지를 다시 받아옵니다.

```bash
dotori clean              # 빌드 캐시만 삭제
dotori clean --deps       # 빌드 캐시 + 받아온 패키지(deps/) 삭제
dotori clean --all --deps # 모든 프로젝트의 빌드 캐시 + deps/ 삭제
dotori clean --dry-run    # 실제 삭제 없이 대상 확인
```

---

## install / uninstall — 빌드 산출물 설치 및 제거

`install`은 이미 빌드된 실행 파일·라이브러리와 `headers { public ... }` 헤더를 prefix에 설치합니다. 빌드 산출물이 없으면 실패하므로 먼저 같은 target/config로 `dotori build`를 실행합니다.

```bash
dotori install --release --prefix /usr/local
dotori install --prefix /usr --destdir ./package-root
DESTDIR=./package-root dotori install --prefix /usr
dotori uninstall --release --prefix /usr/local
```

| 옵션 | 기본값 | 설명 |
|------|--------|------|
| `--project <경로>` | 자동 탐색 | 프로젝트 지정 |
| `--all` | — | 발견한 모든 프로젝트 처리 |
| `--release` | Debug | Release 산출물 선택 |
| `--target <타겟>` | 호스트 | 설치할 빌드 target 선택 |
| `--prefix <경로>` | Unix `/usr/local`, Windows `%ProgramFiles%/Dotori` | 논리 설치 prefix |
| `--destdir <경로>` | `DESTDIR` 환경 변수 | 패키지 제작용 staging root |
| `--dry-run` | — | 복사·삭제 없이 대상만 출력 |

실행 파일은 `bin/`, static/shared library는 `lib/`, 공개 헤더는 `include/`에 설치됩니다. 설치 파일 목록은 `<prefix>/share/dotori/install-manifests/`에 target/config별로 기록됩니다. `uninstall`은 이 manifest의 모든 경로가 prefix 내부인지 먼저 검증한 뒤 기록된 파일만 제거합니다.

`DOTORI_INSTALL_PREFIX`로 기본 prefix를 변경할 수 있습니다. 시스템 prefix에 설치할 때 필요한 권한은 운영체제 정책에 따라 사용자가 직접 부여해야 합니다.

---

## 패키지 관리 명령어

### add — 의존성 추가

```bash
dotori add @scope/name              # 최신 버전 추가
dotori add @scope/name@1.2.0        # 특정 버전 추가
dotori add <URL> --git              # git URL에서 추가
dotori add <경로> --path            # 로컬 경로에서 추가
```

`package.json`의 `"dependencies"` 또는 `"dotoriDependencies"` 필드를 수정합니다.

### remove — 의존성 제거

```bash
dotori remove <패키지명>
```

### update — 의존성 업데이트

```bash
dotori update              # 모든 의존성 업데이트
dotori update <패키지명>   # 특정 패키지만 업데이트
```

### list — 설치된 패키지 목록

```bash
dotori list
```

---

## format — 포매팅

`.dotori` 파일을 정규 형식으로 포매팅합니다.

```bash
dotori format [파일] [옵션]
```

| 옵션 | 설명 |
|------|------|
| `<파일>` | 포매팅할 `.dotori` 파일 또는 디렉토리 경로 (생략 시 현재 디렉토리 탐색) |
| `--project <경로>` | `.dotori` 파일 또는 프로젝트 디렉토리 경로 |
| `--check` | 파일을 수정하지 않고 포매팅 여부만 확인 (포매팅 필요 시 exit 1) |
| `--stdout` | 파일을 수정하지 않고 포매팅 결과를 stdout으로 출력 |

> **참고:** 포매터는 AST 기반으로 동작하므로 원본 주석(`(*...*)`)은 보존되지 않습니다.

```bash
# 현재 디렉토리의 .dotori 파일 포매팅
dotori format

# 특정 파일 포매팅
dotori format .dotori

# 포매팅 여부 확인 (CI 사용 가능)
dotori format --check

# stdout으로 출력
dotori format --stdout .dotori
```

---

## 정보 및 진단 명령어

### info — 프로젝트 정보

현재 프로젝트의 이름, 타입, 소스, 의존성 등을 출력합니다.

```bash
dotori info
dotori info --project ./lib
```

### graph — 의존성 그래프 출력

프로젝트 DAG와 패키지 의존성 그래프를 텍스트 형식으로 출력합니다.

```bash
dotori graph
```

### check — 유효성 검사

`.dotori` 파일의 문법 오류, 이식성 경고 등을 검사합니다.

```bash
dotori check
```

**검사 항목:**

- DSL 문법 오류
- `path` 의존성 대상 파일 존재 여부
- 조건 블록 없이 `compile-flags`/`link-flags` 사용 시 이식성 경고
  - 예: `warning: compile-flags used without a compiler condition (e.g. [msvc], [clang]) — may reduce portability`

### targets — 지원 타겟 목록

현재 호스트에서 빌드 가능한 타겟 목록을 출력합니다.

```bash
dotori targets
```

### toolchain — 감지된 툴체인 정보

설치된 컴파일러, SDK, 링커 등의 정보를 출력합니다.

```bash
dotori toolchain
```

### includes — 헤더 파일 포함 트리 출력

프로젝트 소스 파일의 `#include` 의존 트리를 출력합니다.
컴파일러 없이 순수 텍스트 파싱으로 동작하므로 빌드 없이 사용 가능합니다.

```bash
dotori info includes
dotori info includes --file src/main.cpp
dotori info includes --no-system
dotori info includes --depth 3
```

**옵션:**

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | `.dotori` 파일 또는 디렉토리 경로 |
| `--file <경로>` | 특정 소스 파일 하나만 분석 (생략 시 모든 소스 파일) |
| `--no-system` | 시스템 헤더(`<...>` 형식) 를 하위 트리 없이 표시 |
| `--depth <N>` | 최대 출력 깊이 (기본값: 무제한) |

**출력 예시:**

```
src/main.cpp
├── include/mylib.h
│   ├── include/mylib/types.h
│   └── include/mylib/config.h
└── <stdio.h>

src/util.cpp
├── include/mylib.h  [↑ already shown]
└── <string.h>
```

> **참고:** 전처리기 조건문(`#ifdef`, `#if` 등)을 평가하지 않으므로
> 조건부로 포함되는 헤더도 모두 나열됩니다.

---

## Language Server 명령어

IDE 플러그인이 내부적으로 호출합니다.

```bash
dotori lsp                              # stdio transport (LSP 3.17)
dotori lsp --log-file /tmp/dotori.log   # 디버그 로그 출력
```

완성, hover, 정의 이동, 문서 심볼, 진단 외에 선언된 빌드 옵션의 참조 찾기와
이름 바꾸기, 문서 포매팅을 지원합니다.

---

## setup ide — IDE 개발 환경 구성

CMake 프로젝트와 preset, `compile_commands.json`, `.clangd`, 에디터 작업 파일을
한 번에 생성합니다.

```bash
dotori setup ide                         # VS Code + Zed 설정과 공통 파일 생성
dotori setup ide --ide vscode
dotori setup ide --ide zed
dotori setup ide --ide clion
dotori setup ide --ide rider
dotori setup ide --ide visual-studio
dotori setup ide --ide all
dotori setup ide --target linux-x64 --compiler clang
dotori setup ide --ide vscode --check
dotori setup ide --ide vscode --dry-run
```

`auto`는 CMake와 VS Code/Zed 작업 파일을 생성합니다. CLion과 Rider는
`CMakeLists.txt`와 `CMakePresets.json`을 사용합니다. `visual-studio`는 `.sln`과
`.vcxproj`를 생성하며, Windows에서 `all`은 CMake와 Visual Studio 파일을 모두 생성합니다.
`--check`는 생성 파일의 누락·노후·변경을 검사하고 문제가 있으면 종료 코드 1을
반환합니다. `--dry-run`은 파일을 쓰지 않고 생성 또는 갱신될 파일을 표시합니다.

환경 전체를 점검하거나 자동으로 복구하려면 `doctor`를 사용합니다.

```bash
dotori doctor --ide vscode
dotori doctor --ide rider --project path/to/.dotori
dotori doctor --ide visual-studio --fix
```

`doctor`는 CMake, Ninja, clangd, 대상 컴파일러와 IDE 생성 파일을 함께 검사합니다.
선택한 IDE에 필수인 도구 또는 생성 파일에 문제가 있으면 1을 반환하며, `--fix`는
`setup ide`를 실행해 생성 파일을 다시 만듭니다.

프로젝트 파일이 바뀔 때 IDE 산출물을 자동 갱신할 수도 있습니다.

```bash
dotori watch --ide --project . --target linux-x64 --compiler clang
```

---

## export grammar — 에디터 문법 파일 내보내기

다양한 에디터용 Dotori DSL 구문 강조 파일을 생성합니다.
`DotoriGrammarDefinition.cs`의 중앙 키워드 정의에서 자동으로 생성되므로, DSL 문법이 변경되면 이 명령을 재실행해 파일을 갱신하면 됩니다.

```bash
dotori export grammar                                                    # TextMate 형식 (기본, stdout)
dotori export grammar --format textmate                                  # VSCode / JetBrains
dotori export grammar --format vim                                       # Vim / Neovim
dotori export grammar --format emacs                                     # Emacs
dotori export grammar --format sublime                                   # Sublime Text
dotori export grammar --format zed                                       # Zed / Helix / Neovim (nvim-treesitter)

dotori export grammar --output <경로>                                    # 파일로 저장 (기본: stdout)
```

### 지원 형식

| `--format` | 파일 형식 | 대상 에디터 |
|------------|----------|------------|
| `textmate` (기본) | `.tmLanguage.json` | VSCode, JetBrains/IntelliJ, Sublime Text, TextMate, Atom |
| `vim` | `.vim` | Vim, Neovim |
| `emacs` | `.el` | Emacs |
| `sublime` | `.sublime-syntax` | Sublime Text 3+ |
| `zed` | `highlights.scm` | Zed, Helix, Neovim (nvim-treesitter) |

> **Zed/Tree-sitter 주의**: `highlights.scm`은 별도 Tree-sitter 파서(`grammar.js`)가 빌드되어 있어야 동작합니다.

### 빌드 파일 갱신 예시

```bash
# VSCode / JetBrains TextMate 문법 갱신
dotori export grammar --format textmate --output build/vscode/syntaxes/dotori.tmLanguage.json

# Zed highlights.scm 갱신
dotori export grammar --format zed --output build/zed/languages/dotori/highlights.scm
```

---

## export build-system — 빌드 시스템 파일 생성

IDE 통합을 위해 `.dotori` 파일에서 외부 빌드 시스템 파일을 생성합니다.
생성된 파일은 `git commit` 대상이 아니며, 필요할 때마다 명령을 재실행하여 재생성합니다.

```bash
dotori export build-system                                  # CMakeLists.txt (기본)
dotori export build-system --format cmake                   # CMakeLists.txt
dotori export build-system --format meson                   # meson.build
dotori export build-system --format vcxproj                 # .sln + .vcxproj + .vcxproj.filters (Visual Studio)
dotori export build-system --format pbxproj                 # *.xcodeproj/project.pbxproj (Xcode)
dotori export build-system --format ninja                   # build.ninja
dotori export build-system --format makefile                # Makefile

dotori export build-system --output <경로>                  # 출력 디렉토리 지정
dotori export build-system --config debug                   # debug 설정만 생성
dotori export build-system --config release                 # release 설정만 생성
dotori export build-system --config both                    # debug + release 모두 (기본)
dotori export build-system --target windows-x64             # 특정 타겟 플랫폼
```

### 지원 형식

| `--format` | 생성 파일 | 용도 |
|------------|----------|------|
| `cmake` (기본) | `CMakeLists.txt`, `CMakePresets.json` | CLion, Visual Studio, VS Code (CMake Tools), CMake 직접 사용 |
| `meson` | `meson.build` | GNOME Builder, CLion (Meson), Meson 직접 사용 |
| `vcxproj` | `<name>.sln`, `<name>.vcxproj`, `<name>.vcxproj.filters` | Visual Studio |
| `pbxproj` | `<name>.xcodeproj/project.pbxproj` | Xcode (macOS/iOS) |
| `ninja` | `build.ninja` (또는 `build.debug.ninja` / `build.release.ninja`) | Ninja 직접 사용 |
| `makefile` | `Makefile` | GNU Make 직접 사용 |

### 의존성 처리

| 의존성 종류 | CMake | Meson | vcxproj/pbxproj | Ninja/Makefile |
|------------|-------|-------|-----------------|----------------|
| 경로 (`path:`) | `add_subdirectory()` | `subproject()` | `ProjectReference` | 주석 안내 |
| Git (`git:`) | `FetchContent` | 주석 (wrap 파일 직접 생성) | 주석 안내 | 주석 안내 |
| 버전 (`"x.y.z"`) | 주석 안내 | 주석 안내 | 주석 안내 | 주석 안내 |

### 사용 예시

```bash
# CLion / VS Code 통합
dotori export build-system --format cmake

# Visual Studio 2022
dotori export build-system --format vcxproj --target windows-x64

# Xcode
dotori export build-system --format pbxproj --target macos-arm64
```

CMake 형식은 선택한 타겟과 구성에 맞는 configure/build/test preset도 함께 생성합니다.
예를 들어 `--target linux-x64 --config both`는 `linux-x64-debug`와
`linux-x64-release` preset을 만듭니다. 생성된 프로젝트는 다음과 같이 IDE 밖에서도
동일하게 실행할 수 있습니다.

`--compiler`를 명시하면 탐지된 C/C++ 컴파일러 절대 경로도 preset의
`CMAKE_C_COMPILER`와 `CMAKE_CXX_COMPILER`에 기록되어 IDE와 CLI가 같은
툴체인을 사용합니다.

```bash
cmake --preset linux-x64-debug
cmake --build --preset linux-x64-debug
ctest --preset linux-x64-debug
```

멀티 프로젝트를 `--output`으로 한 디렉터리에 내보낼 때는 프로젝트 사이의 상대
디렉터리 구조가 유지되므로, 각 프로젝트의 `CMakeLists.txt`가 서로 덮어쓰이지 않습니다.

---

## export ide — IDE 통합 파일 생성

clangd 등 C++ IntelliSense 도구와 통합하기 위한 `compile_commands.json`과 `.clangd` 설정 파일을 생성합니다.

```bash
dotori export ide                                     # 호스트 기본 타겟
dotori export ide --compiler=brew-llvm                # brew LLVM 컴파일러 사용
dotori export ide --target linux-x64                  # 타겟 명시
dotori export ide --output ./                         # compile_commands.json 출력 경로 지정
dotori export ide --no-clangd                         # .clangd 생성 생략
dotori export ide --clangd-output /path/to/.clangd    # .clangd 출력 경로 지정
```

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | `.dotori` 파일 또는 디렉토리 경로 |
| `--all` | 모든 프로젝트 포함 |
| `--release` | Release 설정 사용 |
| `--target <타겟>` | 빌드 타겟 (예: `macos-arm64`, `linux-x64`) |
| `--compiler <이름>` | 컴파일러 지정 (`msvc`, `clang`, `brew-llvm` 등) |
| `--runtime-link <모드>` | 런타임 링크 모드 (`static`, `dynamic`) |
| `--libc <라이브러리>` | C 런타임 라이브러리 (`glibc`, `musl`) |
| `--stdlib <라이브러리>` | C++ 표준 라이브러리 (`libc++`, `libstdc++`) |
| `--output <경로>` | `compile_commands.json` 출력 경로 (기본값: 현재 디렉토리) |
| `--clangd` / `--no-clangd` | `.clangd` 파일 생성 여부 (기본값: `true`) |
| `--clangd-output <경로>` | `.clangd` 출력 경로 (기본값: `compile_commands.json`과 같은 디렉토리) |

멀티 프로젝트 환경에서는 모든 프로젝트 항목이 하나의 파일에 병합됩니다.

생성되는 `.clangd` 파일 예시 (`--compiler=brew-llvm` 사용 시):

```yaml
# Generated by dotori
CompileFlags:
  Compiler: "/opt/homebrew/opt/llvm/bin/clang++"
```

> **참고**: Zed, VSCode, CLion 등에서 AppleClang이 아닌 다른 컴파일러(brew-llvm 등)를 사용할 경우, clangd가 해당 컴파일러의 implicit include path를 올바르게 인식하지 못할 수 있습니다. `.clangd` 파일의 `CompileFlags.Compiler` 설정이 이 문제를 해결합니다.

---

## generate-compile-commands — compile_commands.json 생성 (deprecated)

> **deprecated**: `dotori export ide` 사용을 권장합니다.

clangd 등 C++ IntelliSense 도구와 통합하기 위한 `compile_commands.json`과 `.clangd`를 생성합니다.

```bash
dotori generate-compile-commands                      # 호스트 기본 타겟
dotori generate-compile-commands --target linux-x64   # 타겟 명시
dotori generate-compile-commands --output ./          # 출력 경로 지정
dotori generate-compile-commands --no-clangd          # .clangd 생성 생략
```

멀티 프로젝트 환경에서는 모든 프로젝트 항목이 하나의 파일에 병합됩니다.

---

## 레지스트리 명령어

dotori는 GitHub Packages, GitLab Package Registry, JFrog Artifactory, Verdaccio 등 **npm 프로토콜 호환 레지스트리**를 지원합니다.

### login / logout — 인증 토큰 관리

```bash
# 토큰 저장 (--token 생략 시 프롬프트)
dotori login --registry https://npm.pkg.github.com --token ghp_...
dotori login --registry https://npm.pkg.github.com    # 대화형

# 토큰 삭제
dotori logout --registry https://npm.pkg.github.com
```

토큰은 `~/.dotori/config.toml`에 저장됩니다.

`.npmrc` 파일도 지원합니다:

```ini
# 프로젝트 .npmrc 또는 ~/.npmrc
@myorg:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=ghp_TOKEN
```

### publish — 패키지 배포

`package.json`이 있어야 합니다. 패키지 이름은 `@scope/name` 형식이어야 합니다.
파일은 경로 이름순으로 정렬되고 tar 소유자·권한·수정 시각이 정규화되므로,
동일한 파일 내용은 운영체제와 원본 timestamp에 관계없이 동일한 `.tgz` 바이트와
SHA-256 해시를 생성합니다. archive는 임시 파일에 완성한 뒤 원자적으로 교체됩니다.

```bash
dotori publish
dotori publish --registry https://npm.pkg.github.com

# 레지스트리 대신 로컬 디렉토리에 배포
dotori publish --prefix ./local-repo
```

| 옵션 | 설명 |
|------|------|
| `--project <경로>` | `package.json` 또는 프로젝트 디렉토리 경로 |
| `--registry <URL>` | 배포할 레지스트리 URL (기본값: 설정 파일의 기본 레지스트리) |
| `--prefix <경로>` | 레지스트리 대신 로컬 디렉토리에 배포 |
| `--dry-run` | 아카이브만 생성하고 업로드·복사 없이 종료 |

`--registry`와 `--prefix`는 동시에 사용할 수 없습니다.

### search — 패키지 검색

```bash
dotori search <키워드>
dotori search <키워드> --registry https://npm.pkg.github.com
```

---

## 환경 변수

빌드 시 다음 환경 변수가 자동으로 설정됩니다. `pre-build`, `post-build` 스크립트와 DSL 문자열에서 참조할 수 있습니다.

| 변수명 | 예시 값 | 설명 |
|--------|---------|------|
| `DOTORI_TARGET` | `macos-arm64` | 전체 빌드 타겟 ID |
| `DOTORI_CONFIG` | `debug`, `release` | 빌드 구성 |
| `DOTORI_PLATFORM` | `windows`, `linux`, `macos` | 타겟 플랫폼 |
| `DOTORI_ARCH` | `x64`, `arm64`, `wasm32` | 타겟 CPU 아키텍처 |
| `DOTORI_PROJECT_DIR` | `/path/to/project` | 프로젝트 루트 절대 경로 |
| `DOTORI_OUTPUT_DIR` | `.dotori-cache/obj/...` | 링크 결과물 위치 |

패키지 tarball은 SRI integrity별로 `~/.dotori/package-cache/`에 저장되어 여러 프로젝트에서 재사용됩니다. `DOTORI_PACKAGE_CACHE_DIR` 환경 변수로 위치를 변경할 수 있습니다. `--offline`은 프로젝트의 `deps/`가 비어 있어도 lock 파일에 기록된 tarball이 이 캐시에 있으면 설치할 수 있으며, 캐시가 없으면 네트워크 요청 없이 실패합니다.

---

## 캐시 디렉토리 구조

```
.dotori-cache/               ← 프로젝트 로컬 캐시
├── hashes.db                ← 증분 빌드용 해시 DB
├── bmi/                     ← C++ Modules BMI 파일
│   ├── MyLib.ifc            #   MSVC
│   └── MyLib.pcm            #   Clang
├── unity/                   ← Unity Build 임시 파일
│   └── unity_0.cpp
└── obj/                     ← 타겟별 오브젝트 파일
    ├── macos-arm64-debug/
    ├── macos-arm64-release/
    ├── linux-x64-glibc-static-debug/
    └── wasm32-emscripten-release/

deps/                        ← 받아온 패키지 (dotori clean --deps 로 삭제)
├── fmtlib/
│   └── fmt/
│       ├── package.json
│       └── .dotori-cache/     # 이 프로젝트에서만 사용하는 빌드 캐시
└── gabime/
    └── spdlog/
        ├── package.json
        └── .dotori-cache/

dotori-lock.json             ← 의존성 버전 고정 파일 (git에 커밋)
```

> **참고**: 패키지는 전역 캐시 없이 각 프로젝트의 `deps/` 폴더에 독립적으로 저장됩니다.
> 경로는 `deps/<scope>/<name>/` 구조 (`@fmtlib/fmt` → `deps/fmtlib/fmt/`).
