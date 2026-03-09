using System;
using System.Collections.Generic;
using System.IO;
using custom_item_mod;
using DV.Customization;
using DV.Customization.Gadgets;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Loads the ZG2 board AssetBundle and registers items with custom_item_mod's
    /// <see cref="CustomItem"/> framework. The framework handles component setup
    /// (InventoryItemSpec, ItemSaveData, Item, shop integration, save/load intercept).
    /// We add our own <see cref="ZG2Board"/> component and gadget setup on top.
    /// </summary>
    public static class ZG2BoardFactory
    {
        // Internal identifiers used in CustomItemInfo.Name → drives itemPrefabName
        public const string PREFAB_NAME_OLD = "ZG2_Small";
        public const string PREFAB_NAME_MODERN = "ZG2_Large";
        public const string PREFAB_NAME_OLD_WORN = "ZG2_Small_old";
        public const string PREFAB_NAME_MODERN_WORN = "ZG2_Large_old";
        private const string ASSET_BUNDLE_NAME = "zg2boards";

        /// <summary>All registered internal prefab names.</summary>
        public static readonly string[] AllPrefabNames =
        {
            PREFAB_NAME_OLD, PREFAB_NAME_MODERN,
            PREFAB_NAME_OLD_WORN, PREFAB_NAME_MODERN_WORN
        };

        private static AssetBundle loadedBundle;
        private static GameObject gadgetPrefabRoot; // hidden root for standalone gadget templates
        private static readonly Dictionary<string, CustomItem> customItems = new Dictionary<string, CustomItem>();

        public static bool IsInitialised => customItems.Count > 0;

        /// <summary>
        /// Loads the AssetBundle and creates <see cref="CustomItem"/> instances
        /// registered with <see cref="ItemModsFinder.CustomItems"/>.
        /// Called from <see cref="ShopInitPatch"/> during <c>GlobalShopController.Awake</c>.
        /// </summary>
        public static void Initialise()
        {
            if (IsInitialised) return;

            string bundlePath = Path.Combine(Main.ModEntry.Path, ASSET_BUNDLE_NAME);
            if (!File.Exists(bundlePath))
            {
                Main.LogError($"AssetBundle not found at: {bundlePath}");
                return;
            }

            loadedBundle = AssetBundle.LoadFromFile(bundlePath);
            if (loadedBundle == null)
            {
                Main.LogError("Failed to load AssetBundle.");
                return;
            }

            // Hidden root to keep standalone gadget prefab templates alive
            gadgetPrefabRoot = new GameObject("[ZG2 Gadget Prefabs]");
            UnityEngine.Object.DontDestroyOnLoad(gadgetPrefabRoot);
            gadgetPrefabRoot.SetActive(false);

            // Load icon sprites from the asset bundle
            var iconSmallNew   = loadedBundle.LoadAsset<Sprite>("ZUG_S_New_Icon");
            var iconSmallOld   = loadedBundle.LoadAsset<Sprite>("ZUG_S_Old_Icon");
            var iconSmallDrop  = loadedBundle.LoadAsset<Sprite>("ZUG_S_Blue_Icon");
            var iconLargeNew   = loadedBundle.LoadAsset<Sprite>("ZUG_L_New_Icon");
            var iconLargeOld   = loadedBundle.LoadAsset<Sprite>("ZUG_L_Old_Icon");
            var iconLargeDrop  = loadedBundle.LoadAsset<Sprite>("ZUG_L_Blue_Icon");

            // New/clean variants — buyable in shops
            RegisterItem(PREFAB_NAME_OLD, ZG2Board.BoardVariant.Old,
                "Zugschlusstafel (Small)", "Small train end board (Zugschlusstafel).",
                shopAmount: 10, iconStandard: iconSmallNew, iconDropped: iconSmallDrop);
            RegisterItem(PREFAB_NAME_MODERN, ZG2Board.BoardVariant.Modern,
                "Zugschlusstafel (Large)", "Large train end board (Zugschlusstafel).",
                shopAmount: 10, iconStandard: iconLargeNew, iconDropped: iconLargeDrop);

            // Worn/weathered variants — station spawns only (Amount=0 keeps save/load but no shop stock)
            RegisterItem(PREFAB_NAME_OLD_WORN, ZG2Board.BoardVariant.OldWorn,
                "Zugschlusstafel (Small, worn)", "Worn small train end board (Zugschlusstafel).",
                shopAmount: 0, iconStandard: iconSmallOld, iconDropped: iconSmallDrop);
            RegisterItem(PREFAB_NAME_MODERN_WORN, ZG2Board.BoardVariant.ModernWorn,
                "Zugschlusstafel (Large, worn)", "Worn large train end board (Zugschlusstafel).",
                shopAmount: 0, iconStandard: iconLargeOld, iconDropped: iconLargeDrop);

            Main.Log($"ZG2BoardFactory: {customItems.Count} items registered with custom_item_mod.");
        }

        /// <summary>
        /// Returns the item prefab for cloning (e.g. station spawning).
        /// This is the child item inside custom_item_mod's hidden root.
        /// </summary>
        public static GameObject GetPrefab(string prefabName)
        {
            return customItems.TryGetValue(prefabName, out var ci) ? ci.ItemPrefab : null;
        }

        /// <summary>
        /// Returns the <c>itemPrefabName</c> assigned by custom_item_mod for a given board variant.
        /// Format: <c>"custom_item_mod/{name}/prefab"</c>.
        /// </summary>
        public static string GetItemPrefabName(string internalName)
        {
            if (customItems.TryGetValue(internalName, out var ci))
                return ci.ItemSpec.itemPrefabName;
            return internalName; // fallback
        }

        public static void Shutdown()
        {
            customItems.Clear();

            if (gadgetPrefabRoot != null)
            {
                UnityEngine.Object.Destroy(gadgetPrefabRoot);
                gadgetPrefabRoot = null;
            }

            if (loadedBundle != null)
            {
                loadedBundle.Unload(true);
                loadedBundle = null;
            }
        }

        /// <summary>
        /// Creates a <see cref="CustomItem"/> for a single board variant,
        /// adds our <see cref="ZG2Board"/> component and gadget setup,
        /// then injects into <see cref="ItemModsFinder.CustomItems"/>.
        /// </summary>
        private static void RegisterItem(
            string prefabName,
            ZG2Board.BoardVariant variant,
            string displayName,
            string description,
            int shopAmount,
            Sprite iconStandard,
            Sprite iconDropped)
        {
            var source = loadedBundle.LoadAsset<GameObject>(prefabName);
            if (source == null)
            {
                Main.LogError($"AssetBundle does not contain prefab '{prefabName}'.");
                return;
            }

            // Fall back to placeholder if sprite failed to load from bundle
            iconStandard = iconStandard ?? CreatePlaceholderSprite(new Color(0.8f, 0.2f, 0.2f, 1f));
            iconDropped = iconDropped ?? CreatePlaceholderSprite(new Color(0.45f, 0.63f, 0.70f, 1f));

            var itemInfo = new CustomItemInfo
            {
                Name = prefabName,
                Description = description,
                Amount = shopAmount,
                Price = 500,
                ShelfRotation = new Vector3(90f, 180f, 0f),
            };

            // CustomItem constructor handles: CreateItemPrefab (layer, Item, ItemSaveData,
            // ShopRestocker, TrainItemActivityHandlerOverride), SetupItemSpec (InventoryItemSpec,
            // icons, prefabName), shelf sample, ShopItemData.
            // BuildGadgetIfPresent will skip since our Unity prefabs don't have
            // custom_item_components stubs — we do gadget setup below.
            var customItem = new CustomItem(
                itemInfo,
                source,
                iconStandard,
                iconDropped
            );

            // Override display name (CustomItemInfo.Name is used as internal key;
            // custom_item_mod's LocalizationManager patch returns item.Name for display)
            customItem.Name = displayName;
            customItem.Description = description;

            // Nudge the shelf sample back (Z+) so it sits against the shelf surface
            AdjustShelfSamplePosition(customItem);

            // ── Add our custom ZG2Board component ──
            var board = customItem.ItemPrefab.AddComponent<ZG2Board>();
            board.SetVariant(variant);

            // ── Add retroreflective emission driver ──
            customItem.ItemPrefab.AddComponent<ZG2Retroreflective>();

            // ── Set up gadget components (custom_item_mod skipped this since no stubs) ──
            // For large boards, shift the pivot toward the bottom so they can mount higher.
            bool isLarge = prefabName == PREFAB_NAME_MODERN || prefabName == PREFAB_NAME_MODERN_WORN;
            SetupGadgetComponents(customItem.ItemPrefab, prefabName, pivotOffsetUp: isLarge ? 0.2f : 0f);

            // ── Inject into custom_item_mod's item list for shop + save/load integration ──
            ItemModsFinder.CustomItems.Add(customItem);

            customItems[prefabName] = customItem;
            Main.Log($"  Item '{prefabName}' ({displayName}) registered.");
        }

        /// <summary>
        /// Adjusts the shelf display sample's local position so the board
        /// sits against the shelf back wall instead of floating in front.
        /// The sample is the first child of <c>ShopData.shelfItem</c>.
        /// </summary>
        private static void AdjustShelfSamplePosition(CustomItem customItem)
        {
            var shelfTransform = customItem.ShopData.shelfItem.transform;
            if (shelfTransform.childCount == 0) return;

            // The shelf sample is the first (and usually only) child
            var sample = shelfTransform.GetChild(0);
            var pos = sample.localPosition;
            pos.z -= 0.2f;
            sample.localPosition = pos;
        }

        /// <summary>
        /// Sets up gadget system components for board attachment to trains.
        /// Creates a STANDALONE gadget prefab (outside the item hierarchy) and
        /// links to it via GadgetItem on the item root.
        /// 
        /// The gadget template must live outside the item so that when items are
        /// cloned via Object.Instantiate, gadgetPrefab remains an external reference.
        /// This prevents GadgetItem.Awake from marking the prefab as "already assigned"
        /// and allows the game to instantiate fresh GadgetBase copies when placing.
        /// </summary>
        /// <param name="pivotOffsetUp">
        /// Shifts the visual upward in gadget space (Y+ = up along surface),
        /// effectively moving the pivot/attachment point toward the bottom of the board.
        /// </param>
        private static void SetupGadgetComponents(GameObject itemPrefab, string prefabName, float pivotOffsetUp = 0f)
        {
            // ── Create gadget template as a standalone object under our hidden root ──
            var gadgetTemplate = new GameObject($"{prefabName}_Gadget");
            gadgetTemplate.transform.SetParent(gadgetPrefabRoot.transform, false);

            SetLayerRecursive(gadgetTemplate, LayerMask.NameToLayer("Interactable"));

            var gadgetBase = gadgetTemplate.AddComponent<GadgetBase>();
            gadgetBase.removalMethod = GadgetBase.GadgetRemovalMethod.EmptyHand;
            gadgetBase.requiredMountPoints = 0;
            gadgetBase.reservationGroupID = "zg2_board";
            gadgetBase.requireSoldering = false;
            gadgetBase.requirements = new TrainCarCustomization.TrainCarCustomizerBase.TrainCarRequirements
            {
                electricsFuse = false,
                baseControls = false,
                trainCarPresence = TrainCarCustomization.TrainCarCustomizerBase
                    .CustomizerTrainCarRequirements.None,
                simPorts = Array.Empty<TrainCarCustomization.TrainCarCustomizerBase
                    .TrainCarRequirements.PortRequirement>()
            };

            // Search entire hierarchy for the collider (FBX imports often place it on a child)
            var existingCollider = itemPrefab.GetComponentInChildren<BoxCollider>();

            Vector3 boundsCenter;
            Vector3 boundsSize;

            if (existingCollider != null)
            {
                boundsCenter = existingCollider.center;
                boundsSize = existingCollider.size;
            }
            else
            {
                var renderer = itemPrefab.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    var b = renderer.bounds;
                    boundsCenter = itemPrefab.transform.InverseTransformPoint(b.center);
                    boundsSize = b.size;
                    Main.Log($"  Gadget '{prefabName}': no BoxCollider found, using renderer bounds {boundsSize}");
                }
                else
                {
                    boundsCenter = Vector3.zero;
                    boundsSize = new Vector3(0.35f, 0.3f, 0.05f);
                    Main.LogWarning($"  Gadget '{prefabName}': no collider or renderer found, using default bounds.");
                }
            }

            // Swap bounds axes to match the rotated visual: mesh Z (depth) → gadget Y,
            // mesh Y (height) → gadget Z, mesh X (width) stays X.
            // Shift gadget Y by pivotOffsetUp to keep bounds aligned with the shifted visual.
            gadgetBase.boundsCenter = new Vector3(boundsCenter.x, boundsCenter.z + pivotOffsetUp, boundsCenter.y);
            gadgetBase.boundsSize = new Vector3(boundsSize.x, boundsSize.z, boundsSize.y);

            // ── Visual child: rotated so the back face sits against the surface ──
            //
            // DV gadget placement convention (confirmed from game source):
            //   Z+ = outward from the surface (surface normal / depth)
            //   Y+ = up along the surface
            //   X+ = right along the surface
            //   Origin = attachment point on the surface
            //
            // Board mesh convention (from Unity):
            //   Z- = front face,  Z+ = back face (flat side to mount against wall)
            //   Y+ = top,  Y- = bottom
            //
            // Rotation: Euler(-90, 180, 0) maps mesh Z+ → gadget Y+ and mesh Y+ → gadget Z+
            // pivotOffsetUp shifts the visual in gadget Y+ (up along surface).
            float depth = boundsSize.z;

            var visualChild = new GameObject($"{prefabName}_Visual");
            visualChild.transform.SetParent(gadgetTemplate.transform, false);
            visualChild.transform.localRotation = Quaternion.Euler(-90f, 180f, 0f);
            visualChild.transform.localPosition = new Vector3(0f, pivotOffsetUp, 0f);
            visualChild.layer = gadgetTemplate.layer;

            // Copy visual mesh into the visual child
            var meshFilter = itemPrefab.GetComponentInChildren<MeshFilter>();
            var meshRenderer = itemPrefab.GetComponentInChildren<MeshRenderer>();
            if (meshFilter != null && meshRenderer != null)
            {
                var gadgetMesh = visualChild.AddComponent<MeshFilter>();
                gadgetMesh.sharedMesh = meshFilter.sharedMesh;
                var gadgetRenderer = visualChild.AddComponent<MeshRenderer>();
                gadgetRenderer.sharedMaterials = meshRenderer.sharedMaterials;

                gadgetBase.highlightMeshes = new[] { gadgetMesh };

                visualChild.AddComponent<ZG2Retroreflective>();
            }

            // Add collider on the visual child (offset along with the mesh)
            var gadgetCollider = visualChild.AddComponent<BoxCollider>();
            gadgetCollider.center = boundsCenter;
            gadgetCollider.size = boundsSize;

            // ── GadgetItem on the item root references the external gadget template ──
            var gadgetItem = itemPrefab.GetComponent<GadgetItem>();
            if (gadgetItem == null)
            {
                gadgetItem = itemPrefab.AddComponent<GadgetItem>();
            }
            gadgetItem.gadgetPrefab = gadgetBase;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Creates a simple solid-colour placeholder sprite for shop icons.
        /// Replace with proper sprites from the asset bundle when available.
        /// </summary>
        private static Sprite CreatePlaceholderSprite(Color colour)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }
    }
}
