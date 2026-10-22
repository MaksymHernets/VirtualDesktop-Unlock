# Icon artwork

The current icon is based on the icon extracted from the locally installed `VirtualDesktop.Streamer.exe`, edited to add an open-padlock badge. The monitor artwork is derived from Virtual Desktop's branding and is not original project artwork. Rights to the underlying Virtual Desktop artwork remain with its owners; the project's MIT license does not grant rights to that branding. This is an unofficial companion application.

`icon-master.png` is the edited transparent master. `tools/generate-icon.py` packages it as the 512-pixel `icon.png` and the multi-resolution Windows `icon.ico` (16–256 pixels).

`icon-stopped-master.png` and `icon-unloaded-master.png` provide matching red- and orange-lock variants. The same packager creates their PNG and ICO files. All three ICOs are embedded in the app for automatic window/tray status changes.
