Shader "Hidden/PaintMicroderail"
{
    Properties { _MainTex ("Texture", any) = "" {} }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        HLSLINCLUDE

        #include "UnityCG.cginc"
        #include "Packages/com.unity.terrain-tools/Shaders/TerrainTools.hlsl"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;      // 1/width, 1/height, width, height

        sampler2D _BrushTex;
        float4 _BrushTex_TexelSize; 
        sampler2D _FilterTex;

        float4 _BrushParams;
        #define BRUSH_STRENGTH      (_BrushParams[0])
        #define BRUSH_TARGETHEIGHT  (_BrushParams[1])

        struct appdata_t
        {
            float4 vertex : POSITION;
            float2 pcUV : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 pcUV : TEXCOORD0;
        };

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.pcUV = v.pcUV;
            return o;
        }

        ENDHLSL

        Pass
        {
            Name "Paint microdetail tool"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                float2 brushUV = PaintContextUVToBrushUV(i.pcUV + _MainTex_TexelSize.xy * 0.25f);
                float4 filter = tex2D(_FilterTex, i.pcUV + _MainTex_TexelSize.xy * 0.25f);

                float oob = all(saturate(brushUV) == brushUV) ? 1.0f : 0.0f;

                float value = tex2D(_MainTex, i.pcUV + _MainTex_TexelSize.xy * 0.25f);
                float brushShape = oob * tex2D(_BrushTex, brushUV);
                value = value + BRUSH_STRENGTH * brushShape * filter.r;

                return clamp(value, 0, 1.0f);
            }

            ENDHLSL
        }
    }
}