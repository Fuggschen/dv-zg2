using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Identifies a ZG2 board variant and handles per-item save/load.
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

        [SerializeField]
        private BoardVariant variant;

        public BoardVariant Variant => variant;

        private ItemSaveData itemSaveData;
        private ItemSaveData.ItemSaveDataRequestedDelegate saveRequestedHandler;
        private ItemSaveData.ItemSaveDataLoadedDelegate saveLoadedHandler;

        public void SetVariant(BoardVariant v)
        {
            variant = v;
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
            data["zg2_variant"] = (int)variant;
            return data;
        }

        private void OnSaveLoaded(JObject data)
        {
            if (data.TryGetValue(SAVE_KEY_VARIANT, out var token))
            {
                variant = (BoardVariant)token.Value<int>();
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
