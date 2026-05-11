Shader "Hidden/Microdetail/BrushPreview"
{
    SubShader
    {
        ZTest Always Cull Back ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGINCLUDE
            #pragma exclude_renderers gles

            #include "UnityCG.cginc"
            #include "TerrainPreview.cginc"
            #include "Packages/com.unity.terrain-tools/Shaders/TerrainTools.hlsl"
            
            #define CLIP_PREVIEW   clip( IsPcUvPartOfValidTerrainTileTexelSobel( (i.pcPixels + (.5).xx) / _PcPixelRect.zw, _BrushTex_TexelSize.xy * .5 ) - 1 );

            sampler2D _FilterTex;
            sampler2D _BrushTex;
            float4 _BrushTex_TexelSize;

            float _HoleStripeThreshold;
            float _UseAltColor;
            float _IsPaintHolesTool;
        ENDCG

        Pass    // 0
        {
            Name "TerrainPreviewProcedural"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local TERRAINTOOLS_FILTERS_ENABLED

            struct v2f {
                float4 clipPosition : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWorld : TEXCOORD1;
                float3 positionWorldOrig : TEXCOORD2;
                float2 pcPixels : TEXCOORD3;
                float2 brushUV : TEXCOORD4;
            };

            v2f vert(uint vid : SV_VertexID)
            {
                // build a quad mesh, with one vertex per paint context pixel (pcPixel)
                float2 pcPixels = BuildProceduralQuadMeshVertex(vid);

                // compute heightmap UV and sample heightmap
                float2 heightmapUV = PaintContextPixelsToHeightmapUV(pcPixels);
                float heightmapSample = UnpackHeightmap(tex2Dlod(_Heightmap, float4(heightmapUV, 0, 0)));

                // compute brush UV
                float2 brushUV = PaintContextPixelsToBrushUV(pcPixels);

                // compute object position (in terrain space) and world position
                float3 positionObject = PaintContextPixelsToObjectPosition(pcPixels, heightmapSample);
                float3 positionWorld = TerrainObjectToWorldPosition(positionObject);

                v2f o;
                o.uv = heightmapUV;
                o.pcPixels = pcPixels;
                o.positionWorld = positionWorld;
                o.positionWorldOrig = positionWorld;
                o.clipPosition = UnityWorldToClipPos(positionWorld);
                o.brushUV = brushUV;
                return o;
            }

            float SampleTexture(float2 uv, float oob)
            {
                return tex2D(_BrushTex, uv) * oob;
            }

            float ShiftZero(float current)
            {
                if (current > 0.05f)
                    return 100.0f;

                return current;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 texelSize = 0.0025f;
                float endgeThickness = texelSize * 2.0f;

                float oob = all(saturate(i.brushUV) == i.brushUV) ? 1.0f : 0.0f;
                float isEdge = any(i.brushUV < endgeThickness) || any(i.brushUV > (1.0f - endgeThickness)) ? 1.0f : 0.0f;
                
                float center = ShiftZero(SampleTexture(i.brushUV, oob));
                float topLeft = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(-1.0f, 1.0f), oob));
                float top = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(0, 1.0f), oob));
                float topRight = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(1.0f, 1.0f), oob));

                float left = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(-1.0f, 0.0f), oob));
                float right = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(1.0f, 0.0f), oob));

                float bottomLeft = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(-1.0f, -1.0f), oob));
                float bottom = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(0.0f, -1.0f), oob));
                float bottomRight = ShiftZero(SampleTexture(i.brushUV + texelSize * float2(1.0f, -1.0f), oob));

                float Gx = -topLeft + topRight
                            - 2.0f * left + 2.0f * right
                            - bottomLeft + bottomRight;

                float Gy = -topLeft - 2.0f * top - topRight
                            + bottomLeft + 2.0f * bottom + bottomRight;

                float edge = sqrt(Gx * Gx + Gy * Gy);
                float firstHatch = pow(abs(sin((i.brushUV.x + i.brushUV.y) * 200.0f)), 10.0f);
                float secondHatch = pow(abs(sin((i.brushUV.x - i.brushUV.y) * 200.0f)), 10.0f);
                float hatch = saturate(firstHatch + secondHatch);

                return saturate(hatch * (1.0f - saturate(center) * oob) * center * 20.0f + saturate(edge * oob + isEdge * (center > 0.15f))) * 0.65f;

            }
            ENDCG
        }
    }
    Fallback Off
}
