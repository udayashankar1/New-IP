Shader "Custom/Highlight"
{
    Properties
    {
        [HDR] _HighlightColor ("Highlight Color", Color) = (0, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.1, 8.0)) = 2.0
        _RimIntensity ("Rim Intensity", Range(0.0, 10.0)) = 2.0
        _Opacity ("Overall Opacity", Range(0.0, 1.0)) = 1.0
        [Toggle] _Pulse ("Enable Pulse", Float) = 0
        _PulseSpeed ("Pulse Speed", Range(0.0, 10.0)) = 2.0
        _PulseMin ("Pulse Min", Range(0.0, 1.0)) = 0.4
    }

    SubShader
    {
        // Render after opaque geometry, treat as transparent overlay.
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HighlightRim"

            // Additive-style glow that reads on top of the lit surface.
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            float4 _HighlightColor;
            float  _RimPower;
            float  _RimIntensity;
            float  _Opacity;
            float  _Pulse;
            float  _PulseSpeed;
            float  _PulseMin;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldNrm : TEXCOORD0;
                float3 viewDir  : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                o.viewDir  = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNrm);
                float3 v = normalize(i.viewDir);

                // Fresnel rim: strongest at the silhouette edges.
                float rim = 1.0 - saturate(dot(n, v));
                rim = pow(rim, _RimPower) * _RimIntensity;

                // Optional pulsing animation.
                float pulse = 1.0;
                if (_Pulse > 0.5)
                {
                    float s = (sin(_Time.y * _PulseSpeed) * 0.5) + 0.5;
                    pulse = lerp(_PulseMin, 1.0, s);
                }

                float alpha = saturate(rim * _HighlightColor.a * _Opacity * pulse);
                return float4(_HighlightColor.rgb * rim * pulse, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
