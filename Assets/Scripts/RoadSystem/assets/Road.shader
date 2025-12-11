Shader "Custom/URP_Road_PatchyEdge"
{
    Properties
    {
        _BaseMap ("Asphalt Texture", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (1,1,1,1)
        
        _EdgeSoftness ("Edge Width", Range(0.01, 0.5)) = 0.2
        
        _EdgeMaskTex ("Grunge Mask (R)", 2D) = "white" {}
        // REMOVED _MaskScale because we now use the native Tiling/Offset
        _EdgeContrast ("Patch Sharpness", Range(1.0, 20.0)) = 10.0
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
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
                float2 uv : TEXCOORD0;      // Tiled UVs for Asphalt
                float2 rawUV : TEXCOORD1;   // Raw UVs for Mask Calc
            };

            sampler2D _BaseMap;
            sampler2D _EdgeMaskTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                
                // NEW: This variable catches the Tiling/Offset from the Inspector
                float4 _EdgeMaskTex_ST; 
                
                float _EdgeSoftness;
                float _EdgeContrast;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                
                // 1. Asphalt Tiling (Main Map)
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                
                // 2. Pass Raw UVs (Unmodified)
                OUT.rawUV = IN.uv;
                
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = tex2D(_BaseMap, IN.uv) * _BaseColor;

                // --- EDGE MASK LOGIC ---

                // 1. Distance to Edge (Using Raw X so it's always 0..1 across road)
                float distToEdge = min(IN.rawUV.x, 1.0 - IN.rawUV.x);
                float thresholdErosion = 1.0 - smoothstep(0.0, _EdgeSoftness * 1.5, distToEdge);

                // 2. Calculate Mask UVs
                // We manually apply the Tiling/Offset (_ST) from the Inspector to the Raw UVs.
                // We use rawUV.y for length so it is independent of Asphalt tiling.
                // We use rawUV.x for width.
                float2 maskUV;
                maskUV.x = IN.rawUV.x * _EdgeMaskTex_ST.x + _EdgeMaskTex_ST.z;
                maskUV.y = IN.rawUV.y * _EdgeMaskTex_ST.y + _EdgeMaskTex_ST.w;

                float noiseValue = tex2D(_EdgeMaskTex, maskUV).r;

                // 3. Erode & Sharpen
                float erodedAlpha = noiseValue - thresholdErosion;
                float finalAlpha = smoothstep(0.0, 1.0 / _EdgeContrast, erodedAlpha);
                
                col.a *= finalAlpha;

                return col;
            }
            ENDHLSL
        }
    }
}