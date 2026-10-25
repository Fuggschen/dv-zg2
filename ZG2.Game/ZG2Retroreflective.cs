using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Passes nearby light data to the "Custom/Lit Retroreflective" shader as uniform arrays.
    /// The shader computes per-pixel retroreflective emission using worldPos, so only the exact
    /// pixels illuminated by a light will glow — not the entire surface.
    /// </summary>
    public class ZG2Retroreflective : MonoBehaviour
    {
        private const int MaxLights = 8;
        private const float RescanInterval = 0.5f;

        // The range filter over all scene lights is spread over this many frames
        // so a rescan does not land as one spike.
        private const int FilterFrames = 10;

        // Boards and lights both move (trains, carried flashlights), so the scan-time
        // range filter keeps lights that could come into range before the next scan.
        private const float ScanRangeMargin = 50f;

        private static readonly int LightCountID = Shader.PropertyToID("_RetroLightCount");
        private static readonly int LightPosRangeID = Shader.PropertyToID("_RetroLightPosRange");
        private static readonly int LightColorID = Shader.PropertyToID("_RetroLightColor");
        private static readonly int LightSpotDataID = Shader.PropertyToID("_RetroLightSpotData");

        private struct LightData
        {
            public Vector4 PosRange;
            public Vector4 Color;
            public Vector4 SpotData;
            public float Strength;
        }

        // Shared by all boards: the scene is scanned at most every RescanInterval,
        // and light properties are read once per frame, not once per board.
        private static readonly List<ZG2Retroreflective> activeBoards = new List<ZG2Retroreflective>();
        private static readonly List<Vector3> boardCenters = new List<Vector3>();
        private static List<Light> nearbyLights = new List<Light>();
        private static List<Light> pendingLights = new List<Light>();
        private static Light[] allLights = Array.Empty<Light>();
        private static int filterCursor;
        private static bool scanInProgress;
        private static float lastScanTime;
        private static bool rescanRequested = true;
        private static LightData[] snapshot = new LightData[32];
        private static int snapshotCount;
        private static int snapshotFrame = -1;

        private static readonly int[] slotLights = new int[MaxLights];
        private static readonly float[] slotScores = new float[MaxLights];

        private Renderer targetRenderer;
        private MaterialPropertyBlock mpb;
        private int lastCount = -1;

        // Pre-allocated arrays to avoid GC each frame
        private readonly Vector4[] posRange = new Vector4[MaxLights];
        private readonly Vector4[] colors = new Vector4[MaxLights];
        private readonly Vector4[] spotData = new Vector4[MaxLights];

        private void OnEnable()
        {
            targetRenderer = GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            lastCount = -1;
            activeBoards.Add(this);
            rescanRequested = true;
        }

        private void OnDisable()
        {
            activeBoards.Remove(this);
            if (targetRenderer != null)
            {
                targetRenderer.GetPropertyBlock(mpb);
                mpb.SetInt(LightCountID, 0);
                targetRenderer.SetPropertyBlock(mpb);
            }
        }

        private static void StartScan()
        {
            boardCenters.Clear();
            foreach (var board in activeBoards)
            {
                if (board.targetRenderer != null)
                    boardCenters.Add(board.targetRenderer.bounds.center);
            }

            allLights = FindObjectsOfType<Light>();
            filterCursor = 0;
            pendingLights.Clear();
            scanInProgress = true;
            lastScanTime = Time.unscaledTime;
        }

        /// <summary>
        /// Range-filters up to <paramref name="maxCount"/> lights of the current scan.
        /// The previous result stays in use until the whole pass is done.
        /// </summary>
        private static void FilterLights(int maxCount)
        {
            int end = Math.Min(filterCursor + maxCount, allLights.Length);
            for (; filterCursor < end; filterCursor++)
            {
                var light = allLights[filterCursor];
                // Only directed beams (headlights, flashlights) make the film glow;
                // ambient sources like ceiling lamps, lanterns and the sun do not.
                if (light == null || light.type != LightType.Spot) continue;

                Vector3 lightPos = light.transform.position;
                float reach = light.range + ScanRangeMargin;
                float reachSqr = reach * reach;
                foreach (var center in boardCenters)
                {
                    if ((center - lightPos).sqrMagnitude <= reachSqr)
                    {
                        pendingLights.Add(light);
                        break;
                    }
                }
            }

            if (filterCursor < allLights.Length) return;

            var done = pendingLights;
            pendingLights = nearbyLights;
            nearbyLights = done;
            scanInProgress = false;
        }

        private static void RefreshSnapshot()
        {
            if (snapshotFrame == Time.frameCount) return;
            snapshotFrame = Time.frameCount;

            // A newly enabled board may have no lights from the last pass,
            // so it gets a full pass at once instead of waiting FilterFrames.
            if (rescanRequested)
            {
                rescanRequested = false;
                StartScan();
                FilterLights(allLights.Length);
            }
            else
            {
                if (!scanInProgress && Time.unscaledTime - lastScanTime >= RescanInterval)
                    StartScan();
                if (scanInProgress)
                    FilterLights(allLights.Length / FilterFrames + 1);
            }

            if (snapshot.Length < nearbyLights.Count)
                Array.Resize(ref snapshot, nearbyLights.Count);

            snapshotCount = 0;
            foreach (var light in nearbyLights)
            {
                if (light == null || !light.isActiveAndEnabled) continue;

                Color c = light.color * light.intensity;
                Vector3 pos = light.transform.position;
                Vector3 fwd = light.transform.forward;
                float cosHalf = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);

                snapshot[snapshotCount++] = new LightData
                {
                    PosRange = new Vector4(pos.x, pos.y, pos.z, light.range),
                    Color = new Vector4(c.r, c.g, c.b, 0f),
                    SpotData = new Vector4(fwd.x, fwd.y, fwd.z, cosHalf),
                    Strength = new Vector3(c.r, c.g, c.b).magnitude
                };
            }
        }

        private void LateUpdate()
        {
            if (targetRenderer == null || !targetRenderer.isVisible) return;

            RefreshSnapshot();

            Vector3 boardPos = targetRenderer.bounds.center;
            int count = 0;

            // Keep the MaxLights strongest contributions, sorted by attenuation * strength.
            for (int i = 0; i < snapshotCount; i++)
            {
                Vector4 pr = snapshot[i].PosRange;
                float distSqr = (new Vector3(pr.x, pr.y, pr.z) - boardPos).sqrMagnitude;
                float rangeSqr = pr.w * pr.w;
                if (distSqr > rangeSqr) continue;
                float score = (1f - distSqr / rangeSqr) * snapshot[i].Strength;

                if (count == MaxLights && score <= slotScores[MaxLights - 1]) continue;

                int slot = count < MaxLights ? count++ : MaxLights - 1;
                while (slot > 0 && slotScores[slot - 1] < score)
                {
                    slotScores[slot] = slotScores[slot - 1];
                    slotLights[slot] = slotLights[slot - 1];
                    slot--;
                }
                slotScores[slot] = score;
                slotLights[slot] = i;
            }

            if (count == 0 && lastCount == 0) return;
            lastCount = count;

            for (int s = 0; s < count; s++)
            {
                posRange[s] = snapshot[slotLights[s]].PosRange;
                colors[s] = snapshot[slotLights[s]].Color;
                spotData[s] = snapshot[slotLights[s]].SpotData;
            }

            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetInt(LightCountID, count);
            if (count > 0)
            {
                mpb.SetVectorArray(LightPosRangeID, posRange);
                mpb.SetVectorArray(LightColorID, colors);
                mpb.SetVectorArray(LightSpotDataID, spotData);
            }
            targetRenderer.SetPropertyBlock(mpb);
        }
    }
}
