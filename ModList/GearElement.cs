using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Controllers;
using HarmonyLib;
using Kitchen.Modules;
using KitchenData;
using Shapes;
using TMPro;
using UnityEngine;

namespace ModList
{
    /// <summary>
    /// The settings gear on a mod's row in the Mods panel. A thin wrapper (no prefab) around a
    /// small vanilla <see cref="ButtonElement"/>, so it gets vanilla's focus highlight and mouse
    /// clicks, while left / right on it still turn the panel's page (a plain button would
    /// swallow those as navigation instead).
    /// </summary>
    public class GearElement : Element
    {
        private ButtonElement Button;
        private ModListElement Owner;

        internal ModEntry Entry { get; private set; }

        /// <summary>The row on the current page, for keeping focus near it across page turns.</summary>
        internal int Row { get; private set; }

        public override Bounds BoundingBox => Button != null
            ? new Bounds(transform.localPosition + Button.BoundingBox.center, Button.BoundingBox.size)
            : new Bounds(transform.localPosition, Vector3.zero);

        internal void Setup(ModListElement owner, ModEntry entry, int row, float size, ElementStyle style, Action on_activate)
        {
            Owner = owner;
            Entry = entry;
            Row = row;
            Button = Add<ButtonElement>();
            Button.SetSize(size, size);
            Button.SetStyle(style);
            Button.SetLabel("");
            Button.OnActivate += on_activate;
            if (!GearIcon.TryDraw(Button, size))
            {
                Button.SetLabel(GearIcon.FallbackLabel);
            }
        }

        public override void GainFocus()
        {
            base.GainFocus();
            Button?.GainFocus();
        }

        public override void LoseFocus()
        {
            base.LoseFocus();
            Button?.LoseFocus();
        }

        public override bool HandleInteraction(InputState state)
        {
            if (state.MenuLeft == ButtonState.Pressed)
            {
                return Owner != null && Owner.ChangePage(-1);
            }
            if (state.MenuRight == ButtonState.Pressed)
            {
                return Owner != null && Owner.ChangePage(1);
            }
            return Button != null && Button.HandleInteraction(state);
        }

        public override void Destroy()
        {
            // The button registered itself with the mouse UI; let it deregister.
            Button?.Destroy();
            base.Destroy();
        }
    }

    /// <summary>
    /// Draws a gear on a button with Shapes (see <see cref="ShapeKit"/>), so it doesn't depend on
    /// the font having a gear glyph.
    /// </summary>
    internal static class GearIcon
    {
        internal const string FallbackLabel = "*";
        private const int Teeth = 8;

        private static readonly FieldInfo BorderField = AccessTools.Field(typeof(ButtonElement), "BackingBorder");
        private static readonly FieldInfo LabelField = AccessTools.Field(typeof(ButtonElement), "Label");
        private static bool _warned;

        /// <summary>False (and a "*" label instead) if the gear couldn't be drawn.</summary>
        internal static bool TryDraw(ButtonElement button, float size)
        {
            try
            {
                if (Draw(button, size))
                {
                    return true;
                }
                Warn("the button's parts weren't found (game update?)");
            }
            catch (Exception e)
            {
                // Includes Shapes failing to load: Draw is kept out of line so that happens here.
                Warn($"{e.GetType().Name}: {e.Message}");
            }
            return false;
        }

        private static void Warn(string why)
        {
            if (!_warned)
            {
                _warned = true;
                ModMain.LogWarning($"Couldn't draw the settings gear ({why}); using a \"{FallbackLabel}\" label instead");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Draw(ButtonElement button, float size)
        {
            Rectangle border = BorderField?.GetValue(button) as Rectangle;
            TextMeshPro label = LabelField?.GetValue(button) as TextMeshPro;
            if (border == null || label == null)
            {
                return false;
            }
            ShapeLayer layer = ShapeKit.Above(border, label);
            // In the label's plane: the label already draws over the button's backing.
            Transform root = ShapeKit.Group(button.transform, "Gear", new Vector3(0f, 0f, ShapeKit.LocalZ(button.transform, label)), layer).transform;
            Color colour = label.color;

            // Outer radius r. Ring band from 0.34r to 0.78r; teeth from 0.6r to 1.0r.
            float r = size * 0.36f;
            Disc ring = ShapeKit.Add<Disc>(root, "Ring", Vector3.zero, 0f, layer, colour);
            ring.Type = DiscType.Ring;
            ring.Radius = r * 0.56f;      // to the middle of the band
            ring.Thickness = r * 0.44f;
            for (int i = 0; i < Teeth; i++)
            {
                float angle = i * 360f / Teeth;
                Vector3 at = Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, r * 0.8f, 0f);
                Rectangle tooth = ShapeKit.Add<Rectangle>(root, "Tooth", at, angle, layer, colour);
                tooth.Width = r * 0.3f;
                tooth.Height = r * 0.4f;
            }
            return true;
        }
    }
}
