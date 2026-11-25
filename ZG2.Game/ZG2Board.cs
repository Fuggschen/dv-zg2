using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Identifies a ZG2 board variant and handles per-item save/load (variant + wear).
    /// Attached to every ZG2 board item prefab.
    /// </summary>
    public class ZG2Board : MonoBehaviour
    {
        public enum BoardVariant
        {
            Old,
            Modern,
            OldWorn,
            ModernWorn
        }

        private const string SAVE_KEY_VARIANT = "zg2_variant";
        private const string SAVE_KEY_WEAR = "zg2_wear";
        private const string SAVE_KEY_TEX = "zg2_tex";

        [SerializeField]
        private BoardVariant variant;

        private ZG2Wear wear;
        private bool wearInitialised;
        private string textureSet = string.Empty;   // "" = original texture, else custom set (ZG2CustomTextures)
        private bool agedTexturePicked;

        public BoardVariant Variant => variant;
        public bool IsWornVariant => variant == BoardVariant.OldWorn || variant == BoardVariant.ModernWorn;
        public bool IsLarge => variant == BoardVariant.Modern || variant == BoardVariant.ModernWorn;
        public ZG2Wear Wear => wear;
        public string TextureSet => textureSet;

        /// <summary>Board items currently active in the scene (held, lying around, on shelves).</summary>
        internal static readonly System.Collections.Generic.List<ZG2Board> Active = new System.Collections.Generic.List<ZG2Board>();

        private ItemSaveData itemSaveData;
        private ItemSaveData.ItemSaveDataRequestedDelegate saveRequestedHandler;
        private ItemSaveData.ItemSaveDataLoadedDelegate saveLoadedHandler;

        public void SetVariant(BoardVariant v)
        {
            variant = v;
        }

        /// <summary>Sets the wear (station spawn, save restore) and updates the visuals.</summary>
        public void SetWear(ZG2Wear w)
        {
            wear = w;
            wearInitialised = true;
            ZG2BoardWear.Apply(gameObject, this);
        }

        /// <summary>Adds wear from use (visuals are updated by the caller in steps).</summary>
        public void AddWear(float amount)
        {
            float before = wear.Level;
            wear.Level = Mathf.Clamp01(wear.Level + amount);
            wearInitialised = true;

            // A new board that became worn gets a (random) used texture once.
            if (!IsWornVariant && !agedTexturePicked &&
                before < Main.Settings.WornTextureAt && wear.Level >= Main.Settings.WornTextureAt)
            {
                agedTexturePicked = true;
                textureSet = ZG2CustomTextures.PickRandom(newBoard: false);
            }
        }

        /// <summary>Restores wear and texture (save load of a mounted board).</summary>
        public void SetWearAndTexture(ZG2Wear w, string tex)
        {
            textureSet = tex ?? string.Empty;
            agedTexturePicked = !IsWornVariant && w.Level >= Main.Settings.WornTextureAt;
            SetWear(w);
        }

        private void Awake()
        {
            if (TryGetComponent(out itemSaveData))
            {
                saveRequestedHandler = OnSaveRequested;
                saveLoadedHandler = OnSaveLoaded;
                EventHelper.Subscribe(itemSaveData, "ItemSaveDataRequested", saveRequestedHandler);
                EventHelper.Subscribe(itemSaveData, "ItemSaveDataLoaded", saveLoadedHandler);
            }
        }

        private void Start()
        {
            if (!wearInitialised)
            {
                // Worn variants only exist as used station boards; new ones come from the shop.
                textureSet = ZG2CustomTextures.PickRandom(newBoard: !IsWornVariant);
                if (IsWornVariant)
                    SetWear(Main.Settings.RandomStationWear ? ZG2Wear.UsedBoard() : new ZG2Wear { Level = 1f, Hue = 0.5f });
                else
                    SetWear(ZG2Wear.NewBoard());
            }
            else
            {
                ZG2BoardWear.Apply(gameObject, this);
            }
        }

        private void OnEnable()
        {
            if (gameObject.scene.IsValid() && !Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void OnDestroy()
        {
            if (itemSaveData != null)
            {
                EventHelper.Unsubscribe(itemSaveData, "ItemSaveDataRequested", saveRequestedHandler);
                EventHelper.Unsubscribe(itemSaveData, "ItemSaveDataLoaded", saveLoadedHandler);
            }
        }

        private JObject OnSaveRequested(JObject data)
        {
            data[SAVE_KEY_VARIANT] = (int)variant;
            if (wearInitialised) data[SAVE_KEY_WEAR] = wear.Serialize();
            data[SAVE_KEY_TEX] = textureSet ?? string.Empty;
            return data;
        }

        private void OnSaveLoaded(JObject data)
        {
            if (data == null) return;
            if (data.TryGetValue(SAVE_KEY_VARIANT, out var token))
                variant = (BoardVariant)token.Value<int>();
            string tex = data.TryGetValue(SAVE_KEY_TEX, out var t) ? t.Value<string>() : string.Empty;
            if (data.TryGetValue(SAVE_KEY_WEAR, out var w) && ZG2Wear.TryParse(w.Value<string>(), out var loaded))
                SetWearAndTexture(loaded, tex);
            else if (!string.IsNullOrEmpty(tex))
            {
                textureSet = tex;
                if (wearInitialised) ZG2BoardWear.Apply(gameObject, this);
            }
        }

        #region Static helpers

        /// <summary>
        /// Checks whether a <see cref="DV.Customization.Gadgets.GadgetBase"/> is a ZG2 board.
        /// </summary>
        public static bool IsZG2Board(DV.Customization.Gadgets.GadgetBase gadget)
        {
            if (gadget == null) return false;
            // The GadgetItem lives on the item GO; check its ZG2Board component.
            return gadget.GadgetItem != null
                && gadget.GadgetItem.GetComponent<ZG2Board>() != null;
        }

        /// <summary>
        /// Checks whether a <see cref="GameObject"/> has a ZG2Board component.
        /// </summary>
        public static bool IsZG2Board(GameObject go)
        {
            return go != null && go.GetComponent<ZG2Board>() != null;
        }

        /// <summary>
        /// Returns the <c>itemPrefabName</c> for a given variant as assigned by custom_item_mod.
        /// </summary>
        public static string GetPrefabName(BoardVariant v)
        {
            return v switch
            {
                BoardVariant.Old => ZG2BoardFactory.GetItemPrefabName(ZG2BoardFactory.PREFAB_NAME_OLD),
                BoardVariant.Modern => ZG2BoardFactory.GetItemPrefabName(ZG2BoardFactory.PREFAB_NAME_MODERN),
                BoardVariant.OldWorn => ZG2BoardFactory.GetItemPrefabName(ZG2BoardFactory.PREFAB_NAME_OLD_WORN),
                BoardVariant.ModernWorn => ZG2BoardFactory.GetItemPrefabName(ZG2BoardFactory.PREFAB_NAME_MODERN_WORN),
                _ => ZG2BoardFactory.GetItemPrefabName(ZG2BoardFactory.PREFAB_NAME_OLD)
            };
        }

        #endregion
    }
}
