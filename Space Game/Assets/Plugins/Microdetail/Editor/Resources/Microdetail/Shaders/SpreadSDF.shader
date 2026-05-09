Shader "Hidden/SpreadSDF"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Step ("Step", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _Step;

            float4 frag (v2f i) : SV_Target
            {
                float2 firstUV = i.uv + _Step;
                float2 secondUV = i.uv - _Step;
                float4 center = tex2D(_MainTex, i.uv);

                float2 thisPosition = _MainTex_TexelSize.zw * i.uv;
                float2 currentPosition = _MainTex_TexelSize.zw * center.xy;
                float2 thisUV = center.xy;

                float currentDistance = distance(thisPosition, currentPosition);

                float2 firstValue = tex2D(_MainTex, firstUV).xy;
                float2 firstPosition = _MainTex_TexelSize.zw * firstValue;
                float firstDistance = distance(thisPosition, firstPosition);
                if (firstDistance < currentDistance)
                {
                    currentDistance = firstDistance;
                    thisUV = firstValue;
                }

                float2 secondValue = tex2D(_MainTex, secondUV).xy;
                float2 secondPosition = _MainTex_TexelSize.zw * secondValue;
                float secondDistance = distance(thisPosition, secondPosition);
                if (secondDistance < currentDistance)
                    thisUV = secondValue;
                
                return float4(thisUV, 0.0f, 0.0f);
            }
            ENDCG
        }
    }
}
