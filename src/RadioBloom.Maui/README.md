# JustRadio MAUI App

This is the cross-platform MAUI app project for JustRadio.

Targets:

- Windows: `net10.0-windows10.0.19041.0`
- Mac Catalyst: `net10.0-maccatalyst`

Build/run on Windows from this folder:

```powershell
dotnet build .\RadioBloom.Maui.csproj -f net10.0-windows10.0.19041.0 -t:Run
```

Build/run on Mac from this folder:

```bash
dotnet build ./RadioBloom.Maui.csproj -f net10.0-maccatalyst -t:Run
```

The current player uses a hidden MAUI `WebView` audio host for broad cross-platform compatibility. Track metadata is best-effort and depends on whether the station exposes ICY metadata.
