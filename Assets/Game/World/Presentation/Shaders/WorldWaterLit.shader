Shader "Mini Civilization/World Water Lit"
{
    Properties
    {
        [NoScaleOffset] _SurfaceAlbedoArray("Surface Albedo Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceNormalArray("Surface Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceMaskArray("Surface Mask Array", 2DArray) = "" {}
        _Opacity("Opacity", Range(0, 1)) = 1
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 0)
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "UniversalMaterialType" = "Lit"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D_ARRAY(_SurfaceAlbedoArray);
        SAMPLER(sampler_SurfaceAlbedoArray);
        TEXTURE2D_ARRAY(_SurfaceNormalArray);
        SAMPLER(sampler_SurfaceNormalArray);
        TEXTURE2D_ARRAY(_SurfaceMaskArray);
        SAMPLER(sampler_SurfaceMaskArray);

        CBUFFER_START(UnityPerMaterial)
            half4 _EmissionColor;
            half _Opacity;
            half _Cull;
        CBUFFER_END

        struct WorldAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 tangentOS  : TANGENT;
            float2 uv         : TEXCOORD0;
            half4 color       : COLOR;
            float4 surface    : TEXCOORD1;
            float4 textureLayers  : TEXCOORD2;
            float4 textureWeights : TEXCOORD3;
            float4 textureScales  : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct WorldVaryings
        {
            float4 positionCS     : SV_POSITION;
            float3 positionWS     : TEXCOORD0;
            half3 normalWS        : TEXCOORD1;
            half3 tangentWS       : TEXCOORD2;
            half3 bitangentWS     : TEXCOORD3;
            half4 color           : TEXCOORD4;
            half4 surface         : TEXCOORD5;
            float2 uv             : TEXCOORD6;
            nointerpolation float4 textureLayers : TEXCOORD7;
            float4 textureWeights : TEXCOORD8;
            nointerpolation float4 textureScales : TEXCOORD9;
            half3 vertexLighting  : TEXCOORD10;
            half3 vertexSH        : TEXCOORD11;
            half fogFactor        : TEXCOORD12;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        WorldVaryings WorldWaterVertex(WorldAttributes input)
        {
            WorldVaryings output = (WorldVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

            output.positionCS = positionInputs.positionCS;
            output.positionWS = positionInputs.positionWS;
            output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
            output.tangentWS = normalInputs.tangentWS;
            output.bitangentWS = normalInputs.bitangentWS;
            output.color = input.color;
            output.surface = input.surface;
            output.uv = input.uv;
            output.textureLayers = input.textureLayers;
            output.textureWeights = input.textureWeights;
            output.textureScales = input.textureScales;
            output.vertexLighting = VertexLighting(positionInputs.positionWS, output.normalWS);
            output.vertexSH = SampleSHVertex(output.normalWS);

            #if !defined(_FOG_FRAGMENT)
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            #endif

            return output;
        }

        half4 SampleAlbedoArray(float2 uv, float4 layers, float4 weights, float4 scales)
        {
            return
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceAlbedoArray, sampler_SurfaceAlbedoArray, uv * scales.x, layers.x) * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceAlbedoArray, sampler_SurfaceAlbedoArray, uv * scales.y, layers.y) * weights.y +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceAlbedoArray, sampler_SurfaceAlbedoArray, uv * scales.z, layers.z) * weights.z +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceAlbedoArray, sampler_SurfaceAlbedoArray, uv * scales.w, layers.w) * weights.w;
        }

        half4 SampleNormalArray(float2 uv, float4 layers, float4 weights, float4 scales)
        {
            return
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceNormalArray, sampler_SurfaceNormalArray, uv * scales.x, layers.x) * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceNormalArray, sampler_SurfaceNormalArray, uv * scales.y, layers.y) * weights.y +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceNormalArray, sampler_SurfaceNormalArray, uv * scales.z, layers.z) * weights.z +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceNormalArray, sampler_SurfaceNormalArray, uv * scales.w, layers.w) * weights.w;
        }

        half4 SampleMaskArray(float2 uv, float4 layers, float4 weights, float4 scales)
        {
            return
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceMaskArray, sampler_SurfaceMaskArray, uv * scales.x, layers.x) * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceMaskArray, sampler_SurfaceMaskArray, uv * scales.y, layers.y) * weights.y +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceMaskArray, sampler_SurfaceMaskArray, uv * scales.z, layers.z) * weights.z +
                SAMPLE_TEXTURE2D_ARRAY(_SurfaceMaskArray, sampler_SurfaceMaskArray, uv * scales.w, layers.w) * weights.w;
        }

        half4 WorldWaterFragment(WorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            InputData inputData = (InputData)0;
            inputData.positionWS = input.positionWS;
            inputData.positionCS = input.positionCS;
            float4 textureWeights = max(input.textureWeights, 0.0);
            textureWeights /= max(dot(textureWeights, float4(1.0, 1.0, 1.0, 1.0)), 0.00001);
            half4 albedoSample = SampleAlbedoArray(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
            half4 normalSample = SampleNormalArray(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
            half4 maskSample = SampleMaskArray(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
            half3 normalTS = normalize(normalSample.xyz * 2.0h - 1.0h);
            inputData.normalWS = normalize(
                normalTS.x * normalize(input.tangentWS) +
                normalTS.y * normalize(input.bitangentWS) +
                normalTS.z * NormalizeNormalPerPixel(input.normalWS));
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            inputData.vertexLighting = input.vertexLighting;
            inputData.bakedGI = SampleSHPixel(input.vertexSH, inputData.normalWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = albedoSample.rgb * input.color.rgb;
            surfaceData.specular = half3(0.0, 0.0, 0.0);
            surfaceData.metallic = saturate(input.surface.x * maskSample.r);
            surfaceData.smoothness = saturate(input.surface.y * maskSample.a);
            surfaceData.normalTS = normalTS;
            surfaceData.emission = _EmissionColor.rgb;
            surfaceData.occlusion = saturate(input.surface.z * maskSample.g);
            surfaceData.alpha = saturate(albedoSample.a * input.color.a * _Opacity);
            surfaceData.clearCoatMask = 0.0;
            surfaceData.clearCoatSmoothness = 0.0;

            half4 color = UniversalFragmentPBR(inputData, surfaceData);
            color.rgb = MixFog(color.rgb, inputData.fogCoord);
            color.a = surfaceData.alpha;
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WorldWaterVertex
            #pragma fragment WorldWaterFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
