# Changelog

Changes to Mod List, starting from its first public release.

## 0.2.0

- **Mod options:** mods can now add their own settings to the Mods screen. Each one gets a settings gear on its row in the list, which opens a settings page like the game's own; changes apply immediately. Mods without settings have no gear.
- **Number options:** hold left / right to change them quickly. Mods can also give a number a slider you drag with the mouse, and let you type a value (Steam's on-screen keyboard in Big Picture mode and on Steam Deck).
- **Mod List's own options:** sort the list by name or by load order, and show or hide the Workshop tag.
- **For mod authors:** see [docs/MOD_OPTIONS.md](docs/MOD_OPTIONS.md). It's one static class, with no dependency on Mod List.

## 0.1.0

First public release.

- **Mods button:** adds a **Mods** button to the pause menu, just after **Options**, at HQ, in the restaurant and in the tutorial.
- **Mod list:** opens a panel listing every loaded mod, Workshop and local, sorted by name, with its version. Page through with left / right when the list is long.
- **Workshop titles:** Workshop items show their Workshop title, fetched from Steam the first time the panel opens.
- **Versions:** read from the mod's own files (KitchenLib's declared version, then the DLL's version information). Mods that failed to load are marked.
- **Online:** local only, so other players don't need it.
