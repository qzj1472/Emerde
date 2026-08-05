param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$sourceRoot = [System.IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path $sourceRoot -Parent))
$releaseDirectoryName = -join ([char[]](0x7F16, 0x8BD1, 0x53D1, 0x5E03, 0x7248, 0x672C))
$releaseRoot = Join-Path $workspaceRoot $releaseDirectoryName
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $releaseRoot "Emerde.Setup.exe"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$installerProject = Join-Path $sourceRoot "src\Emerde.Installer\Emerde.Installer.csproj"
$applicationProject = Join-Path $sourceRoot "src\Emerde\Emerde.csproj"
$uninstallerProject = Join-Path $sourceRoot "src\Emerde.Uninstaller\Emerde.Uninstaller.csproj"
$cleanupProject = Join-Path $sourceRoot "src\Emerde.Cleanup\Emerde.Cleanup.csproj"
$bootstrapProject = Join-Path $sourceRoot "src\Emerde.Setup.Bootstrap\Emerde.Setup.Bootstrap.csproj"
$packagerProject = Join-Path $sourceRoot "src\Emerde.Setup.Packager\Emerde.Setup.Packager.csproj"
$workRoot = Join-Path $sourceRoot "src\Emerde.Installer\obj\setup\$Configuration"
$applicationPublish = Join-Path $workRoot "publish\application"
$uninstallerPublish = Join-Path $workRoot "publish\uninstaller"
$cleanupPublish = Join-Path $workRoot "publish\cleanup"
$installerPublish = Join-Path $workRoot "publish\installer"
$bootstrapPublish = Join-Path $workRoot "publish\bootstrap"
$applicationStage = Join-Path $workRoot "stage\application"
$bootstrapStage = Join-Path $workRoot "stage\bootstrap"
$runtimeStage = Join-Path $bootstrapStage "runtime"
$applicationArchive = Join-Path $workRoot "application.7z"
$bootstrapArchive = Join-Path $workRoot "bootstrap.tar.zst"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE"
    }
}

function Find-SevenZip {
    if (-not [string]::IsNullOrWhiteSpace($env:SEVEN_ZIP) -and (Test-Path -LiteralPath $env:SEVEN_ZIP -PathType Leaf)) {
        return [System.IO.Path]::GetFullPath($env:SEVEN_ZIP)
    }

    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in @(
        (Join-Path $env:ProgramFiles "7-Zip\7z.exe"),
        (Join-Path $env:ProgramFiles "NVIDIA Corporation\NVIDIA App\7z.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "7-Zip\7z.exe")
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    throw "7z.exe was not found. Set SEVEN_ZIP to a 7-Zip executable."
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Get-LatestRuntimeVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CoreRuntimeRoot,
        [Parameter(Mandatory = $true)]
        [string]$DesktopRuntimeRoot,
        [Parameter(Mandatory = $true)]
        [string]$HostFxrRoot
    )

    $directory = Get-ChildItem -LiteralPath $CoreRuntimeRoot -Directory |
        Where-Object {
            $_.Name -like "9.0.*" -and
            (Test-Path -LiteralPath (Join-Path $DesktopRuntimeRoot $_.Name) -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $HostFxrRoot $_.Name) -PathType Container)
        } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    if ($null -eq $directory) {
        throw "No matching .NET 9 desktop runtime and host were found."
    }

    return $directory.Name
}

if (Test-Path -LiteralPath $workRoot) {
    $resolvedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
    $expectedParent = [System.IO.Path]::GetFullPath((Join-Path $sourceRoot "src\Emerde.Installer\obj\setup"))
    if (-not $resolvedWorkRoot.StartsWith($expectedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected setup directory: $resolvedWorkRoot"
    }
    Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $applicationPublish,$uninstallerPublish,$cleanupPublish,$installerPublish,$bootstrapPublish,$applicationStage,$bootstrapStage -Force | Out-Null

$publishCommon = @(
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "false",
    "--property:PublishSingleFile=true",
    "--property:EnableCompressionInSingleFile=false",
    "--property:PublishReadyToRun=false",
    "--property:PublishTrimmed=false",
    "--property:DebugType=None",
    "--property:DebugSymbols=false",
    "--nologo"
)

Invoke-Checked "dotnet" (@("publish", $applicationProject) + $publishCommon + @(
    "--property:AppHostRelativeDotNet=..\runtime",
    "--property:AppHostDotNetSearch=AppRelative",
    "--output", $applicationPublish
))
Invoke-Checked "dotnet" (@("publish", $uninstallerProject) + $publishCommon + @("--output", $uninstallerPublish))
Invoke-Checked "dotnet" @(
    "publish",
    $cleanupProject,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $cleanupPublish,
    "--nologo"
)
Invoke-Checked "dotnet" (@("publish", $installerProject) + $publishCommon + @("--output", $installerPublish))

foreach ($directory in @($applicationPublish, $uninstallerPublish, $installerPublish)) {
    Invoke-Checked "powershell" @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "Prune-Publish.ps1"),
        "-Root", $directory,
        "-Mode", $Configuration
    )
}

foreach ($directoryName in @("bin", "native", "resources", "licenses", "maintenance")) {
    New-Item -ItemType Directory -Path (Join-Path $applicationStage $directoryName) -Force | Out-Null
}

Get-ChildItem -LiteralPath $applicationPublish -Force | ForEach-Object {
    if ($_.PSIsContainer -and $_.Name -eq "ffmpeg") {
        Copy-DirectoryContents $_.FullName (Join-Path $applicationStage "native\ffmpeg")
    }
    elseif ($_.PSIsContainer -and $_.Name -eq "libvlc") {
        Copy-DirectoryContents $_.FullName (Join-Path $applicationStage "native\libvlc")
    }
    elseif ($_.PSIsContainer -and $_.Name -eq "licenses") {
        Copy-DirectoryContents $_.FullName (Join-Path $applicationStage "licenses")
    }
    elseif (-not $_.PSIsContainer -and $_.Name -eq "LICENSE") {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $applicationStage "licenses\LICENSE") -Force
    }
    elseif (-not $_.PSIsContainer -and $_.Name -in @("COPYRIGHT", "THIRD_PARTY_NOTICES.md")) {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $applicationStage "resources\$($_.Name)") -Force
    }
    else {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $applicationStage "bin") -Recurse -Force
    }
}

$uninstallerExecutable = Join-Path $uninstallerPublish "Emerde.Uninstaller.exe"
if (-not (Test-Path -LiteralPath $uninstallerExecutable -PathType Leaf)) {
    throw "Uninstaller executable was not published."
}
Copy-Item -LiteralPath $uninstallerExecutable -Destination (Join-Path $applicationStage "maintenance\Emerde.Uninstaller.exe") -Force
$cleanupExecutable = Join-Path $cleanupPublish "Emerde.Cleanup.exe"
if (-not (Test-Path -LiteralPath $cleanupExecutable -PathType Leaf)) {
    throw "Cleanup executable was not published."
}
Copy-Item -LiteralPath $cleanupExecutable -Destination (Join-Path $applicationStage "maintenance\Emerde.Cleanup.exe") -Force

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetRoot = Split-Path $dotnetCommand.Source -Parent
$coreRuntimeRoot = Join-Path $dotnetRoot "shared\Microsoft.NETCore.App"
$desktopRuntimeRoot = Join-Path $dotnetRoot "shared\Microsoft.WindowsDesktop.App"
$hostFxrRoot = Join-Path $dotnetRoot "host\fxr"
$runtimeVersion = Get-LatestRuntimeVersion $coreRuntimeRoot $desktopRuntimeRoot $hostFxrRoot

New-Item -ItemType Directory -Path (Join-Path $runtimeStage "host\fxr\$runtimeVersion"),(Join-Path $runtimeStage "shared\Microsoft.NETCore.App\$runtimeVersion"),(Join-Path $runtimeStage "shared\Microsoft.WindowsDesktop.App\$runtimeVersion") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dotnetRoot "dotnet.exe") -Destination $runtimeStage -Force
Copy-DirectoryContents (Join-Path $dotnetRoot "host\fxr\$runtimeVersion") (Join-Path $runtimeStage "host\fxr\$runtimeVersion")
Copy-DirectoryContents (Join-Path $coreRuntimeRoot $runtimeVersion) (Join-Path $runtimeStage "shared\Microsoft.NETCore.App\$runtimeVersion")
Copy-DirectoryContents (Join-Path $desktopRuntimeRoot $runtimeVersion) (Join-Path $runtimeStage "shared\Microsoft.WindowsDesktop.App\$runtimeVersion")
Copy-Item -LiteralPath (Join-Path $dotnetRoot "LICENSE.txt") -Destination (Join-Path $applicationStage "licenses\dotnet-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot "ThirdPartyNotices.txt") -Destination (Join-Path $applicationStage "licenses\dotnet-ThirdPartyNotices.txt") -Force

Invoke-Checked "powershell" @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Prune-Publish.ps1"),
    "-Root", $runtimeStage,
    "-Mode", $Configuration
)

Copy-DirectoryContents $installerPublish (Join-Path $bootstrapStage "installer")
$sevenZip = Find-SevenZip
Push-Location $applicationStage
try {
    Invoke-Checked $sevenZip @(
        "a",
        "-t7z",
        $applicationArchive,
        ".\*",
        "-m0=lzma2",
        "-mx=9",
        "-md=128m",
        "-ms=on",
        "-mmt=on"
    )
}
finally {
    Pop-Location
}

Invoke-Checked "dotnet" @(
    "run",
    "--project", $packagerProject,
    "--configuration", $Configuration,
    "--",
    "pack-zstd",
    $bootstrapStage,
    $bootstrapArchive
)
Invoke-Checked "dotnet" @(
    "publish",
    $bootstrapProject,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $bootstrapPublish,
    "--nologo"
)

$bootstrapExecutable = Join-Path $bootstrapPublish "Emerde.Setup.Bootstrap.exe"
if (-not (Test-Path -LiteralPath $bootstrapExecutable -PathType Leaf)) {
    throw "Setup bootstrap executable was not published."
}

Invoke-Checked "dotnet" @(
    "run",
    "--project", $packagerProject,
    "--configuration", $Configuration,
    "--",
    "assemble",
    $bootstrapExecutable,
    $bootstrapArchive,
    $applicationArchive,
    $OutputPath
)
Invoke-Checked "dotnet" @(
    "run",
    "--project", $packagerProject,
    "--configuration", $Configuration,
    "--",
    "verify",
    $OutputPath
)

$result = Get-Item -LiteralPath $OutputPath
Write-Output "$($result.FullName) $([Math]::Round($result.Length / 1MB, 2)) MiB"
