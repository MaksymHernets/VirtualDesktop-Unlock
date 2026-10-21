<p align="center"><img src="assets/icon.png" width="112" height="112" alt="Virtual Desktop Unlock icon" /></p>

# Virtual Desktop Unlock

**v0.1.2** · Windows x64 · Unofficial · MIT

An unofficial program for extending Virtual Desktop Streaming. Add custom Steam games, including games that use third-party VR mods, to Virtual Desktop's actual headset Games tab using the optional runtime helper. The desktop companion app organizes your installed Steam games and saves selections for the helper. Increasing video streaming bitrate limits is planned for a future update; the current Settings option is disabled.

## Two distinct features

| Feature | Result |
|---|---|
| Optional integration | Adds custom Steam IDs to Streamer's game discovery. Requires Streamer **1.34.23.0**. RE3 was confirmed in the headset and launched in VR. Gothic 1 Classic was also confirmed in the headset Games tab. |
| Virtual Desktop Unlock app | Add/Remove buttons, search, saving, Undo, and Steam launching. Custom additions sync to the helper's shared configuration. Restart Streamer and reconnect the headset to apply changes. |

**Both the app and helper must be version 0.1.1 or later for selection syncing.** Version 0.1.0's helper added only RE3. Removing an entry removes its custom override; native VR titles can still appear automatically through Steam metadata. Adding a game does not install or enable a VR mod.

When the saved selection differs from the running helper's confirmed selection, a **Restart Streamer and reconnect to apply custom additions** link appears below the lists. Click it to close and relaunch Streamer; Windows may request administrator permission. The link clears after the helper confirms the selection. Reconnect the headset and reopen Games afterward. The app also detects restarts performed outside the app; failed or cancelled restarts leave the link available to retry.

## Download and run

Use the gear beside **Virtual Desktop Unlock** to open Settings. **Launch automatically when Windows starts** saves immediately for the current Windows user. Keep the app in its current folder, or disable and enable this option after moving it. Language selection and streaming bitrate-limit removal are disabled placeholders for future updates.

The status block shows whether Streamer is running and the app's library is loaded. It turns green when both are ready, red when Streamer is stopped with a loaded library, and orange while the library is not loaded. The right-hand **VD cache games** panel contains your saved selection for the headset library.

Download the latest build from [Releases](https://github.com/MaksymHernets/virtual-desktop-unlock/releases), or build this source, and extract it to a writable folder. The 0.1.0 release does not include selection syncing.

- Open **Virtual Desktop Unlock.exe** to use the companion app. Requires **.NET Desktop Runtime 10**. No administrator permission is needed for this app.
- Install the optional integration separately using **Install RE3 Integration.cmd**. This requests Administrator permission and briefly restarts Streamer. Requires the exact supported Streamer 1.34.23.0 binary and .NET Framework 4.7.1 or later (4.8 is suitable).
- Install REFramework's VR mod for RE3 yourself. This project does not install the game or its mod.

After installing the integration, reconnect Virtual Desktop in the headset and reopen **Games**. Launch **Resident Evil 3** from there.

If a different or manually installed helper/configuration already exists, the installer refuses to overwrite it. Uninstall that helper with its original removal procedure first. Companion selections are saved in `Data/library.json` next to the app, with the previous version in `library.json.bak`.

## What the integration installs

The .NET runtime reads a small `VirtualDesktop.Streamer.exe.config` and loads `VirtualDesktop.LibraryOverrides.dll` from the Streamer directory. After the WPF application is ready, the helper reads `%APPDATA%\Virtual Desktop Unlock\GameOverrides.xml` and adds the selected custom Steam IDs as OpenXR exceptions. Every game on the right is exported, regardless of its origin or Steam VR flags. Selected games override built-in mod-file checks; unselected exceptions retain their original behavior. An explicitly empty shared file disables all custom additions; if no shared file exists, the helper retains the original RE3 default.

The original Streamer executable is **not modified**, and its developer signature remains valid. The helper DLL is unsigned and runs with Streamer's privileges every time Streamer starts. Installing it is optional. It does not change Windows security settings, Steam files, game executables, or game mods. Not affiliated with or endorsed by Virtual Desktop, Valve, Capcom, or REFramework.

To remove it, run **Uninstall RE3 Integration.cmd**. The uninstaller checks its installation record and removes only the matching helper/configuration. If those files were changed externally, it stops instead. Streamer updates may require uninstalling the helper; unsupported versions are skipped by the helper and rejected by the installer.

Logs: `%ProgramData%\Virtual Desktop\LibraryOverrides.log` and `%ProgramData%\Virtual Desktop Unlock\installer.log`. An installation record is kept alongside the helper. No Streamer binaries are included in this repository or release.

## Build

Install the .NET 10 SDK on Windows x64. The build also uses the .NET Framework C# compiler and WPF reference assemblies supplied with Windows. The app has no third-party NuGet dependencies.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build runs the companion UI/storage tests and an AppDomain-manager loading test, then creates the release ZIP and SHA-256 checksum in `artifacts/dist/`. For the additional delayed-initialization test against your own supported Streamer installation:

```powershell
.\build.ps1 -StreamerPath 'C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe'
```

That test uses an isolated configuration directory and does not launch a VR game or install the integration. A build never changes your current Streamer installation.

## Source map

- `SteamVRLibrary/`: WinForms companion app and tests.
- `src/RuntimeOverrides/`: .NET Framework startup helper, configuration, and test harness.
- `scripts/`: optional integration installer, uninstaller, and launchers.
- `assets/`: original SVG, PNG, and Windows ICO icon.
- `tools/generate-icon.py`: icon renderer (Python + Pillow), needed only when regenerating artwork.
- `docs/verification.md`: compatibility and verification scope.

The MIT license applies to this project's original code and artwork. Install Virtual Desktop and Steam separately from their official sources.
