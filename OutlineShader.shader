Shader ""Custom/OutlineShader""
{
    Properties
    {
        _Color(""Main Color"", Color) = (.5,.5,.5,1)
        _OutlineColor(""Outline Color"", Color) = (0,0,0,1)
        _MainTex(""Base (RGB)"", 2D) = ""white"" { }
        _Outline(""Outline width"", Range (.002,.03)) = .005
    }

    CGINCLUDE
    #include ""UnityCG.cginc""
    ENDCG

    SubShader
    {
        Tags {""Queue""=""Overlay"" }
        LOD 100

        Pass
        {
            Name ""OUTLINE""
            Tags { ""LightMode"" = ""Always"" }

            Cull Front

            ZWrite On
            ZTest LEqual
            ColorMask RGB

            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp always
                Pass replace
            }

            Material
            {
                Diffuse [_OutlineColor]
                Ambient [_OutlineColor]
            }
        }
    }

    SubShader
    {
        LOD 100

        CGPROGRAM
        #pragma surface surf Lambert

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb;
        }
        ENDCG
    }

    Fallback ""Diffuse""
}