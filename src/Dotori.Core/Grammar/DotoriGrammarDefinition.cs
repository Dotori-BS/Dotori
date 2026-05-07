namespace Dotori.Core.Grammar;

/// <summary>
/// Dotori DSL의 모든 키워드와 값을 중앙에서 관리합니다.
/// 문법 파일 생성기들은 이 정의를 기반으로 각 에디터 형식을 생성합니다.
/// </summary>
public static class DotoriGrammarDefinition
{
    /// <summary>최상위 선언 키워드 (project, package)</summary>
    public static readonly string[] TopLevelKeywords = ["project", "package"];

    /// <summary>블록을 여는 키워드 (중괄호 블록 시작)</summary>
    public static readonly string[] BlockKeywords =
    [
        "sources",
        "modules",
        "headers",
        "defines",
        "links",
        "frameworks",
        "framework-paths",
        "compile-flags",
        "link-flags",
        "resources",
        "dependencies",
        "pch",
        "unity-build",
        "output",
        "pre-build",
        "post-build",
        "copy",
        "option",
        "authors",
        "exports",
        "emscripten-flags",
        "assembler",
    ];

    /// <summary>프로퍼티 키워드 (= 값 형태)</summary>
    public static readonly string[] PropertyKeywords =
    [
        "type",
        "std",
        "description",
        "optimize",
        "debug-info",
        "runtime-link",
        "libc",
        "stdlib",
        "lto",
        "warnings",
        "warnings-as-errors",
        "android-api-level",
        "macos-min",
        "ios-min",
        "tvos-min",
        "watchos-min",
        "c-as-cpp",
        "objc-arc",
        "objc-as-objcpp",
        "manifest",
        "header",
        "source",
        "enabled",
        "batch-size",
        "export-map",
        "name",
        "version",
        "license",
        "homepage",
        "include",
        "exclude",
        "public",
        "private",
        "git",
        "tag",
        "commit",
        "path",
        "from",
        "to",
        "binaries",
        "libraries",
        "symbols",
        "default",
        // assembler block
        "tool",
        "format",
        "flags",
    ];

    /// <summary>type = ... 의 열거형 값</summary>
    public static readonly string[] TypeValues =
    [
        "executable",
        "static-library",
        "shared-library",
        "header-only",
    ];

    /// <summary>std = ... 의 열거형 값</summary>
    public static readonly string[] StdValues = ["c++17", "c++20", "c++23"];

    /// <summary>각종 프로퍼티의 열거형 값 (optimize, debug-info, runtime-link, libc, stdlib, warnings, bool)</summary>
    public static readonly string[] EnumValues =
    [
        // optimize
        "none",
        "size",
        "speed",
        "full",
        // debug-info
        "minimal",
        // runtime-link
        "static",
        "dynamic",
        // libc
        "glibc",
        "musl",
        // stdlib
        "libc++",
        "libstdc++",
        // warnings
        "default",
        "all",
        "extra",
        // bool
        "true",
        "false",
        // assembler tool
        "nasm",
        "yasm",
        "gas",
        "as",
        "masm",
        "auto",
        // assembler format (common nasm/yasm values)
        "elf64",
        "elf32",
        "win64",
        "win32",
        "macho64",
        "macho32",
    ];

    /// <summary>조건 블록 내 플랫폼 atom</summary>
    public static readonly string[] PlatformConditions =
    [
        "windows",
        "uwp",
        "linux",
        "android",
        "macos",
        "ios",
        "tvos",
        "watchos",
        "wasm",
    ];

    /// <summary>조건 블록 내 빌드 구성 atom</summary>
    public static readonly string[] ConfigConditions = ["debug", "release"];

    /// <summary>조건 블록 내 컴파일러 atom</summary>
    public static readonly string[] CompilerConditions = ["msvc", "clang"];

    /// <summary>조건 블록 내 런타임 atom</summary>
    public static readonly string[] RuntimeConditions =
    [
        "static",
        "dynamic",
        "glibc",
        "musl",
        "libcxx",
        "libstdcxx",
        "emscripten",
        "bare",
    ];

    /// <summary>project 블록 내에서 `key = value` 형태로 사용되는 프로퍼티 키워드 (Tree-sitter property_stmt 용)</summary>
    public static readonly string[] ProjectPropertyKeywords =
    [
        "type",
        "std",
        "description",
        "optimize",
        "debug-info",
        "runtime-link",
        "libc",
        "stdlib",
        "lto",
        "warnings",
        "warnings-as-errors",
        "android-api-level",
        "macos-min",
        "ios-min",
        "tvos-min",
        "watchos-min",
        "c-as-cpp",
        "objc-arc",
        "objc-as-objcpp",
        "manifest",
        "toolchain",
    ];

    /// <summary>`{ STRING* }` 형태의 단순 문자열 목록 블록 키워드 (Tree-sitter string_list_block 용)</summary>
    public static readonly string[] StringListBlockKeywords =
    [
        "defines",
        "links",
        "frameworks",
        "framework-paths",
        "compile-flags",
        "link-flags",
        "resources",
        "pre-build",
        "post-build",
        "emscripten-flags",
    ];
}
