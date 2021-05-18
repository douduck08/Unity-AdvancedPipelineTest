using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent (typeof (Camera))]
public class ClusterBasedLighting : MonoBehaviour {

    const int clusterGridSize = 32;
    const int maxClusterNum = 2048 * 512;
    const int maxActiveClusterNum = 4096;
    const int maxLightNum = 1024;
    const int maxLightNumPerCluster = 32;

    [SerializeField] ComputeShader activeClusterInitCS;
    [SerializeField] ComputeShader clearClusterOffsetCS;
    [SerializeField] ComputeShader lightCullingCS;
    [SerializeField] Vector3Int clusterNumber;
    [SerializeField] int totalClusterNumber;
    int globalLightCount;

    struct PointLight {
        public Vector4 position; // xyz: world position, w: radius
        public Vector4 color;
    }

    CommandBuffer commandBuffer;
    ComputeBuffer bufferWithArgs;
    ComputeBuffer indirectBuffer;
    ComputeBuffer activeClusterIds;
    ComputeBuffer globalLightList;
    ComputeBuffer clusterLightOffsetList;
    ComputeBuffer lightIndexList;
    ComputeBuffer lightIndexListCounter;

    [Header ("Demo Settings")]
    [SerializeField] RandomLight lights;

    [Header ("Debug")]
    [SerializeField] bool depthSlice = true;
    [SerializeField] bool allBounds = true;
    [SerializeField] bool activeBounds = true;
    [SerializeField] Vector3 boundsDisplayScale = new Vector3 (1, 1, 1);
    [SerializeField] Shader debugAllShader;
    [SerializeField] Shader debugActiveShader;
    [SerializeField] ComputeShader debugMaxLightPerClusterCS;

    List<Bounds> depthSlices = new List<Bounds> ();
    Mesh mesh;
    Material debugAllMaterial;
    Material debugActiveMaterial;
    RenderTexture debugRT;

    void Start () {
        var camera = GetComponent<Camera> ();
        camera.depthTextureMode = DepthTextureMode.Depth;
        InitDispatchResources (camera);
        UpdateLightData (lights.GetLights ());
    }

    void Update () {
        SetupDispatchParams ();
        DispatchCompute ();
        DrawDebug ();
    }

    void InitDispatchResources (Camera camera) {
        commandBuffer = new CommandBuffer () { name = "Cluster Based Lighting" };
        camera.AddCommandBuffer (CameraEvent.BeforeForwardOpaque, commandBuffer);

        bufferWithArgs = new ComputeBuffer (1, 5 * sizeof (uint), ComputeBufferType.IndirectArguments);
        indirectBuffer = new ComputeBuffer (1, 3 * sizeof (uint), ComputeBufferType.IndirectArguments);
        var indirectArgs = new uint[3] { 1, 1, 1 };
        indirectBuffer.SetData (indirectArgs);

        activeClusterIds = new ComputeBuffer (maxActiveClusterNum, 1 * sizeof (uint), ComputeBufferType.Counter);
        globalLightList = new ComputeBuffer (maxLightNum, 8 * sizeof (float), ComputeBufferType.Default);
        clusterLightOffsetList = new ComputeBuffer (maxClusterNum, 2 * sizeof (uint), ComputeBufferType.Default);
        lightIndexList = new ComputeBuffer (maxLightNumPerCluster * maxClusterNum, 1 * sizeof (uint), ComputeBufferType.Default);
        lightIndexListCounter = new ComputeBuffer (1, 1 * sizeof (uint), ComputeBufferType.Default);

        activeClusterInitCS.SetBuffer (0, "ActiveClusterIds", activeClusterIds);
        clearClusterOffsetCS.SetBuffer (0, "ClusterLightOffsetList", clusterLightOffsetList);
        lightCullingCS.SetBuffer (0, "GlobalLightList", globalLightList);
        lightCullingCS.SetBuffer (0, "ClusterLightOffsetList", clusterLightOffsetList);
        lightCullingCS.SetBuffer (0, "LightIndexList", lightIndexList);
        lightCullingCS.SetBuffer (0, "LightIndexListCounter", lightIndexListCounter);

        debugMaxLightPerClusterCS.SetBuffer (0, "ClusterLightOffsetList", clusterLightOffsetList);
    }

    void UpdateLightData (List<Light> lights) {
        globalLightCount = Mathf.Min (lights.Count, maxLightNum);
        var data = new PointLight[maxLightNum];
        for (int i = 0; i < globalLightCount; i++) {
            var position = lights[i].transform.position;
            data[i].position = new Vector4 (position.x, position.y, position.z, lights[i].range);
            data[i].color = lights[i].color * lights[i].intensity;
        }
        globalLightList.SetData (data);
    }

    void SetupDispatchParams () {
        var camera = GetComponent<Camera> ();
        var screenHeight = camera.pixelHeight;
        var screenWidth = camera.pixelWidth;
        clusterNumber.x = Mathf.CeilToInt (1f * screenWidth / clusterGridSize);
        clusterNumber.y = Mathf.CeilToInt (1f * screenHeight / clusterGridSize);

        var zNear = camera.nearClipPlane;
        var zFar = camera.farClipPlane;
        var depthToHeightRatio = Mathf.Tan (camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2f;
        var depthSliceRatio = 1 + depthToHeightRatio / clusterNumber.y;
        clusterNumber.z = Mathf.CeilToInt (Mathf.Log (zFar / zNear) / Mathf.Log (depthSliceRatio));
        totalClusterNumber = clusterNumber.x * clusterNumber.y * clusterNumber.z;

        depthSlices.Clear ();
        for (int k = 0; k < clusterNumber.z; k++) {
            var kNear = zNear * Mathf.Pow (depthSliceRatio, k);
            var kFar = kNear * depthSliceRatio;
            var kHeight = kFar * depthToHeightRatio;
            var kWidth = kHeight * camera.aspect;

            var center = new Vector3 (0f, 0f, (kFar + kNear) * -0.5f);
            var size = new Vector3 (kWidth, kHeight, kFar - kNear);
            depthSlices.Add (new Bounds (center, size));
        }

        Shader.SetGlobalVector ("_ClusterDims", new Vector4 (clusterNumber.x, clusterNumber.y, clusterNumber.z, 0));
        Shader.SetGlobalVector ("_ClusterParams", new Vector4 (clusterGridSize, depthSliceRatio, Mathf.Log (depthSliceRatio), zNear));
        Shader.SetGlobalVector ("_ClusterScreenParams", new Vector4 (screenWidth, screenHeight, 1f / screenWidth, 1f / screenHeight));
        Shader.SetGlobalMatrix ("_ViewMatrix", camera.worldToCameraMatrix);
        Shader.SetGlobalMatrix ("_InvViewMatrix", camera.cameraToWorldMatrix);
        Shader.SetGlobalMatrix ("_InvProjectMatrix", GL.GetGPUProjectionMatrix (camera.projectionMatrix, false).inverse);
        Shader.SetGlobalInt ("_GlobalLightCount", globalLightCount);
    }

    void DispatchCompute () {
        activeClusterIds.SetCounterValue (0);
        lightIndexListCounter.SetData (new uint[1] { 0 });
        commandBuffer.Clear ();

        // find out active cluster
        commandBuffer.SetComputeTextureParam (activeClusterInitCS, 0, "DepthTexture", BuiltinRenderTextureType.Depth);
        commandBuffer.DispatchCompute (activeClusterInitCS, 0, clusterNumber.x, clusterNumber.y, 1);
        commandBuffer.CopyCounterValue (activeClusterIds, bufferWithArgs, 1 * sizeof (uint));

        // clear cluster light offset buffer
        commandBuffer.DispatchCompute (clearClusterOffsetCS, 0, maxClusterNum / 64, 1, 1);

        // culling lights with active cluster
        commandBuffer.CopyCounterValue (activeClusterIds, indirectBuffer, 0 * sizeof (uint));
        commandBuffer.DispatchCompute (lightCullingCS, 0, indirectBuffer, 0);
    }

    void OnDrawGizmos () {
        if (Application.isPlaying) {
            var camera = GetComponent<Camera> ();
            Gizmos.matrix = camera.cameraToWorldMatrix;

            if (depthSlice) {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < depthSlices.Count; i++) {
                    Gizmos.DrawWireCube (depthSlices[i].center, depthSlices[i].size);
                }
            }

            DrawDebug ();
        }
    }

    void OnRenderImage (RenderTexture src, RenderTexture dest) {
        if (debugRT == null) {
            debugRT = new RenderTexture (src.descriptor);
            debugRT.enableRandomWrite = true;
            debugRT.Create ();
        }

        var camera = GetComponent<Camera> ();
        debugMaxLightPerClusterCS.SetTexture (0, "Source", src);
        debugMaxLightPerClusterCS.SetTexture (0, "Result", debugRT);
        debugMaxLightPerClusterCS.Dispatch (0, camera.pixelWidth, camera.pixelHeight, 1);

        Graphics.Blit (debugRT, dest);
    }

    void DrawDebug () {
        if (mesh == null) {
            var cube = GameObject.CreatePrimitive (PrimitiveType.Cube);
            mesh = Instantiate (cube.GetComponent<MeshFilter> ().sharedMesh);
            Destroy (cube);

            var indices = new int[] {
                0, 1, 0, 2, 1, 3, 2, 3,
                4, 5, 4, 6, 5, 7, 6, 7,
                0, 6, 1, 7, 2, 4, 3, 5
            };
            var subMeshIndex = 0;
            mesh.SetIndices (indices, MeshTopology.Lines, subMeshIndex);

            var args = new uint[5] { 0, 0, 0, 0, 0 };
            args[0] = (uint)mesh.GetIndexCount (subMeshIndex);
            args[2] = (uint)mesh.GetIndexStart (subMeshIndex);
            args[3] = (uint)mesh.GetBaseVertex (subMeshIndex);
            bufferWithArgs.SetData (args);
        }

        if (allBounds) {
            if (debugAllMaterial == null) {
                debugAllMaterial = new Material (debugAllShader);
                debugAllMaterial.enableInstancing = true;
                debugAllMaterial.SetColor ("_Color", Color.white);
            }
            debugAllMaterial.SetVector ("_DisplayScale", boundsDisplayScale);
            Graphics.DrawMeshInstancedProcedural (mesh, 0, debugAllMaterial, new Bounds (Vector3.zero, Vector3.one * 10000f), totalClusterNumber);
        }

        if (activeBounds) {
            if (debugActiveMaterial == null) {
                debugActiveMaterial = new Material (debugActiveShader);
                debugActiveMaterial.enableInstancing = true;
                debugActiveMaterial.SetColor ("_Color", Color.red);
            }
            debugActiveMaterial.SetVector ("_DisplayScale", boundsDisplayScale);
            debugActiveMaterial.SetBuffer ("_ActiveClusterIds", activeClusterIds);
            Graphics.DrawMeshInstancedIndirect (mesh, 0, debugActiveMaterial, new Bounds (Vector3.zero, Vector3.one * 10000f), bufferWithArgs, 0);
        }
    }
}
