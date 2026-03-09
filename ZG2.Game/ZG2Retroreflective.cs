using System.Collections;
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

        private static readonly int LightCountID = Shader.PropertyToID("_RetroLightCount");
        private static readonly int LightPosRangeID = Shader.PropertyToID("_RetroLightPosRange");
        private static readonly int LightColorID = Shader.PropertyToID("_RetroLightColor");
        private static readonly int LightSpotDataID = Shader.PropertyToID("_RetroLightSpotData");

        private Renderer targetRenderer;
        private MaterialPropertyBlock mpb;
        private Light[] cachedLights;

        // Pre-allocated arrays to avoid GC each frame
        private readonly Vector4[] posRange = new Vector4[MaxLights];
        private readonly Vector4[] colors = new Vector4[MaxLights];
        private readonly Vector4[] spotData = new Vector4[MaxLights];

        private void OnEnable()
        {
            targetRenderer = GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            StartCoroutine(RefreshLightsCoroutine());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (targetRenderer != null)
            {
                targetRenderer.GetPropertyBlock(mpb);
                mpb.SetInt(LightCountID, 0);
                targetRenderer.SetPropertyBlock(mpb);
            }
        }

        private IEnumerator RefreshLightsCoroutine()
        {
            while (true)
            {
                cachedLights = FindObjectsOfType<Light>();
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void LateUpdate()
        {
            if (targetRenderer == null || cachedLights == null) return;

            Vector3 boardPos = targetRenderer.bounds.center;
            int count = 0;

            for (int i = 0; i < cachedLights.Length && count < MaxLights; i++)
            {
                var light = cachedLights[i];
                if (light == null || !light.isActiveAndEnabled) continue;

                switch (light.type)
                {
                    case LightType.Directional:
                    {
                        Vector3 dir = -light.transform.forward;
                        posRange[count] = new Vector4(dir.x, dir.y, dir.z, -1f);
                        Color c = light.color * light.intensity;
                        colors[count] = new Vector4(c.r, c.g, c.b, 0f);
                        spotData[count] = new Vector4(0f, 0f, 0f, -1f);
                        count++;
                        break;
                    }

                    case LightType.Point:
                    case LightType.Spot:
                    {
                        float dist = Vector3.Distance(light.transform.position, boardPos);
                        if (dist > light.range) continue;

                        Vector3 pos = light.transform.position;
                        posRange[count] = new Vector4(pos.x, pos.y, pos.z, light.range);
                        Color c = light.color * light.intensity;
                        colors[count] = new Vector4(c.r, c.g, c.b, 0f);

                        if (light.type == LightType.Spot)
                        {
                            Vector3 fwd = light.transform.forward;
                            float cosHalf = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                            spotData[count] = new Vector4(fwd.x, fwd.y, fwd.z, cosHalf);
                        }
                        else
                        {
                            spotData[count] = new Vector4(0f, 0f, 0f, -1f);
                        }
                        count++;
                        break;
                    }
                }
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
