using DV.Customization;
using DV.Customization.Gadgets;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Utility class to detect ZG2 board gadgets on a train car.
    /// </summary>
    public static class ZG2GadgetDetector
    {
        /// <summary>
        /// Returns true if the given <see cref="TrainCar"/> has at least one ZG2 board
        /// gadget placed on it (via the gadget customisation system).
        /// </summary>
        public static bool HasZG2Board(TrainCar car)
        {
            if (car == null) return false;

            // Primary approach: use the Customization system's list of linked customizers
            var customization = car.GetComponent<TrainCarCustomization>();
            if (customization != null)
            {
                // Customizers is a ReadOnlyCollection<CustomizerBase> (publicized via Krafs)
                var customizers = customization.Customizers;
                if (customizers != null)
                {
                    for (int i = 0; i < customizers.Count; i++)
                    {
                        if (customizers[i] is GadgetBase gadget && ZG2Board.IsZG2Board(gadget))
                        {
                            return true;
                        }
                    }
                }
            }

            // Fallback: search the car's interior hierarchy directly for any ZG2Board component.
            // This catches boards that might be placed but not formally linked to the
            // Customization system (e.g. during a transition or if parenting differs).
            if (car.interior != null)
            {
                var boards = car.interior.GetComponentsInChildren<ZG2Board>(includeInactive: false);
                if (boards != null && boards.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the free (uncoupled) end of a trainset's end car
        /// has a ZG2 board. This is more precise but optional for v1 —
        /// we check if the car has a board anywhere.
        /// </summary>
        public static bool HasZG2BoardOnFreeEnd(Trainset trainset, bool front)
        {
            if (trainset == null) return false;

            var endCar = front ? trainset.firstCar : trainset.lastCar;
            return HasZG2Board(endCar);
        }
    }
}
