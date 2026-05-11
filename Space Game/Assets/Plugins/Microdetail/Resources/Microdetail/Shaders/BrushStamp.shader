Shader "Hidden/BrushStamp"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Strength("Strength", Float) = 0.25
        _Mask("Mask", Vector) = (1, 1, 1, 1)
        _MaskTexture("MaskTexture", 2D) = "white" {}
        _MaskTextureScale("MaskTextureScale", Float) = 10.0
        _Threshold("Threshold", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend One One
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Strength;
            float4 _Mask;
            sampler2D _MaskTexture;
            float _MaskTextureScale;
            float _Threshold;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float maskTexture = tex2D(_MaskTexture, i.uv * _MaskTextureScale).r;
                float col = saturate(dot(tex2D(_MainTex, i.uv), _Mask) * _Strength * maskTexture - _Threshold) / (1.0f - _Threshold);
                return col;
            }
            ENDCG
        }
    }
}
