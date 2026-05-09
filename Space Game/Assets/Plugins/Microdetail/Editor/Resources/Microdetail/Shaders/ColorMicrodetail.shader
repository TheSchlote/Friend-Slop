Shader "Hidden/ColorMicroderail"
{
    Properties
    {
        _MainTex ("Texture", any) = "" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        HLSLINCLUDE

        #include "UnityCG.cginc"
        #include "Packages/com.unity.terrain-tools/Shaders/TerrainTools.hlsl"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _Color;

        sampler2D _BrushTex;
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
            Name "Color microdetail tool"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                float2 brushUV = PaintContextUVToBrushUV(i.pcUV + _MainTex_TexelSize.xy * 0.5f);
                float4 filter = tex2D(_FilterTex, i.pcUV);

                float oob = all(saturate(brushUV) == brushUV) ? 1.0f : 0.0f;

                float4 value = tex2D(_MainTex, i.pcUV);
                float brushShape = oob * tex2D(_BrushTex, brushUV);
                value = lerp(value, BRUSH_STRENGTH < 0.0f ? float4(1.0f, 1.0f, 1.0f, 1.0f) : _Color, abs(BRUSH_STRENGTH) * brushShape * filter.r);

                return value;
            }

            ENDHLSL
        }
    }
}