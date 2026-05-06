# dotori 문서

dotori는 Cargo 수준의 UX를 C++에 제공하는 독립형 빌드 시스템 + 패키지 매니저입니다.

## 문서 목차

| 문서 | 설명 |
|------|------|
| [CLI 사용법](cli.md) | 모든 명령어와 옵션 상세 설명 |
| [.dotori 파일 구성](dotori-file.md) | 프로젝트 파일 DSL 문법 전체 레퍼런스 |
| [분산 빌드 서버](distributed-build.md) | 빌드 서버 및 워커 설정·운영 방법 |
| [사용 예제](examples.md) | 단계별 실용 예제 모음 |
| [프로젝트 빌드 방법](building.md) | build.sh 및 dotnet CLI로 dotori 소스 빌드하기 |
| [manpage (dotori.1)](dotori.1) | CLI man 페이지 |

## 빠른 시작

```bash
# 1. 새 프로젝트 디렉토리 생성
mkdir hello-cpp && cd hello-cpp

# 2. .dotori 파일 작성
cat > .dotori << 'EOF'
project HelloCpp {
    type = executable
    std  = c++23
    sources { include "src/**/*.cpp" }
}
EOF

# 3. 소스 파일 작성
mkdir src
echo '#include <iostream>
int main() { std::cout << "Hello, dotori!\n"; }' > src/main.cpp

# 4. 빌드 및 실행
dotori build
dotori run
```

## 지원 플랫폼

| 타겟 ID | OS | 아키텍처 | 컴파일러 |
|---------|----|---------|---------|
| `windows-x64` | Windows | amd64 | MSVC / Clang |
| `windows-x86` | Windows | x86 | MSVC / Clang |
| `windows-arm64` | Windows | arm64 | MSVC / Clang |
| `linux-x64` | Linux | amd64 | Clang |
| `linux-arm64` | Linux | arm64 | Clang |
| `macos-arm64` | macOS | arm64 | Clang |
| `macos-x64` | macOS | amd64 | Clang |
| `android-arm64` | Android | arm64 | NDK Clang |
| `android-x64` | Android | amd64 | NDK Clang |
| `android-arm` | Android | armv7 | NDK Clang |
| `ios-arm64` | iOS | arm64 | Clang (크로스) |
| `ios-sim-arm64` | iOS Simulator | arm64 | Clang (크로스) |
| `wasm32-emscripten` | WASM | wasm32 | emcc |
| `wasm32-bare` | WASM | wasm32 | Clang |
