using DV.Customization.Gadgets;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Patches <see cref="GadgetSystemUtility"/> to ensure ZG2 board gadgets
    /// are not blocked by the <c>AllowedGadgetNames</c> whitelist.
    /// 
    /// During normal gameplay <c>AllowedGadgetNames</c> is null (all allowed),
    /// but during tutorials / restricted modes it may be set. This patch
    /// appends our board names so they remain usable in those scenarios.
    /// 
    /// The names must match the <c>itemPrefabName</c> assigned by custom_item_mod
    /// (format: <c>"custom_item_mod/{name}/prefab"</c>).
    /// </summary>
    [HarmonyPatch(typeof(GadgetSystemUtility), nameof(GadgetSystemUtility.AllowedGadgetNames), MethodType.Setter)]
    public static class GadgetWhitelistPatch
    {
        private static string[] zg2GadgetNames;

        /// <summary>
        /// Lazily resolves gadget names from the factory (only available after
        /// <see cref="ZG2BoardFactory.Initialise"/> has run).
        /// </summary>
        private static string[] GetGadgetNames()
        {
            if (zg2GadgetNames != null) return zg2GadgetNames;
            if (!ZG2BoardFactory.IsInitialised) return null;

            var allNames = ZG2BoardFactory.AllPrefabNames;
            zg2GadgetNames = new string[allNames.Length];
            for (int i = 0; i < allNames.Length; i++)
                zg2GadgetNames[i] = ZG2BoardFactory.GetItemPrefabName(allNames[i]);
            return zg2GadgetNames;
        }

        /// <summary>
        /// Prefix on the setter of <c>AllowedGadgetNames</c>.
        /// Modifies the <paramref name="value"/> parameter in-place so the
        /// setter stores an array that includes our gadget names.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(ref string[] value)
        {
            if (!Main.Enabled) return;
            if (value == null) return; // null = all allowed, no need to patch

            var names = GetGadgetNames();
            if (names == null) return; // factory not yet initialised

            // Quick check: if the array already contains our names, skip entirely
            bool allPresent = true;
            for (int i = 0; i < names.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < value.Length; j++)
                {
                    if (value[j] == names[i])
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    allPresent = false;
                    break;
                }
            }
            if (allPresent) return;

            // Append missing names — minimal allocation
            var combined = new string[value.Length + names.Length];
            value.CopyTo(combined, 0);
            names.CopyTo(combined, value.Length);
            value = combined;
        }
    }
}
