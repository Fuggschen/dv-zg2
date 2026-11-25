using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ZG2
{
    /// <summary>
    /// Spawns ZG2 boards near job validator machines at every station.
    /// Uses the station's <see cref="PointOnPlane"/> (jobBookletSpawnSurface)
    /// to find valid spawn positions.
    /// </summary>
    public static class ZG2StationSpawner
    {
        public static IEnumerator SpawnAllNextFrame()
        {
            yield return null;
            SpawnAll();
        }

        /// <summary>
        /// Spawns boards at all stations that have not yet been processed.
        /// </summary>
        public static void SpawnAll()
        {
            // Disabled check until gadget sync is native
            /*
            if (!ZG2MPUtil.IsSinglePlayerOrHost)
            {
                Main.Log("ZG2StationSpawner: skipping spawn on MP client (host-only).");
                return;
            }
            */
            if (!Main.Settings.SpawnBoardsAtStations)
            {
                Main.Log("ZG2StationSpawner: board spawning at stations is disabled.");
                return;
            }

            if (!ZG2BoardFactory.IsInitialised)
            {
                Main.LogWarning("ZG2BoardFactory not initialised — cannot spawn boards.");
                return;
            }

            if (StationController.allStations == null)
            {
                Main.LogWarning("StationController.allStations is null — stations not loaded yet.");
                return;
            }

            int totalSpawned = 0;
            int stationCount = 0;

            foreach (var station in StationController.allStations)
            {
                if (station == null || station.stationInfo == null)
                    continue;

                int count = SpawnAtStation(station);
                if (count > 0)
                {
                    totalSpawned += count;
                    stationCount++;
                }
            }

            Main.Log($"ZG2StationSpawner: spawned {totalSpawned} boards across {stationCount} stations.");
        }

        /// <summary>
        /// Spawns boards at a single station, returning the number spawned.
        /// </summary>
        private static int SpawnAtStation(StationController station)
        {
            // jobBookletSpawnSurface is a private serialized PointOnPlane — accessible via Krafs.Publicizer
            PointOnPlane spawnSurface = station.jobBookletSpawnSurface;

            if (spawnSurface == null)
            {
                Main.LogWarning($"Station '{station.stationInfo.YardID}' has no jobBookletSpawnSurface.");
                return 0;
            }

            int count = Main.Settings.BoardsPerStation;
            int spawned = 0;

            Vector3 position = spawnSurface.transform.position;
            Quaternion rotation = spawnSurface.transform.rotation;

            // Stations only hand out used (worn) boards; new boards are sold in the shops.
            // Each board gets its own random wear level/dirt colour in ZG2Board.Start.
            string[] stationPrefabs = ZG2BoardFactory.StationPrefabNames;
            var t = spawnSurface.transform;

            for (int i = 0; i < count; i++)
            {
                string prefabName = stationPrefabs[Random.Range(0, stationPrefabs.Length)];
                // Spread the boards a little so they do not spawn inside each other.
                Vector3 offset = t.right * Random.Range(-0.25f, 0.25f) + t.forward * Random.Range(-0.15f, 0.15f)
                                 + Vector3.up * (0.03f * i);
                if (SpawnBoard(prefabName, position + offset, rotation * Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f)))
                    spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Instantiates a single board at the given position and rotation.
        /// </summary>
        private static bool SpawnBoard(string prefabName, Vector3 position, Quaternion rotation)
        {
            var prefab = ZG2BoardFactory.GetPrefab(prefabName);
            if (prefab == null)
            {
                Main.LogError($"Prefab '{prefabName}' not found in factory.");
                return false;
            }

            // Offset slightly upward to avoid clipping into the surface
            position += Vector3.up * 0.05f;

            // WorldMover is in WorldStreamer.dll (root namespace)
            var instance = Object.Instantiate(prefab, position, rotation, WorldMover.OriginShiftParent);
            instance.name = prefabName;
            instance.SetActive(true);

            // Mark as non-player property (station item until picked up by player)
            var spec = instance.GetComponent<InventoryItemSpec>();
            if (spec != null)
            {
                spec.BelongsToPlayer = false;
            }

            return true;
        }

        public static void Shutdown()
        {
        }
    }
}
