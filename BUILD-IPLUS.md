# iPlus NuGet Package Scripts

This document explains how to build the **iPlus.\*** NuGet packages from the
Avalonia fork using the provided build scripts, on **Linux** and **Windows**.

The scripts produce the complete package set in `artifacts/nuget/`:

- `iPlus.Avalonia` — the main package. By design it **contains** the DLLs of
  `Avalonia.Base`, `Avalonia.Controls`, `Avalonia.Markup`, `Avalonia.Markup.Xaml`,
  `Avalonia.Dialogs`, etc. (they are merged into it by Numerge).
- Standalone packages that are normally merged (produced additionally by the
  iPlus scripts): `iPlus.Avalonia.Base`, `iPlus.Avalonia.Controls`,
  `iPlus.Avalonia.Markup`, `iPlus.Avalonia.Markup.Xaml`, `iPlus.Avalonia.Dialogs`.
- Platform backends: `iPlus.Avalonia.Desktop`, `iPlus.Avalonia.Android`,
  `iPlus.Avalonia.Browser`, `iPlus.Avalonia.X11`, `iPlus.Avalonia.Wayland`,
  `iPlus.Avalonia.Skia`, `iPlus.Avalonia.HarfBuzz`, `iPlus.Avalonia.Native`,
  `iPlus.Avalonia.iOS` (Windows/macOS only), and more.

> ⚠️ **Do not mix**: the standalone Base/Controls/Markup/Markup.Xaml/Dialogs
> packages duplicate the DLLs that are also inside `iPlus.Avalonia`.
> In a consumer project reference **either** `iPlus.Avalonia` **or** the
> standalone packages — never both.

---

## Scripts

| File | Platform | Purpose |
|---|---|---|
| `build-iplus.sh` | Linux | Full package build |
| `build-iplus.ps1` | Windows | Full package build |

Both scripts do the same:

1. Run the Nuke pipeline (`CreateNugetPackages` by default, or `Package`).
   This packs **all** projects into `build-intermediate/nuget/` and merges
   them into the final set in `artifacts/nuget/`.
2. Copy the standalone `Base` / `Controls` / `Markup` / `Markup.Xaml` /
   `Dialogs` packages from `build-intermediate/nuget/` to `artifacts/nuget/`
   (Numerge removes them from the final output, the scripts add them back).
3. Pack `iPlus.Avalonia.Native` with `-p:ForcePackAvaloniaNative=True`
   (it is not packable by default without the macOS dylib — see below).
4. *(Windows only, optional)* Pack `iPlus.Avalonia.iOS` with `-PackIOS`.

### Usage (Linux)

```bash
cd /path/to/Avalonia

# Default: CreateNugetPackages (no API-diff validation) - recommended
./build-iplus.sh 12.2.902

# Full pipeline including ValidateApiDiff
./build-iplus.sh 12.2.902 Package

# Argument order is flexible:
./build-iplus.sh Package --force-nuget-version 12.2.902
```

If your NuGet cache is redirected (like the iPlus V5 workspace), set
`NUGET_PACKAGES` accordingly:

```bash
NUGET_PACKAGES=/home/<user>/Devel/iPlusGit/V5/packages ./build-iplus.sh 12.2.902
```

### Usage (Windows)

```powershell
cd D:\Devel\iPlusGit\V5\Avalonia

# Default: CreateNugetPackages (no API-diff validation) - recommended
.\build-iplus.ps1 -Version 12.2.902

# Full pipeline including ValidateApiDiff
.\build-iplus.ps1 -Version 12.2.902 -Target Package

# Also pack the iOS package (requires the 'ios' workload, see below)
.\build-iplus.ps1 -Version 12.2.902 -PackIOS

# Optional: custom NuGet cache location
.\build-iplus.ps1 -Version 12.2.902 -NuGetPackages D:\Devel\iPlusGit\V5\packages
```

### Publishing

```bash
dotnet nuget push artifacts/nuget/*.nupkg \
    --api-key <YOUR-API-KEY> \
    --source https://api.nuget.org/v3/index.json \
    --skip-duplicate
```

`--skip-duplicate` makes it safe to push from both Linux and Windows —
packages that already exist are skipped. **Never commit or share your API key.**

---

## Prerequisites per platform package

### Android (`iPlus.Avalonia.Android`)

| Platform | Possible? | Requirement |
|---|---|---|
| Linux | ✅ | `dotnet workload install android` |
| Windows | ✅ | `dotnet workload install android` |

The Android backend is pure C# (Xamarin.AndroidX bindings) — no native build
step is needed. Verify the workload is visible with `dotnet workload list`.

### iOS (`iPlus.Avalonia.iOS`)

| Platform | Possible? | Requirement |
|---|---|---|
| Linux | ❌ | The `ios` workload is **not available on Linux** (`Workload ID ios isn't supported on this platform`) |
| Windows | ✅ | `dotnet workload install ios` |
| macOS | ✅ | `dotnet workload install ios` |

The iOS backend itself is pure C# (rendering comes from Skia/HarfBuzz), but the
Apple targeting packs only install on Windows/macOS. **Build the iOS package on
Windows or macOS** — e.g. with `.\build-iplus.ps1 -Version <ver> -PackIOS`.

### macOS Native (`iPlus.Avalonia.Native`)

| Platform | Possible? | Result |
|---|---|---|
| Linux | ⚠️ partial | Package **without** `libAvalonia.Native.OSX.dylib` (via `ForcePackAvaloniaNative=True`) |
| Windows | ⚠️ partial | Same as Linux |
| macOS | ✅ full | Package **with** the dylib (built via Xcode) |

`libAvalonia.Native.OSX.dylib` is Objective-C++ code compiled from the Xcode
project in `native/Avalonia.Native/src/OSX/` — it **can only be built on macOS**.
The scripts always produce a Native package (restores and compiles fine
everywhere), but a macOS Desktop app would fail at runtime with the
dylib-less package.

**To get a complete Native package**, either:

- build on macOS (the Nuke pipeline builds the dylib automatically there), or
- copy a prebuilt `libAvalonia.Native.OSX.dylib` to
  `artifacts/native/Release/libAvalonia.Native.OSX.dylib` before packing —
  then the dylib is included automatically (no `ForcePackAvaloniaNative` needed).

---

## Recommended workflow (both machines)

1. **Linux**: `./build-iplus.sh <version>` → push everything.
2. **Windows**: `.\build-iplus.ps1 -Version <version> -PackIOS` → push with
   `--skip-duplicate` (only the iOS package will be new).
3. Verify on nuget.org that `iPlus.Avalonia.Native` and `iPlus.Avalonia.iOS`
   exist for the version before updating consumer projects.

## Troubleshooting

- **`MSB4181: The "RestoreTask" task returned false but did not log an error`
  on all projects** — usually a stale workload installation after an SDK
  update. Fix with `dotnet workload repair` (or `dotnet workload update`).
- **`NU1102: Unable to find package iPlus.Avalonia.Native`** — the Native
  package was not published for that version. Run the script (it always packs
  Native) and push it.
- **`NU5129` warnings about props/targets** — build files inside the package
  must be named `<PackageId>.props/.targets`; the fork already renames them
  (see `build/SharedVersion.props`, `packages/Avalonia/`,
  `src/Browser/Avalonia.Browser/build/`).
- **Duplicate type errors in consumer apps** — you referenced both
  `iPlus.Avalonia` and one of the standalone packages. Remove one of them.
