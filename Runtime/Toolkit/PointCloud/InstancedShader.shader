Shader "Instanced/URP/InstancedShader" {
    Properties{
        _MainTex("Albedo (RGB)", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader{
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass {

            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 color : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float height : TEXCOORD5;
            };

            StructuredBuffer<float4x4> positionBuffer;

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float4x4 data = positionBuffer[input.instanceID];

                float3 localPosition = input.positionOS.xyz * data._11;
                float3 worldPosition = data._14_24_34 + localPosition;
                
                output.positionWS = worldPosition;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                
                // 保存高度信息用于着色
                output.height = worldPosition.y;
                
                // 不再从缓冲区获取颜色
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                
                return output;
            }

            // 根据高度计算颜色的辅助函数
            half3 GetColorByHeight(float height)
            {
                // 设置渐变的高度范围
                const float minHeight = -1.5;
                const float maxHeight = 1.0;
                
                if (height <= minHeight)
                {
                    return half3(0, 0, 1); // 蓝色
                }
                else if (height >= maxHeight)
                {
                    return half3(1, 0, 0); // 红色
                }
                else 
                {
                    // 平滑渐变
                    float t = (height - minHeight) / (maxHeight - minHeight);
                    
                    // 实现更精细的多段渐变
                    if (t < 0.25) {
                        // 从蓝色到青色
                        float localT = t / 0.25;
                        return lerp(half3(0, 0, 1), half3(0, 1, 1), localT);
                    }
                    else if (t < 0.5) {
                        // 从青色到绿色
                        float localT = (t - 0.25) / 0.25;
                        return lerp(half3(0, 1, 1), half3(0, 1, 0), localT);
                    }
                    else if (t < 0.75) {
                        // 从绿色到黄色
                        float localT = (t - 0.5) / 0.25;
                        return lerp(half3(0, 1, 0), half3(1, 1, 0), localT);
                    }
                    else {
                        // 从黄色到红色
                        float localT = (t - 0.75) / 0.25;
                        return lerp(half3(1, 1, 0), half3(1, 0, 0), localT);
                    }
                }
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 根据高度生成颜色
                half3 colorByHeight = GetColorByHeight(input.height);
                
                // 最终颜色
                half4 finalColor = half4(colorByHeight, 1.0);
                
                return finalColor;
            }

            ENDHLSL
        }
    }
}