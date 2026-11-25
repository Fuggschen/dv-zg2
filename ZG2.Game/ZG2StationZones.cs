using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Spatial lookup "is this position inside a station?".
    ///
    /// Built once per world load from all rail tracks whose logic track belongs to a station yard
    /// (TrackID.yardId == station YardID). Points along those tracks are rasterised into grids
    /// (absolute coordinates, independent of the floating origin):
    ///   • inner zone  = yard tracks + entry margin (fine 20 m grid)   → a train ENTERS a station here
    ///   • outer zone  = yard tracks + exit distance (coarse 100 m grid, default 1000 m)
    ///                   → a train has LEFT the station once no car is in it
    /// The large exit distance makes all track groups of a station one unit: driving through
    /// several groups of the same station never counts as departure/arrival.
    /// </summary>
    public static class ZG2StationZones
    {
        private const float CELL = 20f;
        private const float OUTER_CELL = 100f;
        private const float SAMPLE_SPACING = 8f;
        private const float OUTER_SAMPLE_SPACING = 50f;

        private static readonly Dictionary<long, int> innerCells = new Dictionary<long, int>();
        private static readonly List<HashSet<long>> outerCells = new List<HashSet<long>>();
        private static readonly List<string> stationIds = new List<string>();
        private static readonly List<StationController> stations = new List<StationController>();

        public static bool IsBuilt { get; private set; }
        public static int Count => stationIds.Count;

        public static string GetYardId(int index)
            => index >= 0 && index < stationIds.Count ? stationIds[index] : "?";

        /// <summary>Index of a station by its yard ID, or -1.</summary>
        public static int IndexOf(string yardId)
        {
            if (string.IsNullOrEmpty(yardId)) return -1;
            for (int i = 0; i < stationIds.Count; i++)
                if (string.Equals(stationIds[i], yardId, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static void Clear()
        {
            innerCells.Clear();
            outerCells.Clear();
            stationIds.Clear();
            stations.Clear();
            IsBuilt = false;
        }

        public static void Build()
        {
            Clear();

            try
            {
                var registry = RailTrackRegistry.Instance;
                if (registry == null)
                {
                    Main.LogWarning("ZG2StationZones: RailTrackRegistry not available.");
                    return;
                }

                float inner = Mathf.Max(0f, Main.Settings.StationEnterMargin);
                float outer = Mathf.Max(inner + 50f, Main.Settings.StationExitMargin);

                var indexByYard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int tracksUsed = 0;

                foreach (var railTrack in registry.AllTracks)
                {
                    if (railTrack == null) continue;
                    if (!RailTrackRegistry.RailTrackToLogicTrack.TryGetValue(railTrack, out var logicTrack) || logicTrack == null)
                        continue;

                    string yardId;
                    try { yardId = logicTrack.ID.yardId; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(yardId)) continue;

                    if (!indexByYard.TryGetValue(yardId, out int index))
                    {
                        var station = StationController.GetStationByYardID(yardId);
                        if (station == null) continue; // generic / mainline tracks

                        index = stationIds.Count;
                        indexByYard[yardId] = index;
                        stationIds.Add(yardId);
                        stations.Add(station);
                        outerCells.Add(new HashSet<long>());
                    }

                    var points = railTrack.GetKinkedPointSet()?.points;
                    if (points == null || points.Length == 0) continue;
                    tracksUsed++;

                    // Kinked points are in absolute coordinates (world = absolute + WorldMover.currentMove).
                    Vector3 lastInner = (Vector3)points[0].position;
                    Vector3 lastOuter = lastInner;
                    MarkInner(index, lastInner, inner);
                    MarkOuter(index, lastOuter, outer);
                    for (int i = 1; i < points.Length; i++)
                    {
                        Vector3 p = (Vector3)points[i].position;
                        bool end = i == points.Length - 1;
                        if (end || (p - lastInner).sqrMagnitude >= SAMPLE_SPACING * SAMPLE_SPACING)
                        {
                            MarkInner(index, p, inner);
                            lastInner = p;
                        }
                        if (end || (p - lastOuter).sqrMagnitude >= OUTER_SAMPLE_SPACING * OUTER_SAMPLE_SPACING)
                        {
                            MarkOuter(index, p, outer);
                            lastOuter = p;
                        }
                    }
                }

                IsBuilt = stationIds.Count > 0;
                Main.Log($"ZG2StationZones: {stationIds.Count} stations, {tracksUsed} yard tracks, " +
                         $"{innerCells.Count} inner cells (margin {inner:F0} m / {outer:F0} m).");
            }
            catch (Exception ex)
            {
                Main.LogError($"ZG2StationZones: build failed: {ex}");
                Clear();
            }
        }

        private static void MarkInner(int station, Vector3 abs, float radius)
        {
            MarkRadius(abs, radius, CELL, key =>
            {
                if (!innerCells.ContainsKey(key)) innerCells[key] = station;
            });
        }

        private static void MarkOuter(int station, Vector3 abs, float radius)
        {
            var set = outerCells[station];
            MarkRadius(abs, radius, OUTER_CELL, key => set.Add(key));
        }

        private static void MarkRadius(Vector3 abs, float radius, float cell, Action<long> mark)
        {
            // Conservative: a cell is marked if its centre is within radius + half cell diagonal.
            float reach = radius + cell * 0.7072f;
            float reachSqr = reach * reach;
            int x0 = Mathf.FloorToInt((abs.x - reach) / cell), x1 = Mathf.FloorToInt((abs.x + reach) / cell);
            int z0 = Mathf.FloorToInt((abs.z - reach) / cell), z1 = Mathf.FloorToInt((abs.z + reach) / cell);
            for (int x = x0; x <= x1; x++)
            {
                float cx = (x + 0.5f) * cell - abs.x;
                for (int z = z0; z <= z1; z++)
                {
                    float cz = (z + 0.5f) * cell - abs.z;
                    if (cx * cx + cz * cz <= reachSqr)
                        mark(Key(x, z));
                }
            }
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private static long KeyOfWorld(Vector3 worldPos, float cell)
        {
            Vector3 abs = worldPos - WorldMover.currentMove;
            return Key(Mathf.FloorToInt(abs.x / cell), Mathf.FloorToInt(abs.z / cell));
        }

        /// <summary>Index of the station whose inner zone contains this world position, or -1.</summary>
        public static int FindInner(Vector3 worldPos)
        {
            if (!IsBuilt) return -1;
            return innerCells.TryGetValue(KeyOfWorld(worldPos, CELL), out int s) ? s : -1;
        }

        /// <summary>True if the world position is inside the outer zone of the given station.</summary>
        public static bool InOuter(int station, Vector3 worldPos)
        {
            if (!IsBuilt || station < 0 || station >= outerCells.Count) return false;
            return outerCells[station].Contains(KeyOfWorld(worldPos, OUTER_CELL));
        }
    }
}
