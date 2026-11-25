using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the separate texture bundle "zg2textures" from all images in Assets/CustomTextures.
/// File names: name_d (colour, required), name_n (normal), name_s (metallic/smoothness),
/// name_e (retroreflective mask). Sets starting with "new" are for new boards.
/// Menu: Assets → ZG2 → Build Texture Bundle. Output: Assets/AssetBundles/zg2textures
/// (copy it next to ZG2.dll – package.ps1 does that automatically).
/// </summary>
public static class BuildTextureBundle
{
    private const string SourceDir = "Assets/CustomTextures";
    private const string OutDir = "Assets/AssetBundles";
    private const string BundleName = "zg2textures";

    [MenuItem("Assets/ZG2/Build Texture Bundle")]
    public static void Build()
    {
        if (!Directory.Exists(SourceDir))
        {
            Debug.LogError("ZG2: folder " + SourceDir + " not found.");
            return;
        }

        var assets = new List<string>();
        foreach (var file in Directory.GetFiles(SourceDir))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".png" && ext != ".tga" && ext != ".jpg" && ext != ".jpeg" && ext != ".psd") continue;

            string path = file.Replace('\\', '/');
            string stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool changed = false;
            if (stem.EndsWith("_n") && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
            if ((stem.EndsWith("_s") || stem.EndsWith("_e")) && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }
            if (importer.maxTextureSize < 4096) { importer.maxTextureSize = 4096; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (changed) importer.SaveAndReimport();

            assets.Add(path);
        }

        if (assets.Count == 0)
        {
            Debug.LogError("ZG2: no textures in " + SourceDir);
            return;
        }

        if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);

        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() };
        BuildPipeline.BuildAssetBundles(OutDir, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

        Debug.Log("ZG2: built " + OutDir + "/" + BundleName + " with " + assets.Count + " texture(s).");
    }
}
