# Dotori 프로젝트 구조

## 프로젝트 개요

**Dotori**는 C# (.NET 10, NativeAOT)으로 작성된 C++ 빌드 시스템 및 패키지 매니저입니다.

**핵심 기술:**
- 언어: C# 12
- 프레임워크: .NET 10
- 배포 모델: NativeAOT
- 테스트 프레임워크: MSTest
- 빌드 시스템: dotnet CLI
- 문서: `docs/` 폴더의 마크다운

---

## 디렉토리 구조

```
Dotori/
├── .claude/                    # Claude AI 에이전트 설정
│   ├── rules/                  # AI 에이전트 가이드라인
│   │   ├── structure.md        # 프로젝트 구조 (이 파일)
│   │   ├── working-guides.md   # 작업 가이드
│   │   └── commands.md         # 사용 가능한 명령어
│   ├── CLAUDE.md              # 세션 시작 지시사항
│   └── settings.local.json    # 로컬 설정
├── .github/                   # GitHub 워크플로우 및 CI/CD
├── src/                       # 소스 코드 - 7개 .NET 컴포넌트
│   ├── Dotori.Core/           # 빌드 엔진 핵심 로직
│   ├── Dotori.Cli/            # 커맨드라인 인터페이스
│   ├── Dotori.LanguageServer/ # LSP (Language Server Protocol) 구현
│   ├── Dotori.PackageManager/ # 의존성 해석 및 패키지 관리
│   ├── Dotori.BuildServer/    # 분산 빌드 서버
│   ├── Dotori.Worker/         # 분산 빌드 워커
│   └── Dotori.Grpc/           # 분산 빌드 gRPC 정의, 원격 실행 및 인증
├── tests/                     # 테스트 프로젝트 (src 구조와 동일)
│   ├── Dotori.Tests.Build/
│   ├── Dotori.Tests.Generators/
│   ├── Dotori.Tests.Graph/
│   ├── Dotori.Tests.LanguageServer/
│   ├── Dotori.Tests.PackageManager/
│   ├── Dotori.Tests.Parsing/
│   ├── Dotori.Tests.BuildServer/
│   └── platform-smoke/        # 플랫폼별 통합 테스트
├── deploy/registry/           # Verdaccio 배포 구성 (Docker Compose/Kubernetes)
├── docs/                      # 사용자 및 개발자 문서
│   ├── index.md               # 문서 목차
│   ├── cli.md                 # CLI 명령어 및 옵션
│   ├── dotori-file.md         # .dotori DSL 파일 형식
│   ├── examples.md            # 사용 예제 및 튜토리얼
│   ├── building.md            # Dotori 자체 빌드 방법
│   ├── distributed-build.md   # 분산 빌드 설정
│   └── dotori.1              # Man page
├── grammar/                   # DSL 문법 정의
├── proto/                     # Protocol Buffer 파일 (gRPC용)
├── build/                     # 빌드 아티팩트 및 출력
├── AGENTS.md                  # LLM 에이전트 개발 가이드 (참조용)
├── SPEC.md                    # 기술 사양 및 구현 현황
├── README.md                  # 프로젝트 개요
├── Dotori.slnx               # Visual Studio 솔루션 파일
├── Directory.Build.props     # 공유된 MSBuild 속성
├── build.sh                  # Unix/Linux/macOS 빌드 스크립트
├── build.ps1                 # Windows PowerShell 빌드 스크립트
└── docker-compose.yml        # Docker Compose 서비스 정의
```

---

## 7개 .NET 컴포넌트

### 1. **Dotori.Core** — 빌드 엔진 핵심

빌드 시스템의 중심 모듈. .dotori DSL 파싱부터 빌드 실행까지 모든 핵심 기능 포함.

- **Build/** — 빌드 타겟 정의, 빌드 그래프 생성, 플랫폼별 빌드 로직
- **Generators/** — CMake/Meson/Ninja/Make/vcxproj/pbxproj 빌드 파일 생성
- **Grammar/** + **Parsing/** — .dotori DSL 렉서/파서/AST 생성
- **Toolchain/** — GCC/Clang/MSVC 감지 및 컴파일러 플래그 관리
- **Linker/** — 링크타임 최적화, 라이브러리 링킹
- **Executor/** — 병렬 빌드 작업 스케줄링
- **Graph/** — 의존성 그래프 구성, 순환 참조 감지
- **Model/** — 프로젝트/타겟/속성 데이터 모델
- **Debugger/** — 디버거 탐지 및 실행
- `DotoriConstants.cs` — 상수 및 기본값 정의

---

### 2. **Dotori.Cli** — 커맨드라인 인터페이스

사용자 진입점. Commands/ 핸들러, BuildContext.cs, Program.cs로 구성.

`BuildContext.LoadProjectGraph`는 빌드와 내보내기의 의존성 그래프·빌드 순서를 구성합니다. `PrepareBuildAsync`는 `build/run/test`에 필요한 패키지 준비와 툴체인 선택을 공통 처리합니다. 실행은 `Commands/ProjectBuildEngine.cs`가 담당하며, PCH 실행·상태 기록은 단일 파일 및 전체 빌드가 공유합니다.

주요 명령어: `build`, `run`, `test`, `watch`, `install`, `export`, `setup`, `package`, `lsp` 및 레지스트리 명령. 전체 등록 목록은 `src/Dotori.Cli/Program.cs` 참조.

---

### 3. **Dotori.PackageManager** — 의존성 관리

SemVer 기반 PubGrub 알고리즘으로 의존성 해석 및 패키지 설치.

핵심 파일: `DependencyResolver.cs`, `PubGrub.cs`, `VersionConstraint.cs`, `LockManager.cs`, `PackageInstaller.cs`, `GitFetcher.cs`, `RegistryClient.cs`, `Config/`

---

### 4. **Dotori.BuildServer** — 분산 빌드 서버

여러 워커에 빌드 작업 분산 실행 및 캐시 관리. `Services/`(gRPC), `Workers/`, `Cache/`로 구성.

---

### 5. **Dotori.Worker** — 빌드 워커

BuildServer로부터 gRPC로 작업 수신, 빌드 실행 후 결과 보고. `Services/`로 구성.

---

### 6. **Dotori.LanguageServer** — LSP 구현

IDE 통합 (자동 완성, Hover, GoToDefinition, References, 진단 정보).

`Handlers/`, `Providers/`, `Protocol/`, `Transport/`, `DocumentStore.cs`, `DotoriLanguageServer.cs`로 구성.

---

### 7. **Dotori.Grpc** — gRPC 정의

`proto/build.proto`에서 분산 빌드 RPC 클라이언트/서버 코드를 생성하고, 원격 빌드 인증 헬퍼를 제공합니다. 레지스트리 통신은 PackageManager의 npm 호환 HTTP 클라이언트가 담당합니다.

`RemoteExecutor`와 `RemoteArgumentMapper`도 이 프로젝트에 위치합니다. Core는 로컬 실행과 `IExecutor` 인터페이스를 제공하며 gRPC를 참조하지 않습니다.

`RemoteBundleClient`는 명시한 툴체인·입력 경로의 스냅샷과 업로드를 담당합니다. `RemoteContentStore`/`RemoteContentTransfer`는 검증된 콘텐츠 전송·캐시를, `RemoteBundleRunner`는 워커의 복원·실행·출력 수집을 담당합니다. 기존 워커 도구 모드와 툴체인 전송 모드의 상세 범위는 `docs/distributed-build.md` 참조.

---

## 테스트 구조

테스트 프로젝트는 소스 코드 구조와 동일하게 구성됩니다:

```
tests/
├── Dotori.Tests.Build/           # 빌드 시스템 테스트
├── Dotori.Tests.Parsing/         # DSL 파서 테스트
├── Dotori.Tests.Generators/      # 빌드 파일 생성 테스트
├── Dotori.Tests.Graph/           # 의존성 그래프 테스트
├── Dotori.Tests.PackageManager/  # 패키지 관리 테스트
├── Dotori.Tests.LanguageServer/  # LSP 테스트
├── Dotori.Tests.BuildServer/     # 분산 빌드 테스트
├── platform-smoke/               # 플랫폼 통합 테스트
├── Directory.Build.props          # 테스트 공통 설정
└── MSTestSettings.cs             # MSTest 설정
```

**테스트 실행:**
```bash
dotnet test
```

---

## 문서 구조

| 파일 | 용도 |
|------|------|
| `docs/index.md` | 문서 목차 및 진입점 |
| `docs/cli.md` | CLI 명령어 레퍼런스 |
| `docs/dotori-file.md` | .dotori DSL 파일 형식 및 문법 |
| `docs/examples.md` | 사용 예제 및 튜토리얼 |
| `docs/building.md` | Dotori 자체 빌드 방법 |
| `docs/distributed-build.md` | 분산 빌드 설정 및 사용법 |
| `deploy/registry/README.md` | Verdaccio 레지스트리 배포 및 인증 설정 |
| `docs/dotori.1` | Man page (Unix 매뉴얼 페이지) |

---

## 지원하는 빌드 시스템 생성기

Dotori.Core/Generators 모듈이 지원하는 형식 (`export build-system --format <형식>`):

| 형식 플래그 | 생성 파일 | 용도 |
|-----------|---------|------|
| `cmake` | CMakeLists.txt | Cross-platform 빌드 |
| `meson` | meson.build | 빠른 빌드 시스템 |
| `ninja` | build.ninja | 최고 성능 빌드 |
| `makefile` | Makefile | 전통적인 Unix 빌드 |
| `vcxproj` | .vcxproj, .vcxproj.filters | Windows 개발 환경 |
| `pbxproj` | *.xcodeproj/project.pbxproj | macOS/iOS 개발 환경 |

---

## 핵심 개념

### .dotori 파일 (DSL)
- C++ 프로젝트의 메인 설정 파일
- 타겟, 의존성, 컴파일러 옵션 등을 정의
- 다양한 빌드 시스템으로 변환 가능

### 빌드 타겟 (Build Target)
- 실행 파일, 정적 라이브러리, 동적 라이브러리 등의 빌드 대상
- 소스 파일, 의존성, 컴파일 옵션으로 구성

### 의존성 그래프 (Dependency Graph)
- 타겟 간의 의존 관계를 그래프로 표현
- 빌드 순서 결정 및 병렬 빌드 지원
- 순환 참조 감지

### 툴체인 (Toolchain)
- 컴파일러 (GCC, Clang, MSVC), 링커 등의 도구 모음
- 플랫폼 및 아키텍처별로 다양한 toolchain 지원

### 버전 제약 조건 (Version Constraint)
- 패키지 버전 범위 지정 (SemVer 기반)
- PubGrub 알고리즘으로 의존성 해석

---

## 중요한 파일

| 파일 | 설명 |
|------|------|
| `SPEC.md` | 기술 사양 및 구현 현황/로드맵 |
| `AGENTS.md` | LLM 에이전트 작업 가이드 (참조용) |
| `Directory.Build.props` | MSBuild 공통 설정 |
| `.editorconfig` | 코드 스타일 설정 |
| `docker-compose.yml` | Docker 개발 환경 구성 |
| `build.sh` / `build.ps1` | 빌드 스크립트 |

---

## 모듈 간 의존성

```
Dotori.Cli
├── Dotori.Core (빌드, 파싱, 생성)
├── Dotori.Grpc (원격 빌드 실행)
├── Dotori.PackageManager (의존성 관리)
└── Dotori.LanguageServer (LSP 지원)

Dotori.Core
└── (직접 프로젝트 참조 없음)

Dotori.BuildServer
├── Dotori.Core
└── Dotori.Grpc

Dotori.Worker
├── Dotori.Core
└── Dotori.Grpc

Dotori.LanguageServer
└── Dotori.Core

Dotori.PackageManager
└── Dotori.Core

Dotori.Grpc
└── Dotori.Core
```

분산 빌드 서버는 gRPC로 Worker와 통신합니다. 위 의존성 트리는 `.csproj`의 직접 프로젝트 참조를 나타냅니다. 레지스트리는 .NET 프로젝트가 아닌 외부 Verdaccio 서비스입니다.
