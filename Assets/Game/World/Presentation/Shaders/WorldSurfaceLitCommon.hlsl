#ifndef MINI_CIVILIZATION_WORLD_SURFACE_LIT_COMMON_INCLUDED
#define MINI_CIVILIZATION_WORLD_SURFACE_LIT_COMMON_INCLUDED

struct WorldAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    half4 color       : COLOR;
    float4 surface    : TEXCOORD1;
    float2 textureLayers  : TEXCOORD2;
    float2 textureWeights : TEXCOORD3;
    float2 textureScales  : TEXCOORD4;
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
    nointerpolation float2 textureLayers : TEXCOORD7;
    float2 textureWeights : TEXCOORD8;
    nointerpolation float2 textureScales : TEXCOORD9;
    half3 vertexLighting  : TEXCOORD10;
    half3 vertexSH        : TEXCOORD11;
    half fogFactor        : TEXCOORD12;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

WorldVaryings WorldLitVertex(WorldAttributes input)
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

float2 NormalizeSurfaceWeights(float2 weights)
{
    weights = max(weights, 0.0);
    return weights / max(weights.x + weights.y, 0.00001);
}

half4 SampleSurfaceAlbedo(float2 uv, float2 layers, float2 weights, float2 scales)
{
    #if defined(_WORLD_ALBEDO_ARRAY)
        half4 result = SAMPLE_TEXTURE2D_ARRAY(
            _SurfaceAlbedoArray, sampler_SurfaceAlbedoArray, uv * scales.x, layers.x);
        UNITY_BRANCH
        if (weights.y > 0.00001)
        {
            result =
                result * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(
                    _SurfaceAlbedoArray,
                    sampler_SurfaceAlbedoArray,
                    uv * scales.y,
                    layers.y) * weights.y;
        }
        return result;
    #else
        return half4(1.0, 1.0, 1.0, 1.0);
    #endif
}

half4 SampleSurfaceNormal(float2 uv, float2 layers, float2 weights, float2 scales)
{
    #if defined(_WORLD_NORMAL_ARRAY)
        half4 result = SAMPLE_TEXTURE2D_ARRAY(
            _SurfaceNormalArray, sampler_SurfaceNormalArray, uv * scales.x, layers.x);
        UNITY_BRANCH
        if (weights.y > 0.00001)
        {
            result =
                result * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(
                    _SurfaceNormalArray,
                    sampler_SurfaceNormalArray,
                    uv * scales.y,
                    layers.y) * weights.y;
        }
        return result;
    #else
        return half4(0.5, 0.5, 1.0, 1.0);
    #endif
}

half4 SampleSurfaceMask(float2 uv, float2 layers, float2 weights, float2 scales)
{
    #if defined(_WORLD_MASK_ARRAY)
        half4 result = SAMPLE_TEXTURE2D_ARRAY(
            _SurfaceMaskArray, sampler_SurfaceMaskArray, uv * scales.x, layers.x);
        UNITY_BRANCH
        if (weights.y > 0.00001)
        {
            result =
                result * weights.x +
                SAMPLE_TEXTURE2D_ARRAY(
                    _SurfaceMaskArray,
                    sampler_SurfaceMaskArray,
                    uv * scales.y,
                    layers.y) * weights.y;
        }
        return result;
    #else
        return half4(1.0, 1.0, 1.0, 1.0);
    #endif
}

void InitializeWorldInputData(
    WorldVaryings input,
    half3 normalTS,
    out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
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
}

#endif
