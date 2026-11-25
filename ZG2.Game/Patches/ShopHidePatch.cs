using custom_item_mod;
using DV.Shops;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Prefix on custom_item_mod's <c>CustomItem.AddToShop</c>: worn boards are station-only,
    /// so they get no shelf slot at all (previously they showed up as permanently sold out).
    /// </summary>
    [HarmonyPatch(typeof(CustomItem), nameof(CustomItem.AddToShop))]
    public static class ShopHidePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CustomItem __instance, Shop shop)
        {
            if (!Main.Enabled) return true;
            return !ZG2BoardFactory.IsHiddenFromShops(__instance);
        }
    }
}
