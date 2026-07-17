# .dotori 파일 구성

`.dotori`는 dotori의 빌드 설정 파일입니다. 디렉토리당 하나만 허용됩니다.
패키지 배포 메타데이터(이름, 버전, 의존성)는 `package.json`에 별도로 작성합니다.

---

## 기본 구조

```
project <이름> {
    # 프로젝트 속성
}
```

라이브러리를 배포하려면 같은 디렉토리에 `package.json`을 작성합니다:

```json
{
  "name": "@scope/my-lib",
  "version": "1.0.0",
  "description": "My C++ library"
}
```

주석은 `# ...` 형식으로 작성합니다.

---

## project 블록

### type — 프로젝트 타입

```
type = executable       # 실행 파일
type = static-library   # 정적 라이브러리
type = shared-library   # 공유 라이브러리 (.dll/.so/.dylib)
type = header-only      # 헤더 전용 라이브러리
```

### std — C++ 표준

```
std = c++17
std = c++20
std = c++23    # 기본값
```

### description — 설명

```
description = "My awesome application"
```

---

## sources 블록 — 소스 파일

글로브 패턴으로 소스 파일을 포함/제외합니다.

```
sources {
    include "src/**/*.cpp"
    include "src/**/*.cc"
    include "src/**/*.m"               # Objective-C (Apple 플랫폼)
    include "src/**/*.mm"              # Objective-C++ (Apple 플랫폼)
    exclude "src/platform/**/*.cpp"    # 특정 경로 제외
}
```

---

## modules 블록 — C++ Modules

```
modules {
    include "src/**/*.cppm"
    include "src/**/*.ixx"
    export-map = true    # module-map.json 자동 생성, 기본: false
}
```

`export-map = true`로 설정하면 `.dotori-cache/obj/<타겟>/bmi/module-map.json`이 생성됩니다.

---

## headers 블록 — 헤더 경로

```
headers {
    public  "include/"    # 의존하는 프로젝트에도 노출
    private "src/"        # 이 프로젝트 내부에서만 사용
}
```

---

## defines 블록 — 전처리기 매크로

```
defines {
    "MY_FEATURE_ENABLED"
    "VERSION_MAJOR=1"
    "VERSION_STRING=\"1.0.0\""
}
```

---

## links 블록 — 링크 라이브러리

```
links {
    "pthread"
    "dl"
    "m"
}
```

---

## frameworks 블록 — Apple 프레임워크

macOS/iOS/tvOS/watchOS 전용입니다.

```
frameworks {
    "Foundation"
    "Metal"
    "MetalKit"
}
```

---

## compile-flags / link-flags — 사용자 정의 플래그

dotori DSL로 제어하기 어려운 컴파일러·링커 옵션을 직접 지정합니다.
이식성을 위해 **조건 블록과 함께 사용**하는 것을 권장합니다.

```
[msvc] {
    compile-flags { "/arch:AVX2" "/fp:fast" }
    link-flags    { "/SUBSYSTEM:WINDOWS" "/OPT:REF" }
}

[clang] {
    compile-flags { "-march=native" "-ffast-math" }
}

[clang.linux] {
    link-flags { "-Wl,--as-needed" "-Wl,--gc-sections" }
}

[release.msvc] {
    compile-flags { "/Oi" "/Ot" }
    link-flags    { "/LTCG" }
}
```

조건 없이 사용하면 `dotori check`에서 이식성 경고가 출력됩니다.

플래그는 dotori가 생성한 플래그 **뒤에** 추가됩니다. 조건 블록 간에는 **누적**됩니다 (덮어쓰기 아님).

---

## runtime-link — 런타임 링크 방식

```
runtime-link = static     # 기본값
runtime-link = dynamic
```

플랫폼별 강제 규칙:

| 플랫폼 | 강제 값 |
|--------|---------|
| UWP | dynamic 강제 |
| iOS / tvOS / watchOS | static 강제 |
| WASM | static 고정 (설정 무시) |

---

## optimize — 최적화 수준

```
optimize = none     # 최적화 없음, Debug 기본
optimize = speed    # 속도 최적화 (-O2 / /O2)
optimize = size     # 크기 최적화 (-Os / /O1)
optimize = full     # 최대 최적화 (-O3 / /Ox /GL)
```

---

## debug-info — 디버그 정보

```
debug-info = full       # 전체 디버그 정보 (-g / /Zi)
debug-info = minimal    # 최소 정보 (-gline-tables-only / /Z7)
debug-info = none       # 디버그 정보 없음
```

---

## lto — 링크 타임 최적화

```
lto = true
lto = false    # 기본값
```

MSVC에서는 `/GL` + `/LTCG`, Clang에서는 `-flto`가 적용됩니다.

---

## warnings — 경고 수준

```
warnings = none
warnings = default    # 기본값
warnings = all        # /W4 / -Wall -Wextra
warnings = extra
```

## warnings-as-errors

```
warnings-as-errors = true
warnings-as-errors = false    # 기본값
```

---

## toolchain — 툴체인 명시적 선택

프로젝트 파일에서 툴체인을 지정합니다. `--compiler` CLI 플래그보다 낮은 우선순위이며, `CXX`/`CC` 환경 변수보다 높은 우선순위입니다.

```
toolchain = "brew-llvm"       # macOS: Homebrew LLVM clang++
toolchain = "apple-clang"     # macOS: xcrun Apple Clang
toolchain = "mise-llvm"       # mise 관리 LLVM
toolchain = "msvc"            # 최신 MSVC (Windows)
toolchain = "msvc-2022"       # Visual Studio 2022 (버전 17.x)
toolchain = "msvc-2019"       # Visual Studio 2019 (버전 16.x)
toolchain = "msvc-2017"       # Visual Studio 2017 (버전 15.x)
toolchain = "clang"           # PATH의 clang++
```

| 값 | 설명 |
|----|------|
| `apple-clang` | xcrun으로 얻는 Apple Clang (macOS 전용) |
| `brew-llvm` | Homebrew LLVM (`/opt/homebrew/opt/llvm/bin`) (macOS 전용) |
| `mise-llvm` | mise 관리 LLVM (`~/.local/share/mise/installs/llvm/`) |
| `clang` | PATH의 clang++ |
| `msvc` | 최신 MSVC (vswhere 자동 탐색) |
| `msvc-2022` | Visual Studio 2022 (버전 17.x) |
| `msvc-2019` | Visual Studio 2019 (버전 16.x) |
| `msvc-2017` | Visual Studio 2017 (버전 15.x) |
| 바이너리 이름 | PATH 검색 (예: `"clang++-18"`) |
| 절대 경로 | 직접 지정 (예: `"/opt/homebrew/opt/llvm/bin/clang++"`) |
| `*.json` | 프로젝트 디렉토리 기준 toolchain profile 파일 |

compiler와 linker, target triple, sysroot를 함께 고정해야 하는 교차 빌드는 JSON profile을 사용합니다.

```text
toolchain = "toolchains/linux-arm64.json"
```

```json
{
  "kind": "clang",
  "compiler": "bin/clang++",
  "linker": "bin/clang++",
  "targetTriple": "aarch64-unknown-linux-musl",
  "sysroot": "sysroot",
  "assembler": {
    "nasm": "bin/nasm",
    "gas": "bin/aarch64-linux-musl-as"
  }
}
```

profile 안의 상대 경로는 JSON 파일 위치를 기준으로 해석됩니다. `compiler`와 `linker`에 디렉토리가 없는 실행 파일 이름을 쓰면 `PATH`에서 검색됩니다. 지원 필드는 `kind` (`clang` 또는 `emscripten`), `compiler`, `linker`, `targetTriple`, 선택적 `sysroot`, `appleSdk`, `assembler` (`nasm`, `yasm`, `gas`, `masm`)입니다. MSVC는 Windows SDK 메타데이터가 필요하므로 profile 대신 `msvc-*` 자동 감지를 사용합니다.

---

## c-as-cpp — C 소스 파일을 C++로 컴파일

```
c-as-cpp = true
c-as-cpp = false    # 기본값
```

`.c` 확장자 파일을 C++ 모드로 컴파일한다.

- **Clang / Emscripten**: 해당 파일 앞에 `-x c++` 플래그를 삽입한다.
- **MSVC**: `/Tp<파일>` 옵션으로 해당 파일을 C++로 강제 컴파일한다.
- `.cpp`, `.cxx` 등 C++ 확장자 파일에는 영향을 주지 않는다.

---

## objc-arc — Objective-C ARC 활성화 (Apple 플랫폼)

```
objc-arc = true
objc-arc = false    # 기본값
```

`.m` / `.mm` 파일 컴파일 시 `-fobjc-arc` 플래그를 추가한다.  
ARC(Automatic Reference Counting)를 사용할 때 설정한다.

- Apple 플랫폼(macOS / iOS / tvOS / watchOS)에서만 의미가 있다.
- C++ 파일(`.cpp` 등)에는 영향을 주지 않는다.

---

## objc-as-objcpp — Objective-C 소스 파일을 Objective-C++로 컴파일 (Apple 플랫폼)

```
objc-as-objcpp = true
objc-as-objcpp = false    # 기본값
```

`.m` 확장자 파일을 Objective-C++ 모드(`-x objective-c++`)로 컴파일한다.  
`.mm` 파일은 항상 Objective-C++로 컴파일되므로 이 옵션의 영향을 받지 않는다.

---

## libc / stdlib — 런타임 라이브러리 선택 (Linux)

```
libc   = glibc      # 기본값
libc   = musl       # musl + static = 완전 정적 바이너리

stdlib = libstdc++  # GCC STL, 기본값
stdlib = libc++     # LLVM STL
```

---

## 플랫폼별 옵션

### Android

```
android-api-level = 26    # 기본: 21
```

### macOS

```
macos-min = "12.0"
```

### iOS

```
ios-min = "15.0"
```

### tvOS / watchOS

```
tvos-min    = "15.0"
watchos-min = "7.0"
```

### Emscripten (WASM)

```
[wasm.emscripten] {
    emscripten-flags { "-sUSE_SDL=2" "-sALLOW_MEMORY_GROWTH" }
}
```

---

## pch 블록 — 프리컴파일 헤더

```
pch {
    header  = "src/pch.h"
    source  = "src/pch.cpp"
    modules = false    # Modules와 PCH 동시 사용 시 경고 출력
}
```

---

## unity-build 블록 — Unity Build

여러 소스 파일을 하나로 묶어 컴파일 속도를 향상시킵니다.

```
unity-build {
    enabled    = true
    batch-size = 8          # 한 번에 묶을 파일 수, 기본: 8
    exclude {
        "src/main.cpp"      # Unity Build에서 제외할 파일
        "src/generated/**"
    }
}
```

C++ Modules 소스 파일(`.cppm`, `.ixx`)은 자동으로 Unity Build에서 제외됩니다.

---

## output 블록 — 출력 디렉토리

빌드 결과물을 지정 경로로 복사합니다. 경로는 `.dotori` 파일 기준 상대 경로입니다.

```
output {
    binaries  = "bin/"     # exe, dll/so/dylib 복사 위치
    libraries = "lib/"     # .lib(import), .a(static) 복사 위치
    symbols   = "pdb/"     # .pdb, .dSYM 복사 위치
}
```

조건 블록과 함께 사용 가능합니다.

```
[release] {
    output { binaries = "dist/" }
}
```

---

## pre-build / post-build 블록 — 빌드 전/후 스크립트

```
pre-build {
    "scripts/gen_version.sh"
    "scripts/download_assets.sh --quiet"
}

post-build {
    "scripts/sign.sh --cert certs/release.p12"
}
```

- 각 문자열은 실행할 명령어입니다.
- 순서대로 실행되며 종료 코드 ≠ 0이면 빌드 실패로 처리됩니다.
- 실행 디렉토리는 프로젝트 루트 (`.dotori` 위치)입니다.
- stdout/stderr가 dotori 콘솔로 연결됩니다.
- `DOTORI_TARGET`, `DOTORI_CONFIG`, `DOTORI_PROJECT_DIR`, `DOTORI_OUTPUT_DIR` 환경 변수가 전달됩니다.

조건 블록과 함께 사용 가능합니다.

```
[windows] {
    pre-build { "scripts\\gen_version.bat" }
}
```

---

## copy 블록 — 파일 복사

빌드 완료 후 파일 또는 폴더를 지정 경로로 복사합니다.
SHA-256 해시 기반 증분 검사를 수행하여 **변경된 파일만** 복사합니다.

```
copy {
    from "assets/**/*"      to "bin/assets/"
    from "config/*.json"    to "bin/"
    from "shaders/"         to "bin/shaders/"
}
```

### 경로 규칙

| 항목 | 설명 |
|------|------|
| `from` — 절대 경로 | `/`로 시작(Unix) 또는 드라이브 문자(`C:\`)로 시작하면 절대 경로 |
| `from` — 상대 경로 | 프로젝트 루트 기준. glob 패턴(`**`, `*`, `?`) 또는 디렉토리 경로 허용 |
| `to` — 절대/상대 경로 | 동일 규칙. 항상 디렉토리로 해석. 존재하지 않으면 자동 생성 |
| 디렉토리 지정 | `from "shaders/"` → `shaders/` 하위 파일 구조를 `to/` 아래에 그대로 유지 |
| glob 지정 | 와일드카드 앞의 경로를 root로 삼아 상대 구조 유지. `from "a/**/*"` → `a/sub/f.png` → `to/sub/f.png` |
| 변경 감지 | 소스 파일 SHA-256 해시가 이전과 동일하면 복사 스킵 |

### clean 동작

`dotori clean` 실행 시 `.dotori-cache/copy-manifest.json`에 기록된 복사 파일만 개별 삭제합니다 (대상 디렉토리 전체를 삭제하지 않음).

### 조건 블록과 조합

```
[windows] {
    copy {
        from "dlls/windows/*.dll"  to "bin/"
    }
}

[release] {
    copy {
        from "data/release/**/*"  to "dist/data/"
    }
}
```

### 환경변수 보간

`from`과 `to` 값 모두 `${VAR}` 보간이 적용됩니다.

```
copy {
    from "${ASSETS_DIR}/**/*"  to "bin/assets/"
}
```

---

## assembler 블록 — 외부 어셈블러

`.asm` / `.s` / `.S` 파일을 NASM, YASM, GAS, MASM 등 외부 어셈블러로 컴파일합니다.
출력 `.o`/`.obj` 파일은 일반 C++ 소스와 함께 링커에 전달됩니다.

```
assembler {
    tool = nasm          # nasm | yasm | gas | as | masm | auto (기본값)
    format = "elf64"     # nasm/yasm 전용 출력 포맷 (생략 시 플랫폼에서 자동 감지)
    include "src/**/*.asm"
    exclude "src/test/**/*.asm"
    flags { "-g" }       # 추가 어셈블러 플래그
    defines { "DEBUG" }  # 전처리기 define (-D / /D)
}
```

### tool 값

| 값 | 어셈블러 | 실행파일 |
|----|---------|---------|
| `nasm` | NASM | `nasm` |
| `yasm` | YASM | `yasm` |
| `gas` / `as` | GNU Assembler | `as` |
| `masm` | Microsoft Macro Assembler | `ml64.exe` / `ml.exe` |
| `auto` | 자동 선택 (기본값) | MSVC 툴체인 → masm, 그 외 → gas |

### format 자동 감지

`format`을 생략하면 타겟 플랫폼에서 자동 감지합니다 (NASM/YASM 전용, GAS/MASM은 무시).

| 타겟 | 자동 감지 포맷 |
|------|--------------|
| `linux-*`, `android-*` | `elf64` |
| `windows-*`, `uwp-*` | `win64` |
| `macos-*` | `macho64` |

GAS와 MASM은 빌드 환경에서 포맷을 자동으로 결정하므로 `-f` 플래그가 필요 없습니다.

### 플랫폼별 분기

`assembler` 블록은 일반 `ProjectItem`이므로 기존 조건 블록 안에서 사용할 수 있습니다.
해당 조건 블록이 일치하지 않으면 해당 플랫폼에서는 어셈블러가 동작하지 않습니다.

```
[windows] {
    assembler {
        tool = masm
        include "src/windows/**/*.asm"
    }
}

[linux] {
    assembler {
        tool = nasm
        format = "elf64"
        include "src/linux/**/*.asm"
    }
}

[macos] {
    assembler {
        tool = nasm
        format = "macho64"
        include "src/macos/**/*.asm"
    }
}

# wasm, ios 등 — assembler 블록 없음 → 어셈블러 미사용
```

공통 소스와 플랫폼별 오버라이드를 혼합할 수도 있습니다.
여러 `assembler` 블록의 `include`/`exclude`/`flags`/`defines`는 누적되고,
`tool`/`format`은 뒤에 오는 블록이 덮어씁니다.

```
assembler {
    include "src/common/**/*.asm"   # 모든 플랫폼 공통 소스
}

[windows] {
    assembler {
        tool = masm                 # tool 오버라이드
        include "src/win/**/*.asm"  # 추가 소스 누적
    }
}
```

---

## option 블록 — 선택적 빌드 기능

프로젝트에 이름 있는 옵션을 선언합니다. CLI 플래그(`--옵션명` / `--no-옵션명`)로 켜고 끌 수 있습니다.

```
option simd {
    default     = true                # 기본 활성 여부 (필수)
    defines     { "SIMD_ENABLED" "SIMD_VER=2" }
    dependencies {
        simd-utils = { path = "../simd-utils" }
    }
}

option experimental {
    default = false
    defines { "EXPERIMENTAL" }
}
```

| 속성 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `default` | bool | ✅ | 기본 활성 여부 (`true` / `false`) |
| `defines` | string 목록 | — | 옵션 활성 시 추가할 전처리기 정의 |
| `dependencies` | 의존성 블록 | — | 옵션 활성 시 추가할 의존성 |

### 조건 블록과 연동

옵션 이름은 기존 `[atom]` 조건 시스템의 atom으로 사용할 수 있습니다.

```
[simd] {
    compile-flags { "-mavx2" }
    sources { include "src/simd/**/*.cpp" }
}

[experimental.release] {
    compile-flags { "-O3" }
}
```

> **참고**: `[simd]` 조건 블록은 `--simd` 플래그(또는 `EnabledOptions`에 `simd` 포함)로 명시적으로 활성화된 경우에만 적용됩니다.
> `default = true`인 옵션도 CLI에서 명시하지 않으면 조건 블록은 적용되지 않습니다.
> `option` 블록의 `defines`/`dependencies`는 `default` 값에 따라 CLI 없이도 적용됩니다.

### 환경변수

옵션 활성 여부가 환경변수로 노출됩니다. 이름은 대문자화하고 `-` → `_`로 변환합니다.

| 옵션 이름 | 환경변수 | 활성 시 | 비활성 시 |
|-----------|---------|---------|-----------|
| `simd` | `DOTORI_OPTION_SIMD` | `1` | `0` |
| `my-feature` | `DOTORI_OPTION_MY_FEATURE` | `1` | `0` |

---

## dependencies 블록 — 의존성

`.dotori`의 `dependencies {}` 블록은 **`path` 의존성 전용**입니다.
레지스트리/git 의존성은 `package.json`에 작성합니다.

```
dependencies {
    # 로컬 경로 의존성 — 빌드 순서 DAG에 포함
    my-lib  = { path = "../lib" }

    # 옵션 조건부 경로 의존성
    simd-utils = { path = "../simd-utils", option = "simd" }
}
```

레지스트리/git 의존성은 `package.json`에:

```json
{
  "dependencies": {
    "@fmtlib/fmt": "^10.0.0",
    "@gabime/spdlog": "~1.13.0"
  },
  "dotoriDependencies": {
    "@wolfpld/tracy": { "git": "https://github.com/wolfpld/tracy", "tag": "v0.11.0" },
    "@gabime/spdlog": { "git": "https://github.com/gabime/spdlog", "tag": "v1.13.0" }
  }
}
```

`path` 의존성은 빌드 순서 DAG에 포함되어 자동으로 선행 빌드됩니다.
`dependencies` 필드는 npm-compatible SemVer 범위, `dotoriDependencies`는 git/path 소스 오버라이드에 사용합니다.

`option` 필드를 지정하면 해당 옵션이 활성일 때만 의존성이 포함됩니다.
여러 옵션을 `{ }` 목록으로 지정하면 **모든 옵션이 동시에 활성일 때**만 포함됩니다 (AND 조건).
옵션이 비활성이면 의존성은 완전히 무시됩니다.

---

## 조건 블록

조건에 따라 다른 설정을 적용합니다.

### 사용 가능한 조건

| 종류 | 값 |
|------|----|
| 플랫폼 | `windows`, `uwp`, `linux`, `android`, `macos`, `ios`, `tvos`, `watchos`, `wasm` |
| 구성 | `debug`, `release` |
| 컴파일러 | `msvc`, `clang` |
| 런타임 링크 | `static`, `dynamic` |
| C 런타임 | `glibc`, `musl` |
| C++ STL | `libcxx`, `libstdcxx` |
| WASM 백엔드 | `emscripten`, `bare` |

### 조건 조합

`.`으로 여러 조건을 조합합니다. 구체적인 조건일수록 우선순위가 높습니다.

```
[windows]               # Windows 전용
[windows.release]       # Windows + Release
[release.msvc]          # Release + MSVC
[clang.linux]           # Clang + Linux
[wasm.emscripten]       # WASM Emscripten
```

```
project MyApp {
    type = executable
    std  = c++23

    [windows] {
        sources { include "src/platform/windows/**/*.cpp" }
        defines { "PLATFORM_WINDOWS" "WIN32_LEAN_AND_MEAN" }
        links   { "kernel32" "user32" }
    }

    [linux] {
        sources { include "src/platform/linux/**/*.cpp" }
        defines { "PLATFORM_LINUX" }
        links   { "pthread" "dl" }
        libc    = glibc
    }

    [macos] {
        sources    { include "src/platform/macos/**/*.cpp" }
        frameworks { "Foundation" "Metal" }
        macos-min  = "12.0"
    }

    [debug] {
        defines    { "DEBUG" "_DEBUG" }
        optimize   = none
        debug-info = full
    }

    [release] {
        defines    { "NDEBUG" }
        optimize   = speed
        lto        = true
    }
}
```

---

## 환경변수 보간 (`${VAR}`)

DSL의 모든 사용자 작성 문자열 안에서 `${VAR}` 형태로 환경변수를 참조할 수 있습니다.
보간은 빌드 시작 시 수행됩니다.

```
headers { public "${BOOST_ROOT}/include/" }
sources { include "${SRC_DIR}/**/*.cpp" }
compile-flags { "-DAPP_VERSION=${APP_VERSION}" }
output  { binaries = "bin/${DOTORI_TARGET}-${DOTORI_CONFIG}/" }
pre-build { "${SCRIPTS_DIR}/gen_version.sh" }
dependencies {
    mylib = { path = "${MYLIB_ROOT}" }
}
```

| 동작 | 설명 |
|------|------|
| `${VAR}` | 환경변수 `VAR` 값으로 치환 |
| 정의되지 않은 변수 | 빈 문자열로 치환 |
| 닫히지 않은 `${` | 리터럴로 유지 |

`type`, `std`, `optimize` 등 DSL 키워드에는 적용되지 않습니다.

dotori가 자동 주입하는 변수:

| 변수 | 예시 |
|------|------|
| `DOTORI_TARGET` | `macos-arm64` |
| `DOTORI_CONFIG` | `debug` |
| `DOTORI_PLATFORM` | `macos` |
| `DOTORI_ARCH` | `arm64` |

---

## package.json — 패키지 메타데이터

라이브러리를 레지스트리에 배포하려면 `.dotori`와 같은 디렉토리에 `package.json`을 작성합니다.

```json
{
  "name": "@myorg/my-lib",
  "version": "1.0.0",
  "description": "My C++ library",
  "license": "MIT",
  "homepage": "https://github.com/me/my-lib",
  "authors": ["Alice <alice@example.com>", "Bob <bob@example.com>"],
  "dependencies": {
    "@fmtlib/fmt": "^10.0.0"
  },
  "dotoriDependencies": {
    "@fmtlib/fmt": { "git": "https://github.com/fmtlib/fmt", "tag": "v10.2.0" }
  },
  "dotori": {
    "exports": { "headers": "include/" }
  }
}
```

| 필드 | 설명 |
|------|------|
| `name` | `@scope/name` 형식 필수 |
| `version` | SemVer |
| `dependencies` | npm-compatible 레지스트리 의존성 |
| `dotoriDependencies` | git/path 소스 오버라이드 |
| `dotori.exports.headers` | 배포 시 포함할 헤더 디렉토리 |

---

## 전체 예시

```
# app/.dotori — 완전한 예시
project MyApp {
    type        = executable
    std         = c++23
    description = "My application"

    sources {
        include "src/**/*.cpp"
        include "src/**/*.cppm"
    }

    modules {
        include "src/**/*.cppm"
    }

    headers {
        public  "include/"
        private "src/"
    }

    [windows] {
        sources { include "src/platform/windows/**/*.cpp" }
        defines { "PLATFORM_WINDOWS" "WIN32_LEAN_AND_MEAN" }
        links   { "kernel32" "user32" "ole32" }
    }

    [linux] {
        sources { include "src/platform/linux/**/*.cpp" }
        defines { "PLATFORM_LINUX" }
        links   { "pthread" "dl" }
        libc    = glibc
        stdlib  = libstdc++
    }

    [macos] {
        sources    { include "src/platform/macos/**/*.cpp" }
        defines    { "PLATFORM_MACOS" }
        frameworks { "Foundation" "Metal" "MetalKit" }
        macos-min  = "12.0"
    }

    [android] {
        sources           { include "src/platform/android/**/*.cpp" }
        defines           { "PLATFORM_ANDROID" }
        android-api-level = 26
    }

    [debug] {
        defines    { "DEBUG" "_DEBUG" }
        optimize   = none
        debug-info = full
    }

    [release] {
        defines    { "NDEBUG" }
        optimize   = speed
        debug-info = none
        lto        = true
    }

    dependencies {
        my-lib = { path = "../lib" }
        # 레지스트리/git 의존성은 package.json에 작성
    }

    pch {
        header = "src/pch.h"
        source = "src/pch.cpp"
    }

    unity-build {
        enabled    = true
        batch-size = 8
        exclude    { "src/main.cpp" }
    }

    output {
        binaries  = "bin/"
        libraries = "lib/"
        symbols   = "pdb/"
    }

    [msvc] {
        compile-flags { "/arch:AVX2" }
        link-flags    { "/SUBSYSTEM:WINDOWS" "/OPT:REF" }
    }

    [clang] {
        compile-flags { "-march=native" }
    }

    pre-build  { "scripts/gen_version.sh" }
    post-build { "scripts/sign.sh" }
}
```

---

## 멀티 프로젝트 레이아웃

솔루션 파일 없이 `path` 의존성으로 프로젝트 간 관계를 표현합니다.

```
repo/
├── app/
│   └── .dotori    ← dependencies { my-lib = { path = "../lib" } }
└── lib/
    └── .dotori    ← project MyLib { type = static-library ... }
```

`app/`에서 `dotori build`를 실행하면:

1. `app/.dotori` 로드
2. `path = "../lib"` 의존성 발견 → `lib/.dotori` 로드
3. DAG: `MyLib` → `MyApp` 순서로 자동 빌드

의존 관계가 없는 프로젝트들은 병렬로 빌드됩니다.

---

## 로컬 오버라이드 파일 (`.dotori.local`)

`.dotori.local`은 팀이 공유하는 `.dotori`를 수정하지 않고, 개발자 개인이 로컬 환경에서만 빌드 설정을 오버라이드할 수 있는 파일입니다. `.dotori`와 같은 디렉토리에 위치하며, git에서 자동으로 무시됩니다.

### 특징

- `.dotori`와 동일한 DSL 문법 사용
- `project { }` 블록만 유효 (`package { }` 무시)
- `.dotori.local`의 모든 설정은 `.dotori`의 어떤 조건 블록보다 높은 우선순위를 가짐
- git에서 무시되므로 팀원 간 공유되지 않음

### 사용 예시

`.dotori` (공유):
```
project MyApp {
    type     = executable
    std      = c++17
    optimize = speed

    [release] {
        std = c++20
        lto = true
    }
}
```

`.dotori.local` (개인, git 무시):
```
project MyApp {
    optimize = none      # 로컬에서는 디버깅을 위해 최적화 끄기
    std      = c++23     # [release] 조건 블록보다 높은 우선순위로 적용
}
```

### 우선순위 규칙

| 소스 | 예시 | 우선순위 |
|------|------|---------|
| 메인 최상위 | `std = c++17` | 낮음 |
| 메인 조건 블록 | `[windows.release.msvc] { ... }` | 중간 |
| 로컬 최상위 | `.dotori.local`의 `std = c++23` | 높음 |
| 로컬 조건 블록 | `.dotori.local`의 `[debug] { ... }` | 가장 높음 |

> **주의**: 스칼라 속성(`std`, `optimize`, `type` 등)은 오버라이드(마지막 값이 최종값)되고,
> 리스트 속성(`defines`, `compile-flags`, `sources` 등)은 누적(append)됩니다.

---

## Lock 파일 (`dotori-lock.json`)

의존성 해결 결과를 저장하는 자동 생성 파일입니다. git에 커밋하여 팀원 간 동일한 의존성 버전을 보장합니다.

```json
{
  "lockVersion": 2,
  "name": "@myorg/myapp",
  "version": "1.0.0",
  "packages": {
    "@fmtlib/fmt@10.2.0": {
      "version": "10.2.0",
      "resolved": "https://npm.pkg.github.com/@fmtlib/fmt/-/fmt-10.2.0.tgz",
      "integrity": "sha512-...",
      "registry": "https://npm.pkg.github.com",
      "dependencies": {}
    },
    "@gabime/spdlog@1.13.0": {
      "version": "1.13.0",
      "resolved": "git+https://github.com/gabime/spdlog#v1.13.0",
      "integrity": "sha256:...",
      "dependencies": { "@fmtlib/fmt": "^10.2.0" }
    }
  }
}
```

수동 편집 금지. `dotori add` / `dotori update` 명령으로 갱신합니다.
