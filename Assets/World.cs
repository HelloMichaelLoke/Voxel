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
    private int chunkDistance = 8;

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

    // Material
    public Material terrainMaterial;
    public Texture2D[] chunkTexturesColor;
    public Texture2D[] chunkTexturesHeight;

    // Chunks store lights, densities and materials
    private Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private Dictionary<Vector2Int, ChunkObject> chunkObjects = new Dictionary<Vector2Int, ChunkObject>();

    // Generate Voxels Job
    private int generateVoxelsJobCount = 3;
    private GenerateVoxelsJob[] generateVoxelsJob;
    private JobHandle[] generateVoxelsJobHandle;
    private bool[] generateVoxelsJobDone;

    // Generate Lights Job
    private int generateLightsJobCount = 3;
    private GenerateLightsJob[] generateLightsJob;
    private JobHandle[] generateLightsJobHandle;
    private bool[] generateLightsJobDone;

    // Generate Mesh Job
    private int generateMeshJobCount = 4;
    private GenerateMeshJob[] generateMeshJob;
    private JobHandle[] generateMeshJobHandle;
    private bool[] generateMeshJobDone;

    // World Edit Mesh Job
    private GenerateMeshJob worldEditMeshJob;
    private JobHandle worldEditMeshJobHandle;
    private bool worldEditMeshJobDone = true;
    private Queue<Vector2Int> worldEditMeshQueue = new Queue<Vector2Int>();
    private Queue<WorldEditData> worldEditQueue = new Queue<WorldEditData>();
    private bool isWorldEditBlocked = false;
    private bool[] worldEditMeshesTouched = new bool[9];

    // Light Removal Job
    private LightRemovalJob lightRemovalJob;
    private JobHandle lightRemovalJobHandle;
    private bool lightRemovalJobDone = true;

    private bool isUpdateChunkQueuePending = false;
    private bool isUnloadChunksPending = false;
    private bool isLoadChunksPending = false;

    private Queue<Vector2Int> generateVoxelsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateLightsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateMeshQueue = new Queue<Vector2Int>();

    private Queue<Vector2Int> chunksToDestroy = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToDeactivate = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToLoad = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToActivate = new Queue<Vector2Int>();

    // Chunk Loading
    private List<Vector2Int> chunkLoadingOrder = new List<Vector2Int>();

    // Diagnostics
    private System.Diagnostics.Stopwatch unloadStopwatch = new System.Diagnostics.Stopwatch();

    private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

    private bool worldEditQueueEmpty = true;
    private System.Diagnostics.Stopwatch worldEditStopwatch = new System.Diagnostics.Stopwatch();

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
        this.GenerateVoxels();
        this.GenerateLights();
        this.GenerateMeshes();
        this.UpdateWorldEdit();
        this.UpdatePlayerPositionLast();

        Debug.Log("v: " + this.generateVoxelsQueue.Count);
        Debug.Log("l: " + this.generateLightsQueue.Count);
        Debug.Log("m: " + this.generateMeshQueue.Count);
    }

    private void OnApplicationQuit()
    {
        this.DisposeJobs();

        foreach (KeyValuePair<Vector2Int, ChunkObject> chunkObject in this.chunkObjects)
        {
            chunkObject.Value.Destroy();
        }
    }

    /// <summary>
    /// Populates the chunk loading order list in a spiral sequence.
    /// </summary>
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

    /// <summary>
    /// Populates the texture array for the terrain material.
    /// </summary>
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

    /// <summary>
    /// Allocates memory for all jobs
    /// </summary>
    private void InitJobs()
    {
        // Generate Voxels Job
        this.generateVoxelsJob = new GenerateVoxelsJob[this.generateVoxelsJobCount];
        this.generateVoxelsJobHandle = new JobHandle[this.generateVoxelsJobCount];
        this.generateVoxelsJobDone = new bool[this.generateVoxelsJobCount];
        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            this.generateVoxelsJobDone[i] = true;
            this.generateVoxelsJob[i] = new GenerateVoxelsJob() {
                voxels = new NativeArray<Voxel>(65536, Allocator.Persistent),
                heights = new NativeArray<float>(256, Allocator.Persistent),
                tempHeights = new NativeArray<float>(324, Allocator.Persistent)
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
                vertexNormals = new NativeHashMap<Vector3, Vector3>(500000, Allocator.Persistent),
                breakPoints = new NativeList<int2>(Allocator.Persistent),
                voxelsMerged = new NativeArray<Voxel>(92416, Allocator.Persistent),
                lightsMerged = new NativeArray<byte>(92416, Allocator.Persistent),
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
                cornerIndices = new NativeArray<int>(8, Allocator.Persistent),
                cellIndices = new NativeList<int>(Allocator.Persistent),
                mappedIndices = new NativeList<ushort>(Allocator.Persistent)
            };

            this.generateMeshJob[i].mcCornerPositions.CopyFrom(Tables.cornerPositions);
            this.generateMeshJob[i].mcCellClasses.CopyFrom(Tables.cellClasses);
            this.generateMeshJob[i].mcCellGeometryCounts.CopyFrom(Tables.cellGeometryCounts);
            this.generateMeshJob[i].mcCellIndices.CopyFrom(Tables.cellIndices);
            this.generateMeshJob[i].mcCellVertexData.CopyFrom(Tables.cellVertexData);
        }

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

        // World Edit Mesh Job
        this.worldEditMeshJob = new GenerateMeshJob()
        {
            vertexNormals = new NativeHashMap<Vector3, Vector3>(500000, Allocator.Persistent),
            breakPoints = new NativeList<int2>(Allocator.Persistent),
            voxelsMerged = new NativeArray<Voxel>(92416, Allocator.Persistent),
            lightsMerged = new NativeArray<byte>(92416, Allocator.Persistent),
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
            cornerIndices = new NativeArray<int>(8, Allocator.Persistent),
            cellIndices = new NativeList<int>(Allocator.Persistent),
            mappedIndices = new NativeList<ushort>(Allocator.Persistent)
        };

        this.worldEditMeshJob.mcCornerPositions.CopyFrom(Tables.cornerPositions);
        this.worldEditMeshJob.mcCellClasses.CopyFrom(Tables.cellClasses);
        this.worldEditMeshJob.mcCellGeometryCounts.CopyFrom(Tables.cellGeometryCounts);
        this.worldEditMeshJob.mcCellIndices.CopyFrom(Tables.cellIndices);
        this.worldEditMeshJob.mcCellVertexData.CopyFrom(Tables.cellVertexData);
    }

    /// <summary>
    /// Updates playerChunkPosition and sets didPlayerEnterChunk.
    /// </summary>
    private void UpdatePlayerPosition()
    {
        this.didPlayerEnterChunk = false;

        this.playerChunkPosition = this.WorldToChunkPosition(this.playerWorldPosition.position);
        this.didPlayerEnterChunk = (this.playerChunkPosition != this.playerChunkPositionLast);
    }

    /// <summary>
    /// Updates chunkLoadingOrigin if the player entered a chunk that is 2 chunks away from the chunkLoadingOrigin.
    /// </summary>
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

    /// <summary>
    /// If didChunkLoadingOriginChange is true:<br />
    /// Clears generateTerrainQueue, generateLightsQueue and generateMeshQueue.<br />
    /// Sets isUpdateChunkQueuePending to true.<br />
    /// If isUpdateChunkQueuePending is true and generateTerrainJob, sunLightJob and meshTerrainJob are done:<br />
    /// Queues up chunksToDestroy, chunksToDeactivate and chunksToLoad if chunkLoadingOrigin changed.
    /// </summary>
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

    /// <summary>
    /// Incrementally destroy & deactivate chunks.
    /// </summary>
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

    /// <summary>
    /// Enqueue jobs for chunks that shall be loaded based on what's already done.
    /// </summary>
    private void LoadChunks()
    {
        if (!this.isLoadChunksPending)
        {
            return;
        }

        while (this.chunksToLoad.Count > 0)
        {
            Vector2Int chunkPosition = this.chunksToLoad.Dequeue();

            if (this.IsInChunkDistance(chunkPosition, 2))
            {
                this.generateVoxelsQueue.Enqueue(chunkPosition);
            }

            if (this.IsInChunkDistance(chunkPosition, 1))
            {
                this.generateLightsQueue.Enqueue(chunkPosition);
            }
            
            if (this.IsInChunkDistance(chunkPosition))
            {
                this.generateMeshQueue.Enqueue(chunkPosition);
            }

            if (this.IsInChunkDistance(chunkPosition))
            {
                this.chunksToActivate.Enqueue(chunkPosition);
            }
        }

        this.isLoadChunksPending = false;
    }

    /// <summary>
    /// Activates queued chunk objects.
    /// </summary>
    private void ActivateChunks()
    {
        if (this.isUpdateChunkQueuePending)
        {
            this.chunksToActivate.Clear();
            return;
        }

        Vector2Int chunkPosition;
        while (this.chunksToActivate.Count > 0)
        {
            chunkPosition = this.chunksToActivate.Dequeue();
            if (this.chunkObjects.ContainsKey(chunkPosition))
            {
                if (!this.chunkObjects[chunkPosition].IsActive())
                {
                    this.chunkObjects[chunkPosition].Activate();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Schedules and Completes GenerateVoxelsJob
    /// </summary>
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

        if (this.generateVoxelsQueue.Count == 0)
        {
            return;
        }

        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            if (this.generateVoxelsJobDone[i] && this.generateVoxelsQueue.Count > 0)
            {
                while (this.generateVoxelsQueue.Count > 0 && this.chunks.ContainsKey(this.generateVoxelsQueue.Peek()))
                {
                    this.generateVoxelsQueue.Dequeue();
                }

                if (this.generateVoxelsQueue.Count == 0)
                {
                    return;
                }

                Vector2Int chunkPosition = this.generateVoxelsQueue.Dequeue();

                this.generateVoxelsJob[i].chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
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

                Chunk chunk = new Chunk();
                chunk.SetVoxelsFromNative(this.generateVoxelsJob[i].voxels);
                chunk.hasVoxels = true;

                Vector2Int chunkPosition = new Vector2Int(this.generateVoxelsJob[i].chunkPosition.x, this.generateVoxelsJob[i].chunkPosition.y);
                this.chunks.Add(chunkPosition, chunk);
            }
        }
    }

    /// <summary>
    /// Schedules and Completes GenerateLightsJob
    /// </summary>
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

        for (int i = 0; i < this.generateLightsJobCount; i++)
        {
            if (this.generateLightsQueue.Count == 0)
            {
                return;
            }

            if (this.generateLightsJobDone[i])
            {
                Vector2Int chunkPosition;

                while (this.generateLightsQueue.Count > 0)
                {
                    chunkPosition = this.generateLightsQueue.Peek();
                    if (!this.chunks.ContainsKey(chunkPosition))
                    {
                        return;
                    }

                    if (this.chunks[chunkPosition].hasLights)
                    {
                        this.generateLightsQueue.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }

                if (this.generateLightsQueue.Count == 0)
                {
                    return;
                }

                chunkPosition = this.generateLightsQueue.Peek();

                bool neighborsDone = true;
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
                            neighborsDone = false;
                        }
                    }
                }

                if (neighborsDone)
                {
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
                else
                {
                    return;
                }
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

    /// <summary>
    /// Schedules and Completes Mesh Jobs
    /// </summary>
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

        for (int i = 0; i < this.generateMeshJobCount; i++)
        {
            if (this.generateMeshQueue.Count == 0)
            {
                return;
            }

            if (this.generateMeshJobDone[i])
            {
                Vector2Int chunkPosition;

                while (this.generateMeshQueue.Count > 0)
                {
                    chunkPosition = this.generateMeshQueue.Peek();

                    if (this.chunks.ContainsKey(chunkPosition))
                    {
                        if (this.chunks[chunkPosition].hasObjects)
                        {
                            this.generateMeshQueue.Dequeue();
                        }
                        else
                        {
                            if (this.chunks[chunkPosition].hasLights)
                            {
                                break;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                if (this.generateMeshQueue.Count == 0)
                {
                    return;
                }

                chunkPosition = this.generateMeshQueue.Peek();

                bool neighborsDone = true;
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
                                neighborsDone = false;
                            }
                        }
                        else
                        {
                            neighborsDone = false;
                        }
                    }
                }

                if (neighborsDone)
                {
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
                else
                {
                    return;
                }
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

                    chunkObject.SetCollider(y, colliderVertices, colliderIndices);
                }

                this.chunkObjects.Add(chunkPosition, chunkObject);
                this.chunks[chunkPosition].hasObjects = true;

                return;
            }
        }
    }

    /// <summary>
    /// Handles terrain sculpting.
    /// </summary>
    private void UpdateWorldEdit()
    {
        if (!this.isWorldEditBlocked && this.lightRemovalJobDone && this.worldEditQueue.Count > 0)
        {
            // Get Edit Position
            WorldEditData worldEditData = this.worldEditQueue.Dequeue();
            EditPosition editPosition = worldEditData.editPosition;
            Vector2Int chunkPosition = editPosition.chunkPosition;

            // Check if all neighbors exist
            bool neighborsDone = true;
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (!this.chunkObjects.ContainsKey(chunkPosition + new Vector2Int(x, z)))
                    {
                        neighborsDone = false;
                    }
                }
            }

            // Return if neighbors don't exist
            if (!neighborsDone)
            {
                return;
            }

            this.worldEditStopwatch.Reset();
            this.worldEditStopwatch.Start();

            this.isWorldEditBlocked = true;

            // Update Voxels
            List<VoxelChange> voxelChanges = worldEditData.voxelChanges;
            bool lightChanged = false;
            bool densityChanged = false;
            bool materialChanged = false;

            foreach (VoxelChange voxelChange in voxelChanges)
            {
                if (voxelChange.oldVoxel.GetDensity() != voxelChange.newVoxel.GetDensity())
                {
                    this.chunks[voxelChange.chunkPosition].SetVoxel(voxelChange.index, voxelChange.newVoxel);
                    densityChanged = true;
                }

                if (voxelChange.oldVoxel.GetMaterial() != voxelChange.newVoxel.GetMaterial())
                {
                    this.chunks[voxelChange.chunkPosition].SetVoxel(voxelChange.index, voxelChange.newVoxel);
                    materialChanged = true;
                }

                if (voxelChange.oldVoxel.IsSolid() != voxelChange.newVoxel.IsSolid())
                {
                    lightChanged = true;
                }
            }

            // Return if nothing changed
            if (!lightChanged && !densityChanged && !materialChanged)
            {
                Debug.Log("nothing changed");
                this.isWorldEditBlocked = false;
                return;
            }

            // Check for touched Meshes
            this.worldEditMeshesTouched = new bool[9] { false, false, false, false, false, false, false, false, false };

            Vector3Int relativePosition = editPosition.relativePosition;

            if (relativePosition.z <= 1 && relativePosition.x <= 1)
                this.worldEditMeshesTouched[0] = true;

            if (relativePosition.z <= 1)
                this.worldEditMeshesTouched[1] = true;

            if (relativePosition.z <= 1 && relativePosition.x == 15)
                this.worldEditMeshesTouched[2] = true;

            if (relativePosition.x <= 1)
                this.worldEditMeshesTouched[3] = true;

            this.worldEditMeshesTouched[4] = true;

            if (relativePosition.x == 15)
                this.worldEditMeshesTouched[5] = true;

            if (relativePosition.z == 15 && relativePosition.x <= 1)
                this.worldEditMeshesTouched[6] = true;

            if (relativePosition.z == 15)
                this.worldEditMeshesTouched[7] = true;

            if (relativePosition.z == 15 && relativePosition.x == 15)
                this.worldEditMeshesTouched[8] = true;

            // If light didn't change then Queue touched Meshes
            if (!lightChanged)
            {
                if (this.worldEditMeshesTouched[0]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(-1, -1));
                if (this.worldEditMeshesTouched[1]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(0, -1));
                if (this.worldEditMeshesTouched[2]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(1, -1));
                if (this.worldEditMeshesTouched[3]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(-1, 0));
                if (this.worldEditMeshesTouched[4]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(0, 0));
                if (this.worldEditMeshesTouched[5]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(1, 0));
                if (this.worldEditMeshesTouched[6]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(-1, 1));
                if (this.worldEditMeshesTouched[7]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(0, 1));
                if (this.worldEditMeshesTouched[8]) this.worldEditMeshQueue.Enqueue(chunkPosition + new Vector2Int(1, 1));

                return;
            }

            // Queue light changes
            foreach (VoxelChange voxelChange in voxelChanges)
            {
                if (voxelChange.oldVoxel.IsSolid() != voxelChange.newVoxel.IsSolid())
                {
                    Vector3Int lightPosition = new Vector3Int(
                        voxelChange.index % 16,
                        Mathf.FloorToInt((float)voxelChange.index / 256.0f),
                        Mathf.FloorToInt(((float)voxelChange.index / 16.0f) % 16)
                    );
                    lightPosition.x += 16;
                    lightPosition.z += 16;
                    int lightIndex = lightPosition.x + lightPosition.z * 48 + lightPosition.y * 2304;

                    if (voxelChange.newVoxel.IsSolid())
                    {
                        this.lightRemovalJob.sunLightRemovalQueue.Enqueue(lightIndex);
                        this.lightRemovalJob.sourceLightRemovalQueue.Enqueue(lightIndex);
                    }
                    else
                    {
                        this.lightRemovalJob.sunLightSpreadQueue.Enqueue(lightIndex);
                        this.lightRemovalJob.sourceLightSpreadQueue.Enqueue(lightIndex);
                    }
                }
            }
            
            this.lightRemovalJob.chunkPosition = chunkPosition;
            this.lightRemovalJob.voxels00.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetVoxels());
            this.lightRemovalJob.voxels10.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, -1)].GetVoxels());
            this.lightRemovalJob.voxels20.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, -1)].GetVoxels());
            this.lightRemovalJob.voxels01.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 0)].GetVoxels());
            this.lightRemovalJob.voxels11.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 0)].GetVoxels());
            this.lightRemovalJob.voxels21.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 0)].GetVoxels());
            this.lightRemovalJob.voxels02.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 1)].GetVoxels());
            this.lightRemovalJob.voxels12.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 1)].GetVoxels());
            this.lightRemovalJob.voxels22.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 1)].GetVoxels());
            this.lightRemovalJob.lights00.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetLights());
            this.lightRemovalJob.lights10.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, -1)].GetLights());
            this.lightRemovalJob.lights20.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, -1)].GetLights());
            this.lightRemovalJob.lights01.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 0)].GetLights());
            this.lightRemovalJob.lights11.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 0)].GetLights());
            this.lightRemovalJob.lights21.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 0)].GetLights());
            this.lightRemovalJob.lights02.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 1)].GetLights());
            this.lightRemovalJob.lights12.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 1)].GetLights());
            this.lightRemovalJob.lights22.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 1)].GetLights());
            this.lightRemovalJobHandle = this.lightRemovalJob.Schedule();
            this.lightRemovalJobDone = false;
        }

        if (!this.lightRemovalJobDone && this.lightRemovalJobHandle.IsCompleted)
        {
            this.lightRemovalJobHandle.Complete();
            this.lightRemovalJobDone = true;

            if (this.lightRemovalJob.chunksTouched[0] || this.worldEditMeshesTouched[0])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(-1, -1);
                if (lightRemovalJob.chunksTouched[0])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights00);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[1] || this.worldEditMeshesTouched[1])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(0, -1);
                if (lightRemovalJob.chunksTouched[1])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights10);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[2] || this.worldEditMeshesTouched[2])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(1, -1);
                if (lightRemovalJob.chunksTouched[2])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights20);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[3] || this.worldEditMeshesTouched[3])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(-1, 0);
                if (lightRemovalJob.chunksTouched[3])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights01);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[4] || this.worldEditMeshesTouched[4])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(0, 0);
                if (lightRemovalJob.chunksTouched[4])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights11);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[5] || this.worldEditMeshesTouched[5])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(1, 0);
                if (lightRemovalJob.chunksTouched[5])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights21);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[6] || this.worldEditMeshesTouched[6])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(-1, 1);
                if (lightRemovalJob.chunksTouched[6])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights02);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[7] || this.worldEditMeshesTouched[7])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(0, 1);
                if (lightRemovalJob.chunksTouched[7])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights12);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
            if (this.lightRemovalJob.chunksTouched[8] || this.worldEditMeshesTouched[8])
            {
                Vector2Int chunkPosition = this.lightRemovalJob.chunkPosition + new Vector2Int(1, 1);
                if (lightRemovalJob.chunksTouched[8])
                    this.chunks[chunkPosition].SetLightsFromNative(this.lightRemovalJob.lights22);
                this.worldEditMeshQueue.Enqueue(chunkPosition);
            }
        }

        if (this.worldEditMeshJobDone && this.worldEditMeshQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.worldEditMeshQueue.Dequeue();

            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector2Int offset = new Vector2Int(x, z);
                    if (!this.chunks.ContainsKey(chunkPosition + offset))
                    {
                        return;
                    }
                }
            }

            Chunk chunk00 = this.chunks[chunkPosition + new Vector2Int(-1, -1)];
            Chunk chunk10 = this.chunks[chunkPosition + new Vector2Int(0, -1)];
            Chunk chunk20 = this.chunks[chunkPosition + new Vector2Int(1, -1)];
            Chunk chunk01 = this.chunks[chunkPosition + new Vector2Int(-1, 0)];
            Chunk chunk11 = this.chunks[chunkPosition + new Vector2Int(0, 0)];
            Chunk chunk21 = this.chunks[chunkPosition + new Vector2Int(1, 0)];
            Chunk chunk02 = this.chunks[chunkPosition + new Vector2Int(-1, 1)];
            Chunk chunk12 = this.chunks[chunkPosition + new Vector2Int(0, 1)];
            Chunk chunk22 = this.chunks[chunkPosition + new Vector2Int(1, 1)];

            this.worldEditMeshJob.vertices.Clear();
            this.worldEditMeshJob.normals.Clear();
            this.worldEditMeshJob.indices.Clear();
            this.worldEditMeshJob.lights.Clear();
            this.worldEditMeshJob.mats1234.Clear();
            this.worldEditMeshJob.mats5678.Clear();
            this.worldEditMeshJob.weights1234.Clear();
            this.worldEditMeshJob.weights5678.Clear();
            this.worldEditMeshJob.breakPoints.Clear();

            this.worldEditMeshJob.voxels00.CopyFrom(chunk00.GetVoxels());
            this.worldEditMeshJob.lights00.CopyFrom(chunk00.GetLights());
            this.worldEditMeshJob.voxels10.CopyFrom(chunk10.GetVoxels());
            this.worldEditMeshJob.lights10.CopyFrom(chunk10.GetLights());
            this.worldEditMeshJob.voxels20.CopyFrom(chunk20.GetVoxels());
            this.worldEditMeshJob.lights20.CopyFrom(chunk20.GetLights());
            this.worldEditMeshJob.voxels01.CopyFrom(chunk01.GetVoxels());
            this.worldEditMeshJob.lights01.CopyFrom(chunk01.GetLights());
            this.worldEditMeshJob.voxels11.CopyFrom(chunk11.GetVoxels());
            this.worldEditMeshJob.lights11.CopyFrom(chunk11.GetLights());
            this.worldEditMeshJob.voxels21.CopyFrom(chunk21.GetVoxels());
            this.worldEditMeshJob.lights21.CopyFrom(chunk21.GetLights());
            this.worldEditMeshJob.voxels02.CopyFrom(chunk02.GetVoxels());
            this.worldEditMeshJob.lights02.CopyFrom(chunk02.GetLights());
            this.worldEditMeshJob.voxels12.CopyFrom(chunk12.GetVoxels());
            this.worldEditMeshJob.lights12.CopyFrom(chunk12.GetLights());
            this.worldEditMeshJob.voxels22.CopyFrom(chunk22.GetVoxels());
            this.worldEditMeshJob.lights22.CopyFrom(chunk22.GetLights());

            this.worldEditMeshJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
            this.worldEditMeshJobHandle = this.worldEditMeshJob.Schedule();
            this.worldEditMeshJobDone = false;
        }

        if (!this.worldEditMeshJobDone && this.worldEditMeshJobHandle.IsCompleted)
        {
            this.worldEditMeshJobHandle.Complete();
            this.worldEditMeshJobDone = true;

            Vector2Int chunkPosition = new Vector2Int(this.worldEditMeshJob.chunkPosition.x, this.worldEditMeshJob.chunkPosition.y);

            if (!this.chunkObjects.ContainsKey(chunkPosition))
                return;

            this.chunkObjects[chunkPosition].SetRenderer(
                this.worldEditMeshJob.vertices.AsArray(),
                this.worldEditMeshJob.normals.AsArray(),
                this.worldEditMeshJob.indices.AsArray(),
                this.worldEditMeshJob.weights1234.AsArray(),
                this.worldEditMeshJob.weights5678.AsArray(),
                this.worldEditMeshJob.mats1234.AsArray(),
                this.worldEditMeshJob.mats5678.AsArray(),
                this.worldEditMeshJob.lights.AsArray()
            );

            for (int i = 0; i < 16; i++)
            {
                int startPositionVertices = this.worldEditMeshJob.breakPoints[i].x;
                int endPositionVertices;
                int startPositionIndices = this.worldEditMeshJob.breakPoints[i].y;
                int endPositionIndices;

                if (i < 15)
                {
                    endPositionVertices = this.worldEditMeshJob.breakPoints[i + 1].x;
                    endPositionIndices = this.worldEditMeshJob.breakPoints[i + 1].y;
                }
                else
                {
                    endPositionVertices = this.worldEditMeshJob.vertices.Length;
                    endPositionIndices = this.worldEditMeshJob.indices.Length;
                }

                if (startPositionVertices == endPositionVertices || startPositionIndices == endPositionIndices)
                {
                    continue;
                }

                int lengthVertices = endPositionVertices - startPositionVertices;
                int lengthIndices = endPositionIndices - startPositionIndices;

                Vector3[] colliderVertices = new Vector3[lengthVertices];
                this.worldEditMeshJob.vertices.AsArray().GetSubArray(startPositionVertices, lengthVertices).CopyTo(colliderVertices);

                for (int j = 0; j < lengthVertices; j++)
                {
                    colliderVertices[j].y -= i * 16.0f;
                }

                int[] colliderIndices = new int[lengthIndices];
                this.worldEditMeshJob.indices.AsArray().GetSubArray(startPositionIndices, lengthIndices).CopyTo(colliderIndices);

                int colliderIndicesOffset = startPositionVertices;

                for (int j = 0; j < lengthIndices; j++)
                {
                    colliderIndices[j] -= colliderIndicesOffset;
                }

                this.chunkObjects[chunkPosition].SetCollider(i, colliderVertices, colliderIndices);
            }

            Debug.Log("done");

            if (this.worldEditMeshQueue.Count == 0)
            {
                this.isWorldEditBlocked = false;
                this.worldEditStopwatch.Stop();
                Debug.Log("worldEdit took: " + this.worldEditStopwatch.ElapsedMilliseconds + "ms");
            }
        }
    }

    /// <summary>
    /// Updates playerChunkPositionLast and playerWorldPositionLast
    /// </summary>
    private void UpdatePlayerPositionLast()
    {
        this.playerChunkPositionLast = this.playerChunkPosition;
        this.playerWorldPositionLast = this.playerWorldPosition.position;
    }

    private void DisposeJobs()
    {
        // Generate Voxels Job
        for (int i = 0; i < this.generateVoxelsJobCount; i++)
        {
            if (!this.generateVoxelsJobDone[i]) this.generateVoxelsJobHandle[i].Complete();
            this.generateVoxelsJob[i].voxels.Dispose();
            this.generateVoxelsJob[i].heights.Dispose();
            this.generateVoxelsJob[i].tempHeights.Dispose();
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
            this.generateMeshJob[i].vertexNormals.Dispose();
            this.generateMeshJob[i].breakPoints.Dispose();
            this.generateMeshJob[i].voxelsMerged.Dispose();
            this.generateMeshJob[i].lightsMerged.Dispose();
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
            this.generateMeshJob[i].cornerIndices.Dispose();
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

        //
        // Mesh Update Job
        //

        if (!this.worldEditMeshJobDone) this.worldEditMeshJobHandle.Complete();
        this.worldEditMeshJob.vertexNormals.Dispose();
        this.worldEditMeshJob.breakPoints.Dispose();
        this.worldEditMeshJob.voxelsMerged.Dispose();
        this.worldEditMeshJob.lightsMerged.Dispose();
        this.worldEditMeshJob.mcCornerPositions.Dispose();
        this.worldEditMeshJob.mcCellClasses.Dispose();
        this.worldEditMeshJob.mcCellGeometryCounts.Dispose();
        this.worldEditMeshJob.mcCellIndices.Dispose();
        this.worldEditMeshJob.mcCellVertexData.Dispose();
        this.worldEditMeshJob.vertices.Dispose();
        this.worldEditMeshJob.normals.Dispose();
        this.worldEditMeshJob.indices.Dispose();
        this.worldEditMeshJob.lights.Dispose();
        this.worldEditMeshJob.mats1234.Dispose();
        this.worldEditMeshJob.mats5678.Dispose();
        this.worldEditMeshJob.weights1234.Dispose();
        this.worldEditMeshJob.weights5678.Dispose();
        this.worldEditMeshJob.cornerPositions.Dispose();
        this.worldEditMeshJob.cornerVoxels.Dispose();
        this.worldEditMeshJob.cornerLights.Dispose();
        this.worldEditMeshJob.cornerIndices.Dispose();
        this.worldEditMeshJob.cellIndices.Dispose();
        this.worldEditMeshJob.mappedIndices.Dispose();
        this.worldEditMeshJob.voxels00.Dispose();
        this.worldEditMeshJob.lights00.Dispose();
        this.worldEditMeshJob.voxels10.Dispose();
        this.worldEditMeshJob.lights10.Dispose();
        this.worldEditMeshJob.voxels20.Dispose();
        this.worldEditMeshJob.lights20.Dispose();
        this.worldEditMeshJob.voxels01.Dispose();
        this.worldEditMeshJob.lights01.Dispose();
        this.worldEditMeshJob.voxels11.Dispose();
        this.worldEditMeshJob.lights11.Dispose();
        this.worldEditMeshJob.voxels21.Dispose();
        this.worldEditMeshJob.lights21.Dispose();
        this.worldEditMeshJob.voxels02.Dispose();
        this.worldEditMeshJob.lights02.Dispose();
        this.worldEditMeshJob.voxels12.Dispose();
        this.worldEditMeshJob.lights12.Dispose();
        this.worldEditMeshJob.voxels22.Dispose();
        this.worldEditMeshJob.lights22.Dispose();
    }

    //
    // World Edit
    //

    public bool WorldEditDraw(Vector3 worldPosition, byte material)
    {
        if (this.isWorldEditBlocked)
            return false;

        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, false);

        if (editPosition.index == -1)
        {
            Debug.Log("WorldEditDraw: Didn't find a close position.");
            return false;
        }

        if (!this.chunks.ContainsKey(editPosition.chunkPosition))
        {
            return false;
        }

        // Voxel
        List<VoxelChange> voxelChanges = new List<VoxelChange>();
        Vector2Int chunkPosition = editPosition.chunkPosition;
        Chunk chunk = this.chunks[chunkPosition];
        int index = editPosition.index;

        Voxel oldVoxel = chunk.GetVoxel(index);
        Voxel newVoxel = new Voxel(-128, material);
        VoxelChange voxelChange = new VoxelChange(chunkPosition, index, oldVoxel, newVoxel);
        voxelChanges.Add(voxelChange);

        oldVoxel = chunk.GetVoxel(index + 361);
        if (!oldVoxel.IsSolid())
        {
            newVoxel = new Voxel(0, 0);
            voxelChange = new VoxelChange(chunkPosition, index + 361, oldVoxel, newVoxel);
            voxelChanges.Add(voxelChange);
        }

        WorldEditData worldEditData = new WorldEditData(editPosition, voxelChanges);

        this.worldEditQueue.Enqueue(worldEditData);

        return true;
    }

    public bool WorldEditErase(Vector3 worldPosition)
    {
        if (this.isWorldEditBlocked)
            return false;

        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, true);

        if (editPosition.index == -1)
        {
            Debug.Log("WorldEditDraw: Didn't find a close position.");
            return false;
        }

        if (!this.chunks.ContainsKey(editPosition.chunkPosition))
        {
            return false;
        }

        // Voxel
        List<VoxelChange> voxelChanges = new List<VoxelChange>();
        Vector2Int chunkPosition = editPosition.chunkPosition;
        Chunk chunk = this.chunks[chunkPosition];
        int index = editPosition.index;

        Voxel oldVoxel = chunk.GetVoxel(index);
        Voxel newVoxel = new Voxel(127, 0);
        VoxelChange voxelChange = new VoxelChange(chunkPosition, index, oldVoxel, newVoxel);
        voxelChanges.Add(voxelChange);

        WorldEditData worldEditData = new WorldEditData(editPosition, voxelChanges);

        this.worldEditQueue.Enqueue(worldEditData);

        return true;
    }

    public bool WorldEditAdd(Vector3 worldPosition, int strength)
    {
        if (this.isWorldEditBlocked)
            return false;

        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, true);

        if (editPosition.index == -1)
        {
            Debug.Log("WorldEditDraw: Didn't find a close position.");
            return false;
        }

        if (!this.chunks.ContainsKey(editPosition.chunkPosition))
        {
            return false;
        }

        // Voxel
        List<VoxelChange> voxelChanges = new List<VoxelChange>();
        Vector2Int chunkPosition = editPosition.chunkPosition;
        Chunk chunk = this.chunks[chunkPosition];
        int index = editPosition.index;

        Voxel oldVoxel = chunk.GetVoxel(index);
        int newDensity = (int)oldVoxel.GetDensity() - strength;
        newDensity = math.clamp(newDensity, -128, -1);
        Voxel newVoxel = new Voxel((sbyte)newDensity, oldVoxel.GetMaterial());
        VoxelChange voxelChange = new VoxelChange(chunkPosition, index, oldVoxel, newVoxel);
        voxelChanges.Add(voxelChange);

        WorldEditData worldEditData = new WorldEditData(editPosition, voxelChanges);

        this.worldEditQueue.Enqueue(worldEditData);

        return true;
    }

    public bool WorldEditSubstract(Vector3 worldPosition, int strength)
    {
        if (this.isWorldEditBlocked)
            return false;

        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, true);

        if (editPosition.index == -1)
        {
            Debug.Log("WorldEditDraw: Didn't find a close position.");
            return false;
        }

        if (!this.chunks.ContainsKey(editPosition.chunkPosition))
        {
            return false;
        }

        // Voxel
        List<VoxelChange> voxelChanges = new List<VoxelChange>();
        Vector2Int chunkPosition = editPosition.chunkPosition;
        Chunk chunk = this.chunks[chunkPosition];
        int index = editPosition.index;

        Voxel oldVoxel = chunk.GetVoxel(index);
        int newDensity = (int)oldVoxel.GetDensity() + strength;
        newDensity = math.clamp(newDensity, -128, -1);
        Voxel newVoxel = new Voxel((sbyte)newDensity, oldVoxel.GetMaterial());
        VoxelChange voxelChange = new VoxelChange(chunkPosition, index, oldVoxel, newVoxel);
        voxelChanges.Add(voxelChange);

        WorldEditData worldEditData = new WorldEditData(editPosition, voxelChanges);

        this.worldEditQueue.Enqueue(worldEditData);

        return true;
    }

    public bool WorldEditPaint(Vector3 worldPosition, byte material)
    {
        if (this.isWorldEditBlocked)
            return false;

        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, true);

        if (editPosition.index == -1)
        {
            Debug.Log("WorldEditDraw: Didn't find a close position.");
            return false;
        }

        if (!this.chunks.ContainsKey(editPosition.chunkPosition))
        {
            return false;
        }

        // Voxel
        List<VoxelChange> voxelChanges = new List<VoxelChange>();
        Vector2Int chunkPosition = editPosition.chunkPosition;
        Chunk chunk = this.chunks[chunkPosition];
        int index = editPosition.index;

        Voxel oldVoxel = chunk.GetVoxel(index);
        Voxel newVoxel = new Voxel(oldVoxel.GetDensity(), material);
        VoxelChange voxelChange = new VoxelChange(chunkPosition, index, oldVoxel, newVoxel);
        voxelChanges.Add(voxelChange);

        WorldEditData worldEditData = new WorldEditData(editPosition, voxelChanges);

        this.worldEditQueue.Enqueue(worldEditData);

        return true;
    }

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

    private EditPosition WorldToEditPosition(Vector3 worldPosition)
    {
        // Rounded Position
        Vector3Int roundedPosition = Vector3Int.RoundToInt(worldPosition);
        roundedPosition.y = Mathf.Clamp(roundedPosition.y, 2, 253);

        // Relative Position
        Vector3Int relativePosition = new Vector3Int(
            roundedPosition.x % 16,
            roundedPosition.y,
            roundedPosition.z % 16
        );

        if (relativePosition.x < 0 && relativePosition.x != 0)
        {
            relativePosition.x = relativePosition.x + 16;
        }
        if (relativePosition.z < 0 && relativePosition.z != 0)
        {
            relativePosition.z = relativePosition.z + 16;
        }

        // Chunk Position
        Vector2Int chunkPosition = new Vector2Int(
            Mathf.FloorToInt(roundedPosition.x / 16.0f),
            Mathf.FloorToInt(roundedPosition.z / 16.0f)
        );

        // Index
        int index = 0;
        index = relativePosition.x + relativePosition.z * 16 + relativePosition.y * 256;

        EditPosition editPosition = new EditPosition(
            worldPosition,
            roundedPosition,
            relativePosition,
            chunkPosition,
            index
        );

        return editPosition;
    }

    //
    // Diagnostics
    //

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

    // Other

    public EditPosition GetClosestEditPosition(Vector3 worldPosition, bool solid)
    {
        EditPosition closestEditPosition = new EditPosition(Vector3.zero, Vector3Int.zero, Vector3Int.zero, Vector2Int.zero, -1);
        float minDistance = 100.0f;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    EditPosition editPosition = this.WorldToEditPosition(worldPosition + new Vector3(x, y, z));

                    Voxel voxel = this.chunks[editPosition.chunkPosition].GetVoxel(editPosition.index);
                    bool isValid = false;
                    if (solid) isValid = (voxel.IsSolid());
                    if (!solid) isValid = (!voxel.IsSolid());

                    float distance = Vector3.Distance(worldPosition, editPosition.roundedPosition);
                    if (distance < minDistance && isValid)
                    {
                        minDistance = distance;
                        closestEditPosition = editPosition;
                    }
                }
            }
        }
        
        return closestEditPosition;
    }

    public float GetLightValue(Vector3 worldPosition)
    {
        EditPosition editPosition = this.GetClosestEditPosition(worldPosition, false);

        if (this.chunks.ContainsKey(editPosition.chunkPosition) && editPosition.index != -1)
        {
            return (float)this.chunks[editPosition.chunkPosition].GetSunLight(editPosition.index) / 15.0f;
        }

        return 0.0f;
    }
}