# CLAUDE.md - Claude AI 에이전트 지시사항

Dotori: C# 12 / .NET 10 (NativeAOT) 로 작성된 C++ 빌드 시스템 및 패키지 매니저.

---

## 새 세션 시작 체크리스트

### 1단계: 프로젝트 구조 이해 (필수)
읽기: `.claude/rules/structure.md`
- 7개 .NET 컴포넌트 역할, 모듈 위치, 의존성 파악

### 2단계: 개발 가이드 검토
읽기: `.claude/rules/working-guides.md`
- Git 커밋 규칙, 테스트 원칙, 문서 동기화 규칙

### 3단계: 프로젝트 현황 확인
읽기: `SPEC.md`
- 구현된 항목(✅), 미구현 항목, 로드맵

---

## 파일 위치 안내

| 찾는 것 | 위치 |
|--------|------|
| 프로젝트 구조 | `.claude/rules/structure.md` |
| 작업 가이드 | `.claude/rules/working-guides.md` |
| 명령어 레퍼런스 | `.claude/rules/commands.md` |
| 기술 사양/로드맵 | `SPEC.md` |
| 빌드 엔진 핵심 | `src/Dotori.Core/` |
| CLI 도구 | `src/Dotori.Cli/` |
| 패키지 관리자 | `src/Dotori.PackageManager/` |
| 언어 서버 (LSP) | `src/Dotori.LanguageServer/` |
| 패키지 레지스트리 배포 | `deploy/registry/` (Verdaccio, npm 호환) |
| 분산 빌드 서버 | `src/Dotori.BuildServer/` |
| 빌드 워커 | `src/Dotori.Worker/` |
| gRPC 정의 | `src/Dotori.Grpc/` |
| 테스트 | `tests/` |
| 사용자 문서 | `docs/` |
| 빌드 스크립트 | `build.sh` (Unix), `build.ps1` (Windows) |
