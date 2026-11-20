using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ModList
{
    /// <summary>
    /// The mod-options contract (API version 1). Documented for mod authors in
    /// docs/MOD_OPTIONS.md; keep the two in sync.
    ///
    /// A mod opts in by declaring, anywhere in its DLL:
    ///   public static class ModListOptions
    ///   {
    ///       public static IEnumerable GetOptions();       // required
    ///       public static void OnOptionsClosed();         // optional
    ///   }
    /// GetOptions returns dictionaries (string keys) describing each option. Only BCL types
    /// cross the boundary, so the mod never references ModList.dll and works the same with or
    /// without Mod List installed. Mod List finds the class by name when the Mods screen opens
    /// (load order doesn't matter) and calls GetOptions every time it shows the page.
    /// </summary>
    internal static class OptionsApi
    {
        public const int Version = 1;
        public const string ProviderTypeName = "ModListOptions";
        public const string GetOptionsMethod = "GetOptions";
        public const string ClosedMethod = "OnOptionsClosed";

        /// <summary>
        /// Largest number of values an "int" option may span. Only there to catch mistakes (such as
        /// int.MinValue to int.MaxValue): the picker draws one value at a time, holding left /
        /// right speeds up with the size of the range, and "slider" / "input" cover the rest.
        /// </summary>
        public const int MaxIntValues = 1000000;
    }

    internal enum OptionKind
    {
        Heading,
        Bool,
        Int,
        Choice,
        Button,
        Unsupported,
    }

    internal sealed class OptionDef
    {
        public OptionKind Kind;
        public string Id;
        public string Label;
        public string Description;
        public string RawType;

        public Delegate Getter;
        public Delegate Setter;
        public Delegate Action;
        private object _enabled;
        private object _visible;

        // bool / choice: Values are the strings handed to the mod (choice) and Labels what's shown.
        public List<string> Values;
        public List<string> Labels;

        // int
        public int Min;
        public int Max;
        public int Step = 1;
        public string Format;
        /// <summary>"slider": draw a bar the mouse can drag (left / right still step).</summary>
        public bool Slider;
        /// <summary>"input": the value can be typed (select it, or click the number).</summary>
        public bool Input;

        /// <summary>Why this option can't be shown, or null.</summary>
        public string Error;

        /// <summary>"enabled": false locks the option (shown, but can't be changed). A failing check locks it.</summary>
        public bool IsEnabled(string mod) => Check(mod, "enabled", _enabled, whenFailed: false);

        /// <summary>"visible": false hides the option (it takes no space). A failing check shows it.</summary>
        public bool IsVisible(string mod) => Check(mod, "visible", _visible, whenFailed: true);

        /// <summary>A bool or Func&lt;bool&gt; key; absent means true. Failures are logged once per option.</summary>
        private bool Check(string mod, string key, object raw, bool whenFailed)
        {
            try
            {
                switch (raw)
                {
                    case null:
                        return true;
                    case bool b:
                        return b;
                    case Delegate d:
                        return Convert.ToBoolean(Invoke.Call(d), CultureInfo.InvariantCulture);
                    default:
                        return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e)
            {
                OptionLog.WarnOnce($"{mod}: option '{Id ?? Label}' \"{key}\" failed: {Invoke.Unwrap(e).Message}");
                return whenFailed;
            }
        }

        /// <summary>Number of entries in the picker (bool, int, choice).</summary>
        public int Count => Kind == OptionKind.Int ? (int)IntCount(Min, Max, Step) : Labels?.Count ?? 0;

        // In long arithmetic: max - min overflows int for wide ranges.
        private static long IntCount(int min, int max, int step) => ((long)max - min) / step + 1;

        /// <summary>The int value at a picker index.</summary>
        public int ValueAt(int index) => (int)(Min + (long)index * Step);

        /// <summary>The picker index for an int value: clamped to min / max, snapped to the nearest step.</summary>
        public int IndexOf(double value)
        {
            double index = Math.Round((value - Min) / Step, MidpointRounding.AwayFromZero);
            return (int)Math.Max(0, Math.Min(Count - 1, index));
        }

        /// <summary>The picker's entries for a bool or choice (an int draws one value at a time: <see cref="LabelAt"/>).</summary>
        public List<string> PickerLabels() => Labels;

        /// <summary>What an int picker shows at an index: the value through "format".</summary>
        public string LabelAt(int index) => FormatInt(ValueAt(index));

        private string FormatInt(int value)
        {
            if (string.IsNullOrEmpty(Format))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
            try
            {
                return string.Format(CultureInfo.InvariantCulture, Format, value);
            }
            catch (FormatException)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Reads a typed number, ignoring anything that isn't part of one ("250%", "1,000", " 5 ").
        /// Decimals are allowed and rounded to the nearest step.
        /// </summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            StringBuilder number = new StringBuilder();
            bool digits = false;
            bool point = false;
            foreach (char c in text.Trim())
            {
                if (c >= '0' && c <= '9')
                {
                    number.Append(c);
                    digits = true;
                }
                else if (c == '-' && number.Length == 0)
                {
                    number.Append(c);
                }
                else if (c == '.' && !point)
                {
                    number.Append(c);
                    point = true;
                }
            }
            return digits && double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads the mod's current value as a picker index. Throws if the getter does.</summary>
        public int ReadIndex()
        {
            object raw = Invoke.Call(Getter);
            switch (Kind)
            {
                case OptionKind.Bool:
                    return Convert.ToBoolean(raw, CultureInfo.InvariantCulture) ? 1 : 0;
                case OptionKind.Int:
                {
                    return IndexOf(Convert.ToInt32(raw, CultureInfo.InvariantCulture));
                }
                case OptionKind.Choice:
                {
                    string value = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    int index = Values.IndexOf(value);
                    if (index < 0)
                    {
                        index = Values.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
                    }
                    return Math.Max(0, index);
                }
                default:
                    return 0;
            }
        }

        /// <summary>Passes the value at a picker index to the mod's setter. Throws if it does.</summary>
        public void WriteIndex(int index)
        {
            object value;
            switch (Kind)
            {
                case OptionKind.Bool:
                    value = index == 1;
                    break;
                case OptionKind.Int:
                    value = ValueAt(index);
                    break;
                case OptionKind.Choice:
                    value = Values[index];
                    break;
                default:
                    return;
            }
            Invoke.Call(Setter, value);
        }

        /// <summary>
        /// Builds an option from one dictionary returned by GetOptions. Never throws: problems
        /// end up in <see cref="Error"/> so the page can show what the author got wrong.
        /// </summary>
        public static OptionDef Parse(object raw)
        {
            OptionDef def = new OptionDef();
            Dictionary<string, object> map = ToMap(raw);
            if (map == null)
            {
                def.Kind = OptionKind.Unsupported;
                def.Error = $"an option isn't a dictionary ({raw?.GetType().Name ?? "null"})";
                return def;
            }

            def.Id = Get<string>(map, "id");
            def.Label = Get<string>(map, "label") ?? def.Id;
            def.Description = Get<string>(map, "description");
            def._enabled = map.TryGetValue("enabled", out object enabled) ? enabled : null;
            def._visible = map.TryGetValue("visible", out object visible) ? visible : null;
            def.RawType = (Get<string>(map, "type") ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(def.Label))
            {
                def.Error = "\"label\" is missing";
            }

            switch (def.RawType)
            {
                case "heading":
                    def.Kind = OptionKind.Heading;
                    break;
                case "bool":
                    def.Kind = OptionKind.Bool;
                    def.Values = new List<string> { "false", "true" };
                    def.Labels = Strings(map, "labels") ?? new List<string> { "Off", "On" };
                    if (def.Labels.Count != 2)
                    {
                        def.Error ??= "\"labels\" for a bool needs exactly 2 entries (off, on)";
                    }
                    RequireAccessors(def, map);
                    break;
                case "int":
                    def.Kind = OptionKind.Int;
                    RequireAccessors(def, map);
                    if (!TryInt(map, "min", out def.Min) || !TryInt(map, "max", out def.Max))
                    {
                        def.Error ??= "\"min\" and \"max\" are required for an int";
                    }
                    else
                    {
                        def.Step = TryInt(map, "step", out int step) ? step : 1;
                        def.Format = Get<string>(map, "format");
                        if (def.Step < 1 || def.Max < def.Min)
                        {
                            def.Error ??= "an int needs max >= min and step >= 1";
                        }
                        else if (IntCount(def.Min, def.Max, def.Step) > OptionsApi.MaxIntValues)
                        {
                            def.Error ??= $"an int may span at most {OptionsApi.MaxIntValues} values";
                        }
                        def.Slider = Flag(def, map, "slider");
                        def.Input = Flag(def, map, "input");
                    }
                    break;
                case "choice":
                    def.Kind = OptionKind.Choice;
                    RequireAccessors(def, map);
                    def.Values = Strings(map, "choices");
                    def.Labels = Strings(map, "labels") ?? def.Values;
                    if (def.Values == null || def.Values.Count == 0)
                    {
                        def.Error ??= "\"choices\" is missing or empty";
                    }
                    else if (def.Labels.Count != def.Values.Count)
                    {
                        def.Error ??= "\"labels\" must have one entry per choice";
                    }
                    break;
                case "button":
                    def.Kind = OptionKind.Button;
                    def.Action = Get<Delegate>(map, "action");
                    if (def.Action == null || Invoke.Arity(def.Action) != 0)
                    {
                        def.Error ??= "\"action\" must be a delegate with no parameters (e.g. System.Action)";
                    }
                    break;
                default:
                    // A newer contract than this Mod List knows, or a typo. Shown, not hidden.
                    def.Kind = OptionKind.Unsupported;
                    break;
            }
            return def;
        }

        private static void RequireAccessors(OptionDef def, Dictionary<string, object> map)
        {
            def.Getter = Get<Delegate>(map, "get");
            def.Setter = Get<Delegate>(map, "set");
            if (def.Getter == null || Invoke.Arity(def.Getter) != 0)
            {
                def.Error ??= "\"get\" must be a delegate with no parameters (e.g. Func<bool>)";
            }
            else if (def.Setter == null || Invoke.Arity(def.Setter) != 1)
            {
                def.Error ??= "\"set\" must be a delegate with one parameter (e.g. Action<bool>)";
            }
        }

        private static Dictionary<string, object> ToMap(object raw)
        {
            if (!(raw is IDictionary dict))
            {
                return null;
            }
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is string key)
                {
                    map[key] = entry.Value;
                }
            }
            return map;
        }

        private static T Get<T>(Dictionary<string, object> map, string key) where T : class
        {
            return map.TryGetValue(key, out object value) ? value as T : null;
        }

        private static bool TryInt(Dictionary<string, object> map, string key, out int value)
        {
            value = 0;
            if (!map.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }
            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>An optional true / false key; anything else is an error on the option.</summary>
        private static bool Flag(OptionDef def, Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }
            try
            {
                return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                def.Error ??= $"\"{key}\" must be true or false";
                return false;
            }
        }

        private static List<string> Strings(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object raw) || raw == null || raw is string || !(raw is IEnumerable items))
            {
                return null;
            }
            return items.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "").ToList();
        }
    }

    /// <summary>Warnings about a mod's options, each logged once per session.</summary>
    internal static class OptionLog
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static void WarnOnce(string message)
        {
            if (Warned.Add(message))
            {
                ModMain.LogWarning(message);
            }
        }
    }

    /// <summary>One mod's ModListOptions class.</summary>
    internal sealed class OptionsProvider
    {
        private readonly MethodInfo _getOptions;
        private readonly MethodInfo _closed;
        public readonly string TypeName;

        public OptionsProvider(MethodInfo getOptions, MethodInfo closed)
        {
            _getOptions = getOptions;
            _closed = closed;
            TypeName = getOptions.DeclaringType?.FullName;
        }

        /// <summary>Calls GetOptions. Returns null and sets <paramref name="error"/> if it fails.</summary>
        public List<OptionDef> Read(string mod, out string error)
        {
            error = null;
            try
            {
                if (!(Invoke.CallStatic(_getOptions) is IEnumerable items))
                {
                    return new List<OptionDef>();
                }
                List<OptionDef> defs = new List<OptionDef>();
                foreach (object item in items)
                {
                    OptionDef def = OptionDef.Parse(item);
                    // GetOptions is called on every open, so each problem is logged only once.
                    if (def.Error != null)
                    {
                        WarnOnce(mod, def, def.Error);
                    }
                    else if (def.Kind == OptionKind.Unsupported)
                    {
                        WarnOnce(mod, def, $"has type '{def.RawType}', which Mod List {ModMain.Version} doesn't support");
                    }
                    defs.Add(def);
                }
                return defs;
            }
            catch (Exception e)
            {
                Exception inner = Invoke.Unwrap(e);
                error = inner.Message;
                ModMain.LogError($"{mod}: {TypeName}.GetOptions failed: {inner}");
                return null;
            }
        }

        private static void WarnOnce(string mod, OptionDef def, string problem)
        {
            OptionLog.WarnOnce($"{mod}: option '{def.Id ?? def.Label}': {problem}");
        }

        public bool HasOptions(string mod)
        {
            List<OptionDef> defs = Read(mod, out string error);
            // A failing provider still gets its gear, so the page can show the error. Otherwise a
            // gear needs at least one option that's visible right now.
            return error != null || (defs != null && defs.Any(d => d.Kind != OptionKind.Heading && d.IsVisible(mod)));
        }

        public void NotifyClosed(string mod)
        {
            if (_closed == null)
            {
                return;
            }
            try
            {
                Invoke.CallStatic(_closed);
            }
            catch (Exception e)
            {
                ModMain.LogError($"{mod}: {TypeName}.OnOptionsClosed failed: {Invoke.Unwrap(e)}");
            }
        }

        private static readonly Dictionary<Assembly, OptionsProvider> Cache = new Dictionary<Assembly, OptionsProvider>();

        /// <summary>The first ModListOptions class found in these assemblies, or null.</summary>
        public static OptionsProvider Find(IEnumerable<Assembly> assemblies)
        {
            foreach (Assembly asm in assemblies)
            {
                if (!Cache.TryGetValue(asm, out OptionsProvider provider))
                {
                    provider = Scan(asm);
                    Cache[asm] = provider;
                }
                if (provider != null)
                {
                    return provider;
                }
            }
            return null;
        }

        private static OptionsProvider Scan(Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            catch (Exception)
            {
                return null;
            }
            foreach (Type type in types)
            {
                if (type.Name != OptionsApi.ProviderTypeName || !type.IsAbstract || !type.IsSealed)
                {
                    continue; // only "static class ModListOptions" (abstract + sealed in IL)
                }
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                MethodInfo get = type.GetMethod(OptionsApi.GetOptionsMethod, flags, null, Type.EmptyTypes, null);
                if (get == null || !typeof(IEnumerable).IsAssignableFrom(get.ReturnType))
                {
                    ModMain.LogWarning($"{type.FullName} in {asm.GetName().Name} has no static IEnumerable {OptionsApi.GetOptionsMethod}(); ignored");
                    continue;
                }
                MethodInfo closed = type.GetMethod(OptionsApi.ClosedMethod, flags, null, Type.EmptyTypes, null);
                ModMain.Log($"Found mod options in {asm.GetName().Name} ({type.FullName})");
                return new OptionsProvider(get, closed);
            }
            return null;
        }
    }

    /// <summary>Calls into other mods' delegates and methods, converting values to what they take.</summary>
    internal static class Invoke
    {
        public static object Call(Delegate d, params object[] args)
        {
            ParameterInfo[] signature = Signature(d);
            object[] converted = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Type target = i < signature.Length ? signature[i].ParameterType : typeof(object);
                converted[i] = Convert(args[i], target);
            }
            return d.DynamicInvoke(converted);
        }

        /// <summary>
        /// The delegate type's own parameters (from its Invoke method). Not d.Method's: a delegate
        /// bound to a static method's first argument (e.g. an extension method) has one more.
        /// </summary>
        public static ParameterInfo[] Signature(Delegate d)
        {
            return d.GetType().GetMethod("Invoke")?.GetParameters() ?? d.Method.GetParameters();
        }

        public static int Arity(Delegate d) => Signature(d).Length;

        public static object CallStatic(MethodInfo method)
        {
            return method.Invoke(null, null);
        }

        public static Exception Unwrap(Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null)
            {
                e = e.InnerException;
            }
            return e;
        }

        private static object Convert(object value, Type target)
        {
            if (value == null || target == typeof(object) || target.IsInstanceOfType(value))
            {
                return value;
            }
            if (target.IsEnum && value is string s)
            {
                return Enum.Parse(target, s, ignoreCase: true);
            }
            return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
    }
}
