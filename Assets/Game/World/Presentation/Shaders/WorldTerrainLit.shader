Shader "Mini Civilization/World Terrain Lit"
{
    Properties
    {
        [NoScaleOffset] _SurfaceAlbedoArray("Surface Albedo Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceNormalArray("Surface Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceMaskArray("Surface Mask Array", 2DArray) = "" {}
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 0)
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
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
            half _Cull;
        CBUFFER_END

        #include "Assets/Game/World/Presentation/Shaders/WorldSurfaceLitCommon.hlsl"

        half4 WorldLitFragment(WorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 textureWeights = NormalizeSurfaceWeights(input.textureWeights);
            half4 albedoSample = SampleSurfaceAlbedo(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
            half4 normalSample = SampleSurfaceNormal(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
            half4 maskSample = SampleSurfaceMask(
                input.uv, input.textureLayers, textureWeights, input.textureScales);
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
            surfaceData.alpha = 1.0;
            surfaceData.clearCoatMask = 0.0;
            surfaceData.clearCoatSmoothness = 0.0;

            half4 color = UniversalFragmentPBR(inputData, surfaceData);
            color.rgb = MixFog(color.rgb, inputData.fogCoord);
            color.a = 1.0;
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WorldLitVertex
            #pragma fragment WorldLitFragment

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

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
