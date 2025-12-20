Shader "Custom/URP_Road_PatchyEdge"
{
    Properties
    {
        _BaseMap ("Asphalt Texture", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (1,1,1,1)
        
        _EdgeSoftness ("Edge Width", Range(0.01, 0.5)) = 0.2
        
        _EdgeMaskTex ("Grunge Mask (R)", 2D) = "white" {}
        _EdgeContrast ("Patch Sharpness", Range(1.0, 20.0)) = 10.0
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            // FIXED: Move out of 'Transparent' bucket. 
            // 2050 draws AFTER Terrain (2000) but BEFORE Transparent Overlays (3000).
            "Queue"="Geometry+50" 
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        // Keep offset to prevent z-fighting with Terrain
        Offset -2, -2 

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 rawUV : TEXCOORD1;
            };

            sampler2D _BaseMap;
            sampler2D _EdgeMaskTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EdgeMaskTex_ST;
                float _EdgeSoftness;
                float _EdgeContrast;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.rawUV = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = tex2D(_BaseMap, IN.uv) * _BaseColor;

                // --- EDGE MASK LOGIC ---
                float distToEdge = min(IN.rawUV.x, 1.0 - IN.rawUV.x);
                float thresholdErosion = 1.0 - smoothstep(0.0, _EdgeSoftness * 1.5, distToEdge);

                float2 maskUV;
                maskUV.x = IN.rawUV.x * _EdgeMaskTex_ST.x + _EdgeMaskTex_ST.z;
                maskUV.y = IN.rawUV.y * _EdgeMaskTex_ST.y + _EdgeMaskTex_ST.w;
                float noiseValue = tex2D(_EdgeMaskTex, maskUV).r;

                float erodedAlpha = noiseValue - thresholdErosion;
                float finalAlpha = smoothstep(0.0, 1.0 / _EdgeContrast, erodedAlpha);
                
                col.a *= finalAlpha;

                return col;
            }
            ENDHLSL
        }
    }
}