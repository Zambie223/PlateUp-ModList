using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ModList
{
    /// <summary>Mod List's own preferences, kept in PlayerPrefs (per user, not per save).</summary>
    internal static class ModListSettings
    {
        private const string SortKey = "zambie.modlist.sort";
        private const string WorkshopTagKey = "zambie.modlist.workshop_tag";

        public const string SortByName = "name";
        public const string SortByLoad = "load";

        public static string Sort
        {
            get => PlayerPrefs.GetString(SortKey, SortByName) == SortByLoad ? SortByLoad : SortByName;
            set => PlayerPrefs.SetString(SortKey, value == SortByLoad ? SortByLoad : SortByName);
        }

        public static bool SortByLoadOrder => Sort == SortByLoad;

        public static bool ShowWorkshopTag
        {
            get => PlayerPrefs.GetInt(WorkshopTagKey, 1) == 1;
            set => PlayerPrefs.SetInt(WorkshopTagKey, value ? 1 : 0);
        }
    }

    /// <summary>
    /// Mod List's own entry in the Mods screen, written exactly the way docs/MOD_OPTIONS.md tells
    /// other mods to do it, so it doubles as the reference implementation.
    /// </summary>
    public static class ModListOptions
    {
        public static IEnumerable GetOptions()
        {
            List<Dictionary<string, object>> options = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "sort",
                    ["type"] = "choice",
                    ["label"] = "Sort mods by",
                    ["choices"] = new[] { ModListSettings.SortByName, ModListSettings.SortByLoad },
                    ["labels"] = new[] { "Name", "Load order" },
                    ["get"] = (Func<string>)(() => ModListSettings.Sort),
                    ["set"] = (Action<string>)(v => ModListSettings.Sort = v),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "workshop_tag",
                    ["type"] = "bool",
                    ["label"] = "Workshop tag",
                    ["description"] = "Marks mods that come from the Steam Workshop.",
                    ["labels"] = new[] { "Hidden", "Shown" },
                    ["get"] = (Func<bool>)(() => ModListSettings.ShowWorkshopTag),
                    ["set"] = (Action<bool>)(v => ModListSettings.ShowWorkshopTag = v),
                },
            };
#if DEBUG
            options.AddRange(DebugSample.Options());
#endif
            return options;
        }

        public static void OnOptionsClosed()
        {
            PlayerPrefs.Save();
        }
    }

#if DEBUG
    /// <summary>
    /// Debug builds only: every option type and edge case, for testing the options page.
    /// Values live in memory only.
    /// </summary>
    internal static class DebugSample
    {
        private static int _number = 3;
        private static string _flavour = "b";
        private static bool _locked = true;
        private static int _presses;
        private static bool _roll;
        private static int _night = 5;
        private static int _chance = 20;
        private static int _percent = 250;
        private static int _wide = 50000;

        public static IEnumerable<Dictionary<string, object>> Options()
        {
            yield return new Dictionary<string, object> { ["type"] = "heading", ["label"] = "Debug sample" };
            yield return new Dictionary<string, object>
            {
                ["id"] = "percent", ["type"] = "int", ["label"] = "Slider + typing (0 to 1000%)",
                ["description"] = "Drag the bar, hold left / right, or select it to type a number.",
                ["min"] = 0, ["max"] = 1000, ["format"] = "{0}%", ["slider"] = true, ["input"] = true,
                ["get"] = (Func<int>)(() => _percent),
                ["set"] = (Action<int>)(v => _percent = v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "wide", ["type"] = "int", ["label"] = "Typing only, steps of 10 (0 to 100000)",
                ["min"] = 0, ["max"] = 100000, ["step"] = 10, ["input"] = true,
                ["get"] = (Func<int>)(() => _wide),
                ["set"] = (Action<int>)(v => _wide = v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "number", ["type"] = "int", ["label"] = "Number (clamped to even by the mod)",
                ["min"] = 1, ["max"] = 30, ["format"] = "Every {0} nights",
                ["get"] = (Func<int>)(() => _number),
                // Rejects odd values, to check that the page re-reads what the mod accepted.
                ["set"] = (Action<int>)(v => _number = v % 2 == 0 ? v : v + 1),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "flavour", ["type"] = "choice", ["label"] = "Flavour",
                ["choices"] = new List<string> { "a", "b", "c" },
                ["labels"] = new List<string> { "Apple", "Banana", "Cherry" },
                ["get"] = (Func<object>)(() => _flavour),
                ["set"] = (Action<object>)(v => _flavour = (string)v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "locked", ["type"] = "bool", ["label"] = "Locked (enabled = false)",
                ["description"] = "Always locked.",
                ["enabled"] = false,
                ["get"] = (Func<bool>)(() => _locked),
                ["set"] = (Action<bool>)(v => _locked = v),
            };
            yield return new Dictionary<string, object>
            {
                // The exact host-only expression docs/MOD_OPTIONS.md recommends, compiled here so
                // the documented namespaces stay correct.
                ["id"] = "host_only", ["type"] = "bool", ["label"] = "Host only",
                ["description"] = "Host's setting applies.",
                ["enabled"] = (Func<bool>)(() => Kitchen.Session.NetworkedPlayState != Platforms.NetworkedPlayState.Client),
                ["get"] = (Func<bool>)(() => _locked),
                ["set"] = (Action<bool>)(v => _locked = v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "press", ["type"] = "button", ["label"] = "Press me (logs)",
                ["action"] = (Action)(() => ModMain.Log($"Debug sample button pressed {++_presses} time(s); number={_number}, flavour={_flavour}")),
            };
            yield return new Dictionary<string, object> { ["type"] = "heading", ["label"] = "Visibility" };
            yield return new Dictionary<string, object>
            {
                ["id"] = "roll", ["type"] = "bool", ["label"] = "Unlock by",
                ["labels"] = new[] { "Fixed night", "Random chance" },
                ["get"] = (Func<bool>)(() => _roll),
                ["set"] = (Action<bool>)(v => _roll = v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "night", ["type"] = "int", ["label"] = "Unlocked on night",
                ["min"] = 1, ["max"] = 15, ["format"] = "Night {0}",
                ["visible"] = (Func<bool>)(() => !_roll),
                ["get"] = (Func<int>)(() => _night),
                ["set"] = (Action<int>)(v => _night = v),
            };
            yield return new Dictionary<string, object>
            {
                ["id"] = "chance", ["type"] = "int", ["label"] = "Chance per night",
                ["min"] = 0, ["max"] = 100, ["format"] = "{0}%", ["slider"] = true,
                ["visible"] = (Func<bool>)(() => _roll),
                ["get"] = (Func<int>)(() => _chance),
                ["set"] = (Action<int>)(v => _chance = v),
            };
            // A heading with nothing visible under it hides itself.
            yield return new Dictionary<string, object> { ["type"] = "heading", ["label"] = "Never shown" };
            yield return new Dictionary<string, object>
            {
                ["id"] = "hidden", ["type"] = "bool", ["label"] = "Always hidden", ["visible"] = false,
                ["get"] = (Func<bool>)(() => false),
                ["set"] = (Action<bool>)(v => { }),
            };
            yield return new Dictionary<string, object> { ["type"] = "heading", ["label"] = "Errors" };
            yield return new Dictionary<string, object> { ["id"] = "future", ["type"] = "slider", ["label"] = "Unknown type" };
            yield return new Dictionary<string, object> { ["id"] = "broken", ["type"] = "int", ["label"] = "Broken int (no min/max)" };
        }
    }
#endif
}
