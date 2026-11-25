using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Additional board textures loaded from disk – no Unity / asset bundle rebuild needed.
    ///
    /// Sources (in this order, first set name wins):
    ///   • texture bundle "zg2textures" next to ZG2.dll (built in Unity: Assets → ZG2 → Build Texture Bundle)
    ///   • texture bundle "zg2textures" in any Mods\&lt;folder&gt;\ZG2Textures\ (texture packs)
    ///   • loose image files in Mods\ZG2\Textures\ and Mods\&lt;folder&gt;\ZG2Textures\
    ///
    /// A texture set is identified by its colour map "&lt;name&gt;_d.png|.jpg|.tga". Optional maps of
    /// the same set: "&lt;name&gt;_n" (normal), "&lt;name&gt;_s" (metallic/smoothness), "&lt;name&gt;_e"
    /// (retroreflective mask). Missing maps are taken from the original worn board.
    /// Sets whose name starts with "new" are new boards, all others are used boards.
    /// The UV layout must match the original textures.
    /// </summary>
    public static class ZG2CustomTextures
    {
        public const string PackFolder = "ZG2Textures";
        public const string BundleFile = "zg2textures";

        private static readonly List<AssetBundle> bundles = new List<AssetBundle>();

        private class TexSet
        {
            public string Name;
            public bool IsNew;
            public string Dir;
            public string ColorFile;
            public Texture2D Color, Normal, Spec, Retro;
            public bool Loaded, Failed, FromBundle;
            public Material Small, Large;
            public Material[] SmallArr, LargeArr;
        }

        private static readonly Dictionary<string, TexSet> sets = new Dictionary<string, TexSet>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> usedNames = new List<string>();
        private static readonly List<string> newNames = new List<string>();
        private static bool scanned;

        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".tga" };

        public static int UsedCount { get { Scan(); return usedNames.Count; } }
        public static int NewCount { get { Scan(); return newNames.Count; } }

        /// <summary>Random texture set for a board: "" = original texture, otherwise a custom set name.</summary>
        public static string PickRandom(bool newBoard)
        {
            if (!Main.Settings.UseCustomTextures) return string.Empty;
            Scan();
            var pool = newBoard ? newNames : usedNames;
            if (pool.Count == 0) return string.Empty;
            // The original texture stays one of the options.
            int i = UnityEngine.Random.Range(0, pool.Count + 1);
            return i == pool.Count ? string.Empty : pool[i];
        }

        /// <summary>Material array for a set, or null (unknown set, disabled, failed to load).</summary>
        public static Material[] GetMaterials(string setName, bool large)
        {
            if (string.IsNullOrEmpty(setName) || !Main.Settings.UseCustomTextures) return null;
            Scan();
            if (!sets.TryGetValue(setName, out var set) || set.Failed) return null;
            if (!set.Loaded) Load(set);
            if (set.Failed) return null;

            if (large)
            {
                if (set.LargeArr == null) { set.Large = BuildMaterial(set, true); set.LargeArr = set.Large != null ? new[] { set.Large } : null; }
                return set.LargeArr;
            }
            if (set.SmallArr == null) { set.Small = BuildMaterial(set, false); set.SmallArr = set.Small != null ? new[] { set.Small } : null; }
            return set.SmallArr;
        }

        public static void Shutdown()
        {
            foreach (var s in sets.Values)
            {
                Destroy(s.Small); Destroy(s.Large);
                if (!s.FromBundle)
                {
                    Destroy(s.Color); Destroy(s.Normal); Destroy(s.Spec); Destroy(s.Retro);
                }
            }
            foreach (var b in bundles)
                if (b != null) b.Unload(true);
            bundles.Clear();
            sets.Clear();
            usedNames.Clear();
            newNames.Clear();
            scanned = false;
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o != null) UnityEngine.Object.Destroy(o);
        }

        #region Scan / load

        private static void Scan()
        {
            if (scanned) return;
            scanned = true;

            try
            {
                var dirs = new List<string>();
                string modDir = Path.GetFullPath(Main.ModEntry.Path);
                var packDirs = new List<string>();

                var modsRoot = new DirectoryInfo(modDir.TrimEnd('\\', '/')).Parent;
                if (modsRoot != null && modsRoot.Exists)
                {
                    foreach (var d in modsRoot.GetDirectories())
                        packDirs.Add(Path.Combine(d.FullName, PackFolder));
                }

                // 1) Asset bundles
                LoadBundle(Path.Combine(modDir, BundleFile));
                foreach (var pd in packDirs) LoadBundle(Path.Combine(pd, BundleFile));

                // 2) Loose files
                dirs.Add(Path.Combine(modDir, "Textures"));
                dirs.AddRange(packDirs);

                foreach (var dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (Array.IndexOf(Extensions, ext) < 0) continue;
                        string stem = Path.GetFileNameWithoutExtension(file);
                        if (!stem.EndsWith("_d", StringComparison.OrdinalIgnoreCase)) continue;

                        string name = stem.Substring(0, stem.Length - 2);
                        if (sets.ContainsKey(name))
                        {
                            Main.LogWarning($"Zg2 textures: set '{name}' exists twice – ignoring {file}");
                            continue;
                        }

                        sets[name] = new TexSet
                        {
                            Name = name,
                            IsNew = name.StartsWith("new", StringComparison.OrdinalIgnoreCase),
                            Dir = dir,
                            ColorFile = file
                        };
                        (sets[name].IsNew ? newNames : usedNames).Add(name);
                    }
                }

                if (sets.Count > 0)
                    Main.Log($"Zg2 textures: {usedNames.Count} used / {newNames.Count} new texture set(s) found.");
            }
            catch (Exception ex)
            {
                Main.LogError($"Zg2 textures: scan failed: {ex.Message}");
            }
        }

        private static void LoadBundle(string file)
        {
            if (!File.Exists(file)) return;
            try
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) { Main.LogError($"Zg2 textures: could not load bundle {file}"); return; }
                bundles.Add(bundle);

                var byName = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
                foreach (var tex in bundle.LoadAllAssets<Texture2D>())
                    if (tex != null) byName[tex.name] = tex;

                int added = 0;
                foreach (var kv in byName)
                {
                    if (!kv.Key.EndsWith("_d", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = kv.Key.Substring(0, kv.Key.Length - 2);
                    if (sets.ContainsKey(name)) continue;

                    byName.TryGetValue(name + "_n", out var n);
                    byName.TryGetValue(name + "_s", out var sp);
                    byName.TryGetValue(name + "_e", out var e);
                    var set = new TexSet
                    {
                        Name = name,
                        IsNew = name.StartsWith("new", StringComparison.OrdinalIgnoreCase),
                        Color = kv.Value, Normal = n, Spec = sp, Retro = e,
                        Loaded = true, FromBundle = true
                    };
                    sets[name] = set;
                    (set.IsNew ? newNames : usedNames).Add(name);
                    added++;
                }
                Main.Log($"Zg2 textures: bundle {file} – {added} set(s).");
            }
            catch (Exception ex)
            {
                Main.LogError($"Zg2 textures: bundle {file} failed: {ex.Message}");
            }
        }

        private static void Load(TexSet set)
        {
            set.Loaded = true;
            try
            {
                set.Color = LoadTexture(set.ColorFile, linear: false);
                if (set.Color == null) { set.Failed = true; return; }
                set.Normal = LoadOptional(set, "_n", linear: true);
                set.Spec = LoadOptional(set, "_s", linear: true);
                set.Retro = LoadOptional(set, "_e", linear: false);
                Main.Log($"Zg2 textures: loaded '{set.Name}' ({set.Color.width}x{set.Color.height}).");
            }
            catch (Exception ex)
            {
                set.Failed = true;
                Main.LogError($"Zg2 textures: failed to load '{set.Name}': {ex.Message}");
            }
        }

        private static Texture2D LoadOptional(TexSet set, string suffix, bool linear)
        {
            foreach (var ext in Extensions)
            {
                string f = Path.Combine(set.Dir, set.Name + suffix + ext);
                if (File.Exists(f)) return LoadTexture(f, linear);
            }
            return null;
        }

        private static Texture2D LoadTexture(string file, bool linear)
        {
            Texture2D tex;
            if (file.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                tex = TgaLoader.Load(File.ReadAllBytes(file), linear);
                if (tex == null) { Main.LogError($"Zg2 textures: unsupported TGA format: {file}"); return null; }
            }
            else
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!tex.LoadImage(File.ReadAllBytes(file), false))
                {
                    Main.LogError($"Zg2 textures: could not read {file}");
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
            }

            tex.name = Path.GetFileNameWithoutExtension(file);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 4;
            // Compress on the GPU side and drop the CPU copy (saves ~3/4 of the memory).
            tex.Apply(true, false); // build mip maps while still readable
            if (tex.width % 4 == 0 && tex.height % 4 == 0) tex.Compress(true);
            tex.Apply(false, true);
            return tex;
        }

        private static Material BuildMaterial(TexSet set, bool large)
        {
            var baseMats = ZG2BoardFactory.GetWornMaterials(large);
            if (baseMats == null || baseMats.Length == 0 || baseMats[0] == null) return null;

            var m = new Material(baseMats[0]) { name = $"ZG2_{set.Name}_{(large ? "L" : "S")}" };
            SetTex(m, "_MainTex", set.Color);
            if (set.Normal != null) SetTex(m, "_BumpMap", set.Normal);
            if (set.Spec != null) { SetTex(m, "_MetallicGlossMap", set.Spec); SetTex(m, "_OcclusionMap", set.Spec); }
            if (set.Retro != null) SetTex(m, "_RetroreflectiveMask", set.Retro);
            return m;
        }

        private static void SetTex(Material m, string prop, Texture tex)
        {
            if (tex != null && m.HasProperty(prop)) m.SetTexture(prop, tex);
        }

        #endregion

        /// <summary>Minimal TGA reader: uncompressed and RLE true-colour, 24/32 bit.</summary>
        private static class TgaLoader
        {
            public static Texture2D Load(byte[] d, bool linear)
            {
                if (d == null || d.Length < 18) return null;
                int idLen = d[0];
                int colorMapType = d[1];
                int type = d[2];
                int width = d[12] | (d[13] << 8);
                int height = d[14] | (d[15] << 8);
                int bpp = d[16];
                int desc = d[17];
                if (colorMapType != 0 || (type != 2 && type != 10) || (bpp != 24 && bpp != 32) || width <= 0 || height <= 0)
                    return null;

                int bytesPP = bpp / 8;
                int pos = 18 + idLen;
                var pixels = new Color32[width * height];
                int count = width * height;
                int i = 0;

                if (type == 2)
                {
                    if (pos + count * bytesPP > d.Length) return null;
                    for (; i < count; i++, pos += bytesPP)
                        pixels[i] = Px(d, pos, bytesPP);
                }
                else
                {
                    while (i < count && pos < d.Length)
                    {
                        int header = d[pos++];
                        int n = (header & 0x7F) + 1;
                        if ((header & 0x80) != 0)
                        {
                            if (pos + bytesPP > d.Length) return null;
                            var c = Px(d, pos, bytesPP);
                            pos += bytesPP;
                            for (int k = 0; k < n && i < count; k++) pixels[i++] = c;
                        }
                        else
                        {
                            if (pos + n * bytesPP > d.Length) return null;
                            for (int k = 0; k < n && i < count; k++, pos += bytesPP) pixels[i++] = Px(d, pos, bytesPP);
                        }
                    }
                }

                // Unity expects bottom-up rows; TGA bit 5 = top-down origin.
                if ((desc & 0x20) != 0)
                {
                    var row = new Color32[width];
                    for (int y = 0; y < height / 2; y++)
                    {
                        int a = y * width, b = (height - 1 - y) * width;
                        Array.Copy(pixels, a, row, 0, width);
                        Array.Copy(pixels, b, pixels, a, width);
                        Array.Copy(row, 0, pixels, b, width);
                    }
                }

                var tex = new Texture2D(width, height, TextureFormat.RGBA32, true, linear);
                tex.SetPixels32(pixels);
                return tex;
            }

            private static Color32 Px(byte[] d, int p, int bytesPP)
                => new Color32(d[p + 2], d[p + 1], d[p], bytesPP == 4 ? d[p + 3] : (byte)255);
        }
    }
}
