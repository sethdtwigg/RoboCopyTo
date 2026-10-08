# Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build
dotnet test
```

`dotnet test` runs the unit tests plus integration tests that call the real `robocopy.exe` against temporary folders under `%TEMP%\RoboCopyTo.Tests` (deleted afterwards). One integration test reads through the `\\localhost\C$` administrative share to verify UNC quoting, so the test account needs access to it (true for a normal administrator account, elevated or not).

## Publishing

```powershell
dotnet publish src/RoboCopyTo.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

Output in `src\RoboCopyTo.App\bin\Release\net10.0-windows\win-x64\publish\`: `RoboCopyTo.exe` (self-contained, about 133 MB, because it includes the .NET runtime and WPF) plus WPF's five native DLLs, which must stay next to it:

```
RoboCopyTo.exe  D3DCompiler_47_cor3.dll  PenImc_cor3.dll  PresentationNative_cor3.dll  vcruntime140_cor3.dll  wpfgfx_cor3.dll
```

The publish command deliberately omits `IncludeNativeLibrariesForSelfExtract=true`. With that flag the DLLs are bundled into the exe, but the .NET host extracts them to `%TEMP%\.net\RoboCopyTo\` at first launch. That writes outside the app's own folders and made the first launch very slow.

## Solution layout

```
RoboCopyTo.sln
src/RoboCopyTo.Core/        No WPF. All robocopy knowledge: options, command building, safety checks,
                            running and parsing robocopy, logs, presets, shell registration.
src/RoboCopyTo.App/         WPF exe: the dialog, the multi-select collector, elevation, CLI switches.
tests/RoboCopyTo.Core.Tests xUnit tests; Fixtures/ holds real captured robocopy output.
```

NuGet packages: `CommunityToolkit.Mvvm` (App), and `xunit`, `xunit.runner.visualstudio`, and `Microsoft.NET.Test.Sdk` (tests only). Nothing else.

## Manual test checklist

These need a real Explorer session, UAC prompt, or hardware, so they are not automated:

- [ ] Register, then right-click a file, a folder, and a mixed selection of 30 items.
- [ ] Copy to a local folder, a USB drive formatted exFAT, and a network share.
- [ ] Run Mirror backup twice; the second run reports "Already up to date".
- [ ] Cancel mid-copy with and without `/Z`.
- [ ] Unplug or disconnect the network destination mid-copy with Network-friendly on.
- [ ] Use an elevation option from a mapped drive.
- [ ] Save, edit, revert, and delete a custom preset.
- [ ] Switch Windows between light and dark mode with the dialog open.
- [ ] Unregister; the menu item is gone and no RoboCopyTo keys remain under HKCU.
