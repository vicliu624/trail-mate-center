Shader "TrailMateCenter/OverlayRamp"
{
    Properties
    {
        _MainTex ("Raster", 2D) = "white" {}
        _RampTex ("Ramp", 2D) = "white" {}
        _ValueMin ("Value Min", Float) = 0
        _ValueMax ("Value Max", Float) = 1
        _ValueScale ("Value Scale", Float) = 1
        _ValueOffset ("Value Offset", Float) = 0
        _NoData ("No Data", Float) = -10
        _HasNoData ("Has NoData", Float) = 0
        _ClassBreakCount ("Class Break Count", Float) = 0
        _Alpha ("Alpha", Float) = 0.6
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _RampTex;
            float4 _MainTex_ST;
            float _ValueMin;
            float _ValueMax;
            float _ValueScale;
            float _ValueOffset;
            float _NoData;
            float _HasNoData;
            float _ClassBreakCount;
            float _ClassBreaks[16];
            float _Alpha;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 sampleColor = tex2D(_MainTex, i.uv);
                float encoded = sampleColor.r;
                float value = encoded * _ValueScale + _ValueOffset;

                if (_HasNoData > 0.5 && abs(value - _NoData) < 1e-3)
                    discard;

                float denom = max(1e-6, _ValueMax - _ValueMin);
                float t = saturate((value - _ValueMin) / denom);
                if (_ClassBreakCount > 0.5)
                {
                    int count = min(16, (int)_ClassBreakCount);
                    int binIndex = 0;
                    [loop]
                    for (int i = 0; i < count; i++)
                    {
                        if (value > _ClassBreaks[i])
                            binIndex = i + 1;
                    }
                    float bins = max(1.0, count + 1.0);
                    t = saturate((binIndex + 0.5) / bins);
                }
                fixed4 ramp = tex2D(_RampTex, float2(t, 0.5));
                ramp.a *= _Alpha;
                return ramp;
            }
            ENDCG
        }
    }
}
