Shader "Hidden/Cainos/Pixel Art Top Down - Village/Render Pass - Gaussian Blur"
{
   SubShader
   {
       Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
       ZWrite Off Cull Off

       Pass
       {
           Name "Gaussian Blur 1st Pass"

           HLSLPROGRAM
           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
           #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

           #pragma vertex Vert
           #pragma fragment Frag

           int _KernelSize;
           float _Sigma;

           float gaussian(float sigma, float pos)
           {
               return (1.0f / sqrt(2.0f * PI * sigma * sigma)) * exp(-(pos * pos) / (2.0f * sigma * sigma));
           }
  
           float4 Frag(Varyings input) : SV_Target0
           {
                float4 output = 0;
                float sum = 0;

                for (int x = -_KernelSize; x <= _KernelSize; ++x)
                {
                    float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + float2(x, 0) * _BlitTexture_TexelSize.xy, _BlitMipLevel);

                    float gauss = gaussian(_Sigma, x);
                    
                    output += c * gauss;
                    sum += gauss;
                }

                return output / sum;
           }

           ENDHLSL
       }

       Pass
       {
           Name "Gaussian Blur 2nd Pass"

           HLSLPROGRAM
           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
           #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

           #pragma vertex Vert
           #pragma fragment Frag

           int _KernelSize;
           float _Sigma;

           float gaussian(float sigma, float pos)
           {
               return (1.0f / sqrt(2.0f * PI * sigma * sigma)) * exp(-(pos * pos) / (2.0f * sigma * sigma));
           }
  
           float4 Frag(Varyings input) : SV_Target0
           {
                float4 output = 0;
                float sum = 0;

                for (int y = -_KernelSize; y <= _KernelSize; ++y)
                {
                    float4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + float2(0, y) * _BlitTexture_TexelSize.xy);
                    float gauss = gaussian(_Sigma, y);
                    
                    output += c * gauss;
                    sum += gauss;
                }

                return output / sum;
           }

           ENDHLSL
       }
   }
}