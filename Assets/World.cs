using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class World : MonoBehaviour
{
    // Temp
    public GameObject locator;
    public Material colliderMaterial;

    // World Settings
    private int chunkDistance = 14;

    // Player Information
    public GameObject playerGameObject;
    public Transform playerWorldPosition;
    private Vector3 playerWorldPositionLast;
    private Vector2Int playerChunkPosition = new Vector2Int(2147483647, 2147483647);
    private Vector2Int playerChunkPositionLast = new Vector2Int(2147483647, 2147483647);
    private bool didPlayerEnterChunk = false;
    private Vector2Int chunkLoadingOrigin = new Vector2Int(2147483647, 2147483647);
    private Vector2Int chunkLoadingOriginNew = new Vector2Int(2147483647, 2147483647);
    private bool didChunkLoadingOriginChange = false;

    // Materials
    public Material terrainMaterial;
    public Texture2D[] chunkTexturesColor;
    public Texture2D[] chunkTexturesHeight;

    // Chunk Storage
    private Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private Dictionary<Vector2Int, ChunkObject> chunkObjects = new Dictionary<Vector2Int, ChunkObject>();

    // Generate Maps Job
    private int generateMapsJobCount = 2;
    private GenerateMapsJob[] generateMapsJob;
    private JobHandle[] generateMapsJobHandle;
    private bool[] generateMapsJobDone;

    // Generate Voxels Job
    private int generateVoxelsJobCount = 2;
    private GenerateVoxelsJob[] generateVoxelsJob;
    private JobHandle[] generateVoxelsJobHandle;
    private bool[] generateVoxelsJobDone;

    // Generate Lights Job
    private int generateLightsJobCount = 2;
    private GenerateLightsJob[] generateLightsJob;
    private JobHandle[] generateLightsJobHandle;
    private bool[] generateLightsJobDone;

    // Generate Mesh Job
    private int generateMeshJobCount = 3;
    private GenerateMeshJob[] generateMeshJob;
    private JobHandle[] generateMeshJobHandle;
    private bool[] generateMeshJobDone;

    // Bake Physics Mesh Job
    private BakePhysicsMeshJob bakePhysicsMeshJob;
    private JobHandle bakePhysicsMeshJobHandle;
    private bool bakePhysicsMeshJobDone;

    // Light Removal Job
    private LightRemovalJob lightRemovalJob;
    private JobHandle lightRemovalJobHandle;
    private bool lightRemovalJobDone = true;

    // Timer
    private System.Diagnostics.Stopwatch unloadStopwatch = new System.Diagnostics.Stopwatch();

    private bool isUpdateChunkQueuePending = false;
    private bool isUnloadChunksPending = false;
    private bool isLoadChunksPending = false;

    private Queue<Vector2Int> generateMapsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateVoxelsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateLightsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateMeshQueue = new Queue<Vector2Int>();
    private Queue<BakePhysicsMeshData> bakePhysicsMeshQueue = new Queue<BakePhysicsMeshData>();

    private Queue<Vector2Int> chunksToDestroy = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToDeactivate = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToLoad = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToActivate = new Queue<Vector2Int>();

    // Chunk Loading
    private List<Vector2Int> chunkLoadingOrder = new List<Vector2Int>();

    // Diagnostics
    private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

    private void Start()
    {
        this.InitChunkLoadingOrder();
        this.InitMaterial();
        this.InitJobs();
    }

    private void Update()
    {
        this.UpdatePlayerPosition();
        this.UpdateChunkLoadingOrigin();
        this.UpdateChunkQueue();
        this.UnloadChunks();
        this.LoadChunks();
        this.ActivateChunks();
        this.GenerateMaps();
        this.GenerateVoxels();
        this.GenerateLights();
        this.GenerateMeshes();
        this.BakePhysicsMeshes();
        this.UpdatePlayerPositionLast();
    }

    private void OnApplicationQuit()
    {
        this.DisposeJobs();

        foreach (KeyValuePair<Vector2Int, ChunkObject> chunkObject in this.chunkObjects)
        {
            chunkObject.Value.Destroy();
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// INITIALIZATION ///////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void InitChunkLoadingOrder()
    {
        this.chunkLoadingOrder.Clear();

        int arraySize = (this.chunkDistance + 2) * 2 + 1;
        arraySize *= arraySize;

        Vector2Int currentPosition = Vector2Int.zero;
        int iterator = 1;
        int i = 0;

        this.chunkLoadingOrder.Add(currentPosition);
        while (i < arraySize)
        {
            for (int j = 1; j <= iterator; j++)
            {
                if (iterator % 2 == 1)
                    currentPosition.y++;
                else
                    currentPosition.y--;

                this.chunkLoadingOrder.Add(currentPosition);
                i++;
            }

            for (int j = 1; j <= iterator; j++)
            {
                if (iterator % 2 == 1)
                    currentPosition.x++;
                else
                    currentPosition.x--;

                this.chunkLoadingOrder.Add(currentPosition);
                i++;
            }

            iterator++;
        }
    }

    private void InitMaterial()
    {
        Texture2DArray texColor = new Texture2DArray(1024, 1024, 6, TextureFormat.RGBA32, true, false);
        texColor.filterMode = FilterMode.Trilinear;
        texColor.anisoLevel = 8;
        texColor.wrapMode = TextureWrapMode.Repeat;
        int count = 0;
        foreach (Texture2D texture in this.chunkTexturesColor)
        {
            texColor.SetPixels(texture.GetPixels(), count, 0);
            count++;
        }
        texColor.Apply();

        Texture2DArray texHeight = new Texture2DArray(1024, 1024, 6, TextureFormat.RGBA32, true, false);
        texHeight.filterMode = FilterMode.Trilinear;
        texHeight.anisoLevel = 8;
        texHeight.wrapMode = TextureWrapMode.Repeat;
        count = 0;
        foreach (Texture2D texture in this.chunkTexturesHeight)
        {
            texHeight.SetPixels(texture.GetPixels(), count, 0);
            count++;
        }
        texHeight.Apply();

        this.terrainMaterial.SetTexture("_TexColor", texColor);
        this.terrainMaterial.SetTexture("_TexHeight", texHeight);
    }

    private void InitJobs()
    {
        // Generate Maps Job
        this.generateMapsJob = new GenerateMapsJob[this.generateMapsJobCount];
        this.generateMapsJobHandle = new JobHandle[this.generateMapsJobCount];
        this.generateMapsJobDone = new bool[this.generateMapsJobCount];
        for (int i = 0; i < this.generateMapsJobCount; i++)
        {
            this.generateMapsJobDone[i] = true;
            this.generateMapsJob[i] = new GenerateMapsJob() {
                heightMapTemp = new NativeArray<float>(400, Allocator.Persistent),
                heightMap = new NativeArray<float>(256, Allocator.Persistent),
                rainMap = new NativeArray<float>(256, Allocator.Persistent),
                heatMap = new NativeArray<float>(256, Allocator.Persistent)
            };
        }

        // Generate Voxels Job
        this.generateVoxelsJob = new GenerateVoxelsJob[this.generateVoxelsJobCount];
        this.generateVoxelsJobHandle = new JobHandle[this.generateVoxelsJobCount];
        this.generateVoxelsJobDone = new bool[this.generateVoxelsJobCount];
        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            this.generateVoxelsJobDone[i] = true;
            this.generateVoxelsJob[i] = new GenerateVoxelsJob() {
                voxels = new NativeArray<Voxel>(65536, Allocator.Persistent),
                heightMap = new NativeArray<float>(256, Allocator.Persistent),
                rainMap = new NativeArray<float>(256, Allocator.Persistent),
                heatMap = new NativeArray<float>(256, Allocator.Persistent)
            };
        }

        // Generate Lights Job
        this.generateLightsJob = new GenerateLightsJob[this.generateLightsJobCount];
        this.generateLightsJobHandle = new JobHandle[this.generateLightsJobCount];
        this.generateLightsJobDone = new bool[this.generateLightsJobCount];
        for (int i = 0; i < this.generateLightsJobCount; i++)
        {
            this.generateLightsJobDone[i] = true;
            this.generateLightsJob[i] = new GenerateLightsJob() {
                lights = new NativeArray<byte>(65536, Allocator.Persistent),
                lightQueue = new NativeQueue<int3>(Allocator.Persistent),
                isSolid = new NativeArray<bool>(589824, Allocator.Persistent),
                sunLights = new NativeArray<byte>(589824, Allocator.Persistent),
                voxels00 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels10 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels20 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels01 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels11 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels21 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels02 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels12 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                voxels22 = new NativeArray<Voxel>(65536, Allocator.Persistent)
            };  
        }

        // Generate Mesh Job
        this.generateMeshJob = new GenerateMeshJob[this.generateMeshJobCount];
        this.generateMeshJobHandle = new JobHandle[this.generateMeshJobCount];
        this.generateMeshJobDone = new bool[this.generateMeshJobCount];
        for (int i = 0; i < this.generateMeshJobCount; i++)
        {
            this.generateMeshJobDone[i] = true;
            this.generateMeshJob[i] = new GenerateMeshJob()
            {
                breakPoints = new NativeList<int2>(Allocator.Persistent),
                voxelsMerged = new NativeArray<Voxel>(92416, Allocator.Persistent),
                lightsMerged = new NativeArray<byte>(92416, Allocator.Persistent),
                voxelGradients = new NativeArray<float3>(73406, Allocator.Persistent),
                voxelLights = new NativeArray<float2>(73406, Allocator.Persistent),
                mcCornerPositions = new NativeArray<float3>(Tables.cornerPositions.Length, Allocator.Persistent),
                mcCellClasses = new NativeArray<byte>(Tables.cellClasses.Length, Allocator.Persistent),
                mcCellGeometryCounts = new NativeArray<int>(Tables.cellGeometryCounts.Length, Allocator.Persistent),
                mcCellIndices = new NativeArray<int>(Tables.cellIndices.Length, Allocator.Persistent),
                mcCellVertexData = new NativeArray<ushort>(Tables.cellVertexData.Length, Allocator.Persistent),
                voxels00 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights00 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels10 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights10 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels20 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights20 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels01 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights01 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels11 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights11 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels21 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights21 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels02 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights02 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels12 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights12 = new NativeArray<byte>(65536, Allocator.Persistent),
                voxels22 = new NativeArray<Voxel>(65536, Allocator.Persistent),
                lights22 = new NativeArray<byte>(65536, Allocator.Persistent),
                vertices = new NativeList<Vector3>(Allocator.Persistent),
                normals = new NativeList<Vector3>(Allocator.Persistent),
                indices = new NativeList<int>(Allocator.Persistent),
                lights = new NativeList<Vector2>(Allocator.Persistent),
                mats1234 = new NativeList<Vector4>(Allocator.Persistent),
                mats5678 = new NativeList<Vector4>(Allocator.Persistent),
                weights1234 = new NativeList<Vector4>(Allocator.Persistent),
                weights5678 = new NativeList<Vector4>(Allocator.Persistent),
                cornerPositions = new NativeArray<float3>(8, Allocator.Persistent),
                cornerVoxels = new NativeArray<Voxel>(8, Allocator.Persistent),
                cornerLights = new NativeArray<float2>(8, Allocator.Persistent),
                cornerGradients = new NativeArray<float3>(8, Allocator.Persistent),
                cellIndices = new NativeList<int>(Allocator.Persistent),
                mappedIndices = new NativeList<ushort>(Allocator.Persistent)
            };

            this.generateMeshJob[i].mcCornerPositions.CopyFrom(Tables.cornerPositions);
            this.generateMeshJob[i].mcCellClasses.CopyFrom(Tables.cellClasses);
            this.generateMeshJob[i].mcCellGeometryCounts.CopyFrom(Tables.cellGeometryCounts);
            this.generateMeshJob[i].mcCellIndices.CopyFrom(Tables.cellIndices);
            this.generateMeshJob[i].mcCellVertexData.CopyFrom(Tables.cellVertexData);
        }

        // Bake Physics Mesh Job
        this.bakePhysicsMeshJobDone = true;
        this.bakePhysicsMeshJobHandle = new JobHandle();
        this.bakePhysicsMeshJob = new BakePhysicsMeshJob()
        {
            bakeMeshData = new NativeList<BakePhysicsMeshData>(Allocator.Persistent)
        };

        // Light Removal Job
        this.lightRemovalJob = new LightRemovalJob()
        {
            voxels = new NativeArray<Voxel>(589824, Allocator.Persistent),
            lights = new NativeArray<byte>(589824, Allocator.Persistent),
            sunLightSpreadQueue = new NativeQueue<int>(Allocator.Persistent),
            sunLightRemovalQueue = new NativeQueue<int>(Allocator.Persistent),
            sourceLightRemovalQueue = new NativeQueue<int>(Allocator.Persistent),
            sourceLightSpreadQueue = new NativeQueue<int>(Allocator.Persistent),
            chunksTouched = new NativeArray<bool>(9, Allocator.Persistent),
            voxels00 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels10 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels20 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels01 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels11 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels21 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels02 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels12 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            voxels22 = new NativeArray<Voxel>(65536, Allocator.Persistent),
            lights00 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights10 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights20 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights01 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights11 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights21 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights02 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights12 = new NativeArray<byte>(65536, Allocator.Persistent),
            lights22 = new NativeArray<byte>(65536, Allocator.Persistent)
        };
    }

    //////////////////////////////////////////////////////////////////////////////
    /// PLAYER POSITION //////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void UpdatePlayerPositionLast()
    {
        this.playerChunkPositionLast = this.playerChunkPosition;
        this.playerWorldPositionLast = this.playerWorldPosition.position;
    }

    private void UpdatePlayerPosition()
    {
        this.didPlayerEnterChunk = false;

        this.playerChunkPosition = this.WorldToChunkPosition(this.playerWorldPosition.position);
        this.didPlayerEnterChunk = (this.playerChunkPosition != this.playerChunkPositionLast);
    }

    private void UpdateChunkLoadingOrigin()
    {
        this.didChunkLoadingOriginChange = false;

        if (!this.didPlayerEnterChunk)
        {
            return;
        }

        int distanceX = Mathf.Abs(this.chunkLoadingOrigin.x - this.playerChunkPosition.x);
        int distanceZ = Mathf.Abs(this.chunkLoadingOrigin.y - this.playerChunkPosition.y);
        if (distanceX < 2 && distanceZ < 2)
        {
            return;
        }

        this.chunkLoadingOriginNew = this.playerChunkPosition;
        this.didChunkLoadingOriginChange = true;
    }

    //////////////////////////////////////////////////////////////////////////////
    /// CHUNK UPDATE /////////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void UpdateChunkQueue()
    {
        if (this.didChunkLoadingOriginChange)
        {
            this.isUpdateChunkQueuePending = true;
        }

        if (!this.isUpdateChunkQueuePending)
        {
            return;
        }

        if (this.isUnloadChunksPending || this.isLoadChunksPending)
        {
            return;
        }

        for (int i = 0; i < generateMapsJobCount; i++)
        {
            if (!this.generateMapsJobDone[i])
            {
                return;
            }
        }

        for (int i = 0; i < generateVoxelsJobCount; i++)
        {
            if (!this.generateVoxelsJobDone[i])
            {
                return;
            }
        }

        for (int i = 0; i < generateLightsJobCount; i++)
        {
            if (!this.generateLightsJobDone[i])
            {
                return;
            }
        }

        for (int i = 0; i < generateMeshJobCount; i++)
        {
            if (!this.generateMeshJobDone[i])
            {
                return;
            }
        }

        if (this.bakePhysicsMeshQueue.Count > 0 || !this.bakePhysicsMeshJobDone)
        {
            return;
        }

        this.chunkLoadingOrigin = this.chunkLoadingOriginNew;

        foreach (KeyValuePair<Vector2Int, Chunk> chunk in this.chunks)
        {
            if (!this.IsInChunkDistance(chunk.Key, 2))
            {
                this.chunksToDestroy.Enqueue(chunk.Key);
            }
        }

        foreach (KeyValuePair<Vector2Int, ChunkObject> chunkObject in this.chunkObjects)
        {
            if (!this.IsInChunkDistance(chunkObject.Key) && this.IsInChunkDistance(chunkObject.Key, 2))
            {
                this.chunksToDeactivate.Enqueue(chunkObject.Key);
            }
        }

        foreach (Vector2Int offset in this.chunkLoadingOrder)
        {
            Vector2Int chunkPosition = this.chunkLoadingOrigin + offset;
            this.chunksToLoad.Enqueue(chunkPosition);
        }

        this.isUpdateChunkQueuePending = false;
        this.isUnloadChunksPending = true;
    }

    private void UnloadChunks()
    {
        if (!this.isUnloadChunksPending)
        {
            return;
        }

        this.unloadStopwatch.Restart();
        while (this.chunksToDestroy.Count > 0 && unloadStopwatch.ElapsedMilliseconds < 1)
        {
            Vector2Int chunkPosition = this.chunksToDestroy.Dequeue();

            if (this.chunks[chunkPosition].hasObjects)
            {
                this.chunkObjects[chunkPosition].Destroy();
                this.chunkObjects.Remove(chunkPosition);
            }
            this.chunks.Remove(chunkPosition);
        }

        this.unloadStopwatch.Restart();
        while (this.chunksToDeactivate.Count > 0 && unloadStopwatch.ElapsedMilliseconds < 1)
        {
            Vector2Int chunkPosition = this.chunksToDeactivate.Dequeue();
            this.chunkObjects[chunkPosition].Deactivate();
        }

        if (this.chunksToDestroy.Count + this.chunksToDeactivate.Count == 0)
        {
            this.isUnloadChunksPending = false;
            this.isLoadChunksPending = true;
        }
    }

    private void LoadChunks()
    {
        if (!this.isLoadChunksPending)
        {
            return;
        }

        while (this.chunksToLoad.Count > 0)
        {
            Vector2Int chunkPosition = this.chunksToLoad.Dequeue();

            if (!this.chunks.ContainsKey(chunkPosition))
            {
                this.generateMapsQueue.Enqueue(chunkPosition);
                this.generateVoxelsQueue.Enqueue(chunkPosition);

                if (this.IsInChunkDistance(chunkPosition, 1))
                {
                    this.generateLightsQueue.Enqueue(chunkPosition);
                }
                
                if (this.IsInChunkDistance(chunkPosition))
                {
                    this.generateMeshQueue.Enqueue(chunkPosition);
                }
            }
            else
            {
                if (!this.chunks[chunkPosition].hasVoxels)
                {
                    this.generateVoxelsQueue.Enqueue(chunkPosition);
                }

                if (this.IsInChunkDistance(chunkPosition, 1) && !this.chunks[chunkPosition].hasLights)
                {
                    this.generateLightsQueue.Enqueue(chunkPosition);
                }

                if (this.IsInChunkDistance(chunkPosition) && !this.chunks[chunkPosition].hasObjects)
                {
                    this.generateMeshQueue.Enqueue(chunkPosition);
                }
            }

            if (this.chunkObjects.ContainsKey(chunkPosition))
            {
                if (!this.chunkObjects[chunkPosition].IsActive() && this.IsInChunkDistance(chunkPosition))
                {
                    this.chunksToActivate.Enqueue(chunkPosition);
                }
                continue;
            }
        }

        this.isLoadChunksPending = false;
    }

    private void ActivateChunks()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.chunksToActivate.Clear();
            return;
        }

        if (this.chunksToActivate.Count > 0)
        {
            this.chunkObjects[this.chunksToActivate.Dequeue()].Activate();
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// GENERATE MAPS ////////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////
    
    private void GenerateMaps()
    {
        this.GenerateMapsSchedule();
        this.GenerateMapsComplete();
    }

    private void GenerateMapsSchedule()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.generateMapsQueue.Clear();
            return;
        }

        for (int i = 0; (this.generateMapsQueue.Count > 0) && (i < this.generateMapsJobCount); i++)
        {
            if (this.generateMapsJobDone[i])
            {
                Vector2Int chunkPosition = this.generateMapsQueue.Dequeue();

                this.generateMapsJob[i].chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
                this.generateMapsJobHandle[i] = this.generateMapsJob[i].Schedule();
                this.generateMapsJobDone[i] = false;
            }
        }
    }

    private void GenerateMapsComplete()
    {
        for (int i = 0; i < this.generateMapsJobCount; i++)
        {
            if (!this.generateMapsJobDone[i] && this.generateMapsJobHandle[i].IsCompleted)
            {
                this.generateMapsJobHandle[i].Complete();
                this.generateMapsJobDone[i] = true;

                Chunk chunk = new Chunk();
                chunk.SetHeightMapFromNative(this.generateMapsJob[i].heightMap);
                chunk.SetRainMapFromNative(this.generateMapsJob[i].rainMap);
                chunk.SetHeatMapFromNative(this.generateMapsJob[i].heatMap);
                chunk.hasMaps = true;

                Vector2Int chunkPosition = new Vector2Int(this.generateMapsJob[i].chunkPosition.x, this.generateMapsJob[i].chunkPosition.y);

                this.chunks.Add(chunkPosition, chunk);
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// GENERATE VOXELS //////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void GenerateVoxels()
    {
        this.GenerateVoxelsSchedule();
        this.GenerateVoxelsComplete();
    }

    private void GenerateVoxelsSchedule()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.generateVoxelsQueue.Clear();
            return;
        }

        for (int i = 0; (this.generateVoxelsQueue.Count > 0) && (i < this.generateVoxelsJobCount); i++)
        {
            if (this.generateVoxelsJobDone[i])
            {
                Vector2Int chunkPosition = this.generateVoxelsQueue.Peek();

                if (!this.chunks.ContainsKey(chunkPosition))
                {
                    return;
                }

                this.generateVoxelsQueue.Dequeue();

                this.generateVoxelsJob[i].chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
                this.generateVoxelsJob[i].heightMap.CopyFrom(this.chunks[chunkPosition].GetHeightMap());
                this.generateVoxelsJob[i].rainMap.CopyFrom(this.chunks[chunkPosition].GetRainMap());
                this.generateVoxelsJob[i].heatMap.CopyFrom(this.chunks[chunkPosition].GetHeatMap());
                this.generateVoxelsJobHandle[i] = this.generateVoxelsJob[i].Schedule();
                this.generateVoxelsJobDone[i] = false;
            }
        }
    }

    private void GenerateVoxelsComplete()
    {
        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            if (!this.generateVoxelsJobDone[i] && this.generateVoxelsJobHandle[i].IsCompleted)
            {
                this.generateVoxelsJobHandle[i].Complete();
                this.generateVoxelsJobDone[i] = true;

                Vector2Int chunkPosition = new Vector2Int(this.generateVoxelsJob[i].chunkPosition.x, this.generateVoxelsJob[i].chunkPosition.y);

                this.chunks[chunkPosition].SetVoxelsFromNative(this.generateVoxelsJob[i].voxels);
                this.chunks[chunkPosition].hasVoxels = true;
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// GENERATE LIGHTS //////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void GenerateLights()
    {
        this.GenerateLightsSchedule();
        this.GenerateLightsComplete();
    }

    private void GenerateLightsSchedule()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.generateLightsQueue.Clear();
            return;
        }

        for (int i = 0; (this.generateLightsQueue.Count > 0) && (i < this.generateLightsJobCount); i++)
        {
            if (this.generateLightsJobDone[i])
            {
                Vector2Int chunkPosition = this.generateLightsQueue.Peek();

                Vector2Int neighborPosition = new Vector2Int(0, 0);
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && z == 0) continue;

                        neighborPosition.x = chunkPosition.x + x;
                        neighborPosition.y = chunkPosition.y + z;

                        if (!this.chunks.ContainsKey(neighborPosition))
                        {
                            return;
                        }
                    }
                }

                this.generateLightsQueue.Dequeue();

                this.generateLightsJob[i].voxels00.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetVoxels());
                this.generateLightsJob[i].voxels10.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, -1)].GetVoxels());
                this.generateLightsJob[i].voxels20.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, -1)].GetVoxels());
                this.generateLightsJob[i].voxels01.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 0)].GetVoxels());
                this.generateLightsJob[i].voxels11.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 0)].GetVoxels());
                this.generateLightsJob[i].voxels21.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 0)].GetVoxels());
                this.generateLightsJob[i].voxels02.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 1)].GetVoxels());
                this.generateLightsJob[i].voxels12.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 1)].GetVoxels());
                this.generateLightsJob[i].voxels22.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 1)].GetVoxels());
                this.generateLightsJob[i].chunkPosition = new int2(chunkPosition.x, chunkPosition.y);

                this.generateLightsJobHandle[i] = this.generateLightsJob[i].Schedule();
                this.generateLightsJobDone[i] = false;
            }
        }
    }

    private void GenerateLightsComplete()
    {
        for (int i = 0; i < this.generateLightsJobCount; i++)
        {
            if (!this.generateLightsJobDone[i] && this.generateLightsJobHandle[i].IsCompleted)
            {
                this.generateLightsJobHandle[i].Complete();
                this.generateLightsJobDone[i] = true;

                Vector2Int chunkPosition = new Vector2Int(this.generateLightsJob[i].chunkPosition.x, this.generateLightsJob[i].chunkPosition.y);
                this.chunks[chunkPosition].SetLightsFromNative(this.generateLightsJob[i].lights);
                this.chunks[chunkPosition].hasLights = true;
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// GENERATE MESHES //////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void GenerateMeshes()
    {
        this.GenerateMeshesSchedule();
        this.GenerateMeshesComplete();
    }

    private void GenerateMeshesSchedule()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.generateMeshQueue.Clear();
            return;
        }

        for (int i = 0; (this.generateMeshQueue.Count > 0) && (i < this.generateMeshJobCount); i++)
        {
            if (this.generateMeshJobDone[i])
            {
                Vector2Int chunkPosition = this.generateMeshQueue.Peek();

                Vector2Int neighborPosition = new Vector2Int(0, 0);
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && z == 0) continue;

                        neighborPosition.x = chunkPosition.x + x;
                        neighborPosition.y = chunkPosition.y + z;

                        if (this.chunks.ContainsKey(neighborPosition))
                        {
                            if (!this.chunks[neighborPosition].hasLights)
                            {
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                }

                this.generateMeshQueue.Dequeue();

                Chunk chunk00 = this.chunks[chunkPosition + new Vector2Int(-1, -1)];
                Chunk chunk10 = this.chunks[chunkPosition + new Vector2Int(0, -1)];
                Chunk chunk20 = this.chunks[chunkPosition + new Vector2Int(1, -1)];
                Chunk chunk01 = this.chunks[chunkPosition + new Vector2Int(-1, 0)];
                Chunk chunk11 = this.chunks[chunkPosition + new Vector2Int(0, 0)];
                Chunk chunk21 = this.chunks[chunkPosition + new Vector2Int(1, 0)];
                Chunk chunk02 = this.chunks[chunkPosition + new Vector2Int(-1, 1)];
                Chunk chunk12 = this.chunks[chunkPosition + new Vector2Int(0, 1)];
                Chunk chunk22 = this.chunks[chunkPosition + new Vector2Int(1, 1)];

                this.generateMeshJob[i].vertices.Clear();
                this.generateMeshJob[i].normals.Clear();
                this.generateMeshJob[i].indices.Clear();
                this.generateMeshJob[i].lights.Clear();
                this.generateMeshJob[i].mats1234.Clear();
                this.generateMeshJob[i].mats5678.Clear();
                this.generateMeshJob[i].weights1234.Clear();
                this.generateMeshJob[i].weights5678.Clear();
                this.generateMeshJob[i].breakPoints.Clear();
                this.generateMeshJob[i].voxels00.CopyFrom(chunk00.GetVoxels());
                this.generateMeshJob[i].lights00.CopyFrom(chunk00.GetLights());
                this.generateMeshJob[i].voxels10.CopyFrom(chunk10.GetVoxels());
                this.generateMeshJob[i].lights10.CopyFrom(chunk10.GetLights());
                this.generateMeshJob[i].voxels20.CopyFrom(chunk20.GetVoxels());
                this.generateMeshJob[i].lights20.CopyFrom(chunk20.GetLights());
                this.generateMeshJob[i].voxels01.CopyFrom(chunk01.GetVoxels());
                this.generateMeshJob[i].lights01.CopyFrom(chunk01.GetLights());
                this.generateMeshJob[i].voxels11.CopyFrom(chunk11.GetVoxels());
                this.generateMeshJob[i].lights11.CopyFrom(chunk11.GetLights());
                this.generateMeshJob[i].voxels21.CopyFrom(chunk21.GetVoxels());
                this.generateMeshJob[i].lights21.CopyFrom(chunk21.GetLights());
                this.generateMeshJob[i].voxels02.CopyFrom(chunk02.GetVoxels());
                this.generateMeshJob[i].lights02.CopyFrom(chunk02.GetLights());
                this.generateMeshJob[i].voxels12.CopyFrom(chunk12.GetVoxels());
                this.generateMeshJob[i].lights12.CopyFrom(chunk12.GetLights());
                this.generateMeshJob[i].voxels22.CopyFrom(chunk22.GetVoxels());
                this.generateMeshJob[i].lights22.CopyFrom(chunk22.GetLights());
                this.generateMeshJob[i].chunkPosition = new int2(chunkPosition.x, chunkPosition.y);

                this.generateMeshJobHandle[i] = this.generateMeshJob[i].Schedule();
                this.generateMeshJobDone[i] = false;
            }
        }
    }

    private void GenerateMeshesComplete()
    {
        for (int i = 0; i < this.generateMeshJobCount; i++)
        {
            if (!this.generateMeshJobDone[i] && this.generateMeshJobHandle[i].IsCompleted)
            {
                this.generateMeshJobHandle[i].Complete();
                this.generateMeshJobDone[i] = true;

                Vector2Int chunkPosition = new Vector2Int(this.generateMeshJob[i].chunkPosition.x, this.generateMeshJob[i].chunkPosition.y);

                ChunkObject chunkObject = new ChunkObject(chunkPosition, this.terrainMaterial);

                chunkObject.SetRenderer(
                    this.generateMeshJob[i].vertices.AsArray(),
                    this.generateMeshJob[i].normals.AsArray(),
                    this.generateMeshJob[i].indices.AsArray(),
                    this.generateMeshJob[i].weights1234.AsArray(),
                    this.generateMeshJob[i].weights5678.AsArray(),
                    this.generateMeshJob[i].mats1234.AsArray(),
                    this.generateMeshJob[i].mats5678.AsArray(),
                    this.generateMeshJob[i].lights.AsArray()
                );

                for (int y = 0; y < 16; y++)
                {
                    int startPositionVertices = this.generateMeshJob[i].breakPoints[y].x;
                    int endPositionVertices;
                    int startPositionIndices = this.generateMeshJob[i].breakPoints[y].y;
                    int endPositionIndices;

                    if (y < 15)
                    {
                        endPositionVertices = this.generateMeshJob[i].breakPoints[y + 1].x;
                        endPositionIndices = this.generateMeshJob[i].breakPoints[y + 1].y;
                    }
                    else
                    {
                        endPositionVertices = this.generateMeshJob[i].vertices.Length;
                        endPositionIndices = this.generateMeshJob[i].indices.Length;
                    }

                    if (startPositionVertices == endPositionVertices || startPositionIndices == endPositionIndices)
                    {
                        continue;
                    }

                    int lengthVertices = endPositionVertices - startPositionVertices;
                    int lengthIndices = endPositionIndices - startPositionIndices;

                    Vector3[] colliderVertices = new Vector3[lengthVertices];
                    this.generateMeshJob[i].vertices.AsArray().GetSubArray(startPositionVertices, lengthVertices).CopyTo(colliderVertices);

                    for (int j = 0; j < lengthVertices; j++)
                    {
                        colliderVertices[j].y -= y * 16.0f;
                    }

                    int[] colliderIndices = new int[lengthIndices];
                    this.generateMeshJob[i].indices.AsArray().GetSubArray(startPositionIndices, lengthIndices).CopyTo(colliderIndices);

                    int colliderIndicesOffset = startPositionVertices;

                    for (int j = 0; j < lengthIndices; j++)
                    {
                        colliderIndices[j] -= colliderIndicesOffset;
                    }

                    chunkObject.SetColliderMesh(y, colliderVertices, colliderIndices);
                    BakePhysicsMeshData bakePhysicsMeshData = new BakePhysicsMeshData(chunkPosition, y, chunkObject.GetMeshInstanceID(y));
                    this.bakePhysicsMeshQueue.Enqueue(bakePhysicsMeshData);
                }

                this.chunkObjects.Add(chunkPosition, chunkObject);
                this.chunks[chunkPosition].hasObjects = true;
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// BAKE PHYSICS MESHES //////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void BakePhysicsMeshes()
    {
        this.BakePhysicsMeshesSchedule();
        this.BakePhysicsMeshesComplete();
    }

    private void BakePhysicsMeshesSchedule()
    {
        if (this.bakePhysicsMeshJobDone && this.bakePhysicsMeshQueue.Count > 0)
        {
            this.bakePhysicsMeshJob.bakeMeshData.Clear();

            while (this.bakePhysicsMeshQueue.Count > 0)
            {
                this.bakePhysicsMeshJob.bakeMeshData.Add(this.bakePhysicsMeshQueue.Dequeue());
            }

            this.bakePhysicsMeshJobHandle = this.bakePhysicsMeshJob.Schedule();
            this.bakePhysicsMeshJobDone = false;
        }
    }

    private void BakePhysicsMeshesComplete()
    {
        if (!this.bakePhysicsMeshJobDone && this.bakePhysicsMeshJobHandle.IsCompleted)
        {
            this.bakePhysicsMeshJobDone = true;
            this.bakePhysicsMeshJobHandle.Complete();

            foreach (BakePhysicsMeshData data in this.bakePhysicsMeshJob.bakeMeshData)
            {
                this.chunkObjects[data.chunkPosition].SetCollider(data.colliderIndex);
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////
    /// CLEAN UP /////////////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void DisposeJobs()
    {
        // Generate Maps Job
        for (int i = 0; i < this.generateMapsJobCount; i++)
        {
            if (!this.generateMapsJobDone[i]) this.generateMapsJobHandle[i].Complete();
            this.generateMapsJob[i].heightMapTemp.Dispose();
            this.generateMapsJob[i].heightMap.Dispose();
            this.generateMapsJob[i].rainMap.Dispose();
            this.generateMapsJob[i].heatMap.Dispose();
        }

        // Generate Voxels Job
        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            if (!this.generateVoxelsJobDone[i]) this.generateVoxelsJobHandle[i].Complete();
            this.generateVoxelsJob[i].voxels.Dispose();
            this.generateVoxelsJob[i].heightMap.Dispose();
            this.generateVoxelsJob[i].rainMap.Dispose();
            this.generateVoxelsJob[i].heatMap.Dispose();
        }

        // Generate Lights Job
        for (int i = 0; i < this.generateLightsJobCount; i++)
        {
            if (!this.generateLightsJobDone[i]) this.generateLightsJobHandle[i].Complete();
            this.generateLightsJob[i].lights.Dispose();
            this.generateLightsJob[i].isSolid.Dispose();
            this.generateLightsJob[i].sunLights.Dispose();
            this.generateLightsJob[i].lightQueue.Dispose();
            this.generateLightsJob[i].voxels00.Dispose();
            this.generateLightsJob[i].voxels10.Dispose();
            this.generateLightsJob[i].voxels20.Dispose();
            this.generateLightsJob[i].voxels01.Dispose();
            this.generateLightsJob[i].voxels11.Dispose();
            this.generateLightsJob[i].voxels21.Dispose();
            this.generateLightsJob[i].voxels02.Dispose();
            this.generateLightsJob[i].voxels12.Dispose();
            this.generateLightsJob[i].voxels22.Dispose();
        }

        // Generate Mesh Job
        for (int i = 0; i < this.generateMeshJobCount; i++)
        {
            if (!this.generateMeshJobDone[i]) this.generateMeshJobHandle[i].Complete();
            this.generateMeshJob[i].breakPoints.Dispose();
            this.generateMeshJob[i].voxelsMerged.Dispose();
            this.generateMeshJob[i].lightsMerged.Dispose();
            this.generateMeshJob[i].voxelGradients.Dispose();
            this.generateMeshJob[i].voxelLights.Dispose();
            this.generateMeshJob[i].mcCornerPositions.Dispose();
            this.generateMeshJob[i].mcCellClasses.Dispose();
            this.generateMeshJob[i].mcCellGeometryCounts.Dispose();
            this.generateMeshJob[i].mcCellIndices.Dispose();
            this.generateMeshJob[i].mcCellVertexData.Dispose();
            this.generateMeshJob[i].vertices.Dispose();
            this.generateMeshJob[i].normals.Dispose();
            this.generateMeshJob[i].indices.Dispose();
            this.generateMeshJob[i].lights.Dispose();
            this.generateMeshJob[i].mats1234.Dispose();
            this.generateMeshJob[i].mats5678.Dispose();
            this.generateMeshJob[i].weights1234.Dispose();
            this.generateMeshJob[i].weights5678.Dispose();
            this.generateMeshJob[i].cornerPositions.Dispose();
            this.generateMeshJob[i].cornerVoxels.Dispose();
            this.generateMeshJob[i].cornerLights.Dispose();
            this.generateMeshJob[i].cornerGradients.Dispose();
            this.generateMeshJob[i].cellIndices.Dispose();
            this.generateMeshJob[i].mappedIndices.Dispose();
            this.generateMeshJob[i].voxels00.Dispose();
            this.generateMeshJob[i].lights00.Dispose();
            this.generateMeshJob[i].voxels10.Dispose();
            this.generateMeshJob[i].lights10.Dispose();
            this.generateMeshJob[i].voxels20.Dispose();
            this.generateMeshJob[i].lights20.Dispose();
            this.generateMeshJob[i].voxels01.Dispose();
            this.generateMeshJob[i].lights01.Dispose();
            this.generateMeshJob[i].voxels11.Dispose();
            this.generateMeshJob[i].lights11.Dispose();
            this.generateMeshJob[i].voxels21.Dispose();
            this.generateMeshJob[i].lights21.Dispose();
            this.generateMeshJob[i].voxels02.Dispose();
            this.generateMeshJob[i].lights02.Dispose();
            this.generateMeshJob[i].voxels12.Dispose();
            this.generateMeshJob[i].lights12.Dispose();
            this.generateMeshJob[i].voxels22.Dispose();
            this.generateMeshJob[i].lights22.Dispose();
        }

        // Bake Physics Mesh Job
        if (!this.bakePhysicsMeshJobDone) this.bakePhysicsMeshJobHandle.Complete();
        this.bakePhysicsMeshJob.bakeMeshData.Dispose();

        //
        // Light Update Job
        //

        if (!this.lightRemovalJobDone) this.lightRemovalJobHandle.Complete();
        this.lightRemovalJob.voxels.Dispose();
        this.lightRemovalJob.lights.Dispose();
        this.lightRemovalJob.sunLightSpreadQueue.Dispose();
        this.lightRemovalJob.sunLightRemovalQueue.Dispose();
        this.lightRemovalJob.sourceLightRemovalQueue.Dispose();
        this.lightRemovalJob.sourceLightSpreadQueue.Dispose();
        this.lightRemovalJob.chunksTouched.Dispose();
        this.lightRemovalJob.voxels00.Dispose();
        this.lightRemovalJob.voxels10.Dispose();
        this.lightRemovalJob.voxels20.Dispose();
        this.lightRemovalJob.voxels01.Dispose();
        this.lightRemovalJob.voxels11.Dispose();
        this.lightRemovalJob.voxels21.Dispose();
        this.lightRemovalJob.voxels02.Dispose();
        this.lightRemovalJob.voxels12.Dispose();
        this.lightRemovalJob.voxels22.Dispose();
        this.lightRemovalJob.lights00.Dispose();
        this.lightRemovalJob.lights10.Dispose();
        this.lightRemovalJob.lights20.Dispose();
        this.lightRemovalJob.lights01.Dispose();
        this.lightRemovalJob.lights11.Dispose();
        this.lightRemovalJob.lights21.Dispose();
        this.lightRemovalJob.lights02.Dispose();
        this.lightRemovalJob.lights12.Dispose();
        this.lightRemovalJob.lights22.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////
    /// HELPERS //////////////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private bool IsInChunkDistance(Vector2Int chunkPosition, int overflow = 0)
    {
        if (Mathf.Abs(chunkPosition.x - this.chunkLoadingOrigin.x) <= this.chunkDistance + overflow && Mathf.Abs(chunkPosition.y - this.chunkLoadingOrigin.y) <= this.chunkDistance + overflow)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private Vector2Int WorldToChunkPosition(Vector3 worldPosition)
    {
        Vector2Int chunkPosition = new Vector2Int(0, 0);
        chunkPosition.x = Mathf.FloorToInt(worldPosition.x / 16.0f);
        chunkPosition.y = Mathf.FloorToInt(worldPosition.z / 16.0f);
        return chunkPosition;
    }

    //////////////////////////////////////////////////////////////////////////////
    /// DIAGNOSTICS //////////////////////////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////

    private void startStopwatch()
    {
        this.stopwatch.Reset();
        this.stopwatch.Start();
    }

    private void stopStopwatch(string name, int threshold)
    {
        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds > threshold)
            Debug.Log(name + ": " + stopwatch.ElapsedMilliseconds);
    }
}