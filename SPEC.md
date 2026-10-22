# dotori — C++ 빌드 시스템 + 패키지 매니저

C# (.NET 10, NativeAOT). 설계 문서: `docs/` 참조

---

## 구현된 항목

### 로컬 툴체인 전송 원격 빌드

- [x] `build --remote-toolchain <JSON>`: 명시한 컴파일러·링커·SDK·입력 경로 전송
- [x] SHA-256 콘텐츠 저장소, 청크 전송, 누락 파일 조회 및 워커 툴체인 재사용
- [x] 워커 OS/CPU·업로드 실행 허용 여부에 따른 라우팅 (사전 컴파일러 설치 불필요)
- [x] 경로 재배치, 출력 파일 회수, 입력·툴체인·환경·인수 기반 컴파일 캐시
- [x] 실제 gRPC 경로에서 CLI/워커 토큰 인증
- 현재 범위: 경로 설정이 명시된 C/C++ 컴파일·링크. OS 시스템 런타임은 호스트가 제공하며, PCH/Modules/Swift 컴파일은 로컬 실행을 유지합니다. 상세 제약은 `docs/distributed-build.md` 참조.

### export build-system — 빌드 시스템 파일 자동 생성

`dotori export build-system --format <fmt>` 명령으로 `.dotori` 파일에서 외부 빌드 시스템 파일 생성.

- [x] CMakeLists.txt (`--format cmake`)
- [x] meson.build (`--format meson`)
- [x] .sln / .vcxproj / .vcxproj.filters (`--format vcxproj`)
- [x] *.xcodeproj/project.pbxproj (`--format pbxproj`)
- [x] build.ninja (`--format ninja`)
- [x] Makefile (`--format makefile`)
- [x] CMakePresets.json (CMake 내보내기 시 configure/build/test preset 자동 생성)
- [x] `setup ide` 공통/VS Code/Zed 개발 환경 자동 구성
- [x] `watch --ide` IDE 산출물 자동 재생성
- [x] VS Code 확장 (TextMate 문법, LSP, build/test/run/setup 명령)
- [x] Zed 확장 (고정 커밋 Tree-sitter 문법/강조 쿼리, LSP 실행 경로·인자·환경 설정)
- [x] JetBrains 2026.2 플러그인 (CLion/Rider 문법 강조, LSP 어댑터, 실행 경로 설정)
- [x] Visual Studio solution 생성 및 VSIX (TextMate 문법, LSP 어댑터)
- [x] IDE 생성/CMake smoke 및 에디터 확장 패키징 CI

---

## 완료된 로드맵

### Phase 1-I: 플랫폼 검증

- [x] `linux-x64` (musl + static)
- [x] `wasm32-emscripten`

---

### Phase 5: Swift 지원

- [x] `swiftc` 컴파일러 드라이버 추가 (`CompilerKind.Swift`)
- [x] `.swift` 소스 파일 인식 및 빌드 파이프라인 연동
- [x] Swift 모듈(`.swiftmodule`) 생성 및 의존성 처리
- [x] Apple 플랫폼 링크 시 Swift 런타임 자동 연결 (`-L$(swiftc --print-target-info)`)
- [x] C++/Swift 혼합 빌드 지원 (Swift C++ interop 및 C ABI)

---

### Phase 4: 레지스트리 서버

- [x] Docker / Kubernetes 배포 구성 (npm 호환 Verdaccio)
