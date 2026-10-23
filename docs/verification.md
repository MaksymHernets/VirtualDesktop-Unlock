# Verification

## Streamer 1.34.25 library compatibility — 2026-10-23

The missing custom games were caused by the old helper's explicit 1.34.23 version guard. Inspection of 1.34.25 identified the same 13 built-in game exceptions and OpenXR enum value 3; isolated test-directory fields moved from metadata tokens 0x0401483F/0x04014840 to 0x04014843/0x04014844. Helper 0.1.6.0 accepts only the exact verified SHA-256 hashes of 1.34.23.0 and 1.34.25.0 and uses the corresponding test-directory mapping.

Both versions passed the desktop checks, startup smoke test and five library scenarios (RE3, Gothic plus RE3, arbitrary IDs with built-in overrides, empty selections, and invalid XML). The 1.34.23 build also passed all 33 bitrate server checks. On 1.34.25, an enabled bitrate configuration was explicitly tested: library overrides applied, no native bitrate hook was installed, and status reported unsupported rather than a restart requirement. This is library compatibility, not a port of the bitrate patch to 1.34.25.

The helper was backed up and updated locally, retaining a valid signature on the original Streamer executable. Streamer 1.34.25's live log confirmed all 17 saved selections, including RE3 and Gothic, with its exception count increasing from 13 to 30. The updated companion app opened successfully. A separate probe using the companion's actual status classes under normal desktop permissions returned Running=True, SelectedGames=17, Applied=True and BitrateRequiresRestart=False. After reconnecting and reopening Games, the user confirmed the games appeared in the headset.

## Startup access-denied fix — 2026-10-23

Windows Application event 1026 showed an unhandled Win32Exception (5) in `StreamerControl.IsRunning`, where `Process.HasExited` requested access to the already-running Streamer process during form construction. The startup log had no entry for the failed launches because construction never completed. Running-status detection now uses process enumeration without opening a synchronization handle. Library and bitrate acknowledgement checks also omit `HasExited`, retaining their process-ID/start-time identity checks and existing error handling.

All 63 desktop checks passed. The rebuilt local app was launched by a non-administrator process while the same Streamer process remained running; it constructed and displayed the window and stayed alive through multiple status refreshes. The saved library's SHA-256 was unchanged. This verified the observed access-denied case without changing Streamer or Windows permissions; a further system reboot was not performed.

## 0.1.4 bitrate control

Tests execute the actual installed 1.34.23 ceiling method after the runtime redirect, its slider interpolation, `UpdateVRBitrates`, and `OculusStreamer` forwarding into uninitialized encoder wrappers. They cover the Quest 3 H.264/H.264+ 1000 Mbps, HEVC/HEVC10 300 Mbps, AV1/AV110 250 Mbps profiles, independent Quest 3S profiles, unconfigured stock behavior, foveation, network limiting, per-view allocation, disable/restore, invalid XML, live update notification and concurrent calls. No actual GPU encoding or headset decoding is performed by this test harness.

The desktop tests cover main-button enable/restore, profile persistence across headset changes, and the functional Settings checkbox. Headset-side actual bitrate, image quality and latency still require manual validation.

The upgraded helper was installed locally and its running Streamer process acknowledged live enable and disable without a restart. The log confirmed Quest 3 ceiling changes from 200/200/200 to 1000/300/250 Mbps and back, while preserving the original executable's valid signature and all existing game overrides.

Independent synthetic NVENC smoke tests through the installed FFmpeg encoded 30 frames at 3840x1920/90 fps with requested HEVC 300 Mbps and AV1 250 Mbps targets. H.264 at 1920x1920/90 fps with a requested 500 Mbps per-view target also encoded successfully. A single 1000 Mbps H.264 stream was rejected by NVENC with an invalid-level error, including an explicit level-5.2 attempt. These checks exercise hardware encoder acceptance on synthetic input; they do not verify Virtual Desktop's complete GPU path, sustained throughput, network delivery or headset decoding.

The same synthetic HEVC and AV1 checks also passed with 10-bit (`p010le`) input. The native-diagnostic build passed 63 desktop checks, 33 dedicated bitrate server checks, the helper startup smoke test and five isolated game-library cases.

The first headset report still showed 200 Mbps in the performance overlay while the override was enabled. End-to-end bitrate unlocking is therefore **not confirmed working**. Configuration acknowledgement and isolated forwarding tests must not be treated as proof of a higher actual stream bitrate. The helper now logs changed live settings every five seconds: headset, active codec/source, automatic adjustment, normalized slider, measured bandwidth, spacewarp/foveation, all three calculated targets and the two main encoder wrappers' target fields. Reading these diagnostics does not change the targets; both the values and read-only behavior are tested. The saved AV1 ceiling during diagnostic installation was 222 Mbps and was preserved. A live VR session confirmed Quest 3, AV1 10-bit, normalized slider 1, measured bandwidth 1000 Mbps, automatic adjustment off and a computed AV1 target of 222 Mbps.

The original test had added an artificial `VRBitrateLimitChanged` subscription to forward the computed values. The actual OculusStreamer does not make that subscription, so that test overstated live-update coverage. The helper now finds the existing OculusStreamer through its real `HmdTypeChanged` subscription and calls the original forwarding method after configuration updates. The revised test uses that real subscription pattern without the artificial forwarding event. A subsequent live VR session showed 111 Mbps AV1 in each main encoder wrapper (222 Mbps combined), but the user still reported 200 Mbps in the headset overlay. Wrapper targets alone therefore do not resolve the discrepancy.

Native NVENC diagnostics now read its configuration's separate average and maximum bitrate fields. Verified metadata tokens identify the fields; generated IL calculates offsets because duplicate obfuscated names make `Marshal.OffsetOf(name)` ambiguous. `ReadProcessMemory` performs kernel-validated reads from the current process and returns unavailable when a configuration pointer cannot be read. The diagnostics never write native configuration. Isolated tests verify separate average/maximum values, unchanged memory, null pointers and invalid pointers. A live Quest 3 AV1 10-bit VR session subsequently showed **average=111,000,000 and maximum=111,000,000 on each of the two encoders**, matching the configured 222 Mbps aggregate budget. This rules out a stale 100 Mbps per-view maximum in that session; it still does not measure actual encoded traffic. The headset overlay's reported 200 Mbps remains unexplained without client-side evidence or a sustained traffic measurement.

A subsequent 24-second traffic sample during the user's moving VR scene measured 84.43, 87.93, 86.09, 93.44, 88.62 and 89.62 Mbps outgoing on the Ethernet adapter. These adapter counters include other host traffic and protocol overhead; they are not an isolated video-payload measurement. The helper remained in VR with the same 111 Mbps average/maximum per encoder throughout. The overlay's 200 Mbps therefore does not correspond to the measured adapter traffic or the modified NVENC target in this experiment. The evidence is consistent with a client-side target display, but the client implementation was not inspected, so that remains an inference. Actual sustained video throughput above 200 Mbps is still unverified: this scene never reached even 200 Mbps of total adapter traffic.

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
