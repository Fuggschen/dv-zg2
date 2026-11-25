using System;
using System.Collections.Generic;
using System.Reflection;

namespace ZG2
{
    /// <summary>
    /// Localisation helper. Reads the game language from I2 Localization via reflection,
    /// so no compile-time reference to I2.Localization is needed.
    /// </summary>
    internal static class ZG2Loc
    {
        private static PropertyInfo currentLanguageProp, currentCodeProp;
        private static bool resolved;

        private static string cachedSource;
        private static string cachedCode = "en";

        private static readonly Dictionary<string, string> nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = "en", ["Bulgarian"] = "bg", ["Czech"] = "cs", ["Danish"] = "da", ["Dutch"] = "nl",
            ["Finnish"] = "fi", ["French"] = "fr", ["German"] = "de", ["Hindi"] = "hi", ["Hungarian"] = "hu",
            ["Italian"] = "it", ["Japanese"] = "ja", ["Korean"] = "ko", ["Norwegian"] = "no", ["Polish"] = "pl",
            ["Romanian"] = "ro", ["Russian"] = "ru", ["Slovak"] = "sk", ["Spanish"] = "es", ["Swedish"] = "sv",
            ["Turkish"] = "tr", ["Ukrainian"] = "uk"
        };

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("I2.Loc.LocalizationManager", false);
                if (t == null) continue;
                const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                currentLanguageProp = t.GetProperty("CurrentLanguage", f);
                currentCodeProp = t.GetProperty("CurrentLanguageCode", f);
                if (currentLanguageProp != null || currentCodeProp != null) break;
            }
        }

        private static string LanguageName()
        {
            try { Resolve(); return currentLanguageProp?.GetValue(null, null) as string ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>Normalised language code: en, de, …, zh-cn, zh-tw, pt, pt-br, no.</summary>
        public static string LanguageCode
        {
            get
            {
                string name = LanguageName();
                string code = string.Empty;
                try { code = currentCodeProp?.GetValue(null, null) as string ?? string.Empty; } catch { }

                string source = name + "|" + code;
                if (source == cachedSource) return cachedCode;
                cachedSource = source;
                cachedCode = Normalise(name, code);
                return cachedCode;
            }
        }

        private static string Normalise(string name, string code)
        {
            string n = name.ToLowerInvariant();
            string c = code.ToLowerInvariant().Replace('_', '-');

            if (n.Contains("chinese") || c.StartsWith("zh"))
                return n.Contains("tradition") || c.Contains("tw") || c.Contains("hk") || c.Contains("hant") ? "zh-tw" : "zh-cn";
            if (n.Contains("portug") || c.StartsWith("pt"))
                return n.Contains("brazil") || n.Contains("brasil") || c.Contains("br") ? "pt-br" : "pt";
            if (c == "nb" || c == "nn") return "no";

            foreach (var kv in nameToCode)
                if (n.StartsWith(kv.Key.ToLowerInvariant())) return kv.Value;

            if (c.Length >= 2) return c.Substring(0, 2);
            return "en";
        }

        public static bool German => LanguageCode == "de";

        public static string T(string de, string en) => German ? de : en;
    }
}
