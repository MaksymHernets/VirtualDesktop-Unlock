# Steam VR Library

A portable Windows companion library for your installed Steam games.

Open **app/Steam VR Library.exe**. The left list contains installed Steam games outside your VR selection; the right list is your personal VR library. On the first run, installed games found in Virtual Desktop's VR metadata cache seed the right list. The cache is not a complete inventory of the headset's Games tab.

Select games and use **Add to VR →** or **← Remove**. Ctrl/Shift selects multiple games; double-click also moves a game. Moves save immediately. **Undo move** reverses the most recent move. Search filters both lists; **Refresh** rescans Steam and preserves saved selections. **Launch selected** starts the game through Steam. Connect Virtual Desktop first and install the appropriate VR mod for flat-screen titles.

The app stores its selection in **app/Data/library.json**, with the previous version in **library.json.bak**. Keep the Data folder when moving/updating the app. A missing library file triggers the first-run seed; an intentionally empty saved library remains empty.

Opening the executable again restores the existing window, including a minimized or off-screen window. Instances are scoped to the current Windows desktop so a background desktop does not block an interactive launch. `app/startup.log` records startup stages for troubleshooting.

This is a separate companion library. Moves do not change Steam installations, install VR mods, or change Virtual Desktop's Games tab. Virtual Desktop and Steam configuration files are read only. This app does not require administrator privileges.

Requires Windows with .NET Desktop Runtime 10. See the [project README](../README.md) for the optional, separate RE3 headset-library integration.

## Development

```powershell
dotnet publish .\SteamVRLibrary.csproj -c Release -o .\app --configfile .\NuGet.Config
& '.\app\Steam VR Library.exe' --self-test
& '.\app\Steam VR Library.exe' --render-preview "$PWD\preview.png"
```

The self-test writes its report to `app/self-test-result.txt` and fixtures to `app/test-results/`. Tests cover Steam library parsing, cached VR detection, persistent changes, empty selections, backups, stale-write rejection, and invalid data. Preview renders the app's real local game lists without modifying the saved selection or launching a game.
