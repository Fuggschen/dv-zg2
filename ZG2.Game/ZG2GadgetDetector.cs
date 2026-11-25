using System.Collections.Generic;
using DV.Customization;
using DV.Customization.Gadgets;
using UnityEngine;

namespace ZG2
{
    /// <summary>Position and facing (surface normal, gadget Z+) of a mounted board.</summary>
    public struct ZG2BoardPose
    {
        public Vector3 Position;
        public Vector3 Forward;

        public ZG2BoardPose(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = forward;
        }
    }

    /// <summary>
    /// Finds ZG2 board gadgets mounted on a train car.
    /// Only real gadgets count – board ITEMS lying in a car interior are ignored
    /// (the old interior fallback allowed "a board anywhere on the train").
    /// </summary>
    public static class ZG2GadgetDetector
    {
        private static readonly List<ZG2BoardPose> tmpLocal = new List<ZG2BoardPose>();

        /// <summary>True if at least one ZG2 board gadget is mounted on the car (local or client-reported).</summary>
        public static bool HasZG2Board(TrainCar car)
        {
            if (car == null) return false;

            var customizers = GetCustomizers(car);
            if (customizers != null)
            {
                for (int i = 0; i < customizers.Count; i++)
                {
                    if (customizers[i] is GadgetBase gadget && ZG2Board.IsZG2Board(gadget))
                        return true;
                }
            }

            if (ZG2MPUtil.IsMultiplayerActive)
            {
                string carId = car.logicCar?.ID;
                if (carId != null && ZG2MPUtil.HasClientReportedBoard(carId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Collects world-space poses of all ZG2 boards mounted on the car.
        /// Includes boards that MP clients reported (stored car-local on the host).
        /// </summary>
        public static void GetBoardPoses(TrainCar car, List<ZG2BoardPose> result)
        {
            result.Clear();
            if (car == null) return;

            var customizers = GetCustomizers(car);
            if (customizers != null)
            {
                for (int i = 0; i < customizers.Count; i++)
                {
                    if (customizers[i] is GadgetBase gadget && gadget != null && ZG2Board.IsZG2Board(gadget))
                    {
                        var t = gadget.transform;
                        result.Add(new ZG2BoardPose(t.position, t.forward));
                    }
                }
            }

            if (ZG2MPUtil.IsMultiplayerActive)
            {
                string carId = car.logicCar?.ID;
                if (carId != null)
                {
                    tmpLocal.Clear();
                    ZG2MPUtil.GetClientBoardPoses(carId, tmpLocal);
                    var ct = car.transform;
                    foreach (var local in tmpLocal)
                        result.Add(new ZG2BoardPose(ct.TransformPoint(local.Position), ct.TransformDirection(local.Forward)));
                }
            }
        }

        // Customizers is a ReadOnlyCollection<CustomizerBase>; the non-generic IList view avoids
        // depending on the exact element type name.
        private static System.Collections.IList GetCustomizers(TrainCar car)
        {
            var customization = car.GetComponent<TrainCarCustomization>();
            return customization != null ? customization.Customizers : null;
        }
    }
}
