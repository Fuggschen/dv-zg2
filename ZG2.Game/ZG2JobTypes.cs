using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Logic.Job;
using DV.ThingTypes;

namespace ZG2
{
    /// <summary>
    /// Decides which jobs are subject to the Zg2 check.
    ///
    /// The configuration is a ';'-separated list of keys. A key matches a job if it is
    ///   • the job type's enum name (vanilla, e.g. "Transport"),
    ///   • the job type's number (job types added by mods are undefined enum values, e.g. "121"), or
    ///   • the job ID abbreviation (middle part of the job ID, e.g. "CH" in "GF-CH-98").
    /// The abbreviation is what players know from the booklets, so it is the preferred key for
    /// job types of other mods.
    /// </summary>
    public static class ZG2JobTypes
    {
        public class VanillaType
        {
            public string Key;       // enum name stored in the settings
            public int NameIndex;    // ZG2JobTypeNames index (translated name)
            public string Abbrev;    // job ID abbreviation
            public string Name => ZG2JobTypeNames.Get(NameIndex);
        }

        /// <summary>A job type added by another mod.</summary>
        public class ModType
        {
            /// <summary>
            /// Settings key. Own job type of a mod: its internal number ("121").
            /// Variant of a vanilla type with its own job ID abbreviation (e.g. TrainServiceRework "SH"): "@SH".
            /// </summary>
            public string Number;
            public string Abbrev;    // job ID abbreviation
            public string Name;      // readable name; "loc:<key>" = game localisation key (translated on display)
            public string ModName;   // name of the mod that adds it
            public bool Installed;   // found in a loaded mod this session
            public Func<string> NameFunc; // live, translated name from the mod's own API (runtime only)

            public bool IsVariant => Number != null && Number.StartsWith("@");

            public string DisplayName
            {
                get
                {
                    if (NameFunc != null)
                    {
                        string n = null;
                        try { n = NameFunc(); } catch { }
                        if (!string.IsNullOrEmpty(n))
                            return string.IsNullOrEmpty(ModName) ? n : $"{ModName}: {n}";
                    }
                    if (string.IsNullOrEmpty(Name)) return ModName ?? string.Empty;
                    if (!Name.StartsWith("loc:")) return Name;
                    string key = Name.Substring(4);
                    string text = Localize(key);
                    if (string.IsNullOrEmpty(text) || text == key) text = Humanize(LastSegment(key));
                    return string.IsNullOrEmpty(ModName) ? text : $"{ModName}: {text}";
                }
            }

            public string Label
            {
                get
                {
                    string name = DisplayName;
                    if (string.IsNullOrEmpty(name))
                        return string.IsNullOrEmpty(Abbrev) ? ZG2Loc.T("Unbekannter Mod-Jobtyp", "Unknown mod job type")
                            : ZG2Loc.T("Mod-Jobtyp", "Mod job type") + $" ({Abbrev})";
                    return string.IsNullOrEmpty(Abbrev) ? name : $"{name} ({Abbrev})";
                }
            }
        }

        private static string LastSegment(string key)
        {
            int i = key.LastIndexOf('/');
            return i >= 0 ? key.Substring(i + 1) : key;
        }

        private static string Localize(string key)
        {
            try { return DV.Localization.LocalizationAPI.L(key); }
            catch { return key; }
        }

        public static string VariantKey(string abbrev) => "@" + abbrev;

        /// <summary>Vanilla job types offered in the settings (Custom and the unused ComplexTransport are left out).</summary>
        public static readonly List<VanillaType> Vanilla = BuildVanilla();

        private static string cachedSource;
        private static readonly HashSet<string> cachedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Job types seen this session: key (name/number) → job ID abbreviation.</summary>
        public static readonly SortedDictionary<string, string> SeenTypes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Job types of other mods: type number → info. Persisted in <see cref="ZG2Settings.KnownJobTypes"/>
        /// ("number:abbrev:name;…"), so names and abbreviations survive restarts.
        /// </summary>
        private static readonly SortedDictionary<string, ModType> known = new SortedDictionary<string, ModType>(StringComparer.OrdinalIgnoreCase);
        private static string knownSource;
        private static bool detected;

        /// <summary>Job types of mods that are installed and active in this session.</summary>
        public static IEnumerable<ModType> KnownModTypes
        {
            get
            {
                LoadKnown();
                var list = new List<ModType>();
                foreach (var t in known.Values) if (t.Installed) list.Add(t);
                list.Sort((x, y) => string.Compare(x.Label, y.Label, StringComparison.OrdinalIgnoreCase));
                return list;
            }
        }

        private static void LoadKnown()
        {
            string src = Main.Settings?.KnownJobTypes ?? string.Empty;
            if (src == knownSource) return;
            knownSource = src;

            var installed = new HashSet<string>();
            foreach (var kv in known) if (kv.Value.Installed) installed.Add(kv.Key);
            known.Clear();

            foreach (var part in src.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = SplitEntry(part);
                string num = f[0].Trim();
                if (num.Length == 0) continue;
                known[num] = new ModType
                {
                    Number = num,
                    Abbrev = f.Length > 1 ? f[1].Trim() : string.Empty,
                    Name = f.Length > 2 ? f[2].Trim() : string.Empty,
                    ModName = f.Length > 3 ? f[3].Trim() : string.Empty,
                    Installed = installed.Contains(num)
                };
            }
        }

        /// <summary>"num:abbrev:name:mod" where name may be "loc:key".</summary>
        private static string[] SplitEntry(string part)
        {
            var raw = part.Split(':');
            var list = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == "loc" && list.Count == 2 && i + 1 < raw.Length)
                {
                    list.Add("loc:" + raw[i + 1]);
                    i++;
                }
                else list.Add(raw[i]);
            }
            return list.ToArray();
        }

        private static void StoreKnown()
        {
            var parts = new List<string>();
            foreach (var t in known.Values)
                parts.Add($"{t.Number}:{Clean(t.Abbrev)}:{CleanName(t.Name)}:{Clean(t.ModName)}");
            Main.Settings.KnownJobTypes = string.Join(";", parts);
            knownSource = Main.Settings.KnownJobTypes;
            try { Main.Settings.Save(Main.ModEntry); } catch { /* saved with the next settings save */ }
        }

        private static string Clean(string s) => (s ?? string.Empty).Replace(":", " ").Replace(";", " ").Trim();

        // Keeps the "loc:" prefix, removes other separators
        private static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.StartsWith("loc:")) return "loc:" + s.Substring(4).Replace(":", "_").Replace(";", "_");
            return Clean(s);
        }

        /// <summary>
        /// Once per session: finds the job types other mods add, by looking for JobType constants /
        /// static fields with undefined values in all loaded mod assemblies (e.g. PassengerJobs:
        /// <c>public const JobType Express = (JobType)101;</c>). Name = the mod's display name; if a
        /// mod adds several types, the field name is appended ("Passenger Jobs – Express").
        /// </summary>
        public static void DetectFromMods()
        {
            if (detected || Main.Settings == null) return;
            detected = true;
            LoadKnown();
            bool changed = false;

            try
            {
                foreach (var entry in UnityModManagerNet.UnityModManager.modEntries)
                {
                    if (entry == null || entry == Main.ModEntry || !entry.Active || entry.Assembly == null) continue;

                    var found = new SortedDictionary<int, string>();
                    foreach (var type in SafeTypes(entry.Assembly))
                    {
                        FieldInfo[] fields;
                        try { fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                        catch { continue; }

                        foreach (var fi in fields)
                        {
                            if (fi.FieldType != typeof(JobType)) continue;
                            object raw;
                            try { raw = fi.IsLiteral ? fi.GetRawConstantValue() : fi.GetValue(null); }
                            catch { continue; }
                            if (raw == null) continue;
                            int value = Convert.ToInt32(raw);
                            if (Enum.IsDefined(typeof(JobType), (JobType)value) || found.ContainsKey(value)) continue;
                            found[value] = Humanize(fi.Name);
                        }
                    }

                    string modName = entry.Info?.DisplayName ?? entry.Info?.Id ?? "Mod";
                    if (modName.IndexOf(' ') < 0) modName = Humanize(modName); // "CircleHaulJobs" → "Circle Haul Jobs"
                    // 0) The mod's own job kind API (e.g. TrainServiceRework.JobKinds) – the API is the
                    //    complete truth for that mod, so the guesswork below is skipped for it.
                    int providersBefore = providers.Count;
                    if (ReadKindApi(entry.Assembly, modName)) changed = true;
                    bool hasKindApi = providers.Count > providersBefore;
                    if (string.Equals(entry.Info?.Id, "TrainServiceRework", StringComparison.OrdinalIgnoreCase))
                    {
                        if (hasKindApi)
                            Main.Log($"Zg2: TrainServiceRework {entry.Info.Version}: job kind API found.");
                        else
                        {
                            Main.LogWarning($"Zg2: TrainServiceRework {entry.Info.Version} has no job kind API (TrainServiceRework.JobKinds) – using the built-in list of its job kinds.");
                            if (RegisterTsrFallback(modName)) changed = true;
                            hasKindApi = true; // no guessing from IL for TSR
                        }
                    }

                    // A mod with its own job kind API is described completely by it: its JobType numbers
                    // are not listed again (would show e.g. "Circle Haul Jobs (CH)" next to the API entry).
                    if (hasKindApi)
                    {
                        foreach (var kv in found)
                            if (known.TryGetValue(kv.Key.ToString(), out var dup)) dup.Installed = false;
                        continue;
                    }

                    // 1) Job ID codes the mod announces itself (see ReadAnnouncedCodes)
                    if (ReadAnnouncedCodes(entry.Assembly, modName)) changed = true;

                    // 2) Fallback: abbreviations from the mod's IdGenerator patch code
                    var ilAbbrevs = hasKindApi ? new List<string>() : FindAbbrevsInIdPatch(entry.Assembly);
                    if (found.Count == 0 && ilAbbrevs.Count == 0) continue;

                    int index = 0;
                    foreach (var kv in found)
                    {
                        string num = kv.Key.ToString();
                        string name = found.Count == 1 ? modName : $"{modName} – {kv.Value}";
                        if (!known.TryGetValue(num, out var t))
                        {
                            t = new ModType { Number = num, Abbrev = string.Empty, ModName = modName };
                            known[num] = t;
                            changed = true;
                            Main.Log($"Zg2: job type '{name}' detected in mod {entry.Info?.Id}.");
                        }
                        if (t.Name != name && !announcedKeys.Contains(num)) { t.Name = name; changed = true; }
                        if (t.ModName != modName) { t.ModName = modName; changed = true; }
                        t.Installed = true;

                        // Unambiguous only if the mod has one job type and one abbreviation (or equally many, in order)
                        if (string.IsNullOrEmpty(t.Abbrev) && ilAbbrevs.Count == found.Count && index < ilAbbrevs.Count)
                        {
                            t.Abbrev = ilAbbrevs[index];
                            changed = true;
                            Main.Log($"Zg2: job type '{name}' – abbreviation {t.Abbrev} (from the mod's job ID code).");
                        }
                        index++;
                    }

                    // Remaining abbreviations = variants of vanilla job types with their own job ID code
                    // (e.g. TrainServiceRework: damaged freight "SH", repair empty haul "MH").
                    var ownAbbrevs = new HashSet<string>();
                    foreach (var kv in found)
                        if (known.TryGetValue(kv.Key.ToString(), out var own) && !string.IsNullOrEmpty(own.Abbrev)) ownAbbrevs.Add(own.Abbrev);
                    bool ownUnknown = found.Count > 0 && ownAbbrevs.Count < found.Count;
                    var variants = ownUnknown ? new List<string>() : ilAbbrevs.Where(a => !ownAbbrevs.Contains(a)).ToList();

                    if (variants.Count > 0)
                    {
                        var locKeys = FindJobNameKeys(entry.Assembly);
                        for (int i = 0; i < variants.Count; i++)
                        {
                            string ab = variants[i];
                            string key = VariantKey(ab);
                            string name = locKeys.Count == variants.Count ? "loc:" + locKeys[i] : string.Empty;
                            if (!known.TryGetValue(key, out var v))
                            {
                                v = new ModType { Number = key, Abbrev = ab };
                                known[key] = v;
                                changed = true;
                                Main.Log($"Zg2: job variant {ab} detected in mod {entry.Info?.Id}.");
                            }
                            // announced names (JobIdCodes) win over this order-based guess
                            if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(v.Name)) { v.Name = name; changed = true; }
                            if (v.ModName != modName) { v.ModName = modName; changed = true; }
                            v.Installed = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogWarning($"Zg2: job type detection failed: {ex.Message}");
            }

            if (changed) StoreKnown();
        }

        /// <summary>
        /// Reads the job ID abbreviations a mod uses from its Harmony patch on IdGenerator.GenerateJobID:
        /// all string literals of 2–3 capital letters (e.g. "CH", "PE", "PR") in the patch method and in
        /// the other methods of its class, in the order they appear. Vanilla abbreviations are ignored.
        /// </summary>
        private static List<string> FindAbbrevsInIdPatch(Assembly modAssembly)
        {
            var result = new List<string>();
            try
            {
                var original = HarmonyLib.AccessTools.Method(typeof(IdGenerator), nameof(IdGenerator.GenerateJobID));
                var info = original != null ? HarmonyLib.Harmony.GetPatchInfo(original) : null;
                if (info == null) return result;

                var methods = new List<MethodInfo>();
                foreach (var patch in info.Prefixes.Concat(info.Postfixes))
                {
                    var m = patch.PatchMethod;
                    if (m == null || m.DeclaringType == null || m.DeclaringType.Assembly != modAssembly) continue;
                    methods.Add(m);
                    foreach (var other in m.DeclaringType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (other != m) methods.Add(other);
                }

                // One level deeper: methods of the same mod called from there
                // (TrainServiceRework's postfix only calls a helper that holds "SH"/"MH").
                var all = new List<MethodBase>(methods);
                foreach (var m in methods)
                    foreach (var called in CalledMethods(m))
                        if (called.DeclaringType != null && called.DeclaringType.Assembly == modAssembly && !all.Contains(called))
                            all.Add(called);

                foreach (var m in all)
                    foreach (var str in StringLiterals(m))
                        if (IsAbbrev(str) && !result.Contains(str) && !Vanilla.Exists(v => v.Abbrev == str))
                            result.Add(str);
            }
            catch (Exception ex)
            {
                if (Main.Settings != null && Main.Settings.DebugLogging) Main.Log($"Zg2: IL scan failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>A mod's job kind API (static class "JobKinds" with GetKindCodes / GetKindCodeFromJobId …).</summary>
        private class KindProvider
        {
            public string ModName;
            public MethodInfo GetCodeFromId;   // string GetKindCodeFromJobId(string jobId)
            public MethodInfo GetName;         // string GetLocalizedName(string code)
            public MethodInfo IsHaul;          // bool IsHaul(string code)

            public string CodeOf(string jobId)
            {
                if (GetCodeFromId == null || string.IsNullOrEmpty(jobId)) return null;
                try { return GetCodeFromId.Invoke(null, new object[] { jobId }) as string; }
                catch { return null; }
            }
        }

        private static readonly List<KindProvider> providers = new List<KindProvider>();

        /// <summary>Job kind code of a job according to the other mods' APIs, or null.</summary>
        private static string ProviderCode(string jobId)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                string code = providers[i].CodeOf(jobId);
                if (!string.IsNullOrEmpty(code)) return code;
            }
            return null;
        }

        /// <summary>
        /// Reads a job kind API from a mod: a public static class named "JobKinds" with
        /// <c>string[] GetKindCodes()</c>, <c>string GetKindCodeFromJobId(string)</c> and optionally
        /// <c>string GetLocalizedName(string)</c> / <c>bool IsHaul(string)</c> (TrainServiceRework provides this).
        /// Only primitive types – no reference to the other mod. Kinds that drive between stations
        /// (IsHaul) are switched on when they are found for the first time, shunting kinds stay off.
        /// </summary>
        /// <summary>
        /// Job kinds of TrainServiceRework versions without the JobKinds API. The codes are stable
        /// (TSR documents them as never renamed); names are the English booklet names.
        /// </summary>
        private static bool RegisterTsrFallback(string modName)
        {
            var kinds = new[]
            {
                new[] { "SH", "Special Haul", "1" },
                new[] { "MH", "Maintenance Haul", "1" },
                new[] { "SO", "Shunting Out", "0" },
                new[] { "MS", "Maintenance", "0" },
                new[] { "SS", "Shunting Sorting", "0" },
            };
            bool changed = false;
            foreach (var k in kinds)
            {
                string key = VariantKey(k[0]);
                bool isNew = !known.TryGetValue(key, out var t);
                if (isNew)
                {
                    t = new ModType { Number = key };
                    known[key] = t;
                    changed = true;
                }
                if (t.Abbrev != k[0]) { t.Abbrev = k[0]; changed = true; }
                if (t.ModName != modName) { t.ModName = modName; changed = true; }
                if (string.IsNullOrEmpty(t.Name) || t.Name.StartsWith("loc:")) { t.Name = k[1]; changed = true; }
                t.Installed = true;
                if (isNew && k[2] == "1") Set(key, true); // first time: hauls on, shunting off
            }
            return changed;
        }

        private static bool ReadKindApi(Assembly asm, string modName)
        {
            bool changed = false;
            var candidates = new List<Type>();
            var direct = asm.GetType("TrainServiceRework.JobKinds", false);
            if (direct != null) candidates.Add(direct);
            foreach (var t in SafeTypes(asm))
                if (t.Name == "JobKinds" && t != direct) candidates.Add(t);

            foreach (var type in candidates)
            {
                if (!type.IsAbstract || !type.IsSealed) continue; // static class
                MethodInfo getCodes, fromId;
                try
                {
                    const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
                    getCodes = type.GetMethod("GetKindCodes", f, null, Type.EmptyTypes, null);
                    fromId = type.GetMethod("GetKindCodeFromJobId", f, null, new[] { typeof(string) }, null);
                    if (getCodes == null || fromId == null) continue;

                    var provider = new KindProvider
                    {
                        ModName = modName,
                        GetCodeFromId = fromId,
                        GetName = type.GetMethod("GetLocalizedName", f, null, new[] { typeof(string) }, null),
                        IsHaul = type.GetMethod("IsHaul", f, null, new[] { typeof(string) }, null)
                    };
                    providers.Add(provider);

                    if (!(getCodes.Invoke(null, null) is string[] codes)) continue;
                    foreach (var rawCode in codes)
                    {
                        if (string.IsNullOrEmpty(rawCode)) continue;
                        string code = rawCode.Trim();
                        string key = VariantKey(code);
                        bool isNew = !known.TryGetValue(key, out var t);
                        if (isNew)
                        {
                            t = new ModType { Number = key, Name = string.Empty };
                            known[key] = t;
                            changed = true;
                            Main.Log($"Zg2: job kind {code} provided by {modName}.");
                        }
                        if (t.Abbrev != code) { t.Abbrev = code; changed = true; }
                        if (t.ModName != modName) { t.ModName = modName; changed = true; }
                        t.Installed = true;

                        var nameMethod = provider.GetName;
                        string c = code;
                        if (nameMethod != null)
                            t.NameFunc = () => nameMethod.Invoke(null, new object[] { c }) as string;

                        // first time: hauls on, shunting off
                        if (isNew && provider.IsHaul != null)
                        {
                            bool haul = false;
                            try { haul = provider.IsHaul.Invoke(null, new object[] { code }) is bool b && b; } catch { }
                            if (haul) Set(key, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.LogWarning($"Zg2: job kind API of {modName} could not be read: {ex.Message}");
                }
            }
            return changed;
        }

        /// <summary>Names of the static member another mod can use to announce its job ID codes.</summary>
        private static readonly string[] AnnounceMemberNames = { "JobIdCodes", "ZG2JobCodes" };
        private static readonly HashSet<string> announcedKeys = new HashSet<string>();

        /// <summary>
        /// Interface for other mods – no reference to ZG2 needed. A mod declares anywhere in its assembly
        /// a public static string array (field, property or parameterless method) named <c>JobIdCodes</c>:
        /// <code>
        /// public static readonly string[] JobIdCodes =
        /// {
        ///     "SH|Transport|train_service_rework/job/damaged_freight",   // CODE | base job type | name (loc key or text)
        ///     "SO|ShuntingUnload|train_service_rework/job/shunting_out",
        /// };
        /// </code>
        /// Base job type: vanilla enum name (Transport, EmptyHaul, ShuntingLoad, ShuntingUnload) for jobs that
        /// only use their own ID code, or the number of an own JobType. Name: a localisation key (contains "/")
        /// or plain text; optional. ZG2 reads this once at game start, already in the main menu.
        /// </summary>
        private static bool ReadAnnouncedCodes(Assembly asm, string modName)
        {
            bool changed = false;
            foreach (var type in SafeTypes(asm))
            {
                foreach (var memberName in AnnounceMemberNames)
                {
                    IEnumerable<string> entries = null;
                    try
                    {
                        const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
                        object value = type.GetField(memberName, f)?.GetValue(null)
                                       ?? type.GetProperty(memberName, f)?.GetValue(null, null);
                        if (value == null)
                        {
                            var m = type.GetMethod(memberName, f, null, Type.EmptyTypes, null);
                            if (m != null) value = m.Invoke(null, null);
                        }
                        entries = value as IEnumerable<string>;
                    }
                    catch { }
                    if (entries == null) continue;

                    foreach (var raw in entries)
                        if (RegisterAnnounced(raw, modName)) changed = true;
                }
            }
            return changed;
        }

        private static bool RegisterAnnounced(string raw, string modName)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            var f = raw.Split('|');
            string code = f[0].Trim().ToUpperInvariant();
            string baseType = f.Length > 1 ? f[1].Trim() : string.Empty;
            string nameRaw = f.Length > 2 ? f[2].Trim() : string.Empty;
            if (code.Length == 0 || Vanilla.Exists(v => v.Abbrev == code)) return false;

            string name = nameRaw.Length == 0 ? string.Empty : (nameRaw.Contains("/") ? "loc:" + nameRaw : nameRaw);

            // Own JobType number → set the abbreviation of that type; otherwise a variant of a vanilla type
            string key = int.TryParse(baseType, out int num) && !Enum.IsDefined(typeof(JobType), (JobType)num)
                ? num.ToString()
                : VariantKey(code);

            bool changed = false;
            if (!known.TryGetValue(key, out var t))
            {
                t = new ModType { Number = key };
                known[key] = t;
                changed = true;
                Main.Log($"Zg2: job code {code} announced by {modName}.");
            }
            if (t.Abbrev != code) { t.Abbrev = code; changed = true; }
            if (name.Length > 0) announcedKeys.Add(key);
            if (name.Length > 0 && t.Name != name) { t.Name = name; changed = true; }
            if (t.ModName != modName) { t.ModName = modName; changed = true; }
            t.Installed = true;
            return changed;
        }

        /// <summary>Methods called (call/callvirt) in a method body.</summary>
        private static IEnumerable<MethodBase> CalledMethods(MethodBase m)
        {
            byte[] il;
            try { il = m.GetMethodBody()?.GetILAsByteArray(); }
            catch { yield break; }
            if (il == null) yield break;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue; // call / callvirt
                int token = BitConverter.ToInt32(il, i + 1);
                int table = token >> 24;
                if (table != 0x06 && table != 0x0A && table != 0x2B) continue; // MethodDef / MemberRef / MethodSpec
                MethodBase target = null;
                try { target = m.Module.ResolveMethod(token); } catch { }
                if (target != null) { yield return target; i += 4; }
            }
        }

        /// <summary>Localisation keys of job names in a mod (const strings containing "/job/").</summary>
        private static List<string> FindJobNameKeys(Assembly asm)
        {
            var keys = new List<string>();
            foreach (var type in SafeTypes(asm))
            {
                FieldInfo[] fields;
                try { fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch { continue; }
                foreach (var f in fields)
                {
                    if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                    string v = null;
                    try { v = f.GetRawConstantValue() as string; } catch { }
                    if (v != null && v.IndexOf("/job/", StringComparison.OrdinalIgnoreCase) >= 0 && !keys.Contains(v))
                        keys.Add(v);
                }
            }
            return keys;
        }

        private static bool IsAbbrev(string s)
        {
            if (s == null || s.Length < 2 || s.Length > 3) return false;
            foreach (char c in s) if (c < 'A' || c > 'Z') return false;
            return true;
        }

        /// <summary>String literals (ldstr) in a method body.</summary>
        private static IEnumerable<string> StringLiterals(MethodBase m)
        {
            byte[] il;
            try { il = m.GetMethodBody()?.GetILAsByteArray(); }
            catch { yield break; }
            if (il == null) yield break;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x72) continue; // ldstr
                int token = BitConverter.ToInt32(il, i + 1);
                if ((token >> 24) != 0x70) continue; // string token
                string s = null;
                try { s = m.Module.ResolveString(token); } catch { }
                if (s != null) { yield return s; i += 4; }
            }
        }

        private static bool abbrevsLearned;

        /// <summary>
        /// Once per session after the world is loaded: asks the game's job ID generator for an ID of each
        /// mod job type (mods patch IdGenerator.GenerateJobID to create their own prefix, e.g. "PE-07"),
        /// reads the abbreviation from it and releases the ID again. Stored permanently in the settings.
        /// </summary>
        public static void LearnAbbreviations()
        {
            if (abbrevsLearned || Main.Settings == null) return;
            DetectFromMods();
            var gen = IdGenerator.Instance;
            if (gen == null) return;
            abbrevsLearned = true;

            bool changed = false;
            foreach (var t in known.Values)
            {
                if (!t.Installed || !int.TryParse(t.Number, out int value)) continue;
                string id = null;
                try
                {
                    id = gen.GenerateJobID((JobType)value, null);
                }
                catch (Exception ex)
                {
                    if (Main.Settings.DebugLogging) Main.Log($"Zg2: no test ID for job type {t.Name}: {ex.Message}");
                }
                finally
                {
                    if (id != null) { try { gen.existingJobIds.Remove(id); } catch { } }
                }

                string ab = AbbrevOfId(id);
                if (!string.IsNullOrEmpty(ab) && ab != t.Abbrev)
                {
                    t.Abbrev = ab;
                    changed = true;
                    Main.Log($"Zg2: job type '{t.Name}' uses job IDs like {id}.");
                }
            }
            if (changed) StoreKnown();
        }

        /// <summary>"GF-CH-12" → "CH", "PE-07" → "PE".</summary>
        private static string AbbrevOfId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var p = id.Split('-');
            if (p.Length >= 3) return p[1];
            if (p.Length == 2 && !int.TryParse(p[0], out _)) return p[0];
            return string.Empty;
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Enumerable.Empty<Type>(); }
        }

        private static string Humanize(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != '_') sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>Settings key for a mod job type: the internal number (stable, never shown).</summary>
        public static string ModKey(string number, string abbrev) => number;

        public static bool IsModTypeEnabled(string number, string abbrev) => IsCovered(number, abbrev);

        public static void SetModType(string number, string abbrev, bool enabled)
        {
            if (enabled)
            {
                Set(ModKey(number, abbrev), true);
            }
            else
            {
                Set(number, false);
                if (!string.IsNullOrEmpty(abbrev)) Set(abbrev, false);
            }
        }

        private static List<VanillaType> BuildVanilla()
        {
            var list = new List<VanillaType>();
            Add(list, new[] { "Transport", "FreightHaul" }, ZG2JobTypeNames.FreightHaul, "FH");
            Add(list, new[] { "EmptyHaul", "LogisticalHaul" }, ZG2JobTypeNames.LogisticalHaul, "LH");
            Add(list, new[] { "ShuntingLoad" }, ZG2JobTypeNames.ShuntingLoad, "SL");
            Add(list, new[] { "ShuntingUnload" }, ZG2JobTypeNames.ShuntingUnload, "SU");
            return list;
        }

        private static void Add(List<VanillaType> list, string[] names, int nameIndex, string abbrev)
        {
            foreach (var n in names)
            {
                if (!Enum.IsDefined(typeof(JobType), n)) continue;
                list.Add(new VanillaType { Key = n, NameIndex = nameIndex, Abbrev = abbrev });
                return;
            }
        }

        /// <summary>Stable key of a job type: enum name if defined, otherwise its number.</summary>
        public static string KeyOf(JobType type)
        {
            return Enum.IsDefined(typeof(JobType), type) ? type.ToString() : ((int)type).ToString();
        }

        /// <summary>Abbreviation from the job ID ("GF-CH-98" → "CH"), or empty.</summary>
        public static string AbbrevOf(Job job)
        {
            var id = job?.ID;
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var parts = id.Split('-');
            return parts.Length >= 3 ? parts[1] : string.Empty;
        }

        public static void RecordSeen(Job job)
        {
            if (job == null) return;
            RecordSeenId(job.jobType, job.ID);
        }

        /// <summary>
        /// Learns job types / variants from a job ID. Called for every generated job ID
        /// (postfix on IdGenerator.GenerateJobID, after all other mods) and for taken jobs.
        /// </summary>
        public static void RecordSeenId(JobType type, string id)
        {
            string key = KeyOf(type);
            string abbrev = AbbrevOfId(id);
            if (!SeenTypes.TryGetValue(key, out var seen) || string.IsNullOrEmpty(seen))
                SeenTypes[key] = abbrev;

            if (Main.Settings == null || abbrev.Length == 0) return;
            if (ProviderCode(id) != null) return; // the mod's own API already knows this job kind
            LoadKnown();

            // Vanilla type with a foreign job ID code (another mod renamed the ID) → variant
            if (Enum.IsDefined(typeof(JobType), type))
            {
                if (Vanilla.Exists(v => v.Abbrev == abbrev)) return;
                string vk = VariantKey(abbrev);
                if (!known.TryGetValue(vk, out var variant))
                {
                    variant = new ModType { Number = vk, Abbrev = abbrev, Name = string.Empty, ModName = IdPatchingModName(), Installed = true };
                    known[vk] = variant;
                    StoreKnown();
                    Main.Log($"Zg2: job variant {abbrev} detected from job ID {id}.");
                }
                variant.Installed = true;
                return;
            }

            // Job types of other mods (undefined enum values): learn the abbreviation
            if (!known.TryGetValue(key, out var t))
            {
                t = new ModType { Number = key, Abbrev = abbrev, Name = string.Empty, ModName = IdPatchingModName(), Installed = true };
                known[key] = t;
                StoreKnown();
                return;
            }
            t.Installed = true;
            if (t.Abbrev != abbrev)
            {
                t.Abbrev = abbrev;
                StoreKnown();
            }
        }

        private static string idPatchingModName;

        /// <summary>Name of the (single) other mod that patches the job ID generator, or empty.</summary>
        private static string IdPatchingModName()
        {
            if (idPatchingModName != null) return idPatchingModName;
            idPatchingModName = string.Empty;
            try
            {
                var original = HarmonyLib.AccessTools.Method(typeof(IdGenerator), nameof(IdGenerator.GenerateJobID));
                var info = original != null ? HarmonyLib.Harmony.GetPatchInfo(original) : null;
                if (info == null) return idPatchingModName;
                var owners = new HashSet<string>(info.Owners);
                owners.Remove(Main.ModEntry.Info.Id);
                if (owners.Count != 1) return idPatchingModName;
                string owner = owners.First();
                foreach (var entry in UnityModManagerNet.UnityModManager.modEntries)
                {
                    if (entry?.Info == null || !string.Equals(entry.Info.Id, owner, StringComparison.OrdinalIgnoreCase)) continue;
                    string n = entry.Info.DisplayName ?? entry.Info.Id;
                    idPatchingModName = n.IndexOf(' ') < 0 ? Humanize(n) : n;
                    break;
                }
                if (idPatchingModName.Length == 0) idPatchingModName = owner.IndexOf(' ') < 0 ? Humanize(owner) : owner;
            }
            catch { }
            return idPatchingModName;
        }

        public static bool IsVanillaKey(string key)
            => Vanilla.Exists(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));

        public static bool IsTracked(Job job)
        {
            if (job == null) return false;
            var keys = GetKeys();

            // Job kind according to the other mod's own API (e.g. TrainServiceRework: SH, MH, SO, MS, SS)
            string kind = ProviderCode(job.ID);
            if (!string.IsNullOrEmpty(kind)) return keys.Contains(VariantKey(kind));

            // A known variant (own job ID code on a vanilla type) is decided by its own switch only
            string jobAbbrev = AbbrevOf(job);
            if (jobAbbrev.Length > 0)
            {
                LoadKnown();
                if (known.ContainsKey(VariantKey(jobAbbrev))) return keys.Contains(VariantKey(jobAbbrev));
            }

            if (keys.Count == 0) return false;
            if (keys.Contains(KeyOf(job.jobType)) || keys.Contains(((int)job.jobType).ToString())) return true;

            // Abbreviation of a mod job type (e.g. "CH"). Not used for vanilla types, so unticking
            // "Freight Haul" cannot be bypassed by an "FH" entry by accident.
            if (Enum.IsDefined(typeof(JobType), job.jobType)) return false;
            string abbrev = AbbrevOf(job);
            return abbrev.Length > 0 && keys.Contains(abbrev);
        }

        /// <summary>Readable description of a configured key, e.g. "CH (121)" or "121 (CH)".</summary>
        public static string Describe(string key)
        {
            LoadKnown();
            foreach (var t in known.Values)
            {
                if (string.Equals(t.Number, key, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(t.Abbrev) && string.Equals(t.Abbrev, key, StringComparison.OrdinalIgnoreCase)))
                    return t.Label;
            }
            foreach (var kv in SeenTypes)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return $"{kv.Value} ({kv.Key})";
                if (string.Equals(kv.Value, key, StringComparison.OrdinalIgnoreCase)) return $"{kv.Value} ({kv.Key})";
            }
            return key;
        }

        /// <summary>True if a seen job type is already covered by a configured key (number or abbreviation).</summary>
        public static bool IsCovered(string typeKey, string abbrev)
        {
            var keys = GetKeys();
            return keys.Contains(typeKey) || (!string.IsNullOrEmpty(abbrev) && keys.Contains(abbrev));
        }

        public static HashSet<string> GetKeys()
        {
            string src = Main.Settings?.JobTypes ?? string.Empty;
            if (src != cachedSource)
            {
                cachedSource = src;
                cachedKeys.Clear();
                foreach (var part in Split(src))
                    cachedKeys.Add(part);
            }
            return cachedKeys;
        }

        public static bool Contains(string key) => GetKeys().Contains(key);

        public static void Set(string key, bool enabled)
        {
            var list = new List<string>(Split(Main.Settings.JobTypes));
            int idx = list.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (enabled && idx < 0) list.Add(key);
            else if (!enabled && idx >= 0) list.RemoveAt(idx);
            Main.Settings.JobTypes = string.Join(";", list);
        }

        public static IEnumerable<string> Split(string src)
        {
            if (string.IsNullOrEmpty(src)) yield break;
            foreach (var raw in src.Split(new[] { ';', ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = raw.Trim();
                if (t.Length > 0) yield return t;
            }
        }
    }
}