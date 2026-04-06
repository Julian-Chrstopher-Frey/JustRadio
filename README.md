# JustRadio

JustRadio is a cross-platform .NET MAUI radio app prototype. It loads internet radio stations by country/region, shows a clean station browser, provides weather and map context for the selected region, and plays streams through a shared WebView-based audio host.

## Project Layout

- `src/RadioBloom.Maui`
  The MAUI app source for Windows and Mac Catalyst.
- `NuGet.Config`
  Uses `nuget.org` as the package source.
- `Launch JustRadio MAUI.cmd`
  Convenience launcher for running the Windows MAUI target from source.

Generated folders such as `bin`, `obj`, `dist-maui*`, `dist-winui*`, and old Windows-only projects are intentionally ignored and should not be committed.

## Requirements

- .NET 10 SDK with the MAUI workload installed.
- Windows: Windows 10 1809 or newer.
- Mac: macOS 14 or newer should be fine, but build/test it on the Mac before treating the Mac version as proven.
- Mac build tooling: Xcode and the MAUI Mac Catalyst workload.

## Run On Windows

From the repository root:

```powershell
.\Launch JustRadio MAUI.cmd
```

Or run the project directly:

```powershell
dotnet build .\src\RadioBloom.Maui\RadioBloom.Maui.csproj -f net10.0-windows10.0.19041.0 -t:Run
```

## Build On Mac

After cloning the repo on your Mac:

```bash
cd /path/to/JustRadio
dotnet workload install maui
dotnet build ./src/RadioBloom.Maui/RadioBloom.Maui.csproj -f net10.0-maccatalyst
```

To run from the command line:

```bash
dotnet build ./src/RadioBloom.Maui/RadioBloom.Maui.csproj -f net10.0-maccatalyst -t:Run
```

## Current Notes

- The app targets `net10.0-maccatalyst` on Mac and `net10.0-windows10.0.19041.0` on Windows.
- Playback currently uses a hidden MAUI `WebView` audio host so the same basic approach can work on Windows and Mac Catalyst.
- Track metadata is best-effort and only appears when the selected radio stream exposes ICY metadata.
- The equalizer is currently a visual playback-state indicator in the MAUI prototype, not the full native FFT analyzer from the earlier Windows-only app.
- Mac Catalyst network access is enabled in `Platforms/MacCatalyst/Entitlements.plist`.
