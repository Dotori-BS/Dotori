# 분산 빌드 서버

dotori는 gRPC 기반 분산 빌드 시스템을 지원합니다.
빌드 서버(Coordinator)가 컴파일 작업을 워커(Worker)들에게 분산하여 빌드 속도를 향상시킵니다.

---

## 아키텍처

```
dotori CLI
    │
    │ gRPC (BuildCoordinator)
    ▼
BuildServer (Coordinator)
    │
    ├── gRPC (BuildWorker) ──→ Worker 1
    ├── gRPC (BuildWorker) ──→ Worker 2
    └── gRPC (BuildWorker) ──→ Worker N
```

- **Coordinator**: CLI로부터 컴파일/링크 요청을 받아 워커에게 분배. 오브젝트 파일 캐시 관리.
- **Worker**: 실제 컴파일러/링커를 실행하여 결과를 반환. 지원 타겟 목록을 코디네이터에 등록.

원격 실행기(`RemoteExecutor`), 원격 인자 변환 및 gRPC 통신은 `Dotori.Grpc` 프로젝트에 모여 있습니다. CLI가 이를 참조하며, 로컬 빌드 엔진인 `Dotori.Core`는 gRPC 의존성 없이 사용할 수 있습니다.

---

## 빠른 시작 (Docker Compose)

제공된 `docker-compose.yml`을 사용하면 코디네이터 1대 + 워커 2대를 한 번에 실행할 수 있습니다.

```bash
# 저장소 루트에서 실행
docker compose up server worker1 worker2
```

기본 포트:

| 서비스 | 포트 |
|--------|------|
| BuildServer (Coordinator) | `5100` |
| Worker 1 | Compose 내부 `5100` |
| Worker 2 | Compose 내부 `5100` |

Compose 실행 전 서로 다른 고강도 토큰을 설정합니다. Worker 포트는 호스트에 공개하지 않습니다.

```bash
export DOTORI_SERVER_TOKEN="$(openssl rand -hex 32)"
export DOTORI_WORKER_TOKEN="$(openssl rand -hex 32)"
docker compose up server worker1 worker2
```

빌드 시 분산 빌드 서버를 사용하려면:

```bash
dotori build --remote http://localhost:5100
```

---

## 로컬 툴체인 전송

`--remote`만 지정하면 기존처럼 워커에 설치된 도구를 사용합니다. `--remote-toolchain`을 추가하면 명시한 로컬 컴파일러·링커·SDK와 프로젝트 입력을 전송해 워커 캐시에서 실행합니다. 컴파일러는 클라이언트에서 선택하고 빌드 명령을 계획하므로 로컬에도 있어야 합니다.

```bash
dotori build --remote http://build-server:5100 --remote-toolchain remote-tools.json
```

예를 들어 프로젝트 루트에 다음 설정을 만듭니다. 실제 설치 경로와 버전에 맞춰 수정합니다.

```json
{
  "toolchainRoots": [
    "C:/Program Files/LLVM",
    "C:/Program Files/Microsoft Visual Studio/18/Community/VC/Tools/MSVC/14.51.36231",
    "C:/Program Files (x86)/Windows Kits/10/Include/10.0.26100.0",
    "C:/Program Files (x86)/Windows Kits/10/Lib/10.0.26100.0",
    "C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0"
  ],
  "inputRoots": ["."],
  "inputExcludes": ["private/*", "*.pfx"],
  "workingDirectory": ".",
  "inheritEnvironment": ["INCLUDE", "LIB", "LIBPATH"],
  "environment": {}
}
```

- 경로는 JSON 파일 기준이며, `toolchainRoots`는 디렉토리 또는 개별 파일입니다. 컴파일러뿐 아니라 보조 실행 파일, DLL/공유 라이브러리, 리소스 헤더, SDK, 표준 라이브러리도 포함해야 합니다. 종속 파일을 자동 탐지하는 기능은 아닙니다.
- Linux는 예를 들어 독립 설치한 LLVM 디렉토리와 sysroot를 선언합니다. 필요한 공유 라이브러리 경로는 `environment`의 `LD_LIBRARY_PATH`에 선언할 수 있습니다. GCC는 `cc1plus`, assembler, linker 및 GCC 지원 파일도 포함해야 합니다. `/usr` 전체를 지정하기보다 필요한 툴체인 설치 경로를 사용합니다.
- `inputRoots`는 의존 프로젝트와 생성 파일을 포함하는 프로젝트 경로입니다. 소스·헤더를 스냅샷하고, 작업이 직접 참조하는 입력도 추가합니다. `.git`, `.env*`, `.aws`, `.ssh`, `.gnupg`, `.dotori-cache`의 자동 탐색은 제외하며, 캐시 안의 필요한 오브젝트·라이브러리 등은 작업 입력으로 별도 수집합니다. `inputExcludes`는 각 입력 root 기준의 `*`/`?` 패턴으로 자동 스냅샷 대상을 줄입니다. 작업이 직접 참조한 입력은 이 패턴과 별도로 수집합니다. 디렉토리 심볼릭 링크는 자동으로 따라가지 않으므로 필요한 링크 경로를 명시적인 root로 선언합니다. 파일 심볼릭 링크는 내용으로 전송합니다.
- 실행 인수와 선언한 환경 변수의 경로를 워커 캐시 경로로 바꿉니다. 워커의 툴체인 `PATH`를 상속하지 않고 전송된 실행 파일들의 디렉토리로 구성합니다. JSON에는 상속하려는 환경 변수만 선언합니다.
- 파일은 SHA-256으로 확인하고 1 MiB 단위로 전송합니다. Coordinator와 Worker 각각에 없는 내용만 업로드하고, 워커는 같은 툴체인 스냅샷을 재사용합니다. 정상적인 컴파일 결과는 툴체인·환경·입력·인수를 포함한 키로 캐시합니다. 링크는 매번 실행 여부를 기존 증분 빌드 로직이 결정합니다.
- 출력 디렉토리에서 생성된 오브젝트·실행 파일 및 부가 파일을 검증해 클라이언트로 가져옵니다. `pre-build`, `post-build`와 manifest embedding은 기존처럼 로컬에서 수행합니다.

워커는 다음 설정으로 업로드 실행을 명시적으로 허용해야 합니다.

```bash
DOTORI_ALLOW_UPLOADED_TOOLCHAINS=true DOTORI_WORKER_TOKEN=<token> dotori-worker
```

같은 OS·CPU의 워커를 선택하므로 Windows 툴체인을 Linux 워커에서 실행하지 않습니다. 해당 워커에 컴파일러가 설치돼 있지 않아도 됩니다. 이 기능은 가상 머신이나 파일 시스템 가상화가 아니며 OS의 로더·시스템 런타임은 호스트가 제공합니다. 설정에 없는 외부 경로나 응답 파일 안의 절대 경로까지 자동으로 수집·변환하지 않습니다.

PCH와 C++ Modules는 컴파일러 상태 파일에 작업 디렉토리의 절대 경로를 포함할 수 있어 관련 컴파일을 로컬에 유지합니다. PCM/IFC 상태 파일을 직접 입력받는 링크와 Swift 컴파일·링크도 로컬에 유지합니다. 이 경우 로그에 로컬 실행을 표시하며, 일반 오브젝트 입력의 링크는 전송할 수 있습니다. 원격 서버 전송 실패 시 기존 로컬 fallback이 적용되지만, 워커의 컴파일 오류나 업로드 실행 거부는 빌드 실패로 반환합니다.

콘텐츠 캐시는 기본적으로 자동 정리하지 않습니다. 서비스 중지 후 전용 캐시 경로를 정리하거나 운영 환경의 디스크 정책으로 관리합니다. 업로드 모드를 사용할 때도 기존 서버/워커 토큰 및 HTTPS 설정을 적용합니다.

### 전송 경로 통합 검증

Windows 또는 Linux의 로컬 Clang/LLD와 Python 3이 필요합니다. 임시 Coordinator/Worker를 시작하고, 워커의 `PATH`를 비운 상태에서 컴파일러·링커·SDK 헤더를 전송해 실제 빌드를 검증합니다. Linux는 컴파일러의 공유 라이브러리도 테스트용 번들에 포함합니다.

```bash
dotnet build Dotori.slnx
python tests/remote-toolchain-smoke.py
```

이 스모크는 x64 Windows/Linux용 컴파일·링크와 출력 실행, 입력 재전송 생략, 헤더 변경 시 캐시 무효화, 잘못된 토큰 거부를 확인합니다. `--cli`, `--server`, `--worker`로 배포 바이너리도 지정할 수 있습니다.

## 수동 설치 및 실행 (기존 워커 도구 모드)

### 워커 실행

```bash
# 바이너리 직접 실행
dotori-worker --port 5100

# 또는 환경 변수로 설정
WORKER_PORT=5100 dotori-worker
```

지원 타겟은 워커가 실행되는 호스트의 툴체인을 자동으로 탐색하여 결정됩니다.

### 빌드 서버(Coordinator) 실행

```bash
# 환경 변수로 워커 목록 지정
DOTORI_WORKERS=http://worker1:5100,http://worker2:5100 dotori-server --port 5100

# 단일 바이너리 옵션
dotori-server \
    --port 5100 \
    --workers http://worker1:5100,http://worker2:5100
```

---

## 환경 변수

### BuildServer (Coordinator)

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `DOTORI_WORKERS` | — | 워커 주소 목록 (쉼표 구분). 예: `http://w1:5100,http://w2:5100` |
| `ASPNETCORE_URLS` | `http://+:5100` | 서버 수신 주소 |
| `BUILD_CACHE_DIR` | `~/.dotori/build-cache` | 오브젝트 캐시 저장 경로 |
| `BUILD_CACHE_MAX_BYTES` | `0` | 캐시 최대 크기(bytes). `0`이면 용량 제한 비활성화 |
| `BUILD_CACHE_MAX_AGE_DAYS` | `0` | 마지막 사용 후 보존 기간(days). `0`이면 만료 비활성화 |
| `BUILD_CONTENT_CACHE_DIR` | `~/.dotori/coordinator-content` | 전송 모드 파일 및 컴파일 결과 메타데이터 캐시 |
| `DOTORI_MAX_BLOB_BYTES` | `8589934592` | 전송 파일 하나의 최대 크기 (청크는 1 MiB) |
| `DOTORI_SERVER_TOKEN` | — | CLI → Coordinator 인증 토큰. 미설정 시 loopback 요청만 허용 |
| `DOTORI_SERVER_TOKEN_FILE` | — | 토큰을 읽을 secret 파일 경로 |
| `DOTORI_REQUIRE_HTTPS` | `false` | `true`이면 HTTPS가 아닌 gRPC 요청을 거부 |
| `DOTORI_WORKER_FAILURE_COOLDOWN_SECONDS` | `30` | 전송 실패 worker를 스케줄링에서 제외하는 기본 시간. 연속 실패 시 지수 증가 |
| `DOTORI_WORKER_HEALTH_TIMEOUT_SECONDS` | `5` | Coordinator가 worker capability를 조회할 때의 제한 시간 |

### Worker

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `WORKER_PORT` | `5100` | 수신 포트 |
| `ASPNETCORE_URLS` | `http://+:5100` | 워커 수신 주소 |
| `DOTORI_WORKER_TOKEN` | — | Coordinator → Worker 인증 토큰. 미설정 시 loopback 요청만 허용 |
| `DOTORI_WORKER_TOKEN_FILE` | — | 토큰을 읽을 secret 파일 경로 |
| `DOTORI_ALLOWED_EXECUTABLES` | 기본 C/C++ 컴파일러·링커·아카이버·어셈블러 | 쉼표로 구분한 worker 실행 파일 allowlist. Windows `.exe` 접미사는 생략 가능 |
| `DOTORI_REQUIRE_HTTPS` | `false` | `true`이면 HTTPS가 아닌 gRPC 요청을 거부 |
| `DOTORI_MAX_INPUT_BYTES` | `67108864` | 요청 하나의 소스/오브젝트 또는 전송 모드 프로젝트 입력 총 크기 제한 (툴체인 제외) |
| `DOTORI_MAX_ARGUMENTS` | `4096` | 컴파일러·링커 인수 최대 개수 |
| `DOTORI_SUPPORTED_TARGETS` | 자동 감지 | worker가 처리할 target triple 목록(쉼표 구분). 컨테이너·교차 toolchain 환경에서는 명시 권장 |
| `DOTORI_EXECUTION_TIMEOUT_SECONDS` | `300` | worker 프로세스 최대 실행 시간 |
| `DOTORI_ALLOW_UPLOADED_TOOLCHAINS` | `false` | 전송받은 툴체인의 실행 및 콘텐츠 API 허용 |
| `DOTORI_CONTENT_CACHE_DIR` | `~/.dotori/worker-content` | 워커 콘텐츠·복원 툴체인·작업 디렉토리 저장 경로 |
| `DOTORI_MAX_BLOB_BYTES` | `8589934592` | 전송 파일 하나의 최대 크기 |

운영 환경에서는 토큰 값을 환경 변수에 직접 넣기보다 Docker/Kubernetes secret 파일을 권장합니다.
`DOTORI_*_TOKEN`이 설정돼 있으면 파일보다 우선합니다.
Coordinator와 Worker는 평문 `http://` 주소에서도 gRPC가 동작하도록 해당
엔드포인트를 HTTP/2(h2c) 전용으로 구성합니다. 앞단 프록시를 사용할 때도
upstream 연결에서 HTTP/2를 유지해야 합니다.

```yaml
services:
  server:
    secrets: [server_token, worker_token]
    environment:
      DOTORI_SERVER_TOKEN_FILE: /run/secrets/server_token
      DOTORI_WORKER_TOKEN_FILE: /run/secrets/worker_token
  worker:
    secrets: [worker_token]
    environment:
      DOTORI_WORKER_TOKEN_FILE: /run/secrets/worker_token
```

HTTPS는 ASP.NET Core 인증서를 구성한 뒤 양쪽 서비스에
`DOTORI_REQUIRE_HTTPS=true`를 지정할 때만 강제됩니다. 기본값은 `false`입니다.

---

## Docker Compose 상세 설정

### 기본 구성

```yaml
services:
  worker1:
    build:
      context: .
      dockerfile: src/Dotori.Worker/Dockerfile
    ports:
      - "5101:5100"

  worker2:
    build:
      context: .
      dockerfile: src/Dotori.Worker/Dockerfile
    ports:
      - "5102:5100"

  server:
    build:
      context: .
      dockerfile: src/Dotori.BuildServer/Dockerfile
    ports:
      - "5100:5100"
    environment:
      - DOTORI_WORKERS=http://worker1:5100,http://worker2:5100
    depends_on:
      - worker1
      - worker2
```

### 워커 수 확장

```bash
# 워커를 4대로 확장
docker compose up --scale worker=4 server

# 또는 docker-compose.yml에서 복제
services:
  worker:
    build: ...
    deploy:
      replicas: 4
```

---

## gRPC API 명세

빌드 서버는 다음 gRPC 서비스를 제공합니다.

### BuildCoordinator (CLI → Server)

```protobuf
service BuildCoordinator {
    rpc FindMissingBlobs(BlobQuery) returns (BlobQuery);
    rpc UploadBlob(stream BlobChunk) returns (BlobReceipt);
    rpc DownloadBlobs(BlobQuery) returns (stream BlobChunk);
    // 컴파일 요청 (응답은 스트리밍으로 로그 + 결과 전달)
    rpc Compile(CompileRequest) returns (stream CompileEvent);
    // 링크 요청
    rpc Link(LinkRequest) returns (LinkResponse);
    // 등록된 워커 목록 조회
    rpc ListWorkers(ListWorkersRequest) returns (ListWorkersResponse);
}
```

### BuildWorker (Server → Worker)

```protobuf
service BuildWorker {
    rpc FindMissingBlobs(BlobQuery) returns (BlobQuery);
    rpc UploadBlob(stream BlobChunk) returns (BlobReceipt);
    rpc DownloadBlobs(BlobQuery) returns (stream BlobChunk);
    rpc Compile(WorkerCompileRequest) returns (stream WorkerCompileEvent);
    rpc Link(WorkerLinkRequest) returns (WorkerLinkResponse);
    rpc Health(HealthRequest) returns (HealthResponse);
}
```

**CompileRequest 주요 필드:**

| 필드 | 설명 |
|------|------|
| `compiler` | 컴파일러 실행 파일명 (워커 라우팅 힌트) |
| `target_triple` | 빌드 타겟 트리플 (예: `x86_64-unknown-linux-gnu`) |
| `args` | 컴파일러 인수 전체 목록 |
| `source_hash` | 소스 파일 SHA-256 (캐시 조회용) |
| `source_bytes` | 소스 파일 내용 (캐시 히트 시 생략 가능) |
| `source_path` | 원본 파일 경로 (진단 메시지용) |
| `bundle` | 전송 모드의 툴체인·입력 manifest, 실행 파일·출력 경로 및 콘텐츠 digest |

**CompileResult 주요 필드:**

| 필드 | 설명 |
|------|------|
| `success` | 컴파일 성공 여부 |
| `exit_code` | 컴파일러 종료 코드 |
| `obj_bytes` | 컴파일된 오브젝트 파일 바이트 |
| `obj_hash` | 오브젝트 파일 SHA-256 (클라이언트 캐시용) |

---

## 캐시 동작

빌드 서버는 소스, 컴파일러 바이너리, 타겟, 인수, 해석된 include 파일의
콘텐츠 fingerprint를 기반으로 오브젝트 파일을 캐시합니다.

```
클라이언트가 CompileRequest 전송
    │
    ├── [캐시 히트] source_hash 일치 → 저장된 obj_bytes 즉시 반환
    │
    └── [캐시 미스] → 워커에 컴파일 위임 → obj_bytes 수신 → 캐시 저장 → 클라이언트 반환
```

동일한 소스가 변경 없이 다시 빌드 요청되면 워커를 거치지 않고 즉시 반환됩니다.

---

## CLI에서 분산 빌드 사용

```bash
# 기본 분산 빌드
dotori build --remote http://build-server:5100

# 병렬 작업 수 지정 (네트워크 대역폭 고려)
dotori build --remote http://build-server:5100 --jobs 32

# Release 빌드 분산 처리
dotori build --release --remote http://build-server:5100

# 크로스 컴파일 분산 처리 (워커가 해당 타겟을 지원해야 함)
dotori build --target linux-x64 --remote http://build-server:5100
```

`--remote`를 지정하지 않으면 로컬에서 빌드합니다.

---

## 워커 등록 및 상태 확인

등록된 워커 목록은 다음 방법으로 확인할 수 있습니다.

```bash
# dotori CLI로 워커 목록 조회
dotori toolchain --remote http://build-server:5100
```

또는 gRPC 클라이언트로 `ListWorkers` RPC를 직접 호출할 수 있습니다.

WorkerInfo 응답 필드:

| 필드 | 설명 |
|------|------|
| `id` | 워커 고유 ID |
| `address` | 워커 gRPC 주소 |
| `targets` | 지원 타겟 트리플 목록 |
| `busy` | 현재 작업 중 여부 |

Coordinator는 첫 작업 또는 `ListWorkers` 조회 시 worker의 `Health` RPC를 호출합니다. 기존 모드는 `target_triple` 지원 여부를 사용하고, 툴체인 전송 모드는 호스트 OS·CPU와 업로드 실행 허용 여부로 선택합니다. capability 조회 또는 작업 전송이 실패하면 다음 호환 worker로 재시도합니다.

---

## 운영 고려사항

### 네트워크

- 소스 파일과 오브젝트 파일이 네트워크를 통해 전송됩니다.
- 대용량 소스 파일이 많은 경우 기가비트 이상의 내부 네트워크를 권장합니다.
- 캐시 히트율이 높으면 네트워크 부하가 크게 줄어듭니다.

### 워커 사양

- 기존 `--remote` 모드는 워커에 컴파일러가 필요합니다. `--remote-toolchain` 모드는 같은 OS/CPU와 필요한 OS 시스템 런타임이 있으면 됩니다.
- C++ 컴파일은 CPU 바운드이므로 코어 수가 많을수록 효율적입니다.
- 워커당 메모리는 최소 2GB 이상을 권장합니다.

### TLS / 보안

현재 버전은 평문 HTTP/gRPC를 사용합니다. 내부 네트워크(VPN, 사내망)에서 운영하거나,
앞단에 TLS 종료 프록시(nginx, Envoy 등)를 배치하는 것을 권장합니다.
