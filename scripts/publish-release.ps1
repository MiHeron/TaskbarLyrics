[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "TaskbarLyrics.App\TaskbarLyrics.App.csproj"
$publishRoot = Join-Path $repoRoot "publish"
$executableName = "TaskbarLyrics"
$projectExecutableName = "TaskbarLyrics.App.exe"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host "Release version (for example 1.2.0)"
}

$Version = $Version.Trim()
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Invalid version: $Version. Use x.x.x format, for example 1.2.0."
}

$packageName = "TaskbarLyrics-$Version"
$outputDirectory = Join-Path $publishRoot $packageName
$archivePath = Join-Path $publishRoot "$packageName.zip"

if (-not $Force -and ((Test-Path $outputDirectory) -or (Test-Path $archivePath))) {
    throw "Release artifacts for version $Version already exist. Use -Force to replace them."
}

$stagingRoot = Join-Path $publishRoot (".release-staging-" + [Guid]::NewGuid().ToString("N"))
$stagingPackage = Join-Path $stagingRoot $packageName
$stagingArchive = Join-Path $stagingRoot "$packageName.zip"

New-Item -ItemType Directory -Path $stagingPackage -Force | Out-Null

try {
    Write-Host "Publishing TaskbarLyrics $Version..." -ForegroundColor Cyan
    & dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $stagingPackage

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $projectExecutablePath = Join-Path $stagingPackage $projectExecutableName
    if (-not (Test-Path $projectExecutablePath)) {
        throw "Published executable was not found: $projectExecutablePath"
    }

    $executablePath = Join-Path $stagingPackage "$executableName.exe"
    Rename-Item -LiteralPath $projectExecutablePath -NewName "$executableName.exe"
    if (-not (Test-Path $executablePath)) {
        throw "Failed to rename published executable to: $executablePath"
    }

    $watchdogProjectPath = Join-Path $repoRoot "TaskbarLyrics.Watchdog\TaskbarLyrics.Watchdog.csproj"
    $watchdogExecutableName = "TaskbarLyrics.Watchdog.exe"
    $watchdogStaging = Join-Path $stagingRoot "watchdog"
    New-Item -ItemType Directory -Path $watchdogStaging -Force | Out-Null

    Write-Host "Publishing TaskbarLyrics watchdog (NativeAOT)..." -ForegroundColor Cyan
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    $vsInstallPath = $null
    if (Test-Path -LiteralPath $vswherePath) {
        $vsInstallPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }

    $vcvarsPath = if ($vsInstallPath) { Join-Path $vsInstallPath "VC\Auxiliary\Build\vcvarsall.bat" } else { $null }
    if ($vcvarsPath -and (Test-Path -LiteralPath $vcvarsPath)) {
        # ILCompiler 8.0.x 的 findvcvarsall.bat 在 VS 2026 下会误报 vswhere，
        # 这里先进入 vcvars 环境并让 AOT 直接使用环境里的 link.exe/LIB。
        $publishArguments = @(
            "publish",
            "`"$watchdogProjectPath`"",
            "-c", "Release",
            "-r", "win-x64",
            "-p:PublishAot=true",
            "-p:IlcUseEnvironmentalTools=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:Version=$Version",
            "-o", "`"$watchdogStaging`""
        ) -join " "
        & cmd.exe /d /c "call `"$vcvarsPath`" amd64 >nul 2>&1 && dotnet $publishArguments"

        if ($LASTEXITCODE -ne 0) {
            throw "Watchdog NativeAOT publish failed with exit code $LASTEXITCODE."
        }
    }
    else {
        Write-Warning "MSVC C++ Build Tools not found. Falling back to the trimmed single-file watchdog."
        & dotnet publish $watchdogProjectPath `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            -p:Version=$Version `
            -o $watchdogStaging

        if ($LASTEXITCODE -ne 0) {
            throw "Watchdog dotnet publish failed with exit code $LASTEXITCODE."
        }
    }

    $watchdogExecutablePath = Join-Path $watchdogStaging $watchdogExecutableName
    if (-not (Test-Path $watchdogExecutablePath)) {
        throw "Published watchdog executable was not found: $watchdogExecutablePath"
    }

    Copy-Item -LiteralPath $watchdogExecutablePath -Destination (Join-Path $stagingPackage $watchdogExecutableName)

    Write-Host "Creating ZIP archive..." -ForegroundColor Cyan
    Compress-Archive -Path $stagingPackage -DestinationPath $stagingArchive -CompressionLevel Optimal

    if ($Force) {
        if (Test-Path $outputDirectory) {
            Remove-Item -LiteralPath $outputDirectory -Recurse -Force
        }
        if (Test-Path $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }

    Move-Item -LiteralPath $stagingPackage -Destination $outputDirectory
    Move-Item -LiteralPath $stagingArchive -Destination $archivePath

    $projectText = [IO.File]::ReadAllText($projectPath)
    $versionMatches = [regex]::Matches($projectText, '<Version>[^<]+</Version>')
    if ($versionMatches.Count -ne 1) {
        throw "Expected exactly one <Version> element in the project file. The source version was not updated."
    }

    $updatedProjectText = [regex]::Replace(
        $projectText,
        '<Version>[^<]+</Version>',
        "<Version>$Version</Version>",
        1)
    [IO.File]::WriteAllText($projectPath, $updatedProjectText, [Text.UTF8Encoding]::new($false))

    Write-Host "Release completed." -ForegroundColor Green
    Write-Host "Version: $Version"
    Write-Host "Directory: $outputDirectory"
    Write-Host "Archive: $archivePath"
    Write-Host "Project version updated: $projectPath"
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
