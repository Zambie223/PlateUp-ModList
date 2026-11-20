using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Controllers;
using HarmonyLib;
using Kitchen;
using Kitchen.Modules;
using TMPro;
using UnityEngine;

namespace ModList
{
    /// <summary>
    /// The "Mods" submenu: the paged list panel (set up like vanilla's RecipeMenu). Every mod that
    /// declares options gets a settings gear at the right end of its row, which opens its options
    /// page; mods without options have no gear. Back / Escape returns to the pause menu through
    /// vanilla's own handling.
    /// </summary>
    public class ModListMenu : Menu<MenuAction>
    {
        private readonly ModOptionsMenu _optionsMenu;
        private ModListElement _element;
        // Where to put the player back when they return from a mod's options page.
        private int _returnPage;
        private OptionsProvider _returnFocus;

        public override bool RequiresBackingPanel { get; protected set; }

        internal ModListMenu(Transform container, ModuleList module_list, ModOptionsMenu options_menu)
            : base(container, module_list)
        {
            _optionsMenu = options_menu;
        }

        public override void Setup(int player_id)
        {
            _element = ModuleDirectory.Add<ModListElement>(Container, new Vector2(0f, 0f));
            _element.SetPlayer(player_id);
            // Registered before it's built: the gears it creates go into the same module list,
            // and it only touches that list while it's a member of it.
            ModuleList.AddModule(_element);
            _element.Build(ModuleList, Style, OpenOptions, _returnPage, _returnFocus);
            _returnPage = 0;
            _returnFocus = null;
        }

        private void OpenOptions(ModEntry entry)
        {
            _returnPage = _element != null ? _element.Page : 0;
            _returnFocus = entry.Options;
            _optionsMenu.Prepare(entry);
            RequestSubMenu(typeof(ModOptionsMenu));
        }
    }

    /// <summary>
    /// A TextPanelElement (the Recipes card) listing mods as rows of "name ... version", with
    /// vanilla's left / right prompts to page through when they don't all fit, and a
    /// <see cref="GearElement"/> on each row whose mod has options.
    ///
    /// Rows per page and name truncation are measured with the panel's own TextMeshPro rather
    /// than hard-coded, so they follow the game's font and size. The gears are placed on the
    /// rows TextMeshPro actually laid out.
    ///
    /// Focus: while the page shows gears, the gears are what up / down moves between (and left /
    /// right on a gear still turns the page); the panel itself only takes focus on a page with
    /// no gears, so paging always works.
    /// </summary>
    public class ModListElement : Element
    {
        private static readonly FieldInfo BodyField = AccessTools.Field(typeof(TextPanelElement), "Text");

        private const float Width = 5f;
        private const float Height = 3.4f;
        // left, top, right, bottom. The bottom keeps the rows clear of the page prompts, which sit
        // on the panel's bottom corners.
        private static readonly Vector4 Margin = new Vector4(0.2f, 0.08f, 0.2f, 0.4f);
        private const float ColumnGap = 0.3f;
        // Extra right margin for the gear column, only when some mod has options.
        private const float GearColumn = 0.4f;
        private const float MinGearSize = 0.2f;
        private const float MaxGearSize = 0.34f;
        // In front of the panel (negative z is towards the camera, as with the panel's title).
        private const float GearZ = -0.1f;
        // Navigation positions for the gears. Vanilla's up / down picks the nearest selectable
        // module by these, ignoring anything closer than 0.25, so they're spaced a full unit
        // apart rather than following the (tighter) row spacing on screen.
        private const float GearNavSpacing = 1f;
        private const int FallbackRowsPerPage = 8;

        // Plain ASCII only in visible text: the game's font lacks some glyphs ("|", ";", bullets).
        private const string Ellipsis = "...";
        private const string WorkshopTag = " <color=#8C8C8C><size=75%>Workshop</size></color>";
        private const string FailedText = "<color=#E07070>failed to load</color>";
        private const string NoVersionText = "<color=#8C8C8C>no version</color>";

        private TextPanelElement Panel;
        private TextMeshPro Body;
        private InputPromptElement LeftPrompt;
        private InputPromptElement RightPrompt;
        private int User;
        private bool Started;
        private int ModCount;
        private int BuiltTitlesVersion = -1;
        private float LineHeight;
        private Vector4 EffectiveMargin = Margin;
        private List<string> Pages = new List<string>();
        private List<List<ModEntry>> PageEntries = new List<List<ModEntry>>();
        private HashSet<ModEntry> WithOptions = new HashSet<ModEntry>();

        private ModuleList Modules;
        private ElementStyle ButtonStyle;
        private Action<ModEntry> OpenOptions;
        private readonly List<GearElement> Gears = new List<GearElement>();
        private OptionsProvider PendingFocus;

        /// <summary>The page being shown, so the menu can come back to it.</summary>
        internal int Page { get; private set; }

        // In the container's coordinates, like vanilla elements.
        public override Bounds BoundingBox => Panel != null
            ? new Bounds(transform.localPosition + Panel.BoundingBox.center, Panel.BoundingBox.size)
            : default;

        public void SetPlayer(int player_id)
        {
            User = player_id;
        }

        /// <summary>
        /// Creates the panel and fills it. Called once, by ModListMenu.Setup, after this element
        /// is in <paramref name="modules"/>.
        /// </summary>
        internal void Build(ModuleList modules, ElementStyle style, Action<ModEntry> open_options, int start_page, OptionsProvider focus)
        {
            if (Started)
            {
                return;
            }
            Modules = modules;
            ButtonStyle = style;
            OpenOptions = open_options;
            Page = Math.Max(0, start_page);
            PendingFocus = focus;

            Panel = Add<TextPanelElement>();
            // Size the panel before attaching the prompts: Attach places them at the panel's
            // bottom corners as they are at that moment.
            Panel.SetStyle(TextPanelElement.Style.Recipe);
            Panel.Resize(Width, Height);
            (LeftPrompt = Add<InputPromptElement>()).Attach(Panel);
            (RightPrompt = Add<InputPromptElement>()).Attach(Panel, left_attach: false);

            Body = BodyField?.GetValue(Panel) as TextMeshPro;
            if (Body != null)
            {
                Body.richText = true;
                Body.enableAutoSizing = false;
                // Rows are truncated to fit, so wrapping would only break the row count.
                Body.enableWordWrapping = false;
                // Not Ellipsis: with it, the game's body font draws nothing when a line doesn't
                // fit the box vertically.
                Body.overflowMode = TextOverflowModes.Overflow;
                Body.alignment = TextAlignmentOptions.TopLeft;
            }
            else
            {
                ModMain.LogWarning("TextPanelElement.Text not found (game update?); the list will be shown unformatted");
            }

            ModCatalog.EnsureWorkshopTitles();
            Started = true;
            Rebuild();
        }

        private void Update()
        {
            // Workshop titles arrive from Steam a moment after the panel opens.
            if (Started && BuiltTitlesVersion != ModCatalog.TitlesVersion)
            {
                Rebuild();
            }
        }

        public override void Destroy()
        {
            // The menu is closing (vanilla clears the module list, gears included); stop the
            // title poll from rebuilding into whatever menu comes next.
            Started = false;
            base.Destroy();
        }

        public override bool HandleInteraction(InputState state)
        {
            if (state.MenuRight == ButtonState.Pressed)
            {
                return ChangePage(1);
            }
            if (state.MenuLeft == ButtonState.Pressed)
            {
                return ChangePage(-1);
            }
            return false;
        }

        /// <summary>Turns the page; also used by the gears. False when there's no page that way.</summary>
        internal bool ChangePage(int delta)
        {
            int target = Mathf.Clamp(Page + delta, 0, Math.Max(0, Pages.Count - 1));
            if (target == Page)
            {
                return false;
            }
            Page = target;
            Show();
            return true;
        }

        private void Rebuild()
        {
            BuiltTitlesVersion = ModCatalog.TitlesVersion;
            // Names can change (and re-sort) when Workshop titles arrive: keep focus on the same mod.
            if (PendingFocus == null)
            {
                PendingFocus = FocusedGear()?.Entry.Options;
            }
            try
            {
                List<ModEntry> entries = ModCatalog.Build();
                WithOptions = new HashSet<ModEntry>();
                foreach (ModEntry entry in entries)
                {
                    if (entry.Options != null && entry.Options.HasOptions(entry.Name))
                    {
                        WithOptions.Add(entry);
                    }
                }
                EffectiveMargin = WithOptions.Count > 0 ? Margin + new Vector4(0f, 0f, GearColumn, 0f) : Margin;
                ModCount = entries.Count;
                PageEntries = new List<List<ModEntry>>();
                Pages = Body != null ? Paginate(entries) : PlainPages(entries);
            }
            catch (Exception e)
            {
                ModMain.LogError($"Failed to build the mod list: {e}");
                ModCount = 0;
                WithOptions = new HashSet<ModEntry>();
                PageEntries = new List<List<ModEntry>>();
                Pages = new List<string> { "Couldn't read the mod list. See Player.log." };
            }
            Show();
        }

        private void Show()
        {
            if (!Started)
            {
                return;
            }
            if (Pages.Count == 0)
            {
                Pages.Add("No mods are loaded.");
            }
            Page = Mathf.Clamp(Page, 0, Pages.Count - 1);
            string title = $"Installed mods ({ModCount})";
            if (Pages.Count > 1)
            {
                title += $" - page {Page + 1} of {Pages.Count}";
            }
            Panel.SetText(title, Pages[Page]);
            if (Body != null)
            {
                Body.margin = EffectiveMargin;
            }
            LeftPrompt.SetButtonForUser(Controls.MenuLeft, User);
            RightPrompt.SetButtonForUser(Controls.MenuRight, User);
            LeftPrompt.SetShown(Page > 0);
            RightPrompt.SetShown(Page < Pages.Count - 1);
            try
            {
                LayoutGears();
            }
            catch (Exception e)
            {
                ModMain.LogError($"Failed to place the settings gears: {e}");
            }
        }

        // ---- Gears -------------------------------------------------------------------------

        private GearElement FocusedGear()
        {
            return Modules?.Selected?.Module is GearElement gear && Gears.Contains(gear) ? gear : null;
        }

        /// <summary>
        /// Replaces the gears with ones for the rows on the current page, then puts focus back:
        /// on the same mod if it's still shown, else the nearest row, else the panel.
        /// </summary>
        private void LayoutGears()
        {
            // Only while this element is in the live module list; after the menu closes, the list
            // belongs to another menu.
            if (Modules == null || !Modules.Modules.Exists(m => m.Module == (IModule)this))
            {
                return;
            }
            GearElement focused = FocusedGear();
            OptionsProvider want = PendingFocus ?? focused?.Entry.Options;
            int wantRow = focused != null ? focused.Row : 0;
            PendingFocus = null;

            RemoveGears();

            List<ModEntry> rows = Page < PageEntries.Count ? PageEntries[Page] : null;
            if (rows != null && rows.Count > 0 && OpenOptions != null)
            {
                Vector3[] centres = RowCentres(rows.Count, out float pitch);
                float size = Mathf.Clamp(pitch * 0.9f, MinGearSize, MaxGearSize);
                for (int r = 0; r < rows.Count; r++)
                {
                    ModEntry entry = rows[r];
                    if (!WithOptions.Contains(entry))
                    {
                        continue;
                    }
                    GearElement gear = ModuleDirectory.Add<GearElement>(transform.parent);
                    gear.Setup(this, entry, r, size, ButtonStyle, () => OpenOptions(entry));
                    gear.transform.localPosition = new Vector3(centres[r].x, centres[r].y, GearZ);
                    Modules.AddModule(gear, new Vector2(0f, -GearNavSpacing * (Gears.Count + 1)));
                    Gears.Add(gear);
                }
            }

            // The panel only takes focus when there's no gear to hold it (it handles paging then).
            SetSelectable(Gears.Count == 0);
            if (Gears.Count == 0)
            {
                Modules.Select(this);
                return;
            }
            GearElement target = want != null ? Gears.Find(g => g.Entry.Options == want) : null;
            if (target == null)
            {
                target = Gears[0];
                foreach (GearElement gear in Gears)
                {
                    if (Math.Abs(gear.Row - wantRow) < Math.Abs(target.Row - wantRow))
                    {
                        target = gear;
                    }
                }
            }
            Modules.Select(target);
        }

        private void RemoveGears()
        {
            if (Gears.Count == 0)
            {
                return;
            }
            Modules.Modules.RemoveAll(m => m.Module is GearElement g && Gears.Contains(g));
            foreach (GearElement gear in Gears)
            {
                gear.Destroy();
            }
            Gears.Clear();
        }

        /// <summary>
        /// The centre of each row's gear slot, in the container's coordinates (the gears' parent),
        /// and the row spacing. Uses the lines TextMeshPro laid out; each row is two lines (name,
        /// then version drawn on the same baseline), so row r is line 2r.
        /// </summary>
        private Vector3[] RowCentres(int count, out float pitch)
        {
            Transform container = transform.parent;
            Vector3[] centres = new Vector3[count];
            pitch = LineHeight > 0f ? LineHeight : (Height - Margin.y - Margin.w) / FallbackRowsPerPage;

            if (Body != null)
            {
                Body.ForceMeshUpdate(ignoreActiveState: true);
                TMP_TextInfo info = Body.textInfo;
                Rect rect = Body.rectTransform.rect;
                float x = rect.xMax - Margin.z - GearColumn / 2f;
                bool measured = info != null && info.lineInfo != null && info.lineCount > 2 * (count - 1);
                if (measured && count > 1)
                {
                    pitch = Mathf.Abs(LineCentre(info, 0) - LineCentre(info, 2));
                }
                for (int r = 0; r < count; r++)
                {
                    float y = measured ? LineCentre(info, 2 * r) : rect.yMax - Margin.y - (r + 0.5f) * pitch;
                    centres[r] = container.InverseTransformPoint(Body.transform.TransformPoint(new Vector3(x, y, 0f)));
                }
                return centres;
            }

            // Unformatted fallback: estimate from the panel's size.
            for (int r = 0; r < count; r++)
            {
                Vector3 local = new Vector3(Width / 2f - Margin.z - GearColumn / 2f, Height / 2f - Margin.y - (r + 0.5f) * pitch, 0f);
                centres[r] = container.InverseTransformPoint(Panel.transform.TransformPoint(local));
            }
            return centres;
        }

        private static float LineCentre(TMP_TextInfo info, int line)
        {
            TMP_LineInfo l = info.lineInfo[line];
            return (l.ascender + l.descender) / 2f;
        }

        // ---- Text --------------------------------------------------------------------------

        private List<string> Paginate(List<ModEntry> entries)
        {
            List<string> pages = new List<string>();
            // Measure with no margins, against the space the margins leave.
            Vector4 savedMargin = Body.margin;
            Body.margin = Vector4.zero;
            try
            {
                float innerWidth = Width - EffectiveMargin.x - EffectiveMargin.z;
                float innerHeight = Height - EffectiveMargin.y - EffectiveMargin.w;
                LineHeight = Body.GetPreferredValues("Ag", innerWidth, 0f).y;
                int rowsPerPage = LineHeight > 0f ? Math.Max(1, Mathf.FloorToInt(innerHeight / LineHeight)) : FallbackRowsPerPage;

                StringBuilder page = new StringBuilder();
                List<ModEntry> pageEntries = new List<ModEntry>();
                foreach (ModEntry entry in entries)
                {
                    if (pageEntries.Count == rowsPerPage)
                    {
                        pages.Add(page.ToString());
                        PageEntries.Add(pageEntries);
                        page.Clear();
                        pageEntries = new List<ModEntry>();
                    }
                    page.Append(Row(entry, innerWidth));
                    pageEntries.Add(entry);
                }
                if (pageEntries.Count > 0)
                {
                    pages.Add(page.ToString());
                    PageEntries.Add(pageEntries);
                }
            }
            finally
            {
                Body.margin = savedMargin;
            }
            return pages;
        }

        /// <summary>
        /// One row: the name left-aligned and the version right-aligned on the same line. The
        /// zero line height after the name keeps the version on the name's line; the closing tag
        /// restores the normal line height before the next row.
        /// </summary>
        private string Row(ModEntry entry, float innerWidth)
        {
            string version = entry.Failed ? FailedText
                : entry.Version == null ? NoVersionText
                : Escape(char.IsDigit(entry.Version[0]) ? "v" + entry.Version : entry.Version);
            string tag = entry.Workshop && ModListSettings.ShowWorkshopTag ? WorkshopTag : "";

            float versionWidth = Measure(version);
            float tagWidth = tag.Length > 0 ? Measure(tag) : 0f;
            float nameRoom = innerWidth - versionWidth - tagWidth - ColumnGap;
            string name = Truncate(entry.Name, nameRoom);

            return $"<align=left>{Escape(name)}{tag}<line-height=0>\n<align=right>{version}</line-height>\n";
        }

        private string Truncate(string name, float room)
        {
            if (Measure(Escape(name)) <= room)
            {
                return name;
            }
            for (int length = name.Length - 1; length > 0; length--)
            {
                string candidate = name.Substring(0, length).TrimEnd() + Ellipsis;
                if (Measure(Escape(candidate)) <= room)
                {
                    return candidate;
                }
            }
            return Ellipsis;
        }

        private float Measure(string richText)
        {
            return Body.GetPreferredValues(richText).x;
        }

        /// <summary>Shows mod-supplied text literally, so a "&lt;" in a name can't start a tag.</summary>
        private static string Escape(string text)
        {
            return "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";
        }

        /// <summary>Fallback if the panel's text component can't be found: no measuring or tags.</summary>
        private List<string> PlainPages(List<ModEntry> entries)
        {
            List<string> pages = new List<string>();
            StringBuilder page = new StringBuilder();
            List<ModEntry> pageEntries = new List<ModEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                ModEntry e = entries[i];
                string version = e.Failed ? "failed to load" : e.Version ?? "no version";
                page.Append(e.Name).Append(e.Workshop && ModListSettings.ShowWorkshopTag ? " (Workshop)" : "").Append(" - ").Append(version).Append('\n');
                pageEntries.Add(e);
                if ((i + 1) % FallbackRowsPerPage == 0 || i == entries.Count - 1)
                {
                    pages.Add(page.ToString());
                    PageEntries.Add(pageEntries);
                    page.Clear();
                    pageEntries = new List<ModEntry>();
                }
            }
            return pages;
        }
    }
}
