using System;
using System.Collections.Generic;
using Kitchen;
using Kitchen.Modules;
using UnityEngine;

namespace ModList
{
    /// <summary>
    /// One mod's options page, laid out like vanilla's settings pages (AccessibilityMenu): for
    /// each option a label, an optional description box, then a left / right picker.
    ///
    /// Changes are passed to the mod the moment a picker moves (vanilla's settings work the same
    /// way), then every control is re-read, so the page always shows what the mod actually
    /// accepted (it may clamp a value, or one option may change another). When the page closes,
    /// the mod's optional OnOptionsClosed is called (a good moment to write a config file).
    ///
    /// Long pages are split, with Previous / Next page buttons.
    /// </summary>
    public class ModOptionsMenu : Menu<MenuAction>
    {
        // Rough heights in menu units (elements are 0.5 tall plus 0.1 padding), and the space a
        // page may use: about what vanilla's longest settings pages take.
        private const float PageBudget = 7.2f;
        private const float RowHeight = 0.6f;
        private const float DescriptionHeight = 0.9f;

        private ModEntry _entry;
        private int _page;
        private bool _changingPage;
        private bool _updating;
        // Every option the mod returned, and which of them were visible when the page was built:
        // when that changes, the page is rebuilt so hidden options take no space.
        private List<OptionDef> _all = new List<OptionDef>();
        private string _shownSignature = "";
        // The option to put focus back on after a rebuild.
        private string _focusKey;

        private sealed class Bound
        {
            public OptionDef Def;
            public LabelElement Label;
            public SelectElement Select;
            public IntPickerElement Picker;
            public ButtonElement Button;
            public bool Enabled;
        }

        private readonly List<Bound> _bound = new List<Bound>();

        public ModOptionsMenu(Transform container, ModuleList module_list)
            : base(container, module_list)
        {
        }

        /// <summary>Chooses the mod to show; call before requesting this menu.</summary>
        internal void Prepare(ModEntry entry)
        {
            _entry = entry;
            _page = 0;
            _focusKey = null;
        }

        private string ModName => _entry?.Name ?? "?";

        public override void Setup(int player_id)
        {
            _bound.Clear();
            _changingPage = false;
            _updating = false;

            AddLabel($"{ModName} options");

            List<OptionDef> defs = _entry?.Options?.Read(ModName, out string error) is List<OptionDef> read ? read : null;
            if (defs == null)
            {
                AddInfo("This mod's options couldn't be loaded. See Player.log.");
                AddBackButton();
                return;
            }
            _all = defs;
            _shownSignature = Signature();
            List<OptionDef> shown = Shown(defs);
            string focusKey = _focusKey;
            _focusKey = null;
            if (shown.Count == 0)
            {
                AddInfo("This mod has no options right now.");
                AddBackButton();
                return;
            }

            List<List<OptionDef>> pages = Paginate(shown);
            if (focusKey != null)
            {
                int focusPage = pages.FindIndex(page => page.Exists(d => Key(d) == focusKey));
                if (focusPage >= 0)
                {
                    _page = focusPage;
                }
            }
            _page = Mathf.Clamp(_page, 0, pages.Count - 1);
            New<SpacerElement>();
            bool first = true;
            foreach (OptionDef def in pages[_page])
            {
                try
                {
                    AddOption(def, first);
                }
                catch (Exception e)
                {
                    // One broken option mustn't take the whole page down.
                    ModMain.LogError($"{ModName}: couldn't show option '{def.Id ?? def.Label}': {e}");
                }
                first = false;
            }

            New<SpacerElement>();
            if (pages.Count > 1)
            {
                if (_page > 0)
                {
                    AddButton($"Previous page ({_page} of {pages.Count})", (int i) => ChangePage(-1));
                }
                if (_page < pages.Count - 1)
                {
                    AddButton($"Next page ({_page + 2} of {pages.Count})", (int i) => ChangePage(1));
                }
            }
            AddBackButton();

            if (focusKey != null)
            {
                Bound focus = _bound.Find(b => Key(b.Def) == focusKey);
                IModule target = focus == null ? null : (IModule)focus.Picker ?? (IModule)focus.Select ?? focus.Button;
                if (target != null)
                {
                    ModuleList.Select(target);
                }
            }
        }

        /// <summary>
        /// The options to lay out: the visible ones, minus headings with nothing visible under
        /// them (before the next heading).
        /// </summary>
        private List<OptionDef> Shown(List<OptionDef> defs)
        {
            List<OptionDef> visible = defs.FindAll(d => d.IsVisible(ModName));
            List<OptionDef> shown = new List<OptionDef>();
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].Kind == OptionKind.Heading)
                {
                    int next = i + 1;
                    if (next >= visible.Count || visible[next].Kind == OptionKind.Heading)
                    {
                        continue;
                    }
                }
                shown.Add(visible[i]);
            }
            return shown;
        }

        private string Signature()
        {
            char[] flags = new char[_all.Count];
            for (int i = 0; i < _all.Count; i++)
            {
                flags[i] = _all[i].IsVisible(ModName) ? '1' : '0';
            }
            return new string(flags);
        }

        private static string Key(OptionDef def) => def?.Id ?? def?.Label;

        public override void TearDown()
        {
            // Page changes rebuild this same menu; only a real close counts.
            if (!_changingPage && _entry?.Options != null)
            {
                _entry.Options.NotifyClosed(ModName);
            }
            _bound.Clear();
        }

        private void AddBackButton()
        {
            AddButton(Localisation["MENU_BACK_SETTINGS"], (int i) => RequestPreviousMenu());
        }

        private void ChangePage(int delta)
        {
            _page += delta;
            Rebuild();
        }

        /// <summary>Builds this page again (not a close, so OnOptionsClosed isn't called).</summary>
        private void Rebuild()
        {
            _changingPage = true;
            RequestSubMenu(typeof(ModOptionsMenu), skip_stack: false, remove_self_from_stack: true);
        }

        private static float Cost(OptionDef def)
        {
            float description = string.IsNullOrWhiteSpace(def.Description) ? 0f : DescriptionHeight;
            switch (def.Kind)
            {
                case OptionKind.Heading:
                    return RowHeight * 2f; // spacer + label
                case OptionKind.Button:
                    return RowHeight + description;
                case OptionKind.Unsupported:
                    return RowHeight + DescriptionHeight;
                default:
                    return RowHeight * 2f + (def.Error != null ? DescriptionHeight : description);
            }
        }

        private static List<List<OptionDef>> Paginate(List<OptionDef> defs)
        {
            List<List<OptionDef>> pages = new List<List<OptionDef>>();
            List<OptionDef> page = new List<OptionDef>();
            float used = 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                OptionDef def = defs[i];
                float cost = Cost(def);
                // A heading at the bottom of a page would be cut off from its options.
                if (def.Kind == OptionKind.Heading && i + 1 < defs.Count)
                {
                    cost += Cost(defs[i + 1]);
                }
                if (page.Count > 0 && used + cost > PageBudget)
                {
                    pages.Add(page);
                    page = new List<OptionDef>();
                    used = 0f;
                }
                page.Add(def);
                used += Cost(def);
            }
            if (page.Count > 0)
            {
                pages.Add(page);
            }
            return pages;
        }

        private void AddOption(OptionDef def, bool firstOnPage)
        {
            switch (def.Kind)
            {
                case OptionKind.Heading:
                    if (!firstOnPage)
                    {
                        New<SpacerElement>();
                    }
                    AddLabel(def.Label.ToUpperInvariant());
                    return;

                case OptionKind.Unsupported:
                    AddLabel(def.Label ?? def.Id ?? "?");
                    AddInfo(def.Error != null
                        ? "Can't show this option: " + def.Error + "."
                        : $"This option needs a newer version of Mod List (type \"{def.RawType}\").");
                    return;

                case OptionKind.Button:
                {
                    if (def.Error != null)
                    {
                        AddLabel(def.Label ?? "?");
                        AddInfo("Can't show this option: " + def.Error + ".");
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(def.Description))
                    {
                        AddInfo(def.Description);
                    }
                    Bound bound = new Bound { Def = def };
                    bound.Button = AddButton(def.Label, (int i) => Press(bound));
                    ApplyEnabled(bound, def.IsEnabled(ModName));
                    _bound.Add(bound);
                    return;
                }

                default:
                {
                    Bound bound = new Bound { Def = def };
                    bound.Label = AddLabel(def.Label);
                    if (def.Error != null)
                    {
                        AddInfo("Can't show this option: " + def.Error + ".");
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(def.Description))
                    {
                        AddInfo(def.Description);
                    }
                    int index;
                    try
                    {
                        index = def.ReadIndex();
                    }
                    catch (Exception e)
                    {
                        ModMain.LogError($"{ModName}: reading option '{def.Id ?? def.Label}' failed: {Invoke.Unwrap(e)}");
                        AddInfo("Couldn't read this option's value. See Player.log.");
                        return;
                    }
                    if (def.Kind == OptionKind.Int)
                    {
                        // Shows one value at a time, with hold-to-repeat, and "slider" / "input".
                        // Setup doesn't report its starting value, so building the page never
                        // calls the mod's setter.
                        IntPickerElement picker = ModuleDirectory.Add<IntPickerElement>(Container);
                        picker.Setup(def, index, Style, DefaultElementSize, (int i) => Picked(bound, i));
                        ModuleList.AddModule(picker);
                        bound.Picker = picker;
                    }
                    else
                    {
                        // AddSelect sets the starting value before subscribing, so building the
                        // page never calls the mod's setter.
                        bound.Select = AddSelect(def.PickerLabels(), (int i) => Picked(bound, i), index);
                    }
                    ApplyEnabled(bound, def.IsEnabled(ModName));
                    _bound.Add(bound);
                    return;
                }
            }
        }

        private void Picked(Bound bound, int index)
        {
            if (_updating)
            {
                return;
            }
            try
            {
                bound.Def.WriteIndex(index);
            }
            catch (Exception e)
            {
                ModMain.LogError($"{ModName}: setting option '{bound.Def.Id ?? bound.Def.Label}' failed: {Invoke.Unwrap(e)}");
            }
            Refresh(bound);
        }

        private void Press(Bound bound)
        {
            if (!bound.Enabled)
            {
                return;
            }
            try
            {
                Invoke.Call(bound.Def.Action);
            }
            catch (Exception e)
            {
                ModMain.LogError($"{ModName}: button '{bound.Def.Id ?? bound.Def.Label}' failed: {Invoke.Unwrap(e)}");
            }
            Refresh(bound);
        }

        /// <summary>
        /// Re-reads every control's value and enabled state from the mod after
        /// <paramref name="changed"/> was used. If an option was shown or hidden, rebuilds the
        /// page instead, keeping focus on <paramref name="changed"/>.
        /// </summary>
        private void Refresh(Bound changed)
        {
            if (Signature() != _shownSignature)
            {
                _focusKey = Key(changed?.Def);
                Rebuild();
                return;
            }
            foreach (Bound bound in _bound)
            {
                ApplyEnabled(bound, bound.Def.IsEnabled(ModName));
                if (bound.Select == null && bound.Picker == null)
                {
                    continue;
                }
                try
                {
                    int index = bound.Def.ReadIndex();
                    if (bound.Picker != null)
                    {
                        // Doesn't report the change, so no guard is needed.
                        bound.Picker.SetIndex(index, notify: false);
                    }
                    else if (bound.Select.Value != index)
                    {
                        // Assigning Value raises the picker's change event; ignore our own.
                        _updating = true;
                        bound.Select.Value = index;
                    }
                }
                catch (Exception e)
                {
                    ModMain.LogError($"{ModName}: reading option '{bound.Def.Id ?? bound.Def.Label}' failed: {Invoke.Unwrap(e)}");
                }
                finally
                {
                    _updating = false;
                }
            }
        }

        private static void ApplyEnabled(Bound bound, bool enabled)
        {
            bound.Enabled = enabled;
            bound.Select?.SetSelectable(enabled);
            bound.Picker?.SetSelectable(enabled);
            // ButtonElement dims its own label when unselectable; pickers don't, so say it.
            bound.Button?.SetSelectable(enabled);
            bound.Label?.SetLabel(enabled ? bound.Def.Label : bound.Def.Label + " (locked)");
        }
    }
}
