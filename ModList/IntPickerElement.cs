using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Controllers;
using HarmonyLib;
using Kitchen;
using Kitchen.Modules;
using KitchenData;
using Shapes;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModList
{
    /// <summary>
    /// The picker for an "int" option. A wrapper (no prefab) around a vanilla
    /// <see cref="SelectElement"/>, which it uses only for looks (backing, label, arrows, focus
    /// highlight); the value lives here and the label shows one value at a time, so a range
    /// doesn't need a label per value.
    ///
    /// On top of vanilla's left / right:
    ///   - holding left / right repeats, speeding up with the size of the range (every int);
    ///   - "slider": a bar along the bottom of the picker that the mouse can click or drag (the
    ///     ends, where the arrows are, still step by one);
    ///   - "input": select the picker, or click the number, to type a value, through the game's
    ///     own text entry (Steam's on-screen keyboard in Big Picture / on Steam Deck). Typed values
    ///     are clamped to min / max and snapped to the nearest step.
    /// The mouse is handled here for the whole picker; the inner SelectElement is taken off the
    /// mouse UI so a click isn't handled twice.
    /// </summary>
    public class IntPickerElement : MouseElement
    {
        private static readonly FieldInfo LeftPromptField = AccessTools.Field(typeof(SelectElement), "LeftPrompt");
        private static readonly FieldInfo RightPromptField = AccessTools.Field(typeof(SelectElement), "RightPrompt");
        private static readonly FieldInfo AnimatorField = AccessTools.Field(typeof(SelectElement), "Animator");
        private static readonly FieldInfo BackingField = AccessTools.Field(typeof(SelectElement), "Backing");
        private static readonly FieldInfo LabelField = AccessTools.Field(typeof(SelectElement), "Label");

        // Hold-to-repeat, in seconds. The step grows with the range, so crossing any range by
        // holding takes a few seconds.
        private const float RepeatDelay = 0.4f;
        private const float RepeatInterval = 0.05f;
        private const float FastAfter = 1.5f;
        private const float FasterAfter = 3f;
        // HandleInteraction is only called while this has focus; a hold that stops being
        // reported (focus moved, menu closed) is dropped after this long.
        private const float HoldTimeout = 0.25f;

        // Slider layout, in menu units. The ends (where vanilla's arrows are) step instead of
        // sliding; with "input", a click on the middle (the number) types instead of sliding.
        private const float ArrowZone = 0.4f;
        private const float TypeZone = 0.45f;
        private const float DragThreshold = 0.08f;
        private const float BarInset = 0.07f;
        private const float BarThickness = 0.04f;
        private const float KnobRadius = 0.055f;
        private const float LockedAlpha = 0.3f;
        private static readonly Color TrackColour = new Color(0.35f, 0.36f, 0.41f);

        private const int MaxTypedLength = 16;
        private static bool _typingWarned;
        private static bool _sliderWarned;

        private SelectElement Picker;
        private OptionDef Def;
        private Action<int> Changed;
        private float Width;
        private float Height;
        private bool HasSlider;
        private bool Typing;
        private GameObject LeftPrompt;
        private GameObject RightPrompt;
        private Animator PickerAnimator;

        // Slider shapes (null without a slider).
        private Rectangle Track;
        private Rectangle Fill;
        private Disc Knob;
        private Color FillColour;
        private Color KnobColour;
        private float TrackHalf;

        private int HoldDirection;
        private float HoldStart;
        private float NextRepeat;
        private float LastHeld;

        private bool PressedHere;
        private bool Dragging;
        private bool Dragged;
        private float PressX;
        private float ReleasedAt = -1f;

        /// <summary>The picker index (0 = min).</summary>
        internal int Index { get; private set; }

        public override Bounds BoundingBox => new Bounds(transform.localPosition, new Vector3(Width, Height, 0f));

        internal void Setup(OptionDef def, int index, ElementStyle style, Vector2 size, Action<int> changed)
        {
            Def = def;
            Changed = changed;
            Width = size.x;
            Height = size.y;
            Typing = def.Input;

            Picker = Add<SelectElement>();
            // This element takes the mouse for the whole picker.
            if (MouseUI.Main != null)
            {
                MouseUI.Main.Deregister(Picker);
            }
            Picker.SetSize(Width, Height);
            // One placeholder entry: the label and arrows are driven from here.
            Picker.SetOptions(new List<string> { "" });
            Picker.SetStyle(style);
            LeftPrompt = LeftPromptField?.GetValue(Picker) as GameObject;
            RightPrompt = RightPromptField?.GetValue(Picker) as GameObject;
            PickerAnimator = AnimatorField?.GetValue(Picker) as Animator;

            TrackHalf = Math.Max(0.1f, Width / 2f - ArrowZone);
            HasSlider = def.Slider && TryBuildSlider();

            Index = -1;
            SetIndex(index, notify: false);
        }

        /// <summary>Shows <paramref name="index"/>; with <paramref name="notify"/>, reports a change.</summary>
        internal void SetIndex(int index, bool notify)
        {
            if (Def == null)
            {
                return;
            }
            int count = Math.Max(1, Def.Count);
            index = Mathf.Clamp(index, 0, count - 1);
            bool changed = index != Index;
            Index = index;
            Picker.SetLabel(Def.LabelAt(index));
            LeftPrompt?.SetActive(index > 0);
            RightPrompt?.SetActive(index < count - 1);
            UpdateSlider();
            if (notify && changed)
            {
                Changed?.Invoke(index);
            }
        }

        private void Step(int delta, bool animate)
        {
            if (!IsSelectable || delta == 0)
            {
                return;
            }
            int target = Mathf.Clamp(Index + delta, 0, Math.Max(0, Def.Count - 1));
            if (target == Index)
            {
                return;
            }
            if (animate && PickerAnimator != null)
            {
                PickerAnimator.Play(delta < 0 ? "Left Highlight" : "Right Highlight");
            }
            SetIndex(target, notify: true);
        }

        // ---- Keyboard / controller -----------------------------------------------------------

        public override bool HandleInteraction(InputState state)
        {
            float now = Time.unscaledTime;
            int pressed = state.MenuLeft == ButtonState.Pressed ? -1 : state.MenuRight == ButtonState.Pressed ? 1 : 0;
            if (pressed != 0)
            {
                Step(pressed, animate: true);
                HoldDirection = pressed;
                HoldStart = now;
                NextRepeat = now + RepeatDelay;
                LastHeld = now;
                return true;
            }
            int held = state.MenuLeft == ButtonState.Held ? -1 : state.MenuRight == ButtonState.Held ? 1 : 0;
            if (held != 0 && held == HoldDirection)
            {
                LastHeld = now;
                if (now >= NextRepeat)
                {
                    Step(held * RepeatStep(now - HoldStart), animate: false);
                    NextRepeat = now + RepeatInterval;
                }
                return true;
            }
            HoldDirection = 0;
            if (state.MenuSelect == ButtonState.Pressed && Typing)
            {
                OpenTyping();
                return true;
            }
            return false;
        }

        private int RepeatStep(float held_for)
        {
            int count = Def.Count;
            if (held_for < FastAfter)
            {
                return 1;
            }
            return Math.Max(1, held_for < FasterAfter ? count / 200 : count / 40);
        }

        // ---- Mouse ---------------------------------------------------------------------------

        public override void OnMouseUIDown()
        {
            base.OnMouseUIDown();
            if (!IsSelectable || !gameObject.activeInHierarchy || !TryMouseX(out float x))
            {
                return;
            }
            PressedHere = true;
            Dragging = false;
            Dragged = false;
            PressX = x;
            ReleasedAt = -1f;
            // On the bar (but not on the number, when it can be typed): slide straight away.
            if (HasSlider && Math.Abs(x) <= TrackHalf && !(Typing && Math.Abs(x) < TypeZone))
            {
                Dragging = true;
                Dragged = true;
                SlideTo(x);
            }
        }

        public override void OnMouseUIUp(Vector3 position)
        {
            base.OnMouseUIUp(position);
            bool pressedHere = PressedHere;
            bool dragged = Dragged;
            PressedHere = false;
            Dragging = false;
            Dragged = false;
            // Only a click that started here, and not the end of a drag (already applied).
            if (!pressedHere || dragged || !IsSelectable || !gameObject.activeInHierarchy)
            {
                return;
            }
            float x = RelativePosition(position).x - transform.localPosition.x;
            if (HasSlider)
            {
                if (x > TrackHalf)
                {
                    Step(1, animate: true);
                }
                else if (x < -TrackHalf)
                {
                    Step(-1, animate: true);
                }
                else if (Typing && Math.Abs(x) < TypeZone)
                {
                    OpenTyping();
                }
                else
                {
                    SlideTo(x);
                }
                return;
            }
            // Vanilla's zones: the outer quarters step, the middle "chooses".
            float fraction = x / Width;
            if (fraction > 0.25f)
            {
                Step(1, animate: true);
            }
            else if (fraction < -0.25f)
            {
                Step(-1, animate: true);
            }
            else if (Typing)
            {
                OpenTyping();
            }
        }

        public override void OnMouseUIRollOver()
        {
            base.OnMouseUIRollOver();
            Picker?.OnMouseUIRollOver();
        }

        public override void OnMouseUIRollOut()
        {
            base.OnMouseUIRollOut();
            Picker?.OnMouseUIRollOut();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (HoldDirection != 0 && now - LastHeld > HoldTimeout)
            {
                HoldDirection = 0;
            }
            if (!PressedHere)
            {
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                // Released. MouseUI only reports the release to elements under the cursor, so
                // forget a press that was released elsewhere.
                Dragging = false;
                if (ReleasedAt < 0f)
                {
                    ReleasedAt = now;
                }
                else if (now - ReleasedAt > HoldTimeout)
                {
                    PressedHere = false;
                    Dragged = false;
                }
                return;
            }
            if (!HasSlider || !TryMouseX(out float x))
            {
                return;
            }
            if (!Dragging && Math.Abs(x - PressX) > DragThreshold)
            {
                Dragging = true;
                Dragged = true;
            }
            if (Dragging)
            {
                SlideTo(x);
            }
        }

        /// <summary>The mouse's x relative to this element's centre, computed the way MouseUI does.</summary>
        private bool TryMouseX(out float x)
        {
            x = 0f;
            MouseUI ui = MouseUI.Main;
            Mouse mouse = Mouse.current;
            if (ui == null || ui.UICamera == null || mouse == null)
            {
                return false;
            }
            Vector2 screen = mouse.position.ReadValue();
            Vector3 world = ui.UICamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, ui.DistanceToUI));
            x = RelativePosition(world).x - transform.localPosition.x;
            return true;
        }

        private void SlideTo(float x)
        {
            if (!IsSelectable || Def.Count < 2)
            {
                return;
            }
            float fraction = Mathf.Clamp01((x + TrackHalf) / (2f * TrackHalf));
            SetIndex(Mathf.RoundToInt(fraction * (Def.Count - 1)), notify: true);
        }

        // ---- Typed entry ---------------------------------------------------------------------

        private void OpenTyping()
        {
            if (!IsSelectable)
            {
                return;
            }
            TextInputView view = TextInputView.Main;
            // The view must be live: once open, it takes all input until it completes.
            if (view == null || !view.isActiveAndEnabled)
            {
                if (!_typingWarned)
                {
                    _typingWarned = true;
                    ModMain.LogWarning("The game's text entry isn't available here, so values can't be typed");
                }
                return;
            }
            string title = $"{Def.Label}: {Def.ValueAt(0).ToString(CultureInfo.InvariantCulture)} to {Def.ValueAt(Def.Count - 1).ToString(CultureInfo.InvariantCulture)}";
            if (Def.Step > 1)
            {
                title += $", steps of {Def.Step.ToString(CultureInfo.InvariantCulture)}";
            }
            TextInputView.RequestTextInput(title, "", MaxTypedLength, Typed);
        }

        private void Typed(TextInputView.TextInputState state, string text)
        {
            // The page may have closed while the entry was open.
            if (this == null || Def == null || state != TextInputView.TextInputState.TextEntryComplete)
            {
                return;
            }
            if (OptionDef.TryParseNumber(text, out double value))
            {
                SetIndex(Def.IndexOf(value), notify: true);
            }
        }

        // ---- Slider drawing ------------------------------------------------------------------

        private bool TryBuildSlider()
        {
            try
            {
                if (BuildSlider())
                {
                    return true;
                }
                WarnSlider("the picker's parts weren't found (game update?)");
            }
            catch (Exception e)
            {
                WarnSlider($"{e.GetType().Name}: {e.Message}");
            }
            return false;
        }

        private static void WarnSlider(string why)
        {
            if (!_sliderWarned)
            {
                _sliderWarned = true;
                ModMain.LogWarning($"Couldn't draw the slider bar ({why}); sliders work as plain pickers");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool BuildSlider()
        {
            Rectangle backing = BackingField?.GetValue(Picker) as Rectangle;
            TextMeshPro label = LabelField?.GetValue(Picker) as TextMeshPro;
            if (backing == null || label == null)
            {
                return false;
            }
            ShapeLayer layer = ShapeKit.Above(backing, label);
            float z = ShapeKit.LocalZ(Picker.transform, label);
            Transform root = ShapeKit.Group(Picker.transform, "Slider", new Vector3(0f, -Height / 2f + BarInset, z), layer).transform;
            FillColour = Colours[ThemeColour.Default];
            KnobColour = label.color;

            Track = ShapeKit.Add<Rectangle>(root, "Track", Vector3.zero, 0f, layer, TrackColour);
            Track.Width = 2f * TrackHalf;
            Track.Height = BarThickness;
            Fill = ShapeKit.Add<Rectangle>(root, "Fill", Vector3.zero, 0f, layer.Raised(1), FillColour);
            Fill.Height = BarThickness;
            Knob = ShapeKit.Add<Disc>(root, "Knob", Vector3.zero, 0f, layer.Raised(2), KnobColour);
            Knob.Radius = KnobRadius;
            return true;
        }

        private void UpdateSlider()
        {
            if (Track == null)
            {
                return;
            }
            try
            {
                ApplySlider();
            }
            catch (Exception e)
            {
                WarnSlider($"{e.GetType().Name}: {e.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplySlider()
        {
            float fraction = Def.Count > 1 ? Index / (float)(Def.Count - 1) : 0f;
            float filled = fraction * 2f * TrackHalf;
            Fill.gameObject.SetActive(filled > 0.001f);
            Fill.Width = Math.Max(filled, 0.001f);
            Fill.transform.localPosition = new Vector3(-TrackHalf + filled / 2f, 0f, 0f);
            Knob.transform.localPosition = new Vector3(-TrackHalf + filled, 0f, 0f);

            float alpha = IsSelectable ? 1f : LockedAlpha;
            Track.Color = WithAlpha(TrackColour, alpha);
            Fill.Color = WithAlpha(FillColour, alpha);
            Knob.Color = WithAlpha(KnobColour, alpha);
        }

        private static Color WithAlpha(Color c, float alpha)
        {
            c.a *= alpha;
            return c;
        }

        // ---- Element plumbing ----------------------------------------------------------------

        public override void SetSelectable(bool selectable, bool keep_full_alpha = false)
        {
            base.SetSelectable(selectable, keep_full_alpha);
            Picker?.SetSelectable(selectable, keep_full_alpha);
            if (!selectable)
            {
                HoldDirection = 0;
                PressedHere = false;
                Dragging = false;
            }
            UpdateSlider();
        }

        public override void GainFocus()
        {
            base.GainFocus();
            Picker?.GainFocus();
        }

        public override void LoseFocus()
        {
            base.LoseFocus();
            Picker?.LoseFocus();
            HoldDirection = 0;
        }

        public override void Destroy()
        {
            Picker?.Destroy();
            base.Destroy();
        }
    }
}
