using System.Collections.Generic;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Teleporters;
using DV.Utils;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Manages map markers (red dots) on wagons that have ZG2 boards mounted as gadgets.
    /// Uses <see cref="DynamicFastTravelDestination"/> with <see cref="FastTravelDestination.MarkerType.Caboose"/>
    /// to reuse the game's existing caboose red-dot map marker prefab.
    ///
    /// The destination component lives on a detached anchor GameObject (not parented to the car)
    /// so that <c>TrainCar.Resolve</c> cannot walk up the hierarchy and find the wagon —
    /// this prevents the game's fast-travel validation from rejecting the teleport.
    /// A <see cref="ZG2MarkerAnchor"/> component keeps the anchor tracking the car's position.
    /// </summary>
    public static class ZG2MapMarkerManager
    {
        /// <summary>
        /// Tracks the anchor GameObjects we've created per car.
        /// The <see cref="DynamicFastTravelDestination"/> lives on these anchors.
        /// </summary>
        private static readonly Dictionary<TrainCar, GameObject> markedCars
            = new Dictionary<TrainCar, GameObject>();

        /// <summary>
        /// Called when a ZG2 gadget is placed on a train car.
        /// Adds a map marker if the setting is enabled and the car doesn't already have one.
        /// </summary>
        public static void OnBoardMounted(TrainCar car)
        {
            if (car == null) return;
            if (!Main.Settings.ShowMapMarkers) return;
            if (markedCars.ContainsKey(car)) return;

            AddMarker(car);
        }

        /// <summary>
        /// Called when a ZG2 gadget is removed from a train car.
        /// Removes the map marker if no other ZG2 boards remain on the car.
        /// </summary>
        public static void OnBoardRemoved(TrainCar car)
        {
            if (car == null) return;
            if (!markedCars.ContainsKey(car)) return;

            // Check if there are still other ZG2 boards on this car
            if (HasOtherZG2Boards(car))
                return;

            RemoveMarker(car);
        }

        /// <summary>
        /// Called when the <see cref="ZG2Settings.ShowMapMarkers"/> toggle changes at runtime.
        /// </summary>
        public static void OnSettingChanged(bool enabled)
        {
            if (enabled)
                ScanAndAddAll();
            else
                RemoveAll();
        }

        /// <summary>
        /// Scans all existing train cars for mounted ZG2 boards and adds markers.
        /// Used on world load and when the setting is toggled on.
        /// </summary>
        public static void ScanAndAddAll()
        {
            if (!Main.Settings.ShowMapMarkers) return;

            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner == null) return;

            foreach (var car in spawner.AllCars)
            {
                if (car == null || markedCars.ContainsKey(car)) continue;
                if (ZG2GadgetDetector.HasZG2Board(car))
                    AddMarker(car);
            }
        }

        /// <summary>
        /// Hooks car deletion and adds markers for boards already mounted. Call on world load.
        /// </summary>
        public static void Initialise()
        {
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                EventHelper.Subscribe(spawner, "CarAboutToBeDeleted", carDeletedHandler);

            ScanAndAddAll();
        }

        public static void Shutdown()
        {
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                EventHelper.Unsubscribe(spawner, "CarAboutToBeDeleted", carDeletedHandler);

            RemoveAll();
        }

        private static readonly CarSpawner.CarSpawnEvent carDeletedHandler = OnCarDeleted;

        // Deleted cars usually return to the pool instead of being destroyed,
        // so ZG2MarkerAnchor never sees its target go null.
        private static void OnCarDeleted(TrainCar car)
        {
            RemoveMarker(car);
        }

        private static void AddMarker(TrainCar car)
        {
            // Create a detached anchor that follows the car's position.
            // NOT parented to the car — this prevents TrainCar.Resolve from finding
            // the wagon when fast-travel tries to validate the destination.
            var anchor = new GameObject($"[ZG2 Marker] {car.logicCar?.ID ?? car.name}");
            Object.DontDestroyOnLoad(anchor);

            var follower = anchor.AddComponent<ZG2MarkerAnchor>();
            follower.SetTarget(car);

            var dest = anchor.AddComponent<DynamicFastTravelDestination>();
            dest.SetMarkerName(car.logicCar?.ID ?? car.carLivery.id);
            dest.markerType = FastTravelDestination.MarkerType.Caboose;
            dest.showOnMap = true;

            markedCars[car] = anchor;
            Main.Log($"Map marker added for car {car.logicCar?.ID}.");
        }

        private static void RemoveMarker(TrainCar car)
        {
            if (markedCars.TryGetValue(car, out var anchor))
            {
                if (anchor != null)
                    Object.Destroy(anchor);
                markedCars.Remove(car);
                Main.Log($"Map marker removed for car {car.logicCar?.ID}.");
            }
        }

        private static void RemoveAll()
        {
            foreach (var kvp in markedCars)
            {
                if (kvp.Value != null)
                    Object.Destroy(kvp.Value);
            }
            markedCars.Clear();
        }

        /// <summary>
        /// Checks whether a car still has any ZG2 board gadgets mounted
        /// (by inspecting the customization system's linked customizers).
        /// </summary>
        private static bool HasOtherZG2Boards(TrainCar car)
        {
            var customization = car.GetComponent<TrainCarCustomization>();
            if (customization == null) return false;

            var customizers = customization.Customizers;
            if (customizers == null) return false;

            for (int i = 0; i < customizers.Count; i++)
            {
                if (customizers[i] is GadgetBase gadget && ZG2Board.IsZG2Board(gadget))
                    return true;
            }
            return false;
        }
    }
}
