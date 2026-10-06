# build-iplus.ps1
#
# iPlus build script (Windows equivalent of build-iplus.sh).
#
# Runs the normal Nuke pipeline (CreateNugetPackages) and additionally produces
# standalone packages for projects that are normally merged into iPlus.Avalonia:
#   iPlus.Avalonia.Base
#   iPlus.Avalonia.Controls
#   iPlus.Avalonia.Markup
#   iPlus.Avalonia.Markup.Xaml
#   iPlus.Avalonia.Dialogs
#   iPlus.Avalonia.Native   (packed with ForcePackAvaloniaNative=True; contains NO
#                            macOS dylib - that can only be built with Xcode)
#
# Usage:
#   .\build-iplus.ps1 -Version 12.2.902 [-Target CreateNugetPackages|Package] [-NuGetPackages <path>] [-PackIOS]
#
# Examples:
#   .\build-iplus.ps1 -Version 12.2.902
#   .\build-iplus.ps1 -Version 12.2.902 -Target Package
#   .\build-iplus.ps1 -Version 12.2.902 -PackIOS     # also pack iPlus.Avalonia.iOS
#
# Output: artifacts\nuget\
#
# NOTE: The standalone Base/Controls/Markup/Markup.Xaml/Dialogs packages duplicate
# the DLLs that are also inside iPlus.Avalonia. Do NOT reference both in the same
# project - reference either iPlus.Avalonia OR the standalone packages.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Nuke target: CreateNugetPackages (default, no API-diff validation)
    # or Package (full pipeline incl. ValidateApiDiff).
    [ValidateSet("CreateNugetPackages", "Package")]
    [string]$Target = "CreateNugetPackages",

    # Optional: custom NuGet global packages folder (NUGET_PACKAGES).
    [string]$NuGetPackages,

    # Also pack iPlus.Avalonia.iOS. Requires the 'ios' workload
    # (dotnet workload install ios) - only possible on Windows/macOS.
    [switch]$PackIOS
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($NuGetPackages) {
    $env:NUGET_PACKAGES = $NuGetPackages
}

Write-Host "==> Running .\build.ps1 $Target --force-nuget-version $Version"
.\build.ps1 $Target --force-nuget-version $Version
if ($LASTEXITCODE -ne 0) { throw "build.ps1 $Target failed with exit code $LASTEXITCODE" }

$intermediate = Join-Path $PSScriptRoot "build-intermediate\nuget"
$output = Join-Path $PSScriptRoot "artifacts\nuget"
New-Item -ItemType Directory -Force -Path $output | Out-Null

# 1) Copy the standalone packages that Numerge removed from the final output.
Write-Host "==> Copying standalone packages from $intermediate"
foreach ($id in @(
    "iPlus.Avalonia.Base",
    "iPlus.Avalonia.Controls",
    "iPlus.Avalonia.Markup",
    "iPlus.Avalonia.Markup.Xaml",
    "iPlus.Avalonia.Dialogs"
)) {
    foreach ($ext in @("nupkg", "snupkg")) {
        $src = Join-Path $intermediate "$id.$Version.$ext"
        if (Test-Path $src) {
            Copy-Item $src $output -Force
            Write-Host "  copied $(Split-Path -Leaf $src)"
        } else {
            Write-Warning "NOT FOUND (was it built?): $src"
        }
    }
}

# 2) Pack Avalonia.Native (not packable by default without the macOS dylib).
Write-Host "==> Packing iPlus.Avalonia.Native"
dotnet pack src/Avalonia.Native/Avalonia.Native.csproj `
    -c Release `
    -p:Version="$Version" `
    -p:ForcePackAvaloniaNative=True `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet pack Avalonia.Native failed with exit code $LASTEXITCODE" }

# 3) Optionally pack Avalonia.iOS (requires the 'ios' workload; not buildable on Linux).
if ($PackIOS) {
    Write-Host "==> Packing iPlus.Avalonia.iOS"
    dotnet pack src/iOS/Avalonia.iOS/Avalonia.iOS.csproj `
        -c Release `
        -p:Version="$Version" `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack Avalonia.iOS failed with exit code $LASTEXITCODE" }
}

Write-Host "==> Done. Packages are in $output"
