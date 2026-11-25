using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;
using UnityEngine;

namespace ZG2.Patches
{
    /// <summary>
    /// Postfix on <see cref="GadgetItem.Place"/>: a ZG2 board was mounted.
    /// Updates the map marker, carries the board's wear over to the gadget and – on MP clients –
    /// reports the car-local pose to the host, which needs it for the train end validation.
    /// </summary>
    [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Place))]
    public static class GadgetPlacedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GadgetBase __result, Customization destination)
        {
            if (!Main.Enabled || __result == null) return;
            if (!ZG2Board.IsZG2Board(__result)) return;
            if (!(destination is TrainCarCustomization trainCustom) || trainCustom.TrainCar == null) return;

            var car = trainCustom.TrainCar;

            ZG2BoardWear.OnBoardMounted(__result);
            ZG2MapMarkerManager.OnBoardMounted(car);

            if (ZG2MPUtil.IsClient)
            {
                string carId = car.logicCar?.ID;
                if (carId != null)
                {
                    var ct = car.transform;
                    var gt = __result.transform;
                    ZG2MPUtil.NotifyBoardAttached(carId, __result.GetInstanceID(),
                        ct.InverseTransformPoint(gt.position), ct.InverseTransformDirection(gt.forward));
                }
            }
            else
            {
                ZG2MPUtil.BroadcastMarkerState(car);
            }
        }
    }

    /// <summary>
    /// Prefix/postfix on <see cref="GadgetBase.Remove"/>: a ZG2 board was unmounted.
    /// </summary>
    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    public static class GadgetRemovedPatch
    {
        public class State
        {
            public TrainCar Car;
            public GadgetBase Gadget;
            public ZG2Board Board;
            public int Key;
        }

        [HarmonyPrefix]
        public static void Prefix(GadgetBase __instance, out State __state)
        {
            __state = null;
            if (!Main.Enabled || !ZG2Board.IsZG2Board(__instance)) return;

            var car = __instance.TrainCar; // captured before Unlink clears it
            if (car == null) return;

            __state = new State
            {
                Car = car,
                Gadget = __instance,
                Board = __instance.GadgetItem != null ? __instance.GadgetItem.GetComponent<ZG2Board>() : null,
                Key = __instance.GetInstanceID()
            };
        }

        [HarmonyPostfix]
        public static void Postfix(State __state)
        {
            if (__state == null || __state.Car == null) return;
            var car = __state.Car;

            ZG2BoardWear.OnBoardRemoved(__state.Gadget, __state.Board);
            ZG2MapMarkerManager.OnBoardRemoved(car);

            if (ZG2MPUtil.IsClient)
            {
                string carId = car.logicCar?.ID;
                if (carId != null)
                    ZG2MPUtil.NotifyBoardRemoved(carId, __state.Key);
            }
            else
            {
                ZG2MPUtil.BroadcastMarkerState(car);
            }
        }
    }
}
