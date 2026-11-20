# Adding in-game options to your mod

Mod List can show your mod's settings in the pause menu: **Mods**, then the settings gear on your mod's row. Players change them on the fly, with the same left / right pickers as the game's own settings pages, plus optional sliders and typed numbers.

- **No dependency:** your mod doesn't reference `ModList.dll` and doesn't need Mod List installed. Without Mod List, nothing happens; with it, your options appear.
- **Nothing to register:** Mod List looks for one class in your DLL when the Mods screen opens.

This page describes **options API version 1**. It applies to Mod List 0.2.0 and later.

## Quick start

Add this class anywhere in your mod (any namespace). The class name and method signatures are what matter.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;

public static class ModListOptions
{
    // Required. Called every time Mod List shows your options, on the main thread.
    public static IEnumerable GetOptions()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "verbose",
                ["type"] = "bool",
                ["label"] = "Verbose logging",
                ["description"] = "Writes extra detail to Player.log.",
                ["get"] = (Func<bool>)(() => MySettings.Verbose),
                ["set"] = (Action<bool>)(v => MySettings.Verbose = v),
            },
            new Dictionary<string, object>
            {
                ["id"] = "interval",
                ["type"] = "int",
                ["label"] = "Decoration nights",
                ["min"] = 1,
                ["max"] = 10,
                ["format"] = "Every {0} nights",
                ["get"] = (Func<int>)(() => MySettings.Interval),
                ["set"] = (Action<int>)(v => MySettings.Interval = v),
            },
        };
    }

    // Optional. Called when the player leaves your options page. A good place to save.
    public static void OnOptionsClosed()
    {
        MySettings.Save();
    }
}
```

That's all. Build, start the game, and open **Pause → Mods**. A settings gear appears at the end of your mod's row in the list; select it to open your options page.

## How it finds your options

- **The class:** a `static class` named exactly `ModListOptions`, in any namespace and with any accessibility, in any DLL in your mod's folder. If your mod has several DLLs, the one with your `IModInitializer` / `IModSystem` is checked first.
- **`GetOptions()`:** static, no parameters, returning anything `IEnumerable` (a `List<Dictionary<string, object>>` is easiest). Each item is one option, described by a dictionary with `string` keys (see below). Keys aren't case-sensitive.
- **`OnOptionsClosed()`:** optional, static, no parameters.
- **When it's called:** the Mods screen calls `GetOptions()` to decide whether your row gets a settings gear (no options, no gear), and your options page calls it again every time it opens. The list can change between calls, for example by hiding host-only options from clients.
- **Mods that failed to load** don't get a gear.
- **A broken `ModListOptions`** (for example `GetOptions()` throwing) still gets a gear, so you can see the error on its page.

## Option types

Every option has these keys:

| Key | Required | Meaning |
| --- | --- | --- |
| `type` | yes | `"bool"`, `"int"`, `"choice"`, `"button"` or `"heading"` |
| `label` | yes | The text shown above the picker, or on the button. Keep it under about 30 characters. |
| `id` | recommended | A stable name for the option. It's used in Mod List's log messages, and to keep focus on the option when the page is rebuilt (see `visible`). Keep ids unique within your mod. |
| `description` | no | A short explanation shown under the label, in one or two lines. |
| `enabled` | no | `bool` or `Func<bool>`. When false, the option is shown but can't be changed: pickers are labelled "(locked)" and buttons are dimmed. It's re-checked after every change. If the check throws, the option is locked and the error is logged once. |
| `visible` | no | `bool` or `Func<bool>`. When false, the option is hidden and takes no space. It's re-checked after every change; when any option appears or disappears, the page is laid out again (paging included) with focus kept on the option the player just changed. A heading with nothing visible under it (before the next heading) is hidden too. If every option is hidden, your mod's gear is hidden. If the check throws, the option is shown and the error is logged once. Use a `Func<bool>` for anything that changes while the page is open: a plain `bool` is only read when the page opens. |

**Locked or hidden?** Use `enabled` when the player should see the option exists but can't use it right now (for example host-only settings on a client). Use `visible` for options that don't apply at all in the current setup, such as two alternatives chosen by another option:

```csharp
["visible"] = (Func<bool>)(() => !RollForUpgrades),   // "Unlocked on night"
["visible"] = (Func<bool>)(() => RollForUpgrades),    // "Chance per night"
```

### `bool`: on / off

| Key | Required | Meaning |
| --- | --- | --- |
| `get` | yes | `Func<bool>` |
| `set` | yes | `Action<bool>` |
| `labels` | no | Two strings: the off text, then the on text. The default is `Off`, `On`. |

### `int`: a number in a range

| Key | Required | Meaning |
| --- | --- | --- |
| `get` | yes | `Func<int>` |
| `set` | yes | `Action<int>` |
| `min`, `max` | yes | Inclusive bounds. |
| `step` | no | The default is 1. The range may span at most 1,000,000 values (`(max - min) / step + 1`); the limit only catches mistakes such as `int.MinValue` to `int.MaxValue`. |
| `format` | no | A .NET format string for the shown value, e.g. `"Every {0} nights"` or `"{0}%"`. |
| `slider` | no | `true` adds a bar along the bottom of the picker that the mouse can click or drag. The ends, where the arrows are, still step by one. The default is `false`. |
| `input` | no | `true` lets the player type a value: select the picker (Enter, or the controller's confirm button), or click the number. The default is `false`. |

How the player changes an `int`:

- **Left / right** steps by one `step`. **Holding** it repeats, and speeds up with the size of the range, so crossing any range by holding takes a few seconds. This is true of every `int`, with or without the keys above.
- **With `slider`:** clicking or dragging the bar picks the value under the mouse. With `input` as well, a click on the number in the middle types a value instead, and dragging still slides.
- **With `input`:** this uses the game's own text entry, the same one used for lobby codes. In Steam Big Picture mode (including Steam Deck) it opens Steam's on-screen keyboard. Otherwise it needs a keyboard; a controller can only cancel it. Anything that isn't part of a number is ignored (`250%` and `1,000` both work), decimals are rounded, and the result is clamped to `min` / `max` and snapped to the nearest `step` before your `set` is called. Empty or cancelled entry changes nothing. Mod List doesn't add a hint, so say "select to type a value" in your `description` if it helps.

If `get` returns a value that's off the step grid or out of range, the nearest valid value is shown.

### `choice`: one of a list

| Key | Required | Meaning |
| --- | --- | --- |
| `get` | yes | `Func<string>`, or a getter returning an enum (its name is matched). |
| `set` | yes | `Action<string>`, or an `Action<YourEnum>` (the name is parsed). |
| `choices` | yes | The values handed to `set`: `string[]`, `List<string>`, or any `IEnumerable`. |
| `labels` | no | The display text, one per choice. The default is the values themselves. |

If `get` returns something that isn't in `choices`, the first choice is shown.

### `button`: run an action

| Key | Required | Meaning |
| --- | --- | --- |
| `action` | yes | `Action`. It's run when the button is pressed. |

Use buttons for one-off actions such as "Reset to defaults". The page re-reads every option afterwards, so values changed by the action show up straight away.

### `heading`: a section title

Only `label` is used. It's shown in capitals, with a gap above it. A heading is never left alone at the bottom of a page.

### Delegate types

`get` and `set` may be any delegate type with the right number of parameters: `Func<object>` and `Action<object>` work too. Mod List converts values to the parameter type (`bool`, `int`, `long`, `string`, enums and so on). Lambdas that capture variables are fine.

## What happens at run time

- **When `set` is called:** as soon as the player moves a picker, the same as the game's own settings. Mod List then calls every `get` on the page again. If your `set` clamps or rejects a value, or changes another option, the page shows what you actually kept.
- **Building the page never calls `set`.**
- **Threads:** everything is called on Unity's main thread, from the pause menu. Don't assume the game is paused while your page is open.
- **Errors:** exceptions from your methods and delegates are caught and logged to `Player.log`, tagged `[Mod List]` with your mod's name. A broken option shows an explanation on the page instead of a picker, and the rest of your page still works. Problems with an option's definition (missing keys, wrong delegate shape) are logged once, with the reason.
- **Long pages:** pages that don't fit on screen get Previous / Next page buttons.
- **Unknown types:** an option `type` that this version of Mod List doesn't know is shown as "needs a newer version of Mod List" rather than hidden. Unknown keys are ignored.

## Guidelines

- **Your mod owns its settings.** Mod List never stores values. `get` and `set` read and write wherever you keep them: a config file, `PlayerPrefs`, or a static field. Your mod works the same with or without Mod List, so:
  - **Save** in `set` or in `OnOptionsClosed()`. `OnOptionsClosed` is cheaper when a picker is stepped through many values. It's also called when the player closes the whole pause menu from your page.
  - **Use a stable, mod-specific path** for config files, e.g. `Path.Combine(Application.persistentDataPath, "MyMod.cfg")`, or `PlayerPrefs` keys with your mod's prefix.
  - **Rewrite config files completely** when saving, or update the lines in place, so a hand-edited file and the menu never disagree.
- **Apply changes live where you can.** If a setting only takes effect later ("from the next day" or "after a restart"), say so in the `description`.
- **Online games:** Mod List syncs nothing; each player sees and changes their own copy of your settings. For settings where the host's value is what counts:
  - **Lock them on clients:** `["enabled"] = (Func<bool>)(() => Kitchen.Session.NetworkedPlayState != Platforms.NetworkedPlayState.Client)`. Note that `Session` is in `Kitchen` but `NetworkedPlayState` is in `Platforms`.
  - **Say so:** add a description such as "Host's setting applies."
  - **Your mod's job:** if clients need the host's value, sending it is up to your mod's own networking.
- **Keep `GetOptions()` cheap and side-effect free.** It's called on every open. Build the dictionaries; don't load files or touch game state there.
- **Text:**
  - **Plain characters only:** the game's menu font lacks some characters, including `|`, `;` and bullets.
  - **Rich-text tags:** they aren't supported in labels, choice labels or descriptions.
- **Destructive settings:** for something like "prepare to uninstall", explain the consequence in the `description`. Consider a `button` that does it explicitly instead of a toggle.

## Checking it works

1. **Startup log:** start the game and look in `Player.log` (`%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\Player.log`) for:
   ```
   [Mod List] Found mod options in MyMod (MyNamespace.ModListOptions)
   ```
   It's written the first time the Mods screen opens.
2. **Mod list:** the Mods list itself is also logged once per session, with `+options` after each mod that has them.
3. **Definition problems** are logged as `[Mod List] MyMod: option 'id': <reason>`.

A Debug build of Mod List adds a sample section to Mod List's own options, covering every type and the error cases. Mod List's `ModListSettings.cs` is a complete, small real example.

## Compatibility promise

- **Version 1 stays stable:** later versions of Mod List only add to it (new optional keys, new option types).
- **Never renamed or removed:** the class name `ModListOptions`, the method names, the existing keys and the existing types.
- **Older Mod List:** a mod that uses a newer key or type with an older Mod List still works; the unknown parts are shown as unsupported or ignored.
