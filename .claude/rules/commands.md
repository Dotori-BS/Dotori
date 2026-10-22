# 사용 가능한 명령어

Dotori 프로젝트에서 자주 사용하는 dotnet CLI 명령어들입니다.

---

## 기본 빌드 명령어

### 전체 솔루션 빌드
```bash
$ dotnet build Dotori.slnx
```

### 특정 프로젝트만 빌드
```bash
$ dotnet build src/Dotori.Core/Dotori.Core.csproj
$ dotnet build src/Dotori.Cli/Dotori.Cli.csproj
$ dotnet build src/Dotori.PackageManager/Dotori.PackageManager.csproj
```

### Release 빌드 (최적화)
```bash
$ dotnet build Dotori.slnx -c Release
```

---

## 빌드 결과물 실행

### CLI 도구 실행
```bash
$ dotnet run -p src/Dotori.Cli/Dotori.Cli.csproj -- [명령어] [옵션]
```

#### 예시
```bash
# 빌드 시스템 내보내기
$ dotnet run -p src/Dotori.Cli/Dotori.Cli.csproj -- export build-system --format cmake

# 프로젝트 빌드
$ dotnet run -p src/Dotori.Cli/Dotori.Cli.csproj -- build
```

### 레지스트리 서버 실행 (Verdaccio)
인증 파일 준비는 `deploy/registry/README.md` 참조.
```bash
$ docker compose --profile registry up -d registry
```

### 빌드 서버 실행
```bash
$ dotnet run -p src/Dotori.BuildServer/Dotori.BuildServer.csproj
```

### 워커 실행
```bash
$ dotnet run -p src/Dotori.Worker/Dotori.Worker.csproj
```

---

## 테스트 명령어

### 전체 테스트 실행
```bash
$ dotnet test
```

### 특정 테스트 프로젝트만 실행
```bash
$ dotnet test tests/Dotori.Tests.Build/
$ dotnet test tests/Dotori.Tests.Parsing/
$ dotnet test tests/Dotori.Tests.PackageManager/
$ dotnet test tests/Dotori.Tests.Generators/
$ dotnet test tests/Dotori.Tests.Graph/
$ dotnet test tests/Dotori.Tests.LanguageServer/
$ dotnet test tests/Dotori.Tests.BuildServer/
```

### 특정 테스트만 실행
```bash
$ dotnet test --filter "TestClass.TestMethod"
$ dotnet test --filter "TestClass"
```

### 테스트 상세 출력
```bash
$ dotnet test -v detailed
$ dotnet test -v diagnostic
```

### 테스트 결과 보고서 생성
```bash
$ dotnet test --logger "trx;LogFileName=test-results.trx"
```

---

## 빌드 정리 및 캐시

### 빌드 결과물 정리
```bash
$ dotnet clean Dotori.slnx
```

### 특정 프로젝트만 정리
```bash
$ dotnet clean src/Dotori.Core/Dotori.Core.csproj
```

---

## 프로젝트 관리

### 프로젝트 정보 확인
```bash
$ dotnet list package
$ dotnet list package --outdated
```

### 의존성 추가
```bash
$ dotnet add package [패키지명]
$ dotnet add src/Dotori.Core/ package [패키지명]
```

### 의존성 제거
```bash
$ dotnet remove package [패키지명]
```

### 의존성 복원
```bash
$ dotnet restore
```

---

## 개발 관련 명령어

### NativeAOT 발행 (배포)
```bash
$ dotnet publish -c Release -p src/Dotori.Cli/Dotori.Cli.csproj
```

### 코드 분석/린트
```bash
$ dotnet format
$ dotnet format --verify-no-changes
```

### 프로젝트 재구축 (clean + build)
```bash
$ dotnet clean Dotori.slnx && dotnet build Dotori.slnx
```

---

## 스크립트를 통한 빌드

### Unix/Linux/macOS
```bash
$ ./build.sh
```

### Windows PowerShell
```powershell
$ .\build.ps1
```

---

## Docker를 이용한 서비스 실행

### Docker Compose로 분산 빌드 서비스 실행
`DOTORI_SERVER_TOKEN`, `DOTORI_WORKER_TOKEN`을 설정한 뒤 실행합니다. 레지스트리는 별도의 `registry` 프로필로 실행합니다.
```bash
$ docker compose up
```

### 특정 서비스만 실행
```bash
$ docker compose --profile registry up -d registry
$ docker compose up server
$ docker compose up worker1 worker2
```

### 서비스 중지
```bash
$ docker compose down
```

---

## 자주 사용하는 명령어 조합

### 완전한 빌드 및 테스트 (로컬 개발)
```bash
$ dotnet clean Dotori.slnx && \
  dotnet build Dotori.slnx && \
  dotnet test
```

### 특정 기능 개발 및 테스트
```bash
# 1. 해당 모듈 빌드
$ dotnet build src/Dotori.Core/Dotori.Core.csproj

# 2. 해당 모듈 테스트만 실행
$ dotnet test tests/Dotori.Tests.Build/

# 3. CLI로 기능 테스트
$ dotnet run -p src/Dotori.Cli/Dotori.Cli.csproj -- export build-system --format cmake
```

### Release 빌드 및 배포 준비
```bash
$ dotnet clean Dotori.slnx -c Release && \
  dotnet build Dotori.slnx -c Release && \
  dotnet publish -c Release -p src/Dotori.Cli/Dotori.Cli.csproj
```

---

## 문제 해결

### 의존성 캐시 문제 해결
```bash
$ dotnet restore --force
$ dotnet clean && dotnet build
```

### NuGet 캐시 초기화
```bash
$ dotnet nuget locals all --clear
$ dotnet restore
```

### 빌드 캐시 전체 초기화
```bash
$ rm -rf **/bin **/obj
$ dotnet clean Dotori.slnx
```
