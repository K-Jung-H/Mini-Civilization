Shader "Mini Civilization/World Terrain Lit"
{
    Properties
    {
        [NoScaleOffset] _SurfaceAlbedoArray("Surface Albedo Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceNormalArray("Surface Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceMaskArray("Surface Mask Array", 2DArray) = "" {}
        [NoScaleOffset] _RoadPatchMap("Road Patch Map", 2D) = "black" {}
        [NoScaleOffset] _RoadPortOffsetMap("Road Port Offset Map", 2D) = "black" {}
        [NoScaleOffset] _RoadShapeMaskArray("Road Shape Mask Array", 2DArray) = "" {}
        [NoScaleOffset] _RoadAlbedoArray("Road Albedo Array", 2DArray) = "" {}
        [NoScaleOffset] _RoadNormalArray("Road Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _RoadSurfaceArray("Road Surface Array", 2DArray) = "" {}
        [HideInInspector] _RoadPatchParameters("Road Patch Parameters", Vector) = (0, 0, 1, 1)
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
        TEXTURE2D(_RoadPatchMap);
        SAMPLER(sampler_RoadPatchMap);
        TEXTURE2D(_RoadPortOffsetMap);
        SAMPLER(sampler_RoadPortOffsetMap);
        TEXTURE2D_ARRAY(_RoadShapeMaskArray);
        SAMPLER(sampler_RoadShapeMaskArray);
        TEXTURE2D_ARRAY(_RoadAlbedoArray);
        SAMPLER(sampler_RoadAlbedoArray);
        TEXTURE2D_ARRAY(_RoadNormalArray);
        SAMPLER(sampler_RoadNormalArray);
        TEXTURE2D_ARRAY(_RoadSurfaceArray);
        SAMPLER(sampler_RoadSurfaceArray);

        CBUFFER_START(UnityPerMaterial)
            half4 _EmissionColor;
            half _Cull;
            float4 _RoadPatchParameters;
        CBUFFER_END

        #include "Assets/Game/World/Presentation/Shaders/WorldSurfaceLitCommon.hlsl"

        struct RoadPatchSample
        {
            half Mask;
            float2 CellUv;
            float SurfaceLayer;
            float TextureScale;
        };

        float2 ResolveRoadBranchUv(float2 cellUv, int direction)
        {
            if (direction == 0)
            {
                return float2(cellUv.y, 1.0 - cellUv.x);
            }

            if (direction == 1)
            {
                return float2(1.0 - cellUv.y, cellUv.x);
            }

            if (direction == 2)
            {
                return float2(1.0 - cellUv.x, 1.0 - cellUv.y);
            }

            return cellUv;
        }

        half SampleRoadBranch(
            float2 cellUv,
            int direction,
            int offset,
            float baseMaskLayer)
        {
            float2 uv = ResolveRoadBranchUv(cellUv, direction);
            int canonicalOffset = direction == 1 || direction == 2
                ? 4 - offset
                : offset;
            if (canonicalOffset > 2)
            {
                canonicalOffset = 4 - canonicalOffset;
                uv.x = 1.0 - uv.x;
            }

            int maskKind = min(canonicalOffset, 2);
            return SAMPLE_TEXTURE2D_ARRAY(
                _RoadShapeMaskArray,
                sampler_RoadShapeMaskArray,
                uv,
                baseMaskLayer + maskKind).r;
        }

        bool TryGetRoadPatchSample(
            WorldVaryings input,
            out RoadPatchSample road)
        {
            road = (RoadPatchSample)0;
            #if !defined(_ROAD_SHAPE_MASK_ARRAY)
                return false;
            #else
                if (input.normalWS.y <= 0.5h)
                {
                    return false;
                }

                float2 patchCell = (input.positionWS.xz
                    - _RoadPatchParameters.xy)
                    / _RoadPatchParameters.z;
                if (any(patchCell < 0.0)
                    || any(patchCell >= _RoadPatchParameters.w))
                {
                    return false;
                }

                float2 cellIndex = floor(patchCell);
                float2 mapUv = (cellIndex + 0.5)
                    / _RoadPatchParameters.w;
                float4 roadData = SAMPLE_TEXTURE2D_LOD(
                    _RoadPatchMap,
                    sampler_RoadPatchMap,
                    mapUv,
                    0);
                int connectionMask = (int)round(roadData.r);
                if (connectionMask == 0)
                {
                    return false;
                }

                float4 offsets = SAMPLE_TEXTURE2D_LOD(
                    _RoadPortOffsetMap,
                    sampler_RoadPortOffsetMap,
                    mapUv,
                    0);
                float2 cellUv = patchCell - cellIndex;
                half mask = 0.0h;
                if ((connectionMask & 1) != 0)
                {
                    mask = max(mask, SampleRoadBranch(
                        cellUv,
                        0,
                        (int)round(offsets.r),
                        roadData.g));
                }

                if ((connectionMask & 2) != 0)
                {
                    mask = max(mask, SampleRoadBranch(
                        cellUv,
                        1,
                        (int)round(offsets.g),
                        roadData.g));
                }

                if ((connectionMask & 4) != 0)
                {
                    mask = max(mask, SampleRoadBranch(
                        cellUv,
                        2,
                        (int)round(offsets.b),
                        roadData.g));
                }

                if ((connectionMask & 8) != 0)
                {
                    mask = max(mask, SampleRoadBranch(
                        cellUv,
                        3,
                        (int)round(offsets.a),
                        roadData.g));
                }

                if (mask <= 0.0001h)
                {
                    return false;
                }

                road.Mask = saturate(mask);
                road.CellUv = cellUv;
                road.SurfaceLayer = roadData.b;
                road.TextureScale = max(roadData.a, 0.01);
                return true;
            #endif
        }

        half4 SampleRoadAlbedo(float2 uv, float layer, float scale)
        {
            #if defined(_ROAD_ALBEDO_ARRAY)
                return SAMPLE_TEXTURE2D_ARRAY(
                    _RoadAlbedoArray,
                    sampler_RoadAlbedoArray,
                    uv * scale,
                    layer);
            #else
                return half4(1.0, 1.0, 1.0, 1.0);
            #endif
        }

        half4 SampleRoadNormal(float2 uv, float layer, float scale)
        {
            #if defined(_ROAD_NORMAL_ARRAY)
                return SAMPLE_TEXTURE2D_ARRAY(
                    _RoadNormalArray,
                    sampler_RoadNormalArray,
                    uv * scale,
                    layer);
            #else
                return half4(0.5, 0.5, 1.0, 1.0);
            #endif
        }

        half4 SampleRoadSurface(float2 uv, float layer, float scale)
        {
            #if defined(_ROAD_SURFACE_ARRAY)
                return SAMPLE_TEXTURE2D_ARRAY(
                    _RoadSurfaceArray,
                    sampler_RoadSurfaceArray,
                    uv * scale,
                    layer);
            #else
                return half4(0.0, 1.0, 1.0, 0.2);
            #endif
        }

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
            RoadPatchSample road;
            if (TryGetRoadPatchSample(input, road))
            {
                albedoSample = lerp(
                    albedoSample,
                    SampleRoadAlbedo(
                        road.CellUv,
                        road.SurfaceLayer,
                        road.TextureScale),
                    road.Mask);
                normalSample = lerp(
                    normalSample,
                    SampleRoadNormal(
                        road.CellUv,
                        road.SurfaceLayer,
                        road.TextureScale),
                    road.Mask);
                maskSample = lerp(
                    maskSample,
                    SampleRoadSurface(
                        road.CellUv,
                        road.SurfaceLayer,
                        road.TextureScale),
                    road.Mask);
            }
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
            #pragma shader_feature_local_fragment _ROAD_SHAPE_MASK_ARRAY
            #pragma shader_feature_local_fragment _ROAD_ALBEDO_ARRAY
            #pragma shader_feature_local_fragment _ROAD_NORMAL_ARRAY
            #pragma shader_feature_local_fragment _ROAD_SURFACE_ARRAY
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
