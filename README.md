<p align="center"><img src="assets/icon.png" width="112" height="112" alt="Virtual Desktop Unlock icon" /></p>

# Virtual Desktop Unlock

**v0.1.0** · Windows x64 · Unofficial · MIT

Add **Resident Evil 3 Remake** to Virtual Desktop's actual headset Games tab using an optional runtime helper. A separate desktop companion app lets you organize installed Steam games into a personal VR library.

## Two distinct features

| Feature | Result |
|---|---|
| Optional RE3 integration | Adds RE3 (Steam app 952060) to Virtual Desktop Streamer's game discovery. Tested with Streamer **1.34.23.0**, REFramework and Quest 3: the game appeared in the headset and launched in VR. |
| Steam VR Library app | Two lists with Add/Remove buttons, search, automatic saving, Undo, and Steam launching. These selections are independent of Virtual Desktop's Games tab. |

**The companion app's transfer buttons do not configure the startup helper.** In v0.1.0 the helper has one fixed override: RE3. There is no general-purpose headset-library editor yet.

## Download and run

Download `VirtualDesktopUnlock-v0.1.0-win-x64.zip` from [Releases](https://github.com/MaksymHernets/virtual-desktop-unlock/releases) and extract it to a writable folder.

- Open **Steam VR Library.exe** to use the companion app. Requires **.NET Desktop Runtime 10**. No administrator permission is needed for this app.
- Install the optional integration separately using **Install RE3 Integration.cmd**. This requests Administrator permission and briefly restarts Streamer. Requires the exact supported Streamer 1.34.23.0 binary and .NET Framework 4.7.1 or later (4.8 is suitable).
- Install REFramework's VR mod for RE3 yourself. This project does not install the game or its mod.

After installing the integration, reconnect Virtual Desktop in the headset and reopen **Games**. Launch **Resident Evil 3** from there.

If a different or manually installed helper/configuration already exists, the installer refuses to overwrite it. Uninstall that helper with its original removal procedure first. Companion selections are saved in `Data/library.json` next to the app, with the previous version in `library.json.bak`.

## What the integration installs

The .NET runtime reads a small `VirtualDesktop.Streamer.exe.config` and loads `VirtualDesktop.LibraryOverrides.dll` from the Streamer directory. After the WPF application is ready, the helper adds `952060 → OpenXR` to Streamer's in-memory game-exception dictionary. Existing entries remain intact.

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
