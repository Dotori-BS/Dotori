# IDE 통합

Dotori 프로젝트는 `setup ide` 한 번으로 IDE가 읽을 빌드 모델과 C/C++ 코드 모델을
만들 수 있습니다. 프로젝트 루트에서 다음 명령으로 시작합니다.

```bash
dotori setup ide --ide <ide>
dotori setup ide --ide <ide> --check   # 생성 파일 누락·변경·노후 여부 확인
dotori setup ide --ide <ide> --dry-run # 쓰지 않고 생성 예정 파일 표시
dotori doctor --ide <ide>              # 도구 체인과 IDE 설정 종합 진단
dotori watch --ide             # .dotori 변경 시 다시 생성
```

`dotori` 실행 파일은 IDE가 상속하는 `PATH`에 있어야 합니다. 생성 파일은 원본이
아니므로 필요할 때 다시 만들고, 팀 정책에 따라 버전 관리 여부를 정합니다.

## IDE별 사용법

| IDE | 준비 명령 | 여는 파일/폴더 | Dotori 언어 지원 |
|-----|-----------|----------------|------------------|
| CLion | `dotori setup ide --ide clion` | 프로젝트 폴더의 `CMakeLists.txt` | `editors/jetbrains` 플러그인 |
| Rider | `dotori setup ide --ide rider` | 프로젝트 폴더의 `CMakeLists.txt` | `editors/jetbrains` 플러그인 |
| Visual Studio | `dotori setup ide --ide visual-studio --target windows-x64` | 생성된 `<Project>.sln` | `editors/visualstudio` VSIX |
| VS Code | `dotori setup ide --ide vscode` | 프로젝트 폴더 | `editors/vscode` 확장 |
| Zed | `dotori setup ide --ide zed` | 프로젝트 폴더 | `editors/zed` 확장 |

`--ide auto`는 CMake, compilation database, VS Code와 Zed 작업 파일을 만듭니다.
Windows에서 `--ide all`을 쓰면 CMake 파일과 Visual Studio solution도 함께 만듭니다.

`setup ide --check`는 생성 파일이 없거나 `.dotori`보다 오래됐거나 에디터 설정의
내용이 바뀌면 1을 반환하므로 CI에서도 사용할 수 있습니다. `dotori doctor --fix`는
컴파일러와 필수 도구를 확인한 뒤 IDE 파일을 다시 생성합니다.

## 공통으로 생성되는 파일

- `compile_commands.json`: clangd 기반 코드 탐색, 진단, 자동 완성
- `.clangd`: 선택한 컴파일러 정보를 clangd에 전달
- `CMakeLists.txt`, `CMakePresets.json`: CLion, Rider, VS Code CMake Tools용
- `.sln`, `.vcxproj`, `.vcxproj.filters`: Visual Studio용
- `.vscode/*`, `.zed/tasks.json`: 빌드·테스트·실행·새로 고침 작업

CMake preset은 `<target>-debug`와 `<target>-release` 이름을 사용합니다.

```bash
cmake --preset windows-x64-debug
cmake --build --preset windows-x64-debug
ctest --preset windows-x64-debug
```

## 확장 빌드

### VS Code

Node.js와 npm이 필요합니다.

```bash
cd editors/vscode
npm install
npm run package
```

생성된 VSIX를 VS Code의 **Extensions: Install from VSIX...**로 설치합니다.

### Zed

Rust가 필요합니다. 로컬 검증은 다음과 같이 실행합니다.

```bash
cd editors/zed
cargo build --locked --release --target wasm32-wasip1
```

Zed의 **Extensions: Install Dev Extension**에서 `editors/zed` 폴더를 선택합니다.
문법은 이 저장소의 고정 커밋에서 불러옵니다. 별도 문법 저장소는 필요하지 않습니다.
`.dotori` 및 `이름.dotori` 파일에 문법 강조와 LSP 기능을 제공합니다.
Dotori가 PATH에 없다면 Zed 설정에서 `lsp.dotori-lsp.binary.path`를 지정합니다.
인자는 기본 `["lsp"]`이며 `binary.arguments`와 `binary.env`로 변경할 수 있습니다.
설정 예시는 [Zed 확장 README](../editors/zed/README.md)를 참조하세요.

### CLion / Rider

JetBrains 2026.2 플랫폼에 맞춰 JDK 25가 필요합니다.

```bash
cd editors/jetbrains
./gradlew test buildPlugin verifyPluginStructure verifyPluginProjectConfiguration
```

`build/distributions`의 ZIP을 IDE의 **Install Plugin from Disk...**로 설치합니다.
플러그인은 JetBrains 2026.2 플랫폼의 LSP API를 사용합니다.
Windows에서는 `gradlew.bat`를 사용합니다. **Settings | Languages & Frameworks | Dotori**에서
실행 파일 경로를 지정할 수 있으며 기본값은 PATH의 `dotori`입니다.
`.dotori` 및 `이름.dotori` 파일을 인식하고 문법 강조, 자동 완성, 진단, Hover,
정의 이동과 포맷팅을 제공합니다. 경로 변경 후 언어 서버를 재시작하거나 프로젝트를 다시 엽니다.
자세한 설치 절차는 [JetBrains 플러그인 README](../editors/jetbrains/README.md)를 참조하세요.

### Visual Studio

.NET SDK와 Visual Studio SDK 빌드 도구가 필요하며 NuGet 패키지로 복원됩니다.

```powershell
dotnet build editors\visualstudio\Dotori.VisualStudio.csproj -c Release
```

`editors/visualstudio/bin/Release/net472/Dotori.VisualStudio.vsix`를 실행해 설치합니다.
VSIX는 `.dotori` TextMate 문법과 `dotori lsp` 언어 클라이언트를 제공합니다.

## 문제 해결

실제 `dotori lsp` 프로세스의 표준 입출력 프로토콜은 저장소 루트에서 다음과 같이 검증합니다. Python 3과 빌드한 CLI가 필요합니다.

```bash
dotnet build src/Dotori.Cli/Dotori.Cli.csproj -c Release
python tests/lsp-smoke.py
# 배포 바이너리 검증 예: python tests/lsp-smoke.py --cli build/cli/dotori.exe
```

이 검증은 초기화·종료, 문서 열기·수정·닫기와 진단 갱신, 자동 완성, 호버, 의존성 정의 이동, 옵션 참조·이름 바꾸기, 포매팅을 확인합니다. 한글·공백이 있는 경로, 문자열 요청 ID, 큰 진단 알림의 프레이밍도 확인하며, OS별 CI smoke 작업에서 실행합니다. 각 에디터 UI에서 확장을 설치하고 사용하는 과정은 별도 확인이 필요합니다.

- 코드 탐색이 오래된 경우 `dotori setup ide --ide <ide>`를 다시 실행합니다.
- clangd가 시스템 헤더를 못 찾으면 `--compiler clang`, `--compiler brew-llvm`처럼
  실제 빌드에 쓰는 컴파일러를 지정합니다. 이 선택은 `.clangd`뿐 아니라
  `CMakePresets.json`의 C/C++ 컴파일러에도 반영됩니다.
- 언어 서버가 시작되지 않으면 IDE 내장 터미널에서 `dotori lsp`가 실행되는지 먼저
  확인합니다.
- Dotori 언어 서버는 선언된 빌드 옵션의 참조 찾기와 이름 바꾸기, 문서 포매팅을
  지원합니다. 이름 바꾸기는 키워드나 선언되지 않은 이름에는 적용되지 않습니다.
- Visual Studio solution은 Windows 타겟으로 생성해야 하며, 해당 C++ workload와
  SDK가 설치되어 있어야 합니다.
