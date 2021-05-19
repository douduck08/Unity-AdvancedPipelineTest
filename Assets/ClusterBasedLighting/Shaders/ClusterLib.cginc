float4 _ClusterDims;
float4 _ClusterParams; // clusterGridSize, depthSliceRatio, logDepthSliceRatio, zNear
float4 _ClusterScreenParams; // screenWidth, screenHeight, 1 / screenWidth, 1 / screenHeight
float4x4 _ViewMatrix;
float4x4 _InvViewMatrix;
float4x4 _InvProjectMatrix;
int _GlobalLightCount;

uint CaculateClusterId(uint3 index)
{
    return index.x + (_ClusterDims.x * index.y) + (_ClusterDims.x * _ClusterDims.y * index.z);
}

uint3 CaculateClusterIndex3D(uint id)
{
    // instance id = i + (clusterDimX * j) + (clusterDimX * clusterDimY * k)
    uint i = id % _ClusterDims.x;
    uint j = (id / _ClusterDims.x) % _ClusterDims.y;
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

float3 CaculateViewPosAtDepth(float4 viewPos, float viewDepth)
{
    // viewDepth is positive
    // viewPos.z is negtive
    viewDepth *= -1.0;
    return float3(viewPos.xy * viewDepth / viewPos.z, viewDepth);
}

void CaculateClusterAabb(uint id, out float3 aabbMin, out float3 aabbMax)
{
    uint3 clusterIndex = CaculateClusterIndex3D(id);

    float depthSliceRatio = _ClusterParams.y;
    float kNear = _ClusterParams.w * pow(depthSliceRatio, clusterIndex.z);
    float kFar = kNear * depthSliceRatio;

    float4 pLeftBottom = ScreenToClipPos(clusterIndex.xy * _ClusterParams.xx, 0);
    float4 pRightTop = ScreenToClipPos((clusterIndex.xy + 1) * _ClusterParams.xx, 0);
    pLeftBottom = ClipToViewPos(pLeftBottom);
    pRightTop = ClipToViewPos(pRightTop);

    float3 nearLB = CaculateViewPosAtDepth(pLeftBottom, kNear);
    float3 farLB = CaculateViewPosAtDepth(pLeftBottom, kFar);
    float3 nearRT = CaculateViewPosAtDepth(pRightTop, kNear);
    float3 farRT = CaculateViewPosAtDepth(pRightTop, kFar);

    aabbMin = min(nearLB, min(nearRT, min(farLB, farRT)));
    aabbMax = max(nearLB, max(nearRT, max(farLB, farRT)));
}