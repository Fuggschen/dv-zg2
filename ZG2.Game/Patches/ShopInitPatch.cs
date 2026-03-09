using DV.Shops;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Hooks into <see cref="GlobalShopController.Awake"/> to register ZG2 boards
    /// with custom_item_mod <b>after</b> its own <c>InitializeItems()</c> prefix
    /// (default priority 400) clears and populates the item list, but <b>before</b>
    /// its <c>FinalizeItems()</c> postfix wires everything into shops.
    ///
    /// Priority 200 ensures our prefix runs after custom_item_mod's prefix (which
    /// uses default priority 400 — Harmony executes prefixes in descending priority order).
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), nameof(GlobalShopController.Awake))]
    public static class ShopInitPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(200)]
        [HarmonyAfter("custom_item_mod")]
        public static void Prefix()
        {
            if (!Main.Enabled) return;

            Main.Log("ShopInitPatch: registering ZG2 items with custom_item_mod...");
            ZG2BoardFactory.Initialise();
        }
    }
}
