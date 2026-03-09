using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;

namespace ZG2.Patches
{
    /// <summary>
    /// Detects when a ZG2 board gadget is placed on a train car.
    /// Postfix on <see cref="GadgetItem.Place"/> — after placement is complete,
    /// checks if the gadget is a ZG2 board and notifies the map marker manager.
    /// </summary>
    [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Place))]
    public static class GadgetPlacedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GadgetBase __result, Customization destination)
        {
            if (!Main.Enabled) return;
            if (__result == null) return;
            if (!ZG2Board.IsZG2Board(__result)) return;

            if (destination is TrainCarCustomization trainCustom && trainCustom.TrainCar != null)
            {
                ZG2MapMarkerManager.OnBoardMounted(trainCustom.TrainCar);
            }
        }
    }

    /// <summary>
    /// Detects when a ZG2 board gadget is removed from a train car.
    /// Prefix on <see cref="GadgetBase.Remove"/> — captures the TrainCar reference
    /// before the gadget is unlinked, then notifies the map marker manager.
    /// </summary>
    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    public static class GadgetRemovedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(GadgetBase __instance, out TrainCar __state)
        {
            __state = null;
            if (!Main.Enabled) return;
            if (!ZG2Board.IsZG2Board(__instance)) return;

            // Capture the TrainCar before Unlink clears the reference
            __state = __instance.TrainCar;
        }

        [HarmonyPostfix]
        public static void Postfix(TrainCar __state)
        {
            if (__state == null) return;
            ZG2MapMarkerManager.OnBoardRemoved(__state);
        }
    }
}
