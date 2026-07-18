#Requires -Version 5.1
<#
.SYNOPSIS
    Dotori 전체 빌드 스크립트 (Windows / PowerShell)

.DESCRIPTION
    각 컴포넌트를 빌드하여 build\<component>\ 폴더에 출력합니다.
    build.sh 와 동일한 옵션 구조를 가집니다.

.PARAMETER Only
    빌드할 대상을 쉼표로 지정합니다. (예: cli,build_server)
    생략 시 전체 대상을 빌드합니다.

.PARAMETER Skip
    건너뛸 대상을 쉼표로 지정합니다.

.PARAMETER Config
    빌드 구성입니다. (기본값: Release)

.PARAMETER Rid
    .NET Runtime Identifier. 지정 시 self-contained로 퍼블리시합니다.
    예: win-x64, win-arm64

.PARAMETER Version
    어셈블리 버전입니다. (예: v1.2.3)

.PARAMETER Install
    CLI 빌드 완료 후 시스템에 설치합니다.

.PARAMETER Uninstall
    설치된 dotori를 시스템에서 제거합니다.

.EXAMPLE
    .\build.ps1
    전체 빌드 (Release)

.EXAMPLE
    .\build.ps1 -Only cli -Rid win-x64 -Install
    CLI를 win-x64 단일 바이너리로 빌드 후 설치

.EXAMPLE
    .\build.ps1 -Only cli,build_server,worker -Rid win-x64 -Version v1.0.0
    CLI + 서버 + 워커를 win-x64용으로 빌드, 버전 태그 포함

.EXAMPLE
    .\build.ps1 -Config Debug
    Debug 구성으로 전체 빌드

.EXAMPLE
    .\build.ps1 -Uninstall
    설치된 dotori를 제거
#>
[CmdletBinding(DefaultParameterSetName = "Build")]
param(
    [Parameter(ParameterSetName = "Build")]
    [string]  $Only      = "",
    [Parameter(ParameterSetName = "Build")]
    [string]  $Skip      = "",
    [Parameter(ParameterSetName = "Build")]
    [string]  $Config    = "Release",
    [Parameter(ParameterSetName = "Build")]
    [string]  $Rid       = "",
    [Parameter(ParameterSetName = "Build")]
    [string]  $Version   = "",
    [Parameter(ParameterSetName = "Build")]
    [switch]  $Install,

    [Parameter(ParameterSetName = "Uninstall", Mandatory)]
    [switch]  $Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ──────────────────────────────────────────────────────────────────────────────
# 유틸리티
# ──────────────────────────────────────────────────────────────────────────────
function Write-Info ($msg) { Write-Host "[build]  $msg" -ForegroundColor Cyan   }
function Write-Ok   ($msg) { Write-Host "[ok]     $msg" -ForegroundColor Green  }
function Write-Warn ($msg) { Write-Host "[warn]   $msg" -ForegroundColor Yellow }
function Write-Err  ($msg) { Write-Host "[error]  $msg" -ForegroundColor Red    }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-InPath ([string]$dir, [string]$scope) {
    $current = [Environment]::GetEnvironmentVariable("PATH", $scope) ?? ""
    foreach ($p in ($current -split ";")) {
        if ($p.TrimEnd("\") -ieq $dir.TrimEnd("\")) { return $true }
    }
    return $false
}

function Add-ToPath ([string]$dir, [string]$scope) {
    $current = [Environment]::GetEnvironmentVariable("PATH", $scope) ?? ""
    $entries = $current -split ";" | Where-Object { $_ -ne "" }
    if ($entries -inotcontains $dir) {
        [Environment]::SetEnvironmentVariable("PATH", ($entries + $dir -join ";"), $scope)
        $env:PATH = "$env:PATH;$dir"
        Write-Ok "PATH 등록 완료: $dir ($scope 스코프)"
    }
}

function Remove-FromPath ([string]$dir, [string]$scope) {
    $current = [Environment]::GetEnvironmentVariable("PATH", $scope) ?? ""
    $entries = $current -split ";" |
        Where-Object { $_.TrimEnd("\") -ine $dir.TrimEnd("\") -and $_ -ne "" }
    [Environment]::SetEnvironmentVariable("PATH", ($entries -join ";"), $scope)
    Write-Ok "PATH 제거 완료: $dir ($scope 스코프)"
}

# ──────────────────────────────────────────────────────────────────────────────
# 경로 상수
# ──────────────────────────────────────────────────────────────────────────────
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir   = Join-Path $ScriptDir "build"
$ProgramDir = "C:\Program Files\Dotori"
$UserDir    = Join-Path $env:LOCALAPPDATA "Dotori"
$IsAdmin    = Test-Admin

# 빌드 대상 정의: 이름 → .csproj 경로 (Language Server는 cli에 통합)
$AllTargets = [ordered]@{
    cli          = "src\Dotori.Cli\Dotori.Cli.csproj"
    build_server = "src\Dotori.BuildServer\Dotori.BuildServer.csproj"
    worker       = "src\Dotori.Worker\Dotori.Worker.csproj"
    registry     = $null
}

# ──────────────────────────────────────────────────────────────────────────────
# 제거 모드
# ──────────────────────────────────────────────────────────────────────────────
if ($Uninstall) {
    Write-Host ""
    Write-Info "dotori 제거를 시작합니다..."
    $removed = $false

    $pfExe = Join-Path $ProgramDir "dotori.exe"
    if (Test-Path $pfExe) {
        if (-not $IsAdmin) {
            Write-Err "'$ProgramDir' 제거에는 관리자 권한이 필요합니다."
            Write-Err "PowerShell을 관리자 권한으로 실행 후 다시 시도하세요."
            exit 1
        }
        Remove-Item $pfExe -Force
        Remove-FromPath $ProgramDir "Machine"
        if ((Get-ChildItem $ProgramDir -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item $ProgramDir -Force -Recurse
        }
        Write-Ok "제거 완료: $pfExe"
        $removed = $true
    }

    $userExe = Join-Path $UserDir "dotori.exe"
    if (Test-Path $userExe) {
        Remove-Item $userExe -Force
        Remove-FromPath $UserDir "User"
        if ((Get-ChildItem $UserDir -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item $UserDir -Force -Recurse
        }
        Write-Ok "제거 완료: $userExe"
        $removed = $true
    }

    if (-not $removed) { Write-Warn "설치된 dotori를 찾을 수 없습니다." }
    exit 0
}

# ──────────────────────────────────────────────────────────────────────────────
# 빌드 대상 필터
# ──────────────────────────────────────────────────────────────────────────────
$onlyList = if ($Only) { $Only -split "," | ForEach-Object { $_.Trim() } } else { @() }
$skipList = if ($Skip) { $Skip -split "," | ForEach-Object { $_.Trim() } } else { @() }

function Should-Build ([string]$target) {
    if ($skipList -icontains $target) { return $false }
    if ($onlyList.Count -gt 0)        { return $onlyList -icontains $target }
    return $true
}

# ──────────────────────────────────────────────────────────────────────────────
# .NET 컴포넌트 빌드 헬퍼
# ──────────────────────────────────────────────────────────────────────────────
function Invoke-DotnetPublish ([string]$label, [string]$proj, [string]$outDir) {
    $ridInfo = if ($Rid)     { " (rid=$Rid)"        } else { "" }
    $verInfo = if ($Version) { " (ver=$Version)"    } else { "" }
    Write-Info "[$label] 빌드 중...${ridInfo}${verInfo} → $outDir"

    $args = @("publish", (Join-Path $ScriptDir $proj),
              "-c", $Config,
              "-o", $outDir,
              "--nologo", "-v", "quiet")
    if ($Rid)     { $args += @("-r", $Rid, "--self-contained") }
    if ($Version) { $args += "-p:Version=$($Version.TrimStart('v'))" }

    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "[$label] dotnet publish 실패 (exit $LASTEXITCODE)" }
    Write-Ok "[$label] 완료"
}

# ──────────────────────────────────────────────────────────────────────────────
# CLI 설치 헬퍼
# ──────────────────────────────────────────────────────────────────────────────
function Install-Cli {
    $binary = Join-Path $BuildDir "cli\dotori.exe"
    if (-not (Test-Path $binary)) {
        Write-Err "[install] CLI 바이너리를 찾을 수 없습니다: $binary"
        Write-Err "[install] 먼저 .\build.ps1 -Only cli 를 실행하세요."
        exit 1
    }

    Write-Host ""
    $installDir = $null
    $pathScope  = "User"

    if ($IsAdmin) {
        $answer = Read-Host "[install] 관리자 권한이 있습니다. '$ProgramDir' 에 설치하시겠습니까? [Y/n]"
        if ($answer -eq "" -or $answer -imatch "^y") {
            $installDir = $ProgramDir
            $pathScope  = "Machine"
        } else {
            $installDir = $UserDir
            $pathScope  = "User"
        }
    } else {
        Write-Info "[install] 관리자 권한이 없습니다. '$UserDir' 에 설치합니다."
        $installDir = $UserDir
        $pathScope  = "User"
    }

    Write-Host ""
    Write-Info "[install] 설치 경로: $installDir"
    if (-not (Test-Path $installDir)) {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    }

    Copy-Item $binary (Join-Path $installDir "dotori.exe") -Force
    Write-Ok "[install] 설치 완료: $(Join-Path $installDir 'dotori.exe')"

    if (-not (Test-InPath $installDir $pathScope)) {
        Add-ToPath $installDir $pathScope
    } else {
        Write-Info "[install] PATH 에 이미 등록되어 있습니다: $installDir"
    }

    Write-Host ""
    Write-Ok "dotori 설치가 완료되었습니다."
    Write-Info "새 터미널을 열거나 아래 명령으로 현재 세션에 즉시 적용하세요:"
    Write-Host "    `$env:PATH = [Environment]::GetEnvironmentVariable('PATH','$pathScope') + ';' + `$env:PATH" -ForegroundColor Gray
}

# ──────────────────────────────────────────────────────────────────────────────
# 메인 빌드
# ──────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[build]  ━━━ Dotori 빌드 ━━━  config=$Config" -ForegroundColor Cyan
Write-Host ""

$failed = [System.Collections.Generic.List[string]]::new()

foreach ($target in $AllTargets.Keys) {
    if (Should-Build $target) {
        $outDir = Join-Path $BuildDir $target
        try {
            if ($target -eq "registry") {
                Write-Info "[registry] 배포 번들 준비 중... → $outDir"
                if (Test-Path $outDir) {
                    Get-ChildItem $outDir -Force | Remove-Item -Recurse -Force
                }
                New-Item -ItemType Directory -Path $outDir -Force | Out-Null
                Copy-Item (Join-Path $ScriptDir "deploy\registry\*") $outDir -Recurse -Force
                Write-Ok "[registry] 완료"
            } else {
                Invoke-DotnetPublish $target $AllTargets[$target] $outDir
            }
        } catch {
            Write-Err $_.Exception.Message
            $failed.Add($target)
        }
    } else {
        Write-Info "[$target] 건너뜀"
    }
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Ok "모든 빌드 완료 → $BuildDir\"
} else {
    Write-Err "실패한 대상: $($failed -join ', ')"
    exit 1
}

# -Install 플래그가 있으면 CLI 설치 수행
if ($Install) { Install-Cli }
