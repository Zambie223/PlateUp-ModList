using System.Reflection;
using HarmonyLib;
using KitchenMods;
using UnityEngine;

namespace ModList
{
    /// <summary>
    /// Entry point. Adds a "Mods" button to the pause (escape) menu that opens a paged panel
    /// listing every mod the game loaded, with its version.
    ///
    /// How it fits together:
    ///   MenuPatches     inserts the button after vanilla's Options and registers ModListMenu.
    ///   ModListMenu     the panel, built like vanilla's Recipes panel (left / right to page).
    ///   ModCatalog      reads the game's own loaded-mod list (KitchenMods.ModPreload.Mods) and
    ///                   works out a display name and version for each mod.
    ///   ModOptions      the mod-options contract: finds each mod's static ModListOptions class
    ///                   and parses what it returns (docs/MOD_OPTIONS.md).
    ///   GearElement     the settings gear on each row whose mod has options.
    ///   ModOptionsMenu  one mod's options page.
    /// Purely local UI: nothing is synced, so it's fine for only some players to have it.
    /// </summary>
    public class ModMain : IModInitializer
    {
        public const string Id = "zambie.modlist";
        public const string Name = "Mod List";
        public const string Version = "0.2.0";

        private static Harmony _harmony;

        public void PostActivate(KitchenMods.Mod mod)
        {
            Log($"v{Version} loaded");

            if (_harmony == null)
            {
                _harmony = new Harmony(Id);
                _harmony.PatchAll(typeof(ModMain).Assembly);
                foreach (MethodBase method in _harmony.GetPatchedMethods())
                {
                    Log($"Patched {method.DeclaringType?.FullName}.{method.Name}");
                }
            }
        }

        public void PreInject() { }

        public void PostInject() { }

        // Everything goes to Player.log:
        // %USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\Player.log
        internal static void Log(string message) => Debug.Log($"[{Name}] {message}");
        internal static void LogWarning(string message) => Debug.LogWarning($"[{Name}] {message}");
        internal static void LogError(string message) => Debug.LogError($"[{Name}] {message}");
    }
}
