# RadioBloom

RadioBloom is organized as a Windows desktop app source repository:

- `src`
  Source code for the current WinUI app and the older WPF fallback.
- `NuGet.Config`
  Package restore configuration.
- `Launch RadioBloom Native.cmd`
  Local launcher for the latest self-contained Windows build, when a `dist-winui-selfcontained-cleancontrols` publish output exists.

Generated build folders such as `dist-winui-*`, `dist`, `build`, `bin`, and `obj` are ignored by Git.

## Recommended local launch

After publishing locally, start the WinUI 3 app with:

```cmd
Launch RadioBloom Native.cmd
```

or open:

```cmd
dist-winui-selfcontained-cleancontrols\RadioBloom.WinUI.exe
```

This is the modern Windows App SDK / WinUI 3 version. The self-contained publish output bundles the .NET runtime with the app, so it does not rely on a separately installed .NET runtime on the target machine. It uses approximate IP location to load nearby stations first, supports manual country/state selection, and falls back to a built-in station catalog if online lookup fails.

## Build

Publish the current Windows app with:

```cmd
dotnet publish src\RadioBloom.WinUI\RadioBloom.WinUI.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishDir=dist-winui-selfcontained-cleancontrols\
```

## Source location

The WinUI app source lives in:

```text
src\RadioBloom.WinUI
```

Main files:

- `src\RadioBloom.WinUI\App.xaml.cs`
- `src\RadioBloom.WinUI\MainWindow.xaml.cs`
- `src\RadioBloom.WinUI\RadioServices.cs`

## WPF fallback

If you want the earlier WPF desktop version, open:

```cmd
dist\RadioBloom.Wpf.exe
```

## Legacy fallback

If you want the earlier working version, run:

```cmd
Legacy\Launch RadioBloom.cmd
```

or:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\Legacy\RadioBloom.ps1
```
