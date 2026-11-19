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
    /// The "Mods" submenu: one paged panel, set up exactly like vanilla's RecipeMenu.
    /// Back / Escape returns to the pause menu through vanilla's own handling.
    /// </summary>
    public class ModListMenu : Menu<MenuAction>
    {
        public override bool RequiresBackingPanel { get; protected set; }

        public ModListMenu(Transform container, ModuleList module_list)
            : base(container, module_list)
        {
        }

        public override void Setup(int player_id)
        {
            ModListElement element = ModuleDirectory.Add<ModListElement>(Container, new Vector2(0f, 0f));
            element.SetPlayer(player_id);
            ModuleList.AddModule(element, element.transform.localPosition.ToFlat());
        }
    }

    /// <summary>
    /// A TextPanelElement (the Recipes card) listing mods as rows of "name ... version", with
    /// vanilla's left / right prompts to page through when they don't all fit.
    ///
    /// Rows per page and name truncation are measured with the panel's own TextMeshPro rather
    /// than hard-coded, so they follow the game's font and size.
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

        // Plain ASCII only in visible text: the game's font lacks some glyphs ("|", ";", bullets).
        private const string Ellipsis = "...";
        private const string WorkshopTag = " <color=#8C8C8C><size=75%>Workshop</size></color>";
        private const string FailedText = "<color=#E07070>failed to load</color>";
        private const string NoVersionText = "<color=#8C8C8C>no version</color>";

        private TextPanelElement Panel;
        private TextMeshPro Body;
        private InputPromptElement LeftPrompt;
        private InputPromptElement RightPrompt;
        private int Page;
        private int User;
        private bool Started;
        private int ModCount;
        private int BuiltTitlesVersion = -1;
        private List<string> Pages = new List<string>();

        public override Bounds BoundingBox => Panel != null ? Panel.BoundingBox : default;

        public void SetPlayer(int player_id)
        {
            User = player_id;
        }

        private void Start()
        {
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

        public override bool HandleInteraction(InputState state)
        {
            int before = Page;
            if (state.MenuRight == ButtonState.Pressed)
            {
                Page++;
            }
            if (state.MenuLeft == ButtonState.Pressed)
            {
                Page--;
            }
            Page = Mathf.Clamp(Page, 0, Math.Max(0, Pages.Count - 1));
            if (Page == before)
            {
                return false;
            }
            Show();
            return true;
        }

        private void Rebuild()
        {
            BuiltTitlesVersion = ModCatalog.TitlesVersion;
            try
            {
                List<ModEntry> entries = ModCatalog.Build();
                ModCount = entries.Count;
                Pages = Body != null ? Paginate(entries) : PlainPages(entries);
            }
            catch (Exception e)
            {
                ModMain.LogError($"Failed to build the mod list: {e}");
                ModCount = 0;
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
                Body.margin = Margin;
            }
            LeftPrompt.SetButtonForUser(Controls.MenuLeft, User);
            RightPrompt.SetButtonForUser(Controls.MenuRight, User);
            LeftPrompt.SetShown(Page > 0);
            RightPrompt.SetShown(Page < Pages.Count - 1);
        }

        private List<string> Paginate(List<ModEntry> entries)
        {
            List<string> pages = new List<string>();
            // Measure with no margins, against the space the margins leave.
            Vector4 savedMargin = Body.margin;
            Body.margin = Vector4.zero;
            try
            {
                float innerWidth = Width - Margin.x - Margin.z;
                float innerHeight = Height - Margin.y - Margin.w;
                float lineHeight = Body.GetPreferredValues("Ag", innerWidth, 0f).y;
                int rowsPerPage = lineHeight > 0f ? Math.Max(1, Mathf.FloorToInt(innerHeight / lineHeight)) : 8;

                StringBuilder page = new StringBuilder();
                int rows = 0;
                foreach (ModEntry entry in entries)
                {
                    if (rows == rowsPerPage)
                    {
                        pages.Add(page.ToString());
                        page.Clear();
                        rows = 0;
                    }
                    page.Append(Row(entry, innerWidth));
                    rows++;
                }
                if (rows > 0)
                {
                    pages.Add(page.ToString());
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
            string tag = entry.Workshop ? WorkshopTag : "";

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
        private static List<string> PlainPages(List<ModEntry> entries)
        {
            const int rowsPerPage = 8;
            List<string> pages = new List<string>();
            StringBuilder page = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                ModEntry e = entries[i];
                string version = e.Failed ? "failed to load" : e.Version ?? "no version";
                page.Append(e.Name).Append(e.Workshop ? " (Workshop)" : "").Append(" - ").Append(version).Append('\n');
                if ((i + 1) % rowsPerPage == 0 || i == entries.Count - 1)
                {
                    pages.Add(page.ToString());
                    page.Clear();
                }
            }
            return pages;
        }
    }
}
