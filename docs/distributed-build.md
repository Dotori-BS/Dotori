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

## 수동 설치 및 실행

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
| `BUILD_CACHE_DIR` | `/tmp/dotori-cache` | 오브젝트 캐시 저장 경로 |
| `DOTORI_SERVER_TOKEN` | — | CLI → Coordinator 인증 토큰. 미설정 시 loopback 요청만 허용 |
| `DOTORI_SERVER_TOKEN_FILE` | — | 토큰을 읽을 secret 파일 경로 |
| `DOTORI_REQUIRE_HTTPS` | `false` | `true`이면 HTTPS가 아닌 gRPC 요청을 거부 |

### Worker

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `WORKER_PORT` | `5100` | 수신 포트 |
| `ASPNETCORE_URLS` | `http://+:5100` | 워커 수신 주소 |
| `DOTORI_WORKER_TOKEN` | — | Coordinator → Worker 인증 토큰. 미설정 시 loopback 요청만 허용 |
| `DOTORI_WORKER_TOKEN_FILE` | — | 토큰을 읽을 secret 파일 경로 |
| `DOTORI_ALLOWED_EXECUTABLES` | 기본 C/C++ 툴체인 | 쉼표로 구분한 worker 실행 파일 allowlist |
| `DOTORI_REQUIRE_HTTPS` | `false` | `true`이면 HTTPS가 아닌 gRPC 요청을 거부 |
| `DOTORI_MAX_INPUT_BYTES` | `67108864` | 요청 하나의 소스 또는 오브젝트 총 입력 크기 제한 |
| `DOTORI_MAX_ARGUMENTS` | `4096` | 컴파일러·링커 인수 최대 개수 |
| `DOTORI_EXECUTION_TIMEOUT_SECONDS` | `300` | worker 프로세스 최대 실행 시간 |

운영 환경에서는 토큰 값을 환경 변수에 직접 넣기보다 Docker/Kubernetes secret 파일을 권장합니다.
`DOTORI_*_TOKEN`이 설정돼 있으면 파일보다 우선합니다.

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

---

## 운영 고려사항

### 네트워크

- 소스 파일과 오브젝트 파일이 네트워크를 통해 전송됩니다.
- 대용량 소스 파일이 많은 경우 기가비트 이상의 내부 네트워크를 권장합니다.
- 캐시 히트율이 높으면 네트워크 부하가 크게 줄어듭니다.

### 워커 사양

- 워커는 컴파일러가 설치된 호스트여야 합니다.
- C++ 컴파일은 CPU 바운드이므로 코어 수가 많을수록 효율적입니다.
- 워커당 메모리는 최소 2GB 이상을 권장합니다.

### TLS / 보안

현재 버전은 평문 HTTP/gRPC를 사용합니다. 내부 네트워크(VPN, 사내망)에서 운영하거나,
앞단에 TLS 종료 프록시(nginx, Envoy 등)를 배치하는 것을 권장합니다.
