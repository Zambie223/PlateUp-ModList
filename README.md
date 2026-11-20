# Mod List

A [PlateUp!](https://store.steampowered.com/app/1599600/PlateUp/) mod that adds a **Mods** button to the pause (escape) menu. It opens a panel listing every mod the game loaded, with its version.

## What it shows

- **Every loaded mod:** Workshop items and local `Mods` folders, sorted by name. Workshop items are tagged "Workshop".
- **Names:**
  - **Local mods:** the folder name under `<game>\Mods\`.
  - **Workshop items:** the item's Workshop title, fetched from Steam the first time the panel opens. Until it arrives, or when offline, the mod's own name or DLL name is shown.
- **Versions:** the game has no version field for mods, so Mod List reads one from the mod's own files, in this order:
  1. The `ModVersion` its author declared, for mods built on KitchenLib.
  2. The DLL's informational version (what `<Version>` sets in an SDK-style project), without any `+build` suffix.
  3. The DLL's file version.
  4. The DLL's assembly version.

  A mod that never set a version usually shows `v1.0.0` (the .NET default), so treat that value with some caution. Mods with no DLL (asset bundles only) show "no version".
- **Failures:** mods the game failed to load are marked "failed to load".

If the list doesn't fit on one page, page through with the left and right menu controls, the same as the Recipes panel. Back or Escape returns to the pause menu.

## Mod options

Mods can add their own settings to the Mods screen. Each mod that does gets a settings gear at the end of its row in the list, which opens a settings page like the game's own; mods without settings have no gear. Select a gear with the mouse, or move between them with up / down (left / right still turns the page). Changes apply as soon as you make them. Hold left / right to change a number quickly; mods can also give numbers a slider and let you type a value.

Mod List has a couple of options itself: the sort order (name or load order), and whether to show the Workshop tag.

**For mod authors:** see [docs/MOD_OPTIONS.md](docs/MOD_OPTIONS.md). It's one static class in your mod. Your mod doesn't depend on Mod List and works the same without it.

## Install

1. Subscribe to the **Harmony** Workshop item (id `2898033283`), which this mod needs (most code mods already do).
2. Download `ModList-<version>.zip` from the [Releases](../../releases) page.
3. Extract it into `<game>\Mods\`, so you end up with `<game>\Mods\ModList\ModList.dll`. `<game>` is the folder containing `PlateUp.exe`, by default `C:\Program Files (x86)\Steam\steamapps\common\PlateUp\PlateUp`. Create the `Mods` folder if it doesn't exist.
4. Restart the game if it's running.

It's local-only: nothing is sent to other players, so it works online whether or not the others have it.

## Building from source

Requires the .NET SDK 8 or newer, PlateUp! installed through Steam, and the Harmony Workshop item on disk (the build compiles against it). `dotnet build -c Release` compiles the mod and copies it into the game's `Mods\ModList\` folder. If PlateUp isn't in the default Steam library, set `PlateUpGameDir` in a `Directory.Build.local.props` next to `Directory.Build.props`, or set the `PLATEUP_GAME_DIR` environment variable.

---

### Responsible AI use disclosure

This mod's code and documentation were written with the assistance of an AI coding tool (Claude, by Anthropic), under the direction of the author (Zambie). The game behaviour it relies on (how the mod loader records mods, and how the pause menu is built) was checked against the game's own code rather than taken from AI recall. Releases are tested in-game before they're published.
