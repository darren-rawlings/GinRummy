# Game Template

Starting point for a new Renegade Games title: a Godot 4.7 C# project for Android phones with
everything Counter Fire needed already wired up.

- **Shared library** (GameHud, Stage, SpinControl, GameSettings, Styles, ComputerTurn, ads, sound, flight recorder) [renegade-core](https://github.com/darren-rawlings/renegade-core) at
  `addons/renegade_core` (git submodule): ads, synthesised sound, developer tools, and the
  [release playbook](https://github.com/darren-rawlings/renegade-core/blob/main/docs/RELEASE_PLAYBOOK.md).
- **AdMob** via the Poing Studios plugin (`addons/admob`), Google's test ads until real IDs exist,
  child-directed by default; a stand-in ad on desktop.
- **Android export** preset with Gradle build and adaptive launcher icons.
- **Tests** for the engine-free rules code (`src/Core`) and **GitHub CI**.

## Start a new game

```
gh repo create darren-rawlings/<repo> --public --template darren-rawlings/renegade-game-template --clone
cd <repo>
git submodule update --init
powershell -File tools/New-Game.ps1 -Title "Mancala" -CodeName Mancala -PackageId mancala
```

Then design in `docs/DESIGN.md`, replace the table in `src/Game/Main.cs` with the game, and draw
the icon in `assets/icon/*.svg` (render with `tools/Render-Icons.ps1`).

## Run

- Desktop: open in Godot, or F5 in VS Code ("Play GameTemplate").
- Developer flags after `--`: `--log`, `--demo`, `--quit-after-demo`, `--games-since-ad=4`.
  On a phone, push them as lines of `files/dev_args.txt` (see renegade-core's playbook).
- Android build (first create the output folder: `mkdir build/android`):
  `Godot.exe --headless --path . --install-android-build-template --export-debug "Android" build/android/GameTemplate.apk`

## Layout

| Folder | What goes there |
|---|---|
| `src/Core` | Rules: plain C#, no Godot types, unit tested |
| `src/Game` | Scene, input, visuals, sound, HUD |
| `tests/` | xUnit tests for `src/Core` |
| `assets/icon` | Icon SVG sources and rendered PNGs |
| `tools/` | `New-Game.ps1`, `Render-Icons.ps1` |
| `docs/` | `DESIGN.md` for this game |
