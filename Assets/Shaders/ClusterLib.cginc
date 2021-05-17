float4 _ClusterDims;
float4 _ClusterParams; // clusterGridSize, depthSliceRatio, logDepthSliceRatio, zNear
float4 _ClusterScreenParams; // screenWidth, screenHeight, 1 / screenWidth, 1 / screenHeight
float4x4 _InvProjectMatrix;

uint CaculateClusterId(uint3 index)
{
    return index.x + (_ClusterDims.x * index.y) + (_ClusterDims.x * _ClusterDims.y * index.z);
}

uint3 CaculateClusterIndex3D(uint id)
{
    // instance id = i + (clusterDimX * j) + (clusterDimX * clusterDimY * k)
    uint i = id % _ClusterDims.x;
    uint j = id % (_ClusterDims.x * _ClusterDims.y) / _ClusterDims.x;
    uint k = id / (_ClusterDims.x * _ClusterDims.y);
    return uint3(i, j, k);
}

uint3 ComputeClusterIndex3D(float2 screenPos, float viewZ)
{
    // i, j = screenPos / clusterGridSize
    uint i = screenPos.x / _ClusterParams.x;
    uint j = screenPos.y / _ClusterParams.x;
    // k = log(z / nearZ) / log(depthSliceRatio)
    uint k = log(viewZ / _ClusterParams.w) / _ClusterParams.z;
    return uint3(i, j, k);
}

float4 ScreenToClipPos(float2 screenPos, float z)
{
    float2 uv = screenPos * _ClusterScreenParams.zw;
    return float4(uv * 2.0 - 1.0, z, 1.0);
}

float4 ClipToViewPos(float4 clipPos)
{
    float4 viewPos = mul(_InvProjectMatrix, clipPos);
    viewPos = viewPos / viewPos.w;
    return viewPos;
}

float2 CaculatePosXYAtDepth(float4 viewPos, float viewDepth)
{
    // viewDepth is positive
    // viewPos.z is negtive
    return viewPos.xy * viewDepth / -viewPos.z;
}

void CaculateClusterAabb(uint id, out float3 center, out float3 size)
{
    uint3 clusterIndex = CaculateClusterIndex3D(id);

    float depthSliceRatio = _ClusterParams.y;
    float kNear = _ClusterParams.w * pow(depthSliceRatio, clusterIndex.z);
    float kFar = kNear * depthSliceRatio;

    float4 pMin = ScreenToClipPos(clusterIndex.xy *_ClusterParams.xx, 0);
    float4 pMax = ScreenToClipPos((clusterIndex.xy + 1) *_ClusterParams.xx, 0);
    pMin = ClipToViewPos(pMin);
    pMax = ClipToViewPos(pMax);

    float2 nearMin = CaculatePosXYAtDepth(pMin, kNear);
    float2 farMin = CaculatePosXYAtDepth(pMin, kFar);
    float2 nearMax = CaculatePosXYAtDepth(pMax, kNear);
    float2 farMax = CaculatePosXYAtDepth(pMax, kFar);
    float2 aabbMin = min(nearMin, min(nearMax, min(farMin, farMax)));
    float2 aabbMax = max(nearMin, max(nearMax, max(farMin, farMax)));

    center = float3((aabbMax + aabbMin) * 0.5, (kFar + kNear) * -0.5);
    size = float3(aabbMax - aabbMin, kFar - kNear);
}