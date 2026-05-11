Shader "FriendSlop/RockTriplanar"
{
    // Triplanar URP shader for procedural rocks. Samples albedo and normal map
    // three times (once per world-space axis projection), blends by surface
    // normal weights. Avoids the pole pinching and longitudinal seams that
    // spherical-UV mapping produces on icosphere-derived meshes. Vertex UVs are
    // ignored - the shader computes everything from world position + world normal.
    //
    // Designed to be paired with the existing Yughues / similar PBR rock textures
    // (base + normal). Specular/gloss is a simple constant via _Smoothness; the
    // M_YFRM_05 spec/gloss map is intentionally unused to keep the shader cheap.
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 1.0
        _TileScale ("Triplanar Tile Scale", Float) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.30
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.20
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                half4 _BaseColor;
                float _BumpScale;
                float _TileScale;
                float _Smoothness;
                float _Metallic;
                float _AmbientBoost;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vIn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nIn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vIn.positionCS;
                OUT.positionWS = vIn.positionWS;
                OUT.normalWS = nIn.normalWS;
                OUT.fogCoord = ComputeFogFactor(vIn.positionCS.z);
                return OUT;
            }

            float3 SampleTriplanarRGB(TEXTURE2D_PARAM(tex, samp), float3 wpos, float3 absN, float scale)
            {
                float2 uvX = wpos.zy * scale;
                float2 uvY = wpos.xz * scale;
                float2 uvZ = wpos.xy * scale;
                float3 cX = SAMPLE_TEXTURE2D(tex, samp, uvX).rgb;
                float3 cY = SAMPLE_TEXTURE2D(tex, samp, uvY).rgb;
                float3 cZ = SAMPLE_TEXTURE2D(tex, samp, uvZ).rgb;
                return cX * absN.x + cY * absN.y + cZ * absN.z;
            }

            // "Whiteout" triplanar normal blending: per-axis sampled normals are
            // unpacked into tangent space, swizzled into world space based on the
            // axis they came from, then summed with the surface normal added back.
            // Cheap and good enough for rock-like surfaces; not RNM-quality.
            float3 SampleTriplanarNormal(float3 wpos, float3 worldNormal, float3 absN, float scale)
            {
                float2 uvX = wpos.zy * scale;
                float2 uvY = wpos.xz * scale;
                float2 uvZ = wpos.xy * scale;

                half3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 nY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);

                // Swap each tangent normal so its z-axis points along the axis it
                // was projected from. Sign of the world normal flips back-faces.
                half sX = sign(worldNormal.x); if (sX == 0) sX = 1;
                half sY = sign(worldNormal.y); if (sY == 0) sY = 1;
                half sZ = sign(worldNormal.z); if (sZ == 0) sZ = 1;
                nX = half3(nX.z * sX, nX.y, nX.x);
                nY = half3(nY.x, nY.z * sY, nY.y);
                nZ = half3(nZ.x, nZ.y, nZ.z * sZ);

                // Add geometric normal so the blended result stays in the upper
                // hemisphere defined by worldNormal, then renormalize.
                float3 blended = nX * absN.x + nY * absN.y + nZ * absN.z + worldNormal;
                return normalize(blended);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wpos  = IN.positionWS;
                float3 wnorm = normalize(IN.normalWS);
                float3 absN  = abs(wnorm);
                absN /= (absN.x + absN.y + absN.z + 1e-5);

                float3 albedoSample = SampleTriplanarRGB(_BaseMap, sampler_BaseMap, wpos, absN, _TileScale);
                half3 albedo = albedoSample * _BaseColor.rgb;
                float3 perturbedNormal = SampleTriplanarNormal(wpos, wnorm, absN, _TileScale);

                // Lighting: main light + soft shadows + SH ambient + small constant
                // boost so dark sides aren't pitch black.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(wpos));
                half NdotL = saturate(dot(perturbedNormal, mainLight.direction));
                half3 directLight = mainLight.color * (NdotL * mainLight.shadowAttenuation);
                half3 indirectLight = SampleSH(perturbedNormal);
                half3 lighting = directLight + indirectLight + _AmbientBoost;

                // Simple Blinn-Phong-ish spec: half-vector dot perturbedNormal,
                // sharpened by Smoothness. Cheap; rocks don't need full PBR.
                half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - wpos);
                half3 halfDir = normalize(mainLight.direction + viewDir);
                half NdotH = saturate(dot(perturbedNormal, halfDir));
                half specPower = lerp(8.0, 64.0, _Smoothness);
                half spec = pow(NdotH, specPower) * _Smoothness * NdotL * mainLight.shadowAttenuation;

                half3 finalColor = albedo * lighting + spec * mainLight.color;
                finalColor = MixFog(finalColor, IN.fogCoord);
                return half4(finalColor, 1);
            }

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes { float4 positionOS : POSITION; };
            struct DepthVaryings   { float4 positionCS : SV_POSITION; };

            DepthVaryings depthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 depthFrag(DepthVaryings IN) : SV_TARGET { return 0; }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
