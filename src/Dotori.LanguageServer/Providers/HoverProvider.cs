using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>
/// Provides Markdown hover documentation for .dotori DSL keywords.
/// </summary>
public static class HoverProvider
{
    private static readonly Dictionary<string, string> Descriptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["type"]           = "프로젝트 빌드 타입 (executable, test, static-library, shared-library, header-only)",
            ["std"]            = "C++ 표준 버전 (c++17, c++20, c++23)",
            ["description"]    = "프로젝트 설명 문자열",
            ["runtime-link"]   = "런타임 링크 방식 (static: /MT, dynamic: /MD)",
            ["libc"]           = "C 런타임 라이브러리 (glibc, musl). Linux 타겟 전용",
            ["stdlib"]         = "C++ 표준 라이브러리 (libc++, libstdc++)",
            ["optimize"]       = "최적화 수준 (none: -O0, size: -Os, speed: -O2, full: -O3)",
            ["debug-info"]     = "디버그 정보 (none, minimal: -gline-tables-only, full: -g)",
            ["lto"]            = "링크 타임 최적화 (true/false). MSVC: /GL+/LTCG, Clang: -flto",
            ["warnings"]       = "경고 수준 (none, default, all, extra)",
            ["warnings-as-errors"] = "경고를 오류로 처리 (true/false). MSVC: /WX, Clang: -Werror",
            ["android-api-level"]  = "Android API 레벨 (예: 26). android-* 타겟 전용",
            ["macos-min"]      = "최소 macOS 버전 (예: \"12.0\")",
            ["ios-min"]        = "최소 iOS 버전 (예: \"15.0\")",
            ["tvos-min"]       = "최소 tvOS 버전",
            ["watchos-min"]    = "최소 watchOS 버전",
            ["pch"]            = "프리컴파일 헤더 설정 (header, source 경로 지정)",
            ["unity-build"]    = "Unity Build 설정 (여러 .cpp를 하나로 합쳐 컴파일 속도 향상)",
            ["dependencies"]   = "의존성 선언 (path 로컬 프로젝트, git/version 패키지)",
            ["sources"]        = "컴파일할 소스 파일 glob 패턴 (include/exclude)",
            ["modules"]        = "C++ Modules (.cppm/.ixx) 파일 glob 패턴",
            ["headers"]        = "헤더 경로 (public: 외부 노출, private: 내부 전용)",
            ["defines"]        = "전처리기 매크로 정의 (예: \"NDEBUG\" \"MY_DEFINE=1\")",
            ["links"]          = "링크할 라이브러리 이름 (예: \"pthread\" \"dl\")",
            ["frameworks"]     = "링크할 Apple 프레임워크 이름 (macOS/iOS 전용)",
            ["framework-paths"] = "Apple .framework/.xcframework 번들 경로",
            ["compile-flags"]  = "추가 컴파일러 플래그 (dotori 생성 플래그 뒤에 추가됨)",
            ["link-flags"]     = "추가 링커 플래그 (dotori 생성 플래그 뒤에 추가됨)",
            ["output"]         = "빌드 결과물 복사 경로 설정 (binaries, libraries, symbols)",
            ["pre-build"]      = "빌드 전 실행할 셸 명령어 목록",
            ["post-build"]     = "빌드 후 실행할 셸 명령어 목록",
            ["copy"]           = "파일/폴더를 지정 경로로 복사. `from \"glob\" to \"dest/\"` 형식. 변경된 파일만 복사 (증분). 조건 블록과 조합 가능",
            ["emscripten-flags"] = "Emscripten 전용 추가 플래그 (예: \"-sUSE_SDL=2\")",
            ["resources"]      = "Windows 리소스 파일 (.rc) 목록",
            ["manifest"]       = "Windows 앱 매니페스트 파일 (.manifest) 경로",
            ["option"]         = "선택적 빌드 옵션 선언. `default` (필수), `defines`, `dependencies` 지정 가능. `dotori build --옵션명`으로 활성화",
            // Package fields
            ["name"]           = "패키지 이름",
            ["version"]        = "패키지 버전 (예: \"1.0.0\")",
            ["license"]        = "라이선스 식별자 (예: \"MIT\", \"Apache-2.0\")",
            ["homepage"]       = "프로젝트 홈페이지 URL",
            ["authors"]        = "패키지 저자 목록",
            ["exports"]        = "패키지 내보내기 경로 맵",
            // Condition atoms
            ["windows"]    = "Windows 플랫폼 조건",
            ["linux"]      = "Linux 플랫폼 조건",
            ["macos"]      = "macOS 플랫폼 조건",
            ["ios"]        = "iOS 플랫폼 조건",
            ["tvos"]       = "tvOS 플랫폼 조건",
            ["watchos"]    = "watchOS 플랫폼 조건",
            ["android"]    = "Android 플랫폼 조건",
            ["uwp"]        = "UWP (Universal Windows Platform) 조건",
            ["wasm"]       = "WebAssembly 플랫폼 조건",
            ["debug"]      = "Debug 빌드 구성 조건",
            ["release"]    = "Release 빌드 구성 조건",
            ["msvc"]       = "MSVC (cl.exe / clang-cl) 컴파일러 조건",
            ["clang"]      = "Clang (clang++) 컴파일러 조건",
            ["static"]     = "Static 런타임 링크 조건",
            ["dynamic"]    = "Dynamic 런타임 링크 조건",
            ["glibc"]      = "glibc (GNU C Library) 런타임 조건",
            ["musl"]       = "musl libc 런타임 조건",
            ["libcxx"]     = "libc++ (LLVM C++ 표준 라이브러리) 조건",
            ["libstdcxx"]  = "libstdc++ (GCC C++ 표준 라이브러리) 조건",
            ["emscripten"] = "Emscripten WASM 백엔드 조건",
            ["bare"]       = "Bare (wasm32-unknown-unknown) WASM 백엔드 조건",
        };

    /// <summary>
    /// Get hover documentation for the word at the given position in the document.
    /// Returns null if no documentation is available.
    /// </summary>
    public static LspHover? GetHover(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line >= lines.Length) return null;

        var currentLine = lines[line];
        var word = ExtractWordAt(currentLine, character);
        if (word is null) return null;

        if (!Descriptions.TryGetValue(word, out var description))
            return null;

        return new LspHover
        {
            Contents = new LspMarkupContent
            {
                Kind  = "markdown",
                Value = $"**{word}**\n\n{description}",
            },
            Range = GetWordRange(line, currentLine, character, word),
        };
    }

    private static string? ExtractWordAt(string line, int character)
    {
        if (character > line.Length) character = line.Length;

        // Find start of word (identifier chars: letters, digits, -, _)
        var start = character;
        while (start > 0 && IsWordChar(line[start - 1]))
            start--;

        // Find end of word
        var end = character;
        while (end < line.Length && IsWordChar(line[end]))
            end++;

        if (start >= end) return null;
        return line[start..end];
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '+';

    private static LspRange GetWordRange(int line, string lineText, int character, string word)
    {
        var start = character;
        while (start > 0 && IsWordChar(lineText[start - 1]))
            start--;

        return new LspRange
        {
            Start = new LspPosition { Line = line, Character = start },
            End   = new LspPosition { Line = line, Character = start + word.Length },
        };
    }
}
