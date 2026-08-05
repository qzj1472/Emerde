param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [ValidateSet("Debug", "Release")]
    [string]$Mode = "Release"
)

$ErrorActionPreference = "Stop"
$resolvedRoot = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Publish directory does not exist: $resolvedRoot"
}

$allowedCultures = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@("en", "en-US", "zh-Hans", "zh-Hant", "ja"),
    [System.StringComparer]::OrdinalIgnoreCase)
$culturePattern = '^[a-z]{2,3}(?:-[A-Za-z]{2,4})?$'

$cultureDirectories = Get-ChildItem -LiteralPath $resolvedRoot -Directory -Recurse -Force |
    Where-Object {
        $_.Name -match $culturePattern -and
        -not $allowedCultures.Contains($_.Name) -and
        (Get-ChildItem -LiteralPath $_.FullName -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name.EndsWith('.resources.dll', [System.StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1)
    } |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($directory in $cultureDirectories) {
    if ($directory.FullName.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force -Filter *.xml |
    Remove-Item -Force

if ($Mode -eq "Release") {
    $diagnosticNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            "createdump.exe",
            "mscordbi.dll",
            "Microsoft.DiaSymReader.Native.amd64.dll"
        ),
        [System.StringComparer]::OrdinalIgnoreCase)

    Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force |
        Where-Object {
            $_.Name.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.StartsWith('mscordaccore', [System.StringComparison]::OrdinalIgnoreCase) -or
            $diagnosticNames.Contains($_.Name)
        } |
        Remove-Item -Force
}
