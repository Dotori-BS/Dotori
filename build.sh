#!/usr/bin/env bash
# build.sh — Dotori 전체 빌드 스크립트
# 각 컴포넌트를 빌드하여 build/<component>/ 폴더에 출력합니다.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$REPO_ROOT/build"
CONFIG="${CONFIG:-Release}"
RID=""        # --rid  <runtime-id>  (예: linux-x64, win-x64, osx-arm64)
VERSION=""    # --version <ver>       (예: v1.2.3)
INSTALL=false # --install            CLI 빌드 후 시스템에 설치

# 색상 출력
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
info()    { echo -e "${CYAN}[build]${NC} $*"; }
success() { echo -e "${GREEN}[ok]${NC} $*"; }
warn()    { echo -e "${YELLOW}[warn]${NC} $*"; }
error()   { echo -e "${RED}[error]${NC} $*" >&2; }

# 빌드 대상 목록 (--only 옵션으로 필터링 가능)
ALL_TARGETS=(cli build_server worker registry grammar)

# 옵션 파싱
ONLY_TARGETS=()
SKIP_TARGETS=()
SHOW_HELP=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --only)    shift; IFS=',' read -ra ONLY_TARGETS <<< "$1" ;;
        --skip)    shift; IFS=',' read -ra SKIP_TARGETS <<< "$1" ;;
        --config)  shift; CONFIG="$1" ;;
        --rid)     shift; RID="$1" ;;
        --version) shift; VERSION="$1" ;;
        --install) INSTALL=true ;;
        --help|-h) SHOW_HELP=true ;;
        *) error "알 수 없는 옵션: $1"; SHOW_HELP=true ;;
    esac
    shift
done

if $SHOW_HELP; then
    cat <<EOF
사용법: ./build.sh [옵션]

옵션:
  --only <targets>   지정한 대상만 빌드 (쉼표 구분)
  --skip <targets>   지정한 대상은 건너뜀 (쉼표 구분)
  --config <cfg>     빌드 구성 (기본값: Release)
  --rid <rid>        .NET Runtime Identifier (예: linux-x64, win-x64, osx-arm64)
                     지정 시 --self-contained 으로 퍼블리시
  --version <ver>    어셈블리 버전 (예: v1.2.3) — -p:Version 으로 전달
  --install          CLI 빌드 후 시스템에 설치 (manpage 포함)
  --help             이 도움말 출력

대상 목록: ${ALL_TARGETS[*]}
  grammar  — Tree-sitter grammar.js + parser.c 생성 (node, tree-sitter-cli 필요)

예시:
  ./build.sh
  ./build.sh --only cli,build_server,worker --rid linux-x64
  ./build.sh --only cli,build_server,worker --rid win-x64 --version v1.0.0
  ./build.sh --config Debug
  ./build.sh --only cli --install
  ./build.sh --only cli --rid linux-x64 --install
  ./build.sh --only grammar
EOF
    exit 0
fi

should_build() {
    local target="$1"
    if [[ ${#SKIP_TARGETS[@]} -gt 0 ]]; then
        for s in "${SKIP_TARGETS[@]}"; do [[ "$s" == "$target" ]] && return 1; done
    fi
    if [[ ${#ONLY_TARGETS[@]} -gt 0 ]]; then
        for o in "${ONLY_TARGETS[@]}"; do [[ "$o" == "$target" ]] && return 0; done
        return 1
    fi
    return 0
}

# ──────────────────────────────────────────────────────────────────────────────
# .NET 컴포넌트 빌드 헬퍼
# ──────────────────────────────────────────────────────────────────────────────
dotnet_publish() {
    local label="$1"
    local proj="$2"
    local out_dir="$3"
    local extra_args="${4:-}"

    # --rid 가 지정된 경우 self-contained 퍼블리시
    local rid_args=""
    if [[ -n "$RID" ]]; then
        rid_args="-r $RID --self-contained"
    fi

    # --version 이 지정된 경우 어셈블리 버전 주입
    local ver_args=""
    if [[ -n "$VERSION" ]]; then
        ver_args="-p:Version=${VERSION#v}"   # 'v' 접두사 제거 (v1.2.3 → 1.2.3)
    fi

    # macOS: NativeAOT가 Xcode clang을 사용하도록 강제
    local clang_args=""
    if [[ "$(uname)" == "Darwin" ]]; then
        local xcode_clang
        xcode_clang=$(xcrun --find clang 2>/dev/null) || true
        if [[ -n "$xcode_clang" ]]; then
            clang_args="-p:CppCompilerAndLinker=$xcode_clang"
        fi
    fi

    info "[$label] 빌드 중...${RID:+ (rid=$RID)}${VERSION:+ (ver=$VERSION)} → $out_dir"
    mkdir -p "$out_dir"
    # shellcheck disable=SC2086
    dotnet publish "$proj" \
        -c "$CONFIG" \
        -o "$out_dir" \
        --nologo \
        -v quiet \
        $rid_args \
        $ver_args \
        $clang_args \
        $extra_args
    success "[$label] 완료"
}

# ──────────────────────────────────────────────────────────────────────────────
# 1. CLI (NativeAOT) — Language Server 포함
# ──────────────────────────────────────────────────────────────────────────────
build_cli() {
    dotnet_publish "cli" \
        "$REPO_ROOT/src/Dotori.Cli/Dotori.Cli.csproj" \
        "$BUILD_DIR/cli"
}

# ──────────────────────────────────────────────────────────────────────────────
# 2. Build Server
# ──────────────────────────────────────────────────────────────────────────────
build_build_server() {
    dotnet_publish "build_server" \
        "$REPO_ROOT/src/Dotori.BuildServer/Dotori.BuildServer.csproj" \
        "$BUILD_DIR/build_server"
}

# ──────────────────────────────────────────────────────────────────────────────
# 3. Worker
# ──────────────────────────────────────────────────────────────────────────────
build_worker() {
    dotnet_publish "worker" \
        "$REPO_ROOT/src/Dotori.Worker/Dotori.Worker.csproj" \
        "$BUILD_DIR/worker"
}

# ──────────────────────────────────────────────────────────────────────────────
# 4. Registry
# ──────────────────────────────────────────────────────────────────────────────
build_registry() {
    info "[registry] 배포 번들 준비 중... → $BUILD_DIR/registry"
    if [[ -d "$BUILD_DIR/registry" ]]; then
        find "$BUILD_DIR/registry" -mindepth 1 -delete
    fi
    mkdir -p "$BUILD_DIR/registry"
    cp -R "$REPO_ROOT/deploy/registry/." "$BUILD_DIR/registry/"
    success "[registry] 완료"
}

# ──────────────────────────────────────────────────────────────────────────────
# 5. Grammar (Tree-sitter)
# ──────────────────────────────────────────────────────────────────────────────
build_grammar() {
    local grammar_dir="$REPO_ROOT/grammar/tree-sitter-dotori"
    local dotori_bin="$BUILD_DIR/cli/dotori"

    # dotori CLI 바이너리 확인
    if [[ ! -f "$dotori_bin" ]]; then
        error "[grammar] dotori CLI를 찾을 수 없습니다: $dotori_bin"
        error "[grammar] 먼저 ./build.sh --only cli 를 실행하세요."
        return 1
    fi

    # node / npm 확인
    if ! command -v node &>/dev/null; then
        error "[grammar] node를 찾을 수 없습니다. Node.js를 설치하세요."
        return 1
    fi

    info "[grammar] grammar.js 생성 중..."
    "$dotori_bin" export grammar --format tree-sitter \
        --output "$grammar_dir/grammar.js"

    info "[grammar] highlights.scm 생성 중..."
    "$dotori_bin" export grammar --format zed \
        --output "$grammar_dir/queries/highlights.scm"

    # tree-sitter-cli 설치
    if [[ ! -f "$grammar_dir/node_modules/.bin/tree-sitter" ]]; then
        info "[grammar] npm install (tree-sitter-cli)..."
        npm install --prefix "$grammar_dir" --silent
    fi

    info "[grammar] tree-sitter generate..."
    "$grammar_dir/node_modules/.bin/tree-sitter" generate \
        --build-path "$grammar_dir"

    # WASM 빌드 (Zed 확장용, emcc 필요)
    if command -v emcc &>/dev/null; then
        local wasm_out="$BUILD_DIR/zed/grammars/dotori"
        info "[grammar] tree-sitter build --wasm → $wasm_out/dotori.wasm"
        mkdir -p "$wasm_out"
        (cd "$grammar_dir" && node_modules/.bin/tree-sitter build --wasm \
            --output "$wasm_out/dotori.wasm")
        success "[grammar] WASM 빌드 완료 → $wasm_out/dotori.wasm"
    else
        warn "[grammar] emcc 없음 — WASM 빌드 건너뜀 (Zed WASM 확장 불가)"
    fi

    success "[grammar] 완료 → $grammar_dir/src/parser.c"
}

# ──────────────────────────────────────────────────────────────────────────────
# CLI 설치
# ──────────────────────────────────────────────────────────────────────────────
install_cli() {
    local binary="$BUILD_DIR/cli/dotori"
    local manpage="$REPO_ROOT/docs/dotori.1"

    if [[ ! -f "$binary" ]]; then
        error "[install] CLI 바이너리를 찾을 수 없습니다: $binary"
        error "[install] 먼저 ./build.sh --only cli 를 실행하세요."
        return 1
    fi

    echo ""
    if [[ $EUID -eq 0 ]]; then
        # ── root: /usr/local/bin 에 직접 설치 ────────────────────────────────
        local bin_dir="/usr/local/bin"
        local man_dir="/usr/local/share/man/man1"

        info "[install] root 권한으로 ${bin_dir}/dotori 에 설치합니다..."
        install -m 755 "$binary" "$bin_dir/dotori"

        if [[ -f "$manpage" ]]; then
            mkdir -p "$man_dir"
            install -m 644 "$manpage" "$man_dir/dotori.1"
            # mandb가 있으면 man 색인 갱신 (없어도 무시)
            command -v mandb &>/dev/null && mandb -q 2>/dev/null || true
            success "[install] manpage 설치 완료: ${man_dir}/dotori.1"
        fi

        success "[install] 설치 완료: ${bin_dir}/dotori"
    else
        # ── 일반 사용자: ~/.local/bin 에 설치할지 확인 ───────────────────────
        local bin_dir="$HOME/.local/bin"
        local man_dir="$HOME/.local/share/man/man1"

        echo -e "${YELLOW}[install]${NC} root 권한이 없습니다."
        echo -e "         ${CYAN}${bin_dir}/dotori${NC} 에 설치하시겠습니까? [Y/n] \c"
        read -r answer </dev/tty
        answer="${answer:-Y}"

        if [[ "$answer" =~ ^[Yy] ]]; then
            mkdir -p "$bin_dir"
            install -m 755 "$binary" "$bin_dir/dotori"

            if [[ -f "$manpage" ]]; then
                mkdir -p "$man_dir"
                install -m 644 "$manpage" "$man_dir/dotori.1"
                success "[install] manpage 설치 완료: ${man_dir}/dotori.1"
            fi

            success "[install] 설치 완료: ${bin_dir}/dotori"

            # PATH 미등록 안내
            if [[ ":$PATH:" != *":${bin_dir}:"* ]]; then
                echo ""
                warn "[install] ${bin_dir} 가 PATH에 등록되어 있지 않습니다."
                warn "         ~/.bashrc 또는 ~/.zshrc 에 아래 줄을 추가하세요:"
                warn "           export PATH=\"\$HOME/.local/bin:\$PATH\""
            fi
        else
            info "[install] 설치를 취소했습니다."
        fi
    fi
}

# ──────────────────────────────────────────────────────────────────────────────
# 메인
# ──────────────────────────────────────────────────────────────────────────────
echo ""
echo -e "${CYAN}━━━ Dotori 빌드 ━━━${NC}  config=${CONFIG}"
echo ""

FAILED=()

run_target() {
    local target="$1"
    if should_build "$target"; then
        "build_$target" || { error "[$target] 빌드 실패"; FAILED+=("$target"); }
    else
        info "[$target] 건너뜀"
    fi
}

for t in "${ALL_TARGETS[@]}"; do
    run_target "$t"
done

echo ""
if [[ ${#FAILED[@]} -eq 0 ]]; then
    success "모든 빌드 완료 → $BUILD_DIR/"
else
    error "실패한 대상: ${FAILED[*]}"
    exit 1
fi

# --install 플래그가 있으면 CLI 설치 수행
if $INSTALL; then
    install_cli || exit 1
fi
