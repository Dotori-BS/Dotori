# 사용 예제

dotori를 사용하는 다양한 실용적인 예제를 단계별로 설명합니다.

---

## 예제 1: Hello World (실행 파일)

가장 간단한 C++ 실행 파일 프로젝트입니다.

```
hello/
├── .dotori
└── src/
    └── main.cpp
```

**.dotori**

```
project Hello {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }
}
```

**src/main.cpp**

```cpp
#include <iostream>

int main() {
    std::cout << "Hello, dotori!\n";
    return 0;
}
```

**빌드 및 실행:**

```bash
dotori build
dotori run
```

---

## 예제 2: 정적 라이브러리 + 실행 파일

라이브러리와 실행 파일을 분리한 멀티 프로젝트 구조입니다.

```
repo/
├── app/
│   ├── .dotori
│   └── src/
│       └── main.cpp
└── lib/
    ├── .dotori
    ├── include/
    │   └── mylib/
    │       └── greet.h
    └── src/
        └── greet.cpp
```

**lib/.dotori**

```
project MyLib {
    type = static-library
    std  = c++23

    sources { include "src/**/*.cpp" }

    headers {
        public  "include/"
        private "src/"
    }
}
```

**lib/include/mylib/greet.h**

```cpp
#pragma once
#include <string>

namespace mylib {
    std::string greet(const std::string& name);
}
```

**lib/src/greet.cpp**

```cpp
#include <mylib/greet.h>

namespace mylib {
    std::string greet(const std::string& name) {
        return "Hello, " + name + "!";
    }
}
```

**app/.dotori**

```
project MyApp {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    dependencies {
        my-lib = { path = "../lib" }
    }
}
```

**app/src/main.cpp**

```cpp
#include <mylib/greet.h>
#include <iostream>

int main() {
    std::cout << mylib::greet("dotori") << "\n";
    return 0;
}
```

**빌드:**

```bash
cd app
dotori build    # lib → app 순서로 자동 빌드
dotori run
```

---

## 예제 3: 외부 패키지 의존성

`fmt`와 `spdlog` 패키지를 사용하는 예제입니다.

**.dotori**

```
project MyServer {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    dependencies {
        fmt    = "10.2.0"
        spdlog = { git = "https://github.com/gabime/spdlog", tag = "v1.13.0" }
    }
}
```

**src/main.cpp**

```cpp
#include <spdlog/spdlog.h>
#include <fmt/format.h>

int main() {
    spdlog::info("Server starting on port {}", 8080);
    auto msg = fmt::format("Hello from {}", "dotori");
    spdlog::info(msg);
    return 0;
}
```

**패키지 설치 및 빌드:**

```bash
dotori build    # 자동으로 의존성 다운로드 후 빌드
```

패키지는 프로젝트의 `deps/` 폴더에 설치되고, `dotori-lock.json`에 버전이 고정됩니다.

---

## 예제 4: 플랫폼별 설정

크로스 플랫폼 게임 엔진을 가정한 예제입니다.

**.dotori**

```
project GameEngine {
    type = shared-library
    std  = c++23

    sources {
        include "src/**/*.cpp"
        exclude "src/platform/**/*.cpp"
    }

    headers {
        public  "include/"
        private "src/"
    }

    [windows] {
        sources { include "src/platform/windows/**/*.cpp" }
        defines { "PLATFORM_WINDOWS" "WIN32_LEAN_AND_MEAN" "NOMINMAX" }
        links   { "kernel32" "user32" "gdi32" "opengl32" }
        runtime-link = dynamic
    }

    [linux] {
        sources { include "src/platform/linux/**/*.cpp" }
        defines { "PLATFORM_LINUX" }
        links   { "pthread" "dl" "GL" "X11" }
        libc    = glibc
        stdlib  = libstdc++
    }

    [macos] {
        sources    { include "src/platform/macos/**/*.cpp" }
        defines    { "PLATFORM_MACOS" }
        frameworks { "Foundation" "Metal" "MetalKit" "AppKit" }
        macos-min  = "12.0"
    }

    [ios] {
        sources    { include "src/platform/ios/**/*.cpp" }
        defines    { "PLATFORM_IOS" }
        frameworks { "Foundation" "UIKit" "Metal" }
        ios-min    = "15.0"
        (* runtime-link = static 자동 강제 *)
    }

    [android] {
        sources           { include "src/platform/android/**/*.cpp" }
        defines           { "PLATFORM_ANDROID" }
        links             { "log" "android" "EGL" "GLESv3" }
        android-api-level = 26
    }

    [debug] {
        defines    { "DEBUG" "_DEBUG" "ENGINE_DEBUG" }
        optimize   = none
        debug-info = full
    }

    [release] {
        defines    { "NDEBUG" }
        optimize   = speed
        lto        = true
    }

    [msvc] {
        compile-flags { "/arch:AVX2" }
    }

    [clang] {
        compile-flags { "-march=native" }
    }
}
```

**타겟별 빌드:**

```bash
# 로컬 플랫폼 (macOS에서)
dotori build

# iOS 크로스 컴파일 (macOS 호스트 필요)
dotori build --target ios-arm64

# Android 크로스 컴파일
dotori build --target android-arm64

# Windows 크로스 컴파일 (macOS/Linux에서, llvm-mingw 필요)
dotori build --target windows-x64

# Release 빌드
dotori build --release --target linux-x64
```

---

## 예제 5: C++ Modules

C++20/23 Modules를 사용하는 예제입니다.

```
modules-demo/
├── .dotori
└── src/
    ├── main.cpp
    ├── math.cppm
    └── string_utils.cppm
```

**.dotori**

```
project ModulesDemo {
    type = executable
    std  = c++23

    sources {
        include "src/**/*.cpp"
        include "src/**/*.cppm"
    }

    modules {
        include "src/**/*.cppm"
        export-map = true
    }
}
```

**src/math.cppm**

```cpp
export module math;

export namespace math {
    constexpr double pi = 3.14159265358979323846;

    double circle_area(double r) {
        return pi * r * r;
    }
}
```

**src/string_utils.cppm**

```cpp
export module string_utils;

import <string>;
import <algorithm>;

export namespace str {
    std::string to_upper(std::string s) {
        std::transform(s.begin(), s.end(), s.begin(), ::toupper);
        return s;
    }
}
```

**src/main.cpp**

```cpp
import math;
import string_utils;
import <iostream>;

int main() {
    std::cout << "pi = " << math::pi << "\n";
    std::cout << "area(5) = " << math::circle_area(5) << "\n";
    std::cout << str::to_upper("hello modules") << "\n";
}
```

**빌드:**

```bash
dotori build
# BMI 생성 순서가 자동으로 결정됩니다
```

**단일 모듈 파일 재빌드:**

```bash
dotori build --file src/math.cppm
# BMI만 재생성됩니다 (--no-link 자동 적용)
```

---

## 예제 6: 프리컴파일 헤더 (PCH)

자주 사용되는 헤더를 미리 컴파일하여 빌드 속도를 향상시킵니다.

```
pch-demo/
├── .dotori
└── src/
    ├── pch.h
    ├── pch.cpp
    └── main.cpp
```

**.dotori**

```
project PchDemo {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    pch {
        header = "src/pch.h"
        source = "src/pch.cpp"
    }
}
```

**src/pch.h**

```cpp
#pragma once

// 자주 사용하는 STL 헤더
#include <iostream>
#include <string>
#include <vector>
#include <memory>
#include <algorithm>
#include <functional>
#include <chrono>
#include <unordered_map>
```

**src/pch.cpp**

```cpp
#include "pch.h"
```

---

## 예제 7: Unity Build

컴파일 단위를 묶어 빌드 속도를 향상시킵니다. 파일이 많은 대형 프로젝트에 유용합니다.

**.dotori**

```
project LargeProject {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    unity-build {
        enabled    = true
        batch-size = 8    (* 8개 파일을 하나의 컴파일 단위로 묶음 *)
        exclude {
            "src/main.cpp"        (* Unity Build에서 제외 *)
            "src/generated/**"    (* 자동 생성 파일 제외 *)
        }
    }
}
```

**특정 파일만 재빌드:**

```bash
# foo.cpp가 포함된 unity batch를 재컴파일 후 전체 링크
dotori build --file src/foo.cpp

# Unity build를 무시하고 foo.cpp 단독 컴파일
dotori build --file src/foo.cpp --no-unity
```

---

## 예제 8: WASM (WebAssembly)

Emscripten을 사용한 WASM 빌드 예제입니다.

**.dotori**

```
project WasmCalc {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    defines { "WASM_BUILD" }

    [wasm.emscripten] {
        emscripten-flags {
            "-sEXPORTED_FUNCTIONS=[_main,_add,_multiply]"
            "-sEXPORTED_RUNTIME_METHODS=[ccall,cwrap]"
            "-sALLOW_MEMORY_GROWTH"
        }
    }
}
```

**빌드:**

```bash
# Emscripten SDK (EMSDK) 설치 후
dotori build --target wasm32-emscripten

# 또는 bare WASM (JavaScript 없이 .wasm만)
dotori build --target wasm32-bare
```

---

## 예제 9: 빌드 전/후 스크립트

빌드 과정에 커스텀 스크립트를 추가합니다.

**.dotori**

```
project MyApp {
    type = executable
    std  = c++23

    sources { include "src/**/*.cpp" }

    pre-build {
        "python3 scripts/gen_version.py"     (* 버전 헤더 자동 생성 *)
        "python3 scripts/embed_assets.py"    (* 에셋 임베딩 *)
    }

    post-build {
        "python3 scripts/run_tests.py"
    }

    [release] {
        post-build {
            "scripts/sign_binary.sh --cert certs/release.p12"
            "scripts/create_installer.sh"
        }
    }

    [windows.release] {
        post-build { "scripts\\sign_binary.bat" }
    }
}
```

스크립트에서 dotori가 제공하는 환경 변수를 활용할 수 있습니다:

**scripts/gen_version.py**

```python
import os
import datetime

target = os.environ.get('DOTORI_TARGET', 'unknown')
config = os.environ.get('DOTORI_CONFIG', 'debug')

with open('src/version.h', 'w') as f:
    f.write(f'#pragma once\n')
    f.write(f'#define BUILD_TARGET "{target}"\n')
    f.write(f'#define BUILD_CONFIG "{config}"\n')
    f.write(f'#define BUILD_DATE "{datetime.date.today()}"\n')
```

---

## 예제 10: 환경변수 보간

경로 설정에 환경변수를 활용합니다.

**.dotori**

```
project MyApp {
    type = executable
    std  = c++23

    sources {
        include "src/**/*.cpp"
        include "${EXTRA_SRC_DIR}/**/*.cpp"    (* 추가 소스 디렉토리 *)
    }

    headers {
        public  "include/"
        public  "${BOOST_ROOT}/include/"       (* Boost 헤더 경로 *)
        private "src/"
    }

    [linux] {
        compile-flags { "-I${MY_SDK}/include" }
        link-flags    { "-L${MY_SDK}/lib" "-lmysdk" }
    }

    output {
        binaries = "bin/${DOTORI_TARGET}-${DOTORI_CONFIG}/"
    }

    pre-build { "${SCRIPTS_DIR}/prepare.sh" }
}
```

**빌드:**

```bash
BOOST_ROOT=/opt/homebrew MY_SDK=/usr/local/my-sdk dotori build
# 출력: bin/macos-arm64-debug/MyApp
```

---

## 예제 11: 라이브러리 배포 (package 블록)

레지스트리에 배포할 라이브러리 설정입니다.

**.dotori**

```
project JsonLib {
    type = static-library
    std  = c++23

    sources { include "src/**/*.cpp" }

    headers {
        public  "include/"
        private "src/"
    }

    [windows.shared-library] {
        defines { "JSONLIB_EXPORTS" }
    }
}

```

**package.json:**

```json
{
  "name": "@me/json-lib",
  "version": "2.1.0",
  "description": "Fast and lightweight JSON library for C++23",
  "license": "MIT",
  "homepage": "https://github.com/me/json-lib",
  "authors": ["Alice <alice@example.com>"],
  "dotori": {
    "exports": { "headers": "include/" }
  }
}
```

**배포:**

```bash
dotori login --registry https://npm.pkg.github.com --token ghp_...
dotori publish
```

---

## 예제 12: 출력 디렉토리 분리

빌드 결과물을 명확히 분리된 디렉토리에 복사합니다.

**.dotori**

```
project MyLib {
    type = static-library
    std  = c++23

    sources { include "src/**/*.cpp" }
    headers { public "include/" }

    output {
        binaries  = "dist/bin/"
        libraries = "dist/lib/"
        symbols   = "dist/pdb/"
    }

    [release] {
        output {
            binaries  = "release/bin/"
            libraries = "release/lib/"
        }
    }
}
```

```bash
dotori build           # → dist/
dotori build --release # → release/
dotori clean           # dist/, release/ 복사본도 함께 삭제
```

---

## 예제 13: 분산 빌드 + CI 환경

CI 파이프라인에서 분산 빌드 서버를 활용하는 예제입니다.

**.github/workflows/build.yml**

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Build (distributed)
        run: |
          dotori build --release \
            --target linux-x64 \
            --remote ${{ secrets.BUILD_SERVER_URL }} \
            --jobs 64
```

빌드 서버 URL은 GitHub Secrets에 저장하고,
빌드 서버는 사내 인프라 또는 클라우드에서 항상 가동 상태를 유지합니다.
캐시 히트율이 높으면 동일 소스 재빌드 시 거의 즉시 완료됩니다.

---

## 예제 14: clangd IntelliSense 통합

VSCode 등에서 `clangd` IntelliSense를 사용하기 위한 `compile_commands.json`을 생성합니다.

```bash
# 현재 플랫폼 기준으로 생성
dotori generate-compile-commands

# 특정 타겟 기준으로 생성
dotori generate-compile-commands --target linux-x64

# 출력 경로 지정
dotori generate-compile-commands --output ./
```

`.vscode/settings.json`에서 clangd 경로를 설정합니다:

```json
{
    "clangd.arguments": [
        "--compile-commands-dir=${workspaceFolder}"
    ]
}
```

---

## 예제 15: 외부 어셈블러 사용

### 크로스 플랫폼 SIMD 최적화 루틴

각 플랫폼에 맞는 어셈블러로 최적화된 루틴을 컴파일합니다.

```
project simd-math {
    type = static-library
    std  = c++23

    sources {
        include "src/**/*.cpp"
    }

    # Linux: NASM으로 asm 컴파일 (elf64 포맷 자동 감지)
    [linux] {
        assembler {
            tool = nasm
            include "src/asm/x86_64/**/*.asm"
            defines { "UNIX_ABI" }
        }
    }

    # macOS: NASM으로 asm 컴파일 (macho64 포맷 자동 감지)
    [macos] {
        assembler {
            tool = nasm
            include "src/asm/x86_64/**/*.asm"
            defines { "UNIX_ABI" }
        }
    }

    # Windows MSVC: MASM 사용 (ml64.exe 자동 감지)
    [windows] {
        assembler {
            tool = masm
            include "src/asm/x86_64/**/*.asm"
            defines { "WIN_ABI" }
        }
    }

    # wasm32 등 — assembler 블록 없음 → C++ 대체 구현 사용
}
```

### GNU Assembler (GAS)로 ARM64 루틴

```
project arm-crypto {
    type = static-library

    sources  { include "src/**/*.cpp" }

    [linux.android] {
        assembler {
            tool = gas
            include "src/asm/aarch64/**/*.S"
            flags { "--defsym" "ANDROID=1" }
        }
    }

    [macos] {
        assembler {
            tool = gas
            include "src/asm/aarch64/**/*.S"
        }
    }
}
```

### 공통 소스 + 플랫폼 오버라이드

```
project mylib {
    type = static-library

    # 공통 asm 소스 (플랫폼 독립)
    assembler {
        include "src/asm/common/**/*.asm"
    }

    # Windows에서만 tool 오버라이드 + 추가 소스
    [windows] {
        assembler {
            tool = masm
            include "src/asm/windows/**/*.asm"
        }
    }
}
```

---

## C 소스를 C++로 컴파일 (c-as-cpp)

레거시 C 코드를 C++ 프로젝트에 통합하거나, C 파일 전체를 C++로 컴파일해야 할 때 사용한다.

```
project {
    name    = "my-app"
    type    = executable
    std     = c++23

    c-as-cpp = true    # .c 파일을 C++ 모드로 컴파일

    sources {
        include "src/**/*.cpp"
        include "vendor/legacy/**/*.c"    # C 라이브러리 소스도 C++로 컴파일
    }
}
```

C++ 확장자 파일(`.cpp`, `.cc`, `.cxx`)은 `c-as-cpp` 설정과 무관하게 항상 C++로 컴파일된다.

---

## Objective-C / Objective-C++ (Apple 플랫폼)

### Objective-C 앱 (ARC 사용)

```
project MyObjcApp {
    type = executable

    [macos] {
        objc-arc = true

        sources {
            include "src/**/*.m"
            include "src/**/*.mm"
        }

        frameworks {
            "Cocoa"
            "Foundation"
        }
    }
}
```

### Objective-C++로 강제 컴파일

`.m` 파일을 Objective-C++ 모드로 컴파일하여 C++ 코드와 혼용할 때:

```
project MyMixedApp {
    type = executable

    [macos] {
        objc-arc      = true
        objc-as-objcpp = true   # .m 파일도 -x objective-c++ 로 컴파일

        sources {
            include "src/**/*.cpp"
            include "src/**/*.m"
        }

        frameworks {
            "Foundation"
        }
    }
}
```

### C++/Objective-C 혼합 라이브러리

```
project MyBridge {
    type = static-library

    sources {
        include "src/**/*.cpp"
    }

    [macos] {
        objc-arc = true

        sources {
            include "src/apple/**/*.mm"
        }

        frameworks {
            "Foundation"
            "AppKit"
        }
    }

    [ios] {
        objc-arc = true

        sources {
            include "src/apple/**/*.mm"
        }

        frameworks {
            "Foundation"
            "UIKit"
        }
    }
}
```


