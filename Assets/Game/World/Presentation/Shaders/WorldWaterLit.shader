Shader "Mini Civilization/World Water Lit"
{
    Properties
    {
        [NoScaleOffset] _SurfaceAlbedoArray("Surface Albedo Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceNormalArray("Surface Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceMaskArray("Surface Mask Array", 2DArray) = "" {}
        _Opacity("Opacity", Range(0, 1)) = 1
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 0)
        _HorizontalFlowSpeed("Horizontal Flow Speed", Float) = 0.18
        _VerticalFlowSpeed("Vertical Flow Speed", Float) = 0.35
        _StaticFlowSpeed("Static Flow Speed", Float) = 0.035
        _StaticFlowDirection("Static Flow Direction", Vector) = (0.707, 0.707, 0, 0)
        [HideInInspector] _Cull("Cull", Float) = 2
        _DepthBiasFactor("Depth Bias Factor", Float) = 0
        _DepthBiasUnits("Depth Bias Units", Float) = 0
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
        #define _SURFACE_TYPE_TRANSPARENT 1
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
            float _HorizontalFlowSpeed;
            float _VerticalFlowSpeed;
            float _StaticFlowSpeed;
            float4 _StaticFlowDirection;
        CBUFFER_END

        #include "Assets/Game/World/Presentation/Shaders/WorldSurfaceLitCommon.hlsl"

        half4 WorldWaterFragment(WorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 flowDirection = input.flow.xy;
            float flowSpeed = _HorizontalFlowSpeed;
            if (input.flow.z > 0.5)
            {
                flowDirection = float2(0.0, -1.0);
                flowSpeed = _VerticalFlowSpeed;
            }
            else if (dot(flowDirection, flowDirection) < 0.0001)
            {
                float2 staticDirection = _StaticFlowDirection.xy;
                flowDirection = staticDirection
                    / max(length(staticDirection), 0.0001);
                flowSpeed = _StaticFlowSpeed;
            }

            float2 animatedUv = input.uv
                + flowDirection * flowSpeed * _Time.y;
            float2 textureWeights = NormalizeSurfaceWeights(input.textureWeights);
            half4 albedoSample = SampleSurfaceAlbedo(
                animatedUv, input.textureLayers, textureWeights, input.textureScales);
            half4 normalSample = SampleSurfaceNormal(
                animatedUv, input.textureLayers, textureWeights, input.textureScales);
            half4 maskSample = SampleSurfaceMask(
                animatedUv, input.textureLayers, textureWeights, input.textureScales);
            half3 normalTS = normalize(normalSample.xyz * 2.0h - 1.0h);
            InputData inputData;
            InitializeWorldInputData(input, normalTS, inputData);

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
            Offset [_DepthBiasFactor], [_DepthBiasUnits]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WorldLitVertex
            #pragma fragment WorldWaterFragment

            #pragma shader_feature_local_fragment _WORLD_ALBEDO_ARRAY
            #pragma shader_feature_local_fragment _WORLD_NORMAL_ARRAY
            #pragma shader_feature_local_fragment _WORLD_MASK_ARRAY
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
