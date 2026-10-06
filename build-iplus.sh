#!/usr/bin/env bash
#
# iPlus build script.
#
# Runs the normal Nuke pipeline (CreateNugetPackages) and additionally produces
# standalone packages for projects that are normally merged into iPlus.Avalonia:
#   iPlus.Avalonia.Base
#   iPlus.Avalonia.Controls
#   iPlus.Avalonia.Markup.Xaml
#   iPlus.Avalonia.Dialogs
#   iPlus.Avalonia.Native   (packed with ForcePackAvaloniaNative=True; contains NO
#                            macOS dylib - that can only be built with Xcode)
#
# Usage:
#   ./build-iplus.sh <version> [CreateNugetPackages|Package]
#
# Examples:
#   ./build-iplus.sh 12.2.902
#   ./build-iplus.sh 12.2.902 Package
#
# Output: artifacts/nuget/
#
# NOTE: The standalone Base/Controls/Markup.Xaml/Dialogs packages duplicate the
# DLLs that are also inside iPlus.Avalonia. Do NOT reference both in the same
# project - reference either iPlus.Avalonia OR the standalone packages.

set -euo pipefail
cd "$(dirname "$0")"

# Accepts any argument order and the --force-nuget-version flag:
#   ./build-iplus.sh 12.2.902 [target]
#   ./build-iplus.sh [target] 12.2.902
#   ./build-iplus.sh Package --force-nuget-version 12.2.902
VERSION=""
TARGET="CreateNugetPackages"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --force-nuget-version) VERSION="${2:-}"; shift 2 ;;  # flag with value
        --force-nuget-version=*) VERSION="${1#*=}"; shift ;; # flag with =value
        [0-9]*) VERSION="$1"; shift ;;                       # version starts with a digit
        *) TARGET="$1"; shift ;;                             # anything else = target name
    esac
done
if [[ -z "$VERSION" ]]; then
    echo "Usage: ./build-iplus.sh <version> [CreateNugetPackages|Package]" >&2
    exit 1
fi

export NUGET_PACKAGES="${NUGET_PACKAGES:-/home/damir/SHARED/Devel/iPlusGit/V5/packages}"

echo "==> Running ./build.sh $TARGET --force-nuget-version $VERSION"
./build.sh "$TARGET" --force-nuget-version "$VERSION"

INTERMEDIATE="build-intermediate/nuget"
OUTPUT="artifacts/nuget"
mkdir -p "$OUTPUT"

# 1) Copy the standalone packages that Numerge removed from the final output.
echo "==> Copying standalone packages from $INTERMEDIATE"
for ID in iPlus.Avalonia.Base iPlus.Avalonia.Controls iPlus.Avalonia.Markup iPlus.Avalonia.Markup.Xaml iPlus.Avalonia.Dialogs; do
    for EXT in nupkg snupkg; do
        SRC="$INTERMEDIATE/$ID.$VERSION.$EXT"
        if [ -f "$SRC" ]; then
            cp -v "$SRC" "$OUTPUT/"
        else
            echo "WARNING: $SRC not found (was it built?)" >&2
        fi
    done
done

# 2) Pack Avalonia.Native (not packable by default without the macOS dylib).
echo "==> Packing iPlus.Avalonia.Native"
dotnet pack src/Avalonia.Native/Avalonia.Native.csproj \
    -c Release \
    -p:Version="$VERSION" \
    -p:ForcePackAvaloniaNative=True \
    -o "$OUTPUT"

echo "==> Done. Packages are in $OUTPUT"
