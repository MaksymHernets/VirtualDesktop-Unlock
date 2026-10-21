# v0.1.0 verification

The working runtime-helper approach was verified with Virtual Desktop Streamer 1.34.23.0, Steam Resident Evil 3 (952060), REFramework, and a Meta Quest 3. The user confirmed that RE3 appeared in Virtual Desktop's actual headset Games tab and launched in VR.

The helper preserves all 13 existing exception-table entries and adds one OpenXR override. The original executable's signature remained valid. The release keeps this mechanism but packages it with versioned original source, an icon, and a portable installer.

Automated checks cover:

- Steam VDF parsing across multiple library folders, Unicode paths, malformed manifests, and path containment.
- Cache-based initial companion selections without writing Steam/Virtual Desktop settings.
- Add, Remove, multi-selection, Undo, search, persisted selections, and an intentionally empty library.
- Atomic saves, backups, stale-write conflicts, and invalid-data rejection.
- Hidden/minimized/off-screen companion-window recovery and second-instance activation.
- Loading the .NET startup manager from an application configuration file.
- Optional delayed helper initialization against a locally supplied supported Streamer binary.

Only the RE3 override and Streamer 1.34.23.0 are supported by this release. Companion selections are not sent to the helper. The release installer refuses any other executable hash, refuses unrelated existing configuration/helper files, and has not been tested against future Streamer updates. Neither proprietary binaries nor personal test logs are redistributed.
