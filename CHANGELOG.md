# Changelog

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
