# dotori — C++ 빌드 시스템 + 패키지 매니저

C# (.NET 10, NativeAOT). 설계 문서: `docs/` 참조

---

## 구현된 항목

### export build-system — 빌드 시스템 파일 자동 생성

`dotori export build-system --format <fmt>` 명령으로 `.dotori` 파일에서 외부 빌드 시스템 파일 생성.

- [x] CMakeLists.txt (`--format cmake`)
- [x] meson.build (`--format meson`)
- [x] .vcxproj / .vcxproj.filters (`--format vcxproj`)
- [x] *.xcodeproj/project.pbxproj (`--format pbxproj`)
- [x] build.ninja (`--format ninja`)
- [x] Makefile (`--format makefile`)

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
