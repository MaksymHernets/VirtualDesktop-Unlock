# Changelog

## 0.1.5 — 2026-10-23

- Fix startup crashes when Windows denies process synchronization access to an already-running Streamer after reboot.
- Detect Streamer through process enumeration and remove unnecessary `HasExited` calls from library and bitrate acknowledgement checks.
- Keep the existing helper 0.1.4.0 and bitrate behavior; upgrading the desktop app does not require reinstalling that helper.

## 0.1.4 — 2026-10-22

- Add main-screen Remove/Restore bitrate limits controls and a functional Settings checkbox.
- Place the bitrate toggle and profile buttons in the top row beside Refresh.
- Add independent headset/codec profiles with the Quest 3 preset: H.264/H.264+ 1000, HEVC/HEVC10 300, AV1/AV110 250 Mbps.
- Extend the x64 startup helper with a verified in-memory bitrate-ceiling redirect, live configuration updates, acknowledgement, and stock fallback on disable or invalid configuration.
- Preserve bitrate slider scaling, automatic network limiting, unconfigured headset limits and original per-view allocation.
- Add isolated server tests for real Streamer calculations and encoder-wrapper forwarding; client-side stream validation remains pending.
- Refresh existing encoder wrappers through the original Streamer forwarding routine when profiles change.
- Add read-only live diagnostics for calculation inputs, encoder targets and NVENC average/maximum bitrate. A 222 Mbps AV1 budget reached native configuration, but actual video traffic above 200 Mbps remains unverified and the headset overlay still showed 200.

## 0.1.3 — 2026-10-22

- Add red (Streamer stopped) and orange (library not loaded) lock variants; switch window and tray icons automatically and show status in the tray tooltip.
- Replace the application and tray artwork with the extracted Streamer monitor icon plus an open-padlock badge.
- Add a system tray icon with Open, Settings, and Exit actions; double-click restores the window.
- Add a persisted Show in system tray setting, enabled by default. Closing the window hides it to the tray while enabled; disabling the setting restores normal close-to-exit behavior.
- Restore the window before removing its tray icon when the setting is disabled.

## 0.1.2 — 2026-10-22

- Add a gear button next to the application title and a Settings window.
- Add an optional current-user Windows startup setting; changes save immediately without administrator permission.
- Show disabled language selection and streaming bitrate-limit removal options for future updates.
- Rename the application to Virtual Desktop Unlock and the right panel to VD cache games; show live Streamer and library status.

## 0.1.1 — 2026-10-22

- Show a clickable restart message only while the saved selection is pending. Restart Streamer with elevation when clicked and verify the live helper before clearing the message; allow retry after failure or cancellation.
- Connect custom additions in the desktop app to the actual Streamer startup helper through a shared XML configuration.
- Preserve RE3 and existing manually added selections, including Gothic 1 Classic.
- Sync Add, Remove, and Undo; show the required Streamer restart/reconnect step in the interface.
- Export all selected games and bypass built-in mod-file checks for explicit selections; preserve unselected exceptions and native detection. Reject invalid XML without changing the original table.
- Add regression coverage for Gothic + RE3, empty selections, unsafe XML, and UI-to-helper synchronization.

## 0.1.0 — 2026-10-21

- First public release of Virtual Desktop Unlock.
- Optional RE3 integration adds Steam app 952060 to Virtual Desktop Streamer 1.34.23's actual Games list through an in-memory OpenXR override.
- Preserves the original signed Streamer executable and its existing game exceptions.
- Includes install and uninstall scripts with version/hash checks, conflict detection, and startup-failure rollback.
- Includes the separate Steam VR companion library with two lists, multi-selection, automatic saving, search, Undo, and Steam launching.
- Restores hidden, minimized, or off-screen companion windows when launched again.
- Adds an original monitor, headset, and open-lock icon in SVG, PNG, and multi-size ICO formats.
