using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kitchen;
using Kitchen.Modules;

namespace ModList
{
    /// <summary>
    /// Inserts the "Mods" button into the pause menu's main page, directly after vanilla's
    /// Options button, in every scene (HQ, restaurant, tutorial).
    ///
    /// A transpiler is used because menu elements get their positions as they're added, so a
    /// postfix could only put the button below "Continue". The match is narrow: the
    /// <c>ldarg.0; ldarg.0; call get_Localisation; ldstr "MENU_REMOVE_INPUT"</c> sequence that
    /// starts the statement after Options. If a game update changes it, the menu is left
    /// untouched and a warning is logged.
    ///
    /// Other mods patch the same method with the same technique on different anchors (Easy Floor
    /// Planner before "MENU_OPTIONS", Restart Day before "MENU_ABANDON"); Harmony chains the
    /// transpilers, and none of the inserted code disturbs the others' anchors.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Setup))]
    internal static class MainMenu_Setup_Patch
    {
        public const string Label = "Mods";

        private static readonly MethodInfo AddSubmenuButton = AccessTools.Method(
            typeof(Menu<MenuAction>), "AddSubmenuButton", new[] { typeof(string), typeof(Type), typeof(bool) });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 3; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr && (codes[i].operand as string) == "MENU_REMOVE_INPUT"
                    && codes[i - 1].opcode == OpCodes.Call && (codes[i - 1].operand as MethodInfo)?.Name == "get_Localisation"
                    && codes[i - 2].opcode == OpCodes.Ldarg_0
                    && codes[i - 3].opcode == OpCodes.Ldarg_0)
                {
                    // codes[i - 3] starts the "Remove input" statement and may be a jump target,
                    // so its labels move to our first instruction.
                    CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0);
                    codes[i - 3].MoveLabelsTo(first);
                    codes.InsertRange(i - 3, new[]
                    {
                        first,
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MainMenu_Setup_Patch), nameof(AddModsButton))),
                    });
                    return codes;
                }
            }
            ModMain.LogWarning("Couldn't find the Remove Input button in MainMenu.Setup (game update?); the Mods button won't be shown");
            return codes;
        }

        private static void AddModsButton(MainMenu menu)
        {
            try
            {
                if (AddSubmenuButton == null)
                {
                    ModMain.LogWarning("Menu.AddSubmenuButton not found; the Mods button won't be shown");
                    return;
                }
                AddSubmenuButton.Invoke(menu, new object[] { Label, typeof(ModListMenu), false });
            }
            catch (Exception e)
            {
                // Never let our button break the vanilla pause menu.
                ModMain.LogError($"Failed to add the Mods button: {e}");
            }
        }
    }

    /// <summary>
    /// Registers the Mods submenu. LocalMenuView clears its menus and calls SetupMenus every time
    /// the pause menu opens, then wires up the events of everything registered, so a postfix here
    /// gets the same treatment as the vanilla submenus.
    /// </summary>
    [HarmonyPatch(typeof(PlayerPauseView), "SetupMenus")]
    internal static class PlayerPauseView_SetupMenus_Patch
    {
        private static readonly FieldInfo MenusField = AccessTools.Field(typeof(LocalMenuView<MenuAction>), "Menus");
        private static readonly FieldInfo ModuleListField = AccessTools.Field(typeof(LocalMenuView<MenuAction>), "ModuleList");

        static void Postfix(PlayerPauseView __instance)
        {
            try
            {
                Dictionary<Type, Menu<MenuAction>> menus = MenusField?.GetValue(__instance) as Dictionary<Type, Menu<MenuAction>>;
                ModuleList moduleList = ModuleListField?.GetValue(__instance) as ModuleList;
                if (menus == null || moduleList == null)
                {
                    ModMain.LogWarning("PlayerPauseView internals not found; the Mods menu can't be registered");
                    return;
                }
                menus[typeof(ModListMenu)] = new ModListMenu(__instance.ButtonContainer, moduleList);
            }
            catch (Exception e)
            {
                ModMain.LogError($"Failed to register the Mods menu: {e}");
            }
        }
    }
}
