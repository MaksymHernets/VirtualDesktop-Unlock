# Verification

## 0.1.3 system tray

The system tray setting defaults to enabled and persists in the current user's AppData settings file. Automated checks cover the default, saved disable/enable, close-to-tray, restoring the window and taskbar entry, disabling tray mode while hidden, normal closing when disabled, and explicit Exit while enabled. Preview and automated tests suppress actual tray icons and use isolated preference files. The Settings window with the new checkbox was rendered and visually inspected.

Window and tray icons use embedded white, red and orange open-padlock variants. Tests cover transitions to red when Streamer stops, back to white when it restarts with a loaded library, and orange while the app library is not loaded. Orange takes priority if Streamer is also stopped. The generated artwork was visually inspected and packaged into 16–256 pixel Windows icons with transparency.

## 0.1.2 settings and interface

The application and executable are named Virtual Desktop Unlock. The right panel is labeled VD cache games. The status block updates every three seconds: green for a running Streamer with a loaded app library, red for a stopped Streamer with a loaded library, and orange while the app library is not loaded. This reports the desktop app's library load state; the restart link separately tracks whether the helper has applied the selected IDs.

Settings open from the gear beside the title. Tests cover reading, enabling, disabling and reopening the startup preference, write-failure recovery, and disabled language/bitrate placeholders. The Windows startup registration was additionally verified in an isolated current-user registry key, including quoting executable paths with spaces and removing the value. The actual Windows Run key was left unchanged during testing. Main-window and Settings renders were visually inspected.

The clickable restart flow was tested for pending changes, failure/retry, successful application, removal and undo. The real restart command successfully restarted the installed Streamer, and its new process logged all 15 selected IDs. Runtime status checks distinguished the applied selection from a modified selection.

## 0.1.1 regression coverage

The updated app exports all selections into the startup helper's shared XML configuration. Tests cover Gothic 1 Classic (65540) alongside RE3 (952060), removal of all custom overrides, rejected unsafe XML, and Add/Remove/Undo synchronization. The live helper log confirmed Gothic, RE3, RE7 and Village IDs in the updated process. The user confirmed Gothic appears in the headset; Gothic VR launch remains unverified. Regression tests also cover arbitrary IDs and selected built-in RealVR/TeamBeef exceptions. The original signed Streamer remains unchanged.

The working runtime-helper approach was verified with Virtual Desktop Streamer 1.34.23.0, Steam Resident Evil 3 (952060), REFramework, and a Meta Quest 3. The user confirmed that RE3 appeared in Virtual Desktop's actual headset Games tab and launched in VR.

The original v0.1.0 helper preserved all 13 existing exception-table entries and added one OpenXR override. The current helper preserves unselected built-in exceptions and replaces selected exceptions with the unconditional OpenXR type. The original executable's signature remained valid.

Automated checks cover:

- Steam VDF parsing across multiple library folders, Unicode paths, malformed manifests, and path containment.
- Cache-based initial companion selections without writing Steam/Virtual Desktop settings.
- Add, Remove, multi-selection, Undo, search, persisted selections, and an intentionally empty library.
- Atomic saves, backups, stale-write conflicts, and invalid-data rejection.
- Hidden/minimized/off-screen companion-window recovery and second-instance activation.
- Loading the .NET startup manager from an application configuration file.
- Optional delayed helper initialization against a locally supplied supported Streamer binary.

The v0.1.0 release supports only RE3 and does not sync selections. Version v0.1.3 supports all selected Steam IDs with Streamer 1.34.23.0 and helper v0.1.1; arbitrary games have not each been tested in the headset. The release installer refuses any other executable hash, refuses unrelated existing configuration/helper files, and has not been tested against future Streamer updates. Neither proprietary binaries nor personal test logs are redistributed.
