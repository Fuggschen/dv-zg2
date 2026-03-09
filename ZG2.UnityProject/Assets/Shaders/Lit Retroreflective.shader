// Standard PBS surface shader with per-pixel retroreflective emission.
// ZG2Retroreflective MonoBehaviour passes nearby light data as uniform arrays;
// the shader evaluates retroreflective alignment at each pixel using worldPos.
Shader "Custom/Lit Retroreflective"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "white" {}

        _Metallic("Metallic", Range(0, 1)) = 0.0
        _Glossiness("Smoothness", Range(0, 1)) = 0.5
        _MetallicGlossMap("Metallic (R) Smoothness (A)", 2D) = "white" {}

        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0

        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1.0

        _RetroreflectiveMask("Retroreflective Mask", 2D) = "white" {}
        _RetroStrength("Retro Strength", Float) = 2.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        #define RETRO_MAX_LIGHTS 8

        sampler2D _MainTex;
        sampler2D _MetallicGlossMap;
        sampler2D _BumpMap;
        sampler2D _OcclusionMap;
        sampler2D _RetroreflectiveMask;

        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        half _BumpScale;
        half _OcclusionStrength;
        half _RetroStrength;

        // Light data arrays set per-frame from ZG2Retroreflective via MaterialPropertyBlock
        // _RetroLightPosRange: xyz = world position (or direction for directional), w = range (-1 = directional)
        // _RetroLightColor:    xyz = color * intensity
        // _RetroLightSpotData: xyz = spot forward direction, w = cos(halfAngle) (-1 = non-spot)
        int _RetroLightCount;
        float4 _RetroLightPosRange[RETRO_MAX_LIGHTS];
        float4 _RetroLightColor[RETRO_MAX_LIGHTS];
        float4 _RetroLightSpotData[RETRO_MAX_LIGHTS];

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_RetroreflectiveMask;
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;

            half4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = mg.r * _Metallic;
            o.Smoothness = mg.a * _Glossiness;

            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale);
            o.Occlusion = LerpOneTo(tex2D(_OcclusionMap, IN.uv_MainTex).g, _OcclusionStrength);

            half mask = tex2D(_RetroreflectiveMask, IN.uv_RetroreflectiveMask).r;

            // Per-pixel retroreflective emission
            float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);
            half retro = 0;

            for (int i = 0; i < _RetroLightCount && i < RETRO_MAX_LIGHTS; i++)
            {
                float4 posRange = _RetroLightPosRange[i];
                float3 lightColor = _RetroLightColor[i].rgb;
                float4 spotData = _RetroLightSpotData[i];

                float3 lightDir;
                float atten = 1.0;

                if (posRange.w < 0) // directional
                {
                    lightDir = posRange.xyz;
                }
                else // point or spot
                {
                    float3 toPixel = IN.worldPos - posRange.xyz;
                    float dist = length(toPixel);
                    if (dist > posRange.w) continue;

                    lightDir = -toPixel / dist;
                    float nd = dist / posRange.w;
                    atten = saturate(1.0 - nd * nd);

                    if (spotData.w >= 0) // spot
                    {
                        float cosAngle = dot(spotData.xyz, toPixel / dist);
                        if (cosAngle < spotData.w) continue;
                        // Smooth outer 30% of cone
                        float cosInner = lerp(1.0, spotData.w, 0.7);
                        if (cosAngle < cosInner)
                            atten *= (cosAngle - spotData.w) / (cosInner - spotData.w);
                    }
                }

                // Retroreflective alignment: peaks when lightDir ≈ viewDir
                float alignment = max(0, dot(lightDir, viewDir));
                alignment = alignment * alignment * alignment;

                retro += alignment * atten * length(lightColor);
            }

            o.Emission = c.rgb * mask * retro * _RetroStrength;
        }
        ENDCG
    }

    FallBack "Standard"
}