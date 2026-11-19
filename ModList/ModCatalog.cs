using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using KitchenMods;

namespace ModList
{
    internal sealed class ModEntry
    {
        public string Name;
        /// <summary>Display version ("1.2.3"), or null when the mod has none to read.</summary>
        public string Version;
        public bool Workshop;
        public ulong WorkshopId;
        public bool Failed;
    }

    /// <summary>
    /// Builds the list shown in the panel from the game's own record of loaded mods,
    /// <see cref="ModPreload.Mods"/> (Workshop items first, then local Mods folders).
    ///
    /// The game has no concept of a mod version, so one is read from the mod's own files, in
    /// this order:
    ///   1. A public string "ModVersion" field or property on one of the mod's IModInitializer
    ///      instances. KitchenLib's BaseMod declares this, so KitchenLib mods report the version
    ///      their author typed in. Matched by name, so KitchenLib isn't referenced.
    ///   2. The DLL's [AssemblyInformationalVersion] (what an SDK-style project's &lt;Version&gt;
    ///      sets), with any "+build" suffix removed.
    ///   3. [AssemblyFileVersion].
    ///   4. The assembly version.
    /// When a mod has several DLLs, the one holding its mod code (initializers, systems or
    /// components) is used; failing that, the one named like the mod, then the first by name.
    /// </summary>
    internal static class ModCatalog
    {
        private static readonly FieldInfo InitializersField = AccessTools.Field(typeof(AssemblyModPack), "Initializers");
        private static readonly FieldInfo SystemsField = AccessTools.Field(typeof(AssemblyModPack), "Systems");
        private static readonly FieldInfo ComponentsField = AccessTools.Field(typeof(AssemblyModPack), "Components");

        // Workshop items are known to the loader only by their numeric id; vanilla asks Steam for
        // the titles only when showing its "failed to load" error. We ask once, the first time the
        // panel opens, and the panel redraws when they arrive.
        private static readonly object TitleLock = new object();
        private static readonly Dictionary<ulong, string> WorkshopTitles = new Dictionary<ulong, string>();
        private static int _titlesVersion;
        // Reset from the Steam query's continuation thread when the fetch fails.
        private static volatile bool _titleFetchStarted;
        private static bool _loggedOnce;

        /// <summary>Changes whenever new Workshop titles arrive.</summary>
        public static int TitlesVersion => Volatile.Read(ref _titlesVersion);

        public static List<ModEntry> Build()
        {
            List<ModEntry> list = new List<ModEntry>();
            foreach (Mod mod in ModPreload.Mods)
            {
                if (mod == null)
                {
                    continue;
                }
                try
                {
                    list.Add(Describe(mod));
                }
                catch (Exception e)
                {
                    ModMain.LogWarning($"Couldn't read mod '{mod.Name}': {e.Message}");
                    list.Add(new ModEntry { Name = mod.Name ?? "?", Workshop = mod.ID != 0, WorkshopId = mod.ID });
                }
            }
            // Alphabetical; ties (e.g. a local copy and the Workshop copy of the same mod) keep
            // load order, Workshop first, because the sort is stable.
            list = list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                ModMain.Log($"{list.Count} mod(s): " + string.Join(", ", list.Select(e =>
                    $"{e.Name} [{(e.Workshop ? "Workshop " + e.WorkshopId : "local")}] "
                    + (e.Failed ? "FAILED" : e.Version ?? "no version"))));
            }
            return list;
        }

        private static ModEntry Describe(Mod mod)
        {
            bool workshop = mod.Source is SteamWorkshopModSource || mod.ID != 0;
            ModEntry entry = new ModEntry
            {
                Workshop = workshop,
                WorkshopId = mod.ID,
                Failed = mod.State == ModState.FailedDuringLoad,
            };

            List<AssemblyModPack> packs = mod.GetPacks<AssemblyModPack>().Where(p => p.Asm != null).ToList();
            AssemblyModPack main = packs.FirstOrDefault(HasModCode)
                ?? packs.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p.Name), mod.Name, StringComparison.OrdinalIgnoreCase))
                // Sorted, because file listing order isn't guaranteed. For the Harmony item (12
                // library DLLs, no mod code) this picks 0Harmony.dll.
                ?? packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

            string declaredName = null;
            string declaredVersion = null;
            foreach (AssemblyModPack pack in packs)
            {
                if (InitializersField?.GetValue(pack) is IList initializers)
                {
                    foreach (object init in initializers)
                    {
                        declaredVersion ??= ReadString(init, "ModVersion");
                        declaredName ??= ReadString(init, "ModName");
                    }
                }
            }

            entry.Version = declaredVersion != null ? Clean(declaredVersion)
                : main != null ? AssemblyVersion(main.Asm)
                : null;

            if (!workshop)
            {
                // Local mods: the folder name under <game>\Mods\, which is what the player sees.
                entry.Name = mod.Name;
            }
            else
            {
                string title;
                lock (TitleLock)
                {
                    WorkshopTitles.TryGetValue(mod.ID, out title);
                }
                entry.Name = title
                    ?? declaredName
                    ?? (main != null ? Path.GetFileNameWithoutExtension(main.Name) : null)
                    ?? mod.Name;
            }
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                entry.Name = workshop ? mod.ID.ToString() : "?";
            }
            return entry;
        }

        private static bool HasModCode(AssemblyModPack pack)
        {
            return Count(InitializersField, pack) > 0 || Count(SystemsField, pack) > 0 || Count(ComponentsField, pack) > 0;
        }

        private static int Count(FieldInfo field, AssemblyModPack pack)
        {
            return field?.GetValue(pack) is ICollection c ? c.Count : 0;
        }

        private static string ReadString(object obj, string member)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                Type type = obj.GetType();
                object value = type.GetField(member, flags)?.GetValue(obj);
                if (value == null)
                {
                    PropertyInfo prop = type.GetProperty(member, flags);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        value = prop.GetValue(obj);
                    }
                }
                string s = value as string;
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string AssemblyVersion(Assembly asm)
        {
            try
            {
                string info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    return Clean(info);
                }
                string file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                if (!string.IsNullOrWhiteSpace(file))
                {
                    return Clean(file);
                }
                return Clean(asm.GetName().Version?.ToString());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// "1.2.0+abc123" -> "1.2.0" (SemVer build metadata, which the .NET 8+ SDK fills with the
        /// git commit), and "2.13.0.0" -> "2.13.0" (the unused fourth part of assembly versions).
        /// </summary>
        internal static string Clean(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }
            string v = version.Trim();
            int plus = v.IndexOf('+');
            if (plus > 0)
            {
                v = v.Substring(0, plus);
            }
            string[] parts = v.Split('.');
            if (parts.Length == 4 && parts[3] == "0" && parts.Take(3).All(p => p.Length > 0 && p.All(char.IsDigit)))
            {
                v = string.Join(".", parts, 0, 3);
            }
            return v.Length == 0 ? null : v;
        }

        /// <summary>
        /// Asks Steam for the titles of the loaded Workshop items, once per session (retried on
        /// the next open if it failed, e.g. offline). Uses the game's own query
        /// (SteamWorkshopModSource.PopulateModNames) on throwaway copies, so the game's Mod
        /// objects aren't touched. Only distinct non-zero ids are passed: that method builds a
        /// dictionary keyed by id, which would throw on the duplicate id 0 of local mods.
        /// </summary>
        public static void EnsureWorkshopTitles()
        {
            if (_titleFetchStarted)
            {
                return;
            }
            _titleFetchStarted = true;
            try
            {
                SteamWorkshopModSource source = ModPreload.Sources?.OfType<SteamWorkshopModSource>().FirstOrDefault();
                List<Mod> copies = ModPreload.Mods
                    .Where(m => m != null && m.ID != 0)
                    .Select(m => m.ID)
                    .Distinct()
                    .Select(id => new Mod(id, id.ToString()) { Source = source })
                    .ToList();
                if (source == null || copies.Count == 0)
                {
                    return;
                }
                source.PopulateModNames(copies).ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion)
                    {
                        _titleFetchStarted = false;
                        ModMain.LogWarning($"Couldn't get Workshop titles from Steam: {task.Exception?.GetBaseException().Message ?? task.Status.ToString()}");
                        return;
                    }
                    int found = 0;
                    lock (TitleLock)
                    {
                        foreach (Mod copy in copies)
                        {
                            if (!string.IsNullOrWhiteSpace(copy.Name) && copy.Name != copy.ID.ToString())
                            {
                                WorkshopTitles[copy.ID] = copy.Name;
                                found++;
                            }
                        }
                    }
                    Interlocked.Increment(ref _titlesVersion);
                    ModMain.Log($"Got {found} of {copies.Count} Workshop title(s) from Steam");
                });
            }
            catch (Exception e)
            {
                _titleFetchStarted = false;
                ModMain.LogWarning($"Couldn't ask Steam for Workshop titles: {e.Message}");
            }
        }
    }
}
