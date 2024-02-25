﻿Shader"Custom/Basic/Wireframe"
{
    Properties
    {
        [PowerSlider(3.0)]
        _WireframeVal ("Edge width", Range(0., 0.5)) = 0.05
        _EdgeColor ("Edge color", color) = (.102, .969, .141, 1.0)
        _MainColor ("Main Color", Color) = (.102, .969, .141, 0.3)
    }

    SubShader
    {
        
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            LOD 100
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
 
            #include "UnityCG.cginc"
 
            struct v2g
            {
                float4 worldPos : SV_POSITION;
            };
 
            struct g2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
            };
 
            v2g vert(appdata_base v)
            {
                v2g o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                return o;
            }
 
            [maxvertexcount(3)]
            void geom(triangle v2g IN[3], inout TriangleStream<g2f> triStream)
            {
                float3 param = float3(0., 0., 0.);
                float EdgeA = length(IN[0].worldPos - IN[1].worldPos);
                float EdgeB = length(IN[1].worldPos - IN[2].worldPos);
                float EdgeC = length(IN[2].worldPos - IN[0].worldPos);
 
                if (EdgeA > EdgeB && EdgeA > EdgeC)
                    param.y = 1.;
                else if (EdgeB > EdgeC && EdgeB > EdgeA)
                    param.x = 1.;
                else
                    param.z = 1.;
 
                g2f o;
                o.pos = mul(UNITY_MATRIX_VP, IN[0].worldPos);
                o.bary = float3(1., 0., 0.) + param;
                triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, IN[1].worldPos);
                o.bary = float3(0., 0., 1.) + param;
                triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, IN[2].worldPos);
                o.bary = float3(0., 1., 0.) + param;
                triStream.Append(o);
            }
 
            float _WireframeVal;
            fixed4 _EdgeColor;
            fixed4 _MainColor;
 
            fixed4 frag(g2f i) : SV_Target
            {
                if (!any(bool3(i.bary.x < _WireframeVal, i.bary.y < _WireframeVal, i.bary.z < _WireframeVal)))
                                //discard;
                    return _MainColor;
 
                return _EdgeColor;
            }
 
            ENDCG
        }
         
    }
}
