Shader "FriendSlop/PlanetTerrainTriplanar"
{
    // Procedural-planet terrain shader. Reads vertex UV.y as normalized elevation
    // (the same channel PlanetTerrainGenerator writes per vertex), computes 4 band
    // weights using the boundaries + sharpness shared with the C# side, samples 4
    // detail textures via triplanar projection on world position, and multiplies
    // the blended detail luminance against the elevation gradient color. Lambert
    // lighting + ambient SH keeps the shader cheap and predictable.
    Properties
    {
        [MainTexture] _GradientTex ("Elevation Gradient", 2D) = "white" {}
        _RockTex  ("Detail: Rock",  2D) = "gray" {}
        _DirtTex  ("Detail: Dirt",  2D) = "gray" {}
        _GrassTex ("Detail: Grass", 2D) = "gray" {}
        _PeakTex  ("Detail: Peak",  2D) = "gray" {}
        _TriplanarTileScale ("Triplanar Tile Scale", Float) = 0.25
        _BandBoundary01 ("Band Boundary 01", Range(0, 1)) = 0.15
        _BandBoundary12 ("Band Boundary 12", Range(0, 1)) = 0.40
        _BandBoundary23 ("Band Boundary 23", Range(0, 1)) = 0.90
        _BandSharpness ("Band Sharpness", Range(0, 1)) = 0.7
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.25
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
                float4 _GradientTex_ST;
                float4 _RockTex_ST;
                float4 _DirtTex_ST;
                float4 _GrassTex_ST;
                float4 _PeakTex_ST;
                float _TriplanarTileScale;
                float _BandBoundary01;
                float _BandBoundary12;
                float _BandBoundary23;
                float _BandSharpness;
                float _DetailStrength;
                float _AmbientBoost;
            CBUFFER_END

            TEXTURE2D(_GradientTex); SAMPLER(sampler_GradientTex);
            TEXTURE2D(_RockTex);     SAMPLER(sampler_RockTex);
            TEXTURE2D(_DirtTex);     SAMPLER(sampler_DirtTex);
            TEXTURE2D(_GrassTex);    SAMPLER(sampler_GrassTex);
            TEXTURE2D(_PeakTex);     SAMPLER(sampler_PeakTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vIn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nIn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vIn.positionCS;
                OUT.positionWS = vIn.positionWS;
                OUT.normalWS = nIn.normalWS;
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(vIn.positionCS.z);
                return OUT;
            }

            // Per-boundary transition half-width: capped by half the smaller adjacent
            // band so the narrowest band still gets a real plateau. Mirrors the C#
            // TransitionHalf in PlanetTerrainGenerator.SampleStops.
            float TransitionHalf(float sharpness, float left, float right)
            {
                float maxHalf = min(left, right) * 0.5;
                return maxHalf * (1.0 - sharpness);
            }

            float SampleTriplanarLuminance(TEXTURE2D_PARAM(tex, samp), float3 wpos, float3 absN, float scale)
            {
                float2 uvX = wpos.zy * scale;
                float2 uvY = wpos.xz * scale;
                float2 uvZ = wpos.xy * scale;
                float lX = SAMPLE_TEXTURE2D(tex, samp, uvX).r;
                float lY = SAMPLE_TEXTURE2D(tex, samp, uvY).r;
                float lZ = SAMPLE_TEXTURE2D(tex, samp, uvZ).r;
                return lX * absN.x + lY * absN.y + lZ * absN.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wpos  = IN.positionWS;
                float3 wnorm = normalize(IN.normalWS);
                float3 absN  = abs(wnorm);
                absN /= (absN.x + absN.y + absN.z + 1e-5);
                float t = saturate(IN.uv.y);

                // Defensively order the boundaries; the user's inspector values
                // could cross over and we don't want a divide-by-zero or NaN.
                float b1 = clamp(_BandBoundary01, 0.001, 0.999);
                float b2 = clamp(_BandBoundary12, b1 + 0.001, 0.999);
                float b3 = clamp(_BandBoundary23, b2 + 0.001, 0.999);

                float wRock  = b1;
                float wDirt  = b2 - b1;
                float wGrass = b3 - b2;
                float wPeak  = 1.0 - b3;

                float halfRD = TransitionHalf(_BandSharpness, wRock,  wDirt);
                float halfDG = TransitionHalf(_BandSharpness, wDirt,  wGrass);
                float halfGP = TransitionHalf(_BandSharpness, wGrass, wPeak);

                float ramp01 = smoothstep(b1 - halfRD, b1 + halfRD, t);
                float ramp12 = smoothstep(b2 - halfDG, b2 + halfDG, t);
                float ramp23 = smoothstep(b3 - halfGP, b3 + halfGP, t);

                float weightRock  = 1.0 - ramp01;
                float weightDirt  = ramp01 - ramp12;
                float weightGrass = ramp12 - ramp23;
                float weightPeak  = ramp23;

                float scale = _TriplanarTileScale;
                float lRock  = SampleTriplanarLuminance(_RockTex,  sampler_RockTex,  wpos, absN, scale);
                float lDirt  = SampleTriplanarLuminance(_DirtTex,  sampler_DirtTex,  wpos, absN, scale);
                float lGrass = SampleTriplanarLuminance(_GrassTex, sampler_GrassTex, wpos, absN, scale);
                float lPeak  = SampleTriplanarLuminance(_PeakTex,  sampler_PeakTex,  wpos, absN, scale);

                float detailL = lRock * weightRock + lDirt * weightDirt + lGrass * weightGrass + lPeak * weightPeak;

                // Sample the 2x256 elevation strip at uv.x = 0.5 (centered horizontally).
                float3 gradientRGB = SAMPLE_TEXTURE2D(_GradientTex, sampler_GradientTex, float2(0.5, t)).rgb;

                // Detail acts as a brightness modulator centered on 1.0 so a 50%
                // gray detail leaves the gradient alone. Strength=0 returns pure
                // gradient; strength=1 makes the detail fully drive luminance.
                float detailMod = lerp(1.0, detailL * 2.0, _DetailStrength);
                half3 albedo = gradientRGB * detailMod;

                // Lighting: main light + soft shadows + SH ambient + small constant
                // boost so dark sides aren't pitch black on the procedural planet.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(wpos));
                half NdotL = saturate(dot(wnorm, mainLight.direction));
                half3 directLight = mainLight.color * (NdotL * mainLight.shadowAttenuation);
                half3 indirectLight = SampleSH(wnorm);
                half3 lighting = directLight + indirectLight + _AmbientBoost;

                half3 finalColor = albedo * lighting;
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
