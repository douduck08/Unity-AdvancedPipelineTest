using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent (typeof (Camera))]
public class ClusterBasedLighting : MonoBehaviour {

    const int clusterGridSize = 32;
    const int maxActiveClusterNum = 4096;

    [SerializeField] ComputeShader clusterInitCS;
    [SerializeField] Vector3Int clusterNumber;
    [SerializeField] int totalClusterNumber;

    List<Bounds> clusters = new List<Bounds> ();
    CommandBuffer commandBuffer;
    ComputeBuffer activeClusterIds;
    ComputeBuffer bufferWithArgs;

    [Header ("Debug")]
    [SerializeField] bool depthSlice = true;
    [SerializeField] bool allBounds = true;
    [SerializeField] bool activeBounds = true;
    [SerializeField] Vector3 boundsDisplayScale = new Vector3 (1, 1, 1);
    [SerializeField] Shader debugAabbShader;
    [SerializeField] Shader debugActiveShader;

    Mesh mesh;
    Material debugAabbMaterial;
    Material debugActiveMaterial;

    void Start () {
        var camera = GetComponent<Camera> ();
        camera.depthTextureMode = DepthTextureMode.Depth;

        commandBuffer = new CommandBuffer () { name = "Cluster Based Lighting" };
        camera.AddCommandBuffer (CameraEvent.BeforeForwardOpaque, commandBuffer);

        activeClusterIds = new ComputeBuffer (maxActiveClusterNum, sizeof (int), ComputeBufferType.Counter);
        clusterInitCS.SetBuffer (0, "ActiveClusterIds", activeClusterIds);

        bufferWithArgs = new ComputeBuffer (1, 5 * sizeof (uint), ComputeBufferType.IndirectArguments);
    }

    void Update () {
        SetupGPUParams ();
        DispatchCompute ();
        DrawDebug ();
    }

    void SetupGPUParams () {
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

        clusters.Clear ();
        for (int k = 0; k < clusterNumber.z; k++) {
            var kNear = zNear * Mathf.Pow (depthSliceRatio, k);
            var kFar = kNear * depthSliceRatio;
            var kHeight = kFar * depthToHeightRatio;
            var kWidth = kHeight * camera.aspect;

            var center = new Vector3 (0f, 0f, (kFar + kNear) * -0.5f);
            var size = new Vector3 (kWidth, kHeight, kFar - kNear);
            clusters.Add (new Bounds (center, size));
        }

        Shader.SetGlobalVector ("_ClusterDims", new Vector4 (clusterNumber.x, clusterNumber.y, clusterNumber.z, 0));
        Shader.SetGlobalVector ("_ClusterParams", new Vector4 (clusterGridSize, depthSliceRatio, Mathf.Log (depthSliceRatio), zNear));
        Shader.SetGlobalVector ("_ClusterScreenParams", new Vector4 (screenWidth, screenHeight, 1f / screenWidth, 1f / screenHeight));
        Shader.SetGlobalMatrix ("_InvViewMatrix", camera.cameraToWorldMatrix);
        Shader.SetGlobalMatrix ("_InvProjectMatrix", GL.GetGPUProjectionMatrix (camera.projectionMatrix, false).inverse);
    }

    void DispatchCompute () {
        activeClusterIds.SetCounterValue (0);

        commandBuffer.Clear ();
        commandBuffer.SetComputeTextureParam (clusterInitCS, 0, "DepthTexture", BuiltinRenderTextureType.Depth);
        commandBuffer.DispatchCompute (clusterInitCS, 0, clusterNumber.x, clusterNumber.y, 1);
        commandBuffer.CopyCounterValue (activeClusterIds, bufferWithArgs, 1 * sizeof (uint));
    }

    void OnDrawGizmos () {
        if (Application.isPlaying) {
            var camera = GetComponent<Camera> ();
            Gizmos.matrix = camera.cameraToWorldMatrix;

            if (depthSlice) {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < clusters.Count; i++) {
                    Gizmos.DrawWireCube (clusters[i].center, clusters[i].size);
                }
            }

            DrawDebug ();
        }
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

            uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
            args[0] = (uint)mesh.GetIndexCount (subMeshIndex);
            args[2] = (uint)mesh.GetIndexStart (subMeshIndex);
            args[3] = (uint)mesh.GetBaseVertex (subMeshIndex);
            bufferWithArgs.SetData (args);
        }

        if (allBounds) {
            if (debugAabbMaterial == null) {
                debugAabbMaterial = new Material (debugAabbShader);
                debugAabbMaterial.enableInstancing = true;
                debugAabbMaterial.SetColor ("_Color", Color.white);
            }
            debugAabbMaterial.SetVector ("_DisplayScale", boundsDisplayScale);
            Graphics.DrawMeshInstancedProcedural (mesh, 0, debugAabbMaterial, new Bounds (Vector3.zero, Vector3.one * 10000f), totalClusterNumber);
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
