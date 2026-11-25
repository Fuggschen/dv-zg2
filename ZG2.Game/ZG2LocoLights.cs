using System.Collections.Generic;
using System.Text;
using DV.Simulation.Cars;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Checks whether a locomotive shows red tail lights on a given side.
    ///
    /// DV locos switch their lamps by swapping the lamp renderer's material between the
    /// <see cref="Headlight"/>'s emissionMaterialLit / emissionMaterialUnlit (e.g. DE2:
    /// "[headlights_de2]/FrontSide/ext headlights_glass_red_F").
    /// A red lamp is a renderer that is named "red" or whose lit material is clearly red;
    /// it is lit if it currently uses one of the loco's lit headlight materials.
    /// </summary>
    public static class ZG2LocoLights
    {
        private static readonly Dictionary<Material, bool> redCache = new Dictionary<Material, bool>();
        private static readonly HashSet<string> reportedLiveries = new HashSet<string>();
        private static readonly HashSet<string> reportedStates = new HashSet<string>();
        private static readonly HashSet<Material> litMats = new HashSet<Material>();
        private static readonly int[] EmissionIDs =
        {
            Shader.PropertyToID("_EmissionColor"),
            Shader.PropertyToID("_EmissiveColor"),
            Shader.PropertyToID("_Emission")
        };
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static MaterialPropertyBlock mpb;

        /// <summary>
        /// True if a red lamp on the <paramref name="outward"/> side of the loco is lit.
        /// <paramref name="detectable"/> is false if no red lamp was found on that side at all.
        ///
        /// Red lamps are found by name ("red" anywhere in the path below the car, e.g.
        /// "ext headlights_glass_red_F") or by a red lit headlight material. A red lamp counts as lit if
        /// it is active and its current material is one of the loco's lit headlight materials or glows
        /// (emission clearly above black). Red Unity lights on that side count as well.
        /// </summary>
        public static bool HasRedLightLit(TrainCar car, Vector3 outward, out bool detectable)
        {
            detectable = false;
            if (car == null) return false;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            var lights = car.GetComponentsInChildren<Headlight>(true);
            litMats.Clear();
            foreach (var h in lights)
                if (h != null && h.emissionMaterialLit != null) litMats.Add(h.emissionMaterialLit);

            Transform root = car.transform;
            Vector3 center = root.position;
            StringBuilder diag = null;

            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                if (Vector3.Dot(r.bounds.center - center, outward) <= 0f) continue; // other side

                var mat = r.sharedMaterial;
                bool redByName = PathHasRed(r.transform, root) || MeshHasRed(r);
                bool redByColor = mat != null && litMats.Contains(mat) && IsRed(mat);
                if (!redByName && !redByColor) continue;

                detectable = true;
                bool active = r.enabled && r.gameObject.activeInHierarchy;
                float glow = Glow(r, mat);
                bool lit = active && mat != null && (litMats.Contains(mat) || glow > 0.15f);

                if (diag == null) diag = new StringBuilder();
                var meshF = r.GetComponent<MeshFilter>();
                diag.Append("\n    ").Append(Path(r.transform, root))
                    .Append(" mesh=").Append(meshF != null && meshF.sharedMesh != null ? meshF.sharedMesh.name : "-")
                    .Append($" active={active} mat={(mat != null ? mat.name : "-")} glow={glow:0.00} lit={lit}");

                if (lit) return true;
            }

            foreach (var l in car.GetComponentsInChildren<Light>(true))
            {
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                if (Vector3.Dot(l.transform.position - center, outward) <= 0f) continue;
                Color c = l.color;
                if (c.r > 0.3f && c.r > c.g * 2f && c.r > c.b * 2f) { detectable = true; return true; }
            }

            if (!detectable) Report(car, lights, "no red lamp renderer on this side");
            else ReportState(car, diag);
            return false;
        }

        /// <summary>Mesh name, e.g. "headlight_glass_red_R", "dm3_headlights_glass_red_F", "..._LOD1".</summary>
        private static bool MeshHasRed(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            return mesh != null && mesh.name.IndexOf("glass_red", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool PathHasRed(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
                if (x.name.IndexOf("red", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Brightness of the emission (property block wins over the material).</summary>
        private static float Glow(Renderer r, Material mat)
        {
            float best = 0f;
            r.GetPropertyBlock(mpb);
            foreach (int id in EmissionIDs)
            {
                Color c = Color.black;
                bool has = false;
                if (!mpb.isEmpty)
                {
                    var v = mpb.GetVector(id);
                    if (v != Vector4.zero) { c = new Color(v.x, v.y, v.z); has = true; }
                }
                if (!has && mat != null && mat.HasProperty(id))
                {
                    if (id == EmissionIDs[0] && mat.shader != null && !mat.IsKeywordEnabled("_EMISSION") && mat.shaderKeywords.Length > 0)
                        continue; // Standard shader with emission switched off
                    c = mat.GetColor(id);
                }
                best = Mathf.Max(best, c.maxColorComponent);
            }
            return best;
        }

        /// <summary>Red lamps found but none lit: log their state once per livery and state.</summary>
        private static void ReportState(TrainCar car, StringBuilder diag)
        {
            if (diag == null) return;
            string livery = car.carLivery != null ? car.carLivery.id : car.name;
            string text = diag.ToString();
            if (reportedStates.Count > 200) reportedStates.Clear();
            if (!reportedStates.Add(livery + text)) return;
            Main.LogWarning($"Zg2: red tail lamps of {livery} found but none counted as lit:{text}");
        }

        /// <summary>Once per livery: log the lamp structure so the detection can be adjusted.</summary>
        private static void Report(TrainCar car, Headlight[] lights, string why)
        {
            string livery = car.carLivery != null ? car.carLivery.id : car.name;
            if (!reportedLiveries.Add(livery)) return;
            var sb = new StringBuilder();
            sb.Append($"Zg2: red tail lights of {livery} not detectable ({why}). Headlights:");
            foreach (var h in lights)
                if (h != null) sb.Append("\n    ").Append(Path(h.transform, car.transform));
            Main.LogWarning(sb.ToString());
        }

        private static string Path(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        private static bool IsRed(Material m)
        {
            if (redCache.TryGetValue(m, out bool red)) return red;
            Color c = m.HasProperty(EmissionIDs[0]) ? m.GetColor(EmissionIDs[0])
                    : m.HasProperty(ColorID) ? m.GetColor(ColorID) : Color.black;
            red = c.r > 0.05f && c.r > c.g * 1.8f && c.r > c.b * 1.8f;
            redCache[m] = red;
            return red;
        }
    }
}
