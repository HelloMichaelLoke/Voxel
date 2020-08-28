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
    private bool didChunkLoadingOriginChange = false;

    // Material
    public Material terrainMaterial;
    public Texture2D[] chunkTexturesColor;
    public Texture2D[] chunkTexturesHeight;

    // Chunks store lights, densities and materials
    private Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private Dictionary<Vector2Int, ChunkObject> chunkObjects = new Dictionary<Vector2Int, ChunkObject>();

    // Job for generating densities and materials
    private GenerateTerrainJob generateTerrainJob;
    private JobHandle generateTerrainJobHandle;
    private bool generateTerrainJobDone = true;
    
    // Job for generating sun lights
    private SunLightJob sunLightJob;
    private JobHandle sunLightJobHandle;
    private bool sunLightJobDone = true;

    // Job for generating the mesh
    private MeshTerrainJob meshTerrainJob;
    private JobHandle meshTerrainJobHandle;
    private bool meshTerrainJobDone = true;

    // World Edit Mesh Job
    private MeshTerrainJob worldEditMeshJob;
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

    private Queue<Vector2Int> generateTerrainQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateLightsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateMeshQueue = new Queue<Vector2Int>();

    private Queue<Vector2Int> chunksToDestroy = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToDeactivate = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToLoad = new Queue<Vector2Int>();

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
        this.GenerateTerrains();
        this.GenerateLights();
        this.GenerateMeshes();
        this.UpdateWorldEdit();
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
        // Terrain Job
        this.generateTerrainJob.voxels = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.generateTerrainJob.heights = new NativeArray<float>(256, Allocator.Persistent);
        this.generateTerrainJob.tempHeights = new NativeArray<float>(324, Allocator.Persistent);

        // Light Job
        this.sunLightJob.lights = new NativeArray<byte>(65536, Allocator.Persistent);
        this.sunLightJob.lightQueue = new NativeQueue<int3>(Allocator.Persistent);
        this.sunLightJob.isSolid = new NativeArray<bool>(589824, Allocator.Persistent);
        this.sunLightJob.sunLights = new NativeArray<byte>(589824, Allocator.Persistent);
        this.sunLightJob.voxels00 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels10 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels20 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels01 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels11 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels21 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels02 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels12 = new NativeArray<Voxel>(65536, Allocator.Persistent);
        this.sunLightJob.voxels22 = new NativeArray<Voxel>(65536, Allocator.Persistent);

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

        // Mesh Job
        this.meshTerrainJob = new MeshTerrainJob()
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

        this.meshTerrainJob.mcCornerPositions.CopyFrom(Tables.cornerPositions);
        this.meshTerrainJob.mcCellClasses.CopyFrom(Tables.cellClasses);
        this.meshTerrainJob.mcCellGeometryCounts.CopyFrom(Tables.cellGeometryCounts);
        this.meshTerrainJob.mcCellIndices.CopyFrom(Tables.cellIndices);
        this.meshTerrainJob.mcCellVertexData.CopyFrom(Tables.cellVertexData);

        // World Edit Mesh Job
        this.worldEditMeshJob = new MeshTerrainJob()
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

        this.chunkLoadingOrigin = this.playerChunkPosition;
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
            this.generateTerrainQueue.Clear();
            this.generateLightsQueue.Clear();
            this.generateMeshQueue.Clear();
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

        this.generateTerrainQueue.Clear();
        this.generateLightsQueue.Clear();
        this.generateMeshQueue.Clear();

        if (!this.generateTerrainJobDone || !this.sunLightJobDone || !this.meshTerrainJobDone)
        {
            return;
        }

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

        this.startStopwatch();
        while (this.chunksToLoad.Count > 0)
        {
            Vector2Int chunkPosition = this.chunksToLoad.Dequeue();
            this.generateTerrainQueue.Enqueue(chunkPosition);
        }
        this.stopStopwatch("queue up generate terrain queue", 0);

        this.isLoadChunksPending = false;
    }

    private void GenerateTerrains()
    {
        // Dequeue and schedule terrain generation of the chunk (densities & materials)
        if (this.generateTerrainJobDone && this.generateTerrainQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.generateTerrainQueue.Dequeue();

            if (this.chunks.ContainsKey(chunkPosition))
            {
                if (!this.chunks[chunkPosition].hasLights)
                {
                    this.generateLightsQueue.Enqueue(chunkPosition);
                }
                else if (!this.chunks[chunkPosition].hasObjects)
                {
                    this.generateMeshQueue.Enqueue(chunkPosition);
                }
                else if (this.chunks[chunkPosition].hasObjects)
                {
                    if (this.IsInChunkDistance(chunkPosition) && !this.chunkObjects[chunkPosition].IsActive())
                    {
                        this.chunkObjects[chunkPosition].Activate();
                    }
                }

                return;
            }

            this.generateTerrainJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
            this.generateTerrainJobHandle = this.generateTerrainJob.Schedule();
            this.generateTerrainJobDone = false;
        }

        // Save chunk with densities and material to the chunk dictionary and enqueue chunk for light generation
        if (!this.generateTerrainJobDone && this.generateTerrainJobHandle.IsCompleted)
        {
            this.generateTerrainJobHandle.Complete();
            this.generateTerrainJobDone = true;

            Chunk chunk = new Chunk();
            chunk.SetVoxelsFromNative(this.generateTerrainJob.voxels);
            chunk.hasVoxels = true;

            Vector2Int chunkPosition = new Vector2Int(this.generateTerrainJob.chunkPosition.x, this.generateTerrainJob.chunkPosition.y);
            this.chunks.Add(chunkPosition, chunk);

            if (!this.isUpdateChunkQueuePending && this.IsInChunkDistance(chunkPosition, 1))
            {
                this.generateLightsQueue.Enqueue(chunkPosition);
            }
        }
    }

    private void GenerateLights()
    {
        // Dequeue and schedule light generation of the chunk (lights)
        if (this.sunLightJobDone && this.generateLightsQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.generateLightsQueue.Peek();

            if (this.chunks[chunkPosition].hasLights)
            {
                this.generateMeshQueue.Enqueue(chunkPosition);
                this.generateLightsQueue.Dequeue();
                return;
            }

            if (!this.IsInChunkDistance(chunkPosition, 1))
            {
                this.generateLightsQueue.Dequeue();
                return;
            }

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

                this.sunLightJob.voxels00.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetVoxels());
                this.sunLightJob.voxels10.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, -1)].GetVoxels());
                this.sunLightJob.voxels20.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, -1)].GetVoxels());
                this.sunLightJob.voxels01.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 0)].GetVoxels());
                this.sunLightJob.voxels11.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 0)].GetVoxels());
                this.sunLightJob.voxels21.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 0)].GetVoxels());
                this.sunLightJob.voxels02.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 1)].GetVoxels());
                this.sunLightJob.voxels12.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 1)].GetVoxels());
                this.sunLightJob.voxels22.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 1)].GetVoxels());
                this.sunLightJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);

                this.sunLightJobHandle = this.sunLightJob.Schedule();
                this.sunLightJobDone = false;
            }
        }

        // Save chunk lights and enqueue for mesh generation
        if (!this.sunLightJobDone && this.sunLightJobHandle.IsCompleted)
        {
            this.sunLightJobHandle.Complete();
            this.sunLightJobDone = true;

            Vector2Int chunkPosition = new Vector2Int(this.sunLightJob.chunkPosition.x, this.sunLightJob.chunkPosition.y);
            this.chunks[chunkPosition].SetLightsFromNative(this.sunLightJob.lights);
            this.chunks[chunkPosition].hasLights = true;

            if (!this.isUpdateChunkQueuePending && this.IsInChunkDistance(chunkPosition))
            {
                this.generateMeshQueue.Enqueue(chunkPosition);
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
        if (this.meshTerrainJobDone && this.generateMeshQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.generateMeshQueue.Peek();

            if (this.chunkObjects.ContainsKey(chunkPosition))
            {
                if (this.IsInChunkDistance(chunkPosition) && !this.chunkObjects[chunkPosition].IsActive())
                {
                    this.chunkObjects[chunkPosition].Activate();
                }
                this.generateMeshQueue.Dequeue();
                return;
            }

            if (!this.IsInChunkDistance(chunkPosition))
            {
                this.generateMeshQueue.Dequeue();
                return;
            }

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

                this.meshTerrainJob.vertices.Clear();
                this.meshTerrainJob.normals.Clear();
                this.meshTerrainJob.indices.Clear();
                this.meshTerrainJob.lights.Clear();
                this.meshTerrainJob.mats1234.Clear();
                this.meshTerrainJob.mats5678.Clear();
                this.meshTerrainJob.weights1234.Clear();
                this.meshTerrainJob.weights5678.Clear();
                this.meshTerrainJob.breakPoints.Clear();
                this.meshTerrainJob.voxels00.CopyFrom(chunk00.GetVoxels());
                this.meshTerrainJob.lights00.CopyFrom(chunk00.GetLights());
                this.meshTerrainJob.voxels10.CopyFrom(chunk10.GetVoxels());
                this.meshTerrainJob.lights10.CopyFrom(chunk10.GetLights());
                this.meshTerrainJob.voxels20.CopyFrom(chunk20.GetVoxels());
                this.meshTerrainJob.lights20.CopyFrom(chunk20.GetLights());
                this.meshTerrainJob.voxels01.CopyFrom(chunk01.GetVoxels());
                this.meshTerrainJob.lights01.CopyFrom(chunk01.GetLights());
                this.meshTerrainJob.voxels11.CopyFrom(chunk11.GetVoxels());
                this.meshTerrainJob.lights11.CopyFrom(chunk11.GetLights());
                this.meshTerrainJob.voxels21.CopyFrom(chunk21.GetVoxels());
                this.meshTerrainJob.lights21.CopyFrom(chunk21.GetLights());
                this.meshTerrainJob.voxels02.CopyFrom(chunk02.GetVoxels());
                this.meshTerrainJob.lights02.CopyFrom(chunk02.GetLights());
                this.meshTerrainJob.voxels12.CopyFrom(chunk12.GetVoxels());
                this.meshTerrainJob.lights12.CopyFrom(chunk12.GetLights());
                this.meshTerrainJob.voxels22.CopyFrom(chunk22.GetVoxels());
                this.meshTerrainJob.lights22.CopyFrom(chunk22.GetLights());
                this.meshTerrainJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);

                this.meshTerrainJobHandle = this.meshTerrainJob.Schedule();
                this.meshTerrainJobDone = false;
            }
        }
    }

    private void GenerateMeshesComplete()
    {
        if (!this.meshTerrainJobDone && this.meshTerrainJobHandle.IsCompleted)
        {
            this.meshTerrainJobHandle.Complete();
            this.meshTerrainJobDone = true;

            Vector2Int chunkPosition = new Vector2Int(this.meshTerrainJob.chunkPosition.x, this.meshTerrainJob.chunkPosition.y);

            ChunkObject chunkObject = new ChunkObject(chunkPosition, this.terrainMaterial);

            chunkObject.SetRenderer(
                this.meshTerrainJob.vertices.AsArray(),
                this.meshTerrainJob.normals.AsArray(),
                this.meshTerrainJob.indices.AsArray(),
                this.meshTerrainJob.weights1234.AsArray(),
                this.meshTerrainJob.weights5678.AsArray(),
                this.meshTerrainJob.mats1234.AsArray(),
                this.meshTerrainJob.mats5678.AsArray(),
                this.meshTerrainJob.lights.AsArray()
            );

            for (int i = 0; i < 16; i++)
            {
                int startPositionVertices = this.meshTerrainJob.breakPoints[i].x;
                int endPositionVertices;
                int startPositionIndices = this.meshTerrainJob.breakPoints[i].y;
                int endPositionIndices;

                if (i < 15)
                {
                    endPositionVertices = this.meshTerrainJob.breakPoints[i + 1].x;
                    endPositionIndices = this.meshTerrainJob.breakPoints[i + 1].y;
                }
                else
                {
                    endPositionVertices = this.meshTerrainJob.vertices.Length;
                    endPositionIndices = this.meshTerrainJob.indices.Length;
                }

                if (startPositionVertices == endPositionVertices || startPositionIndices == endPositionIndices)
                {
                    continue;
                }

                int lengthVertices = endPositionVertices - startPositionVertices;
                int lengthIndices = endPositionIndices - startPositionIndices;

                Vector3[] colliderVertices = new Vector3[lengthVertices];
                this.meshTerrainJob.vertices.AsArray().GetSubArray(startPositionVertices, lengthVertices).CopyTo(colliderVertices);

                for (int j = 0; j < lengthVertices; j++)
                {
                    colliderVertices[j].y -= i * 16.0f;
                }

                int[] colliderIndices = new int[lengthIndices];
                this.meshTerrainJob.indices.AsArray().GetSubArray(startPositionIndices, lengthIndices).CopyTo(colliderIndices);

                int colliderIndicesOffset = startPositionVertices;

                for (int j = 0; j < lengthIndices; j++)
                {
                    colliderIndices[j] -= colliderIndicesOffset;
                }

                chunkObject.SetCollider(i, colliderVertices, colliderIndices);
            }

            this.chunkObjects.Add(chunkPosition, chunkObject);
            this.chunks[chunkPosition].hasObjects = true;
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
        if (!this.generateTerrainJobDone) this.generateTerrainJobHandle.Complete();
        if (!this.sunLightJobDone) this.sunLightJobHandle.Complete();
        if (!this.meshTerrainJobDone) this.meshTerrainJobHandle.Complete();
        if (!this.worldEditMeshJobDone) this.worldEditMeshJobHandle.Complete();
        if (!this.lightRemovalJobDone) this.lightRemovalJobHandle.Complete();

        //
        // Generate Terrain Job
        //

        this.generateTerrainJob.voxels.Dispose();
        this.generateTerrainJob.heights.Dispose();
        this.generateTerrainJob.tempHeights.Dispose();

        //
        // Sun Light Job
        //

        this.sunLightJob.lights.Dispose();
        this.sunLightJob.isSolid.Dispose();
        this.sunLightJob.sunLights.Dispose();
        this.sunLightJob.lightQueue.Dispose();
        this.sunLightJob.voxels00.Dispose();
        this.sunLightJob.voxels10.Dispose();
        this.sunLightJob.voxels20.Dispose();
        this.sunLightJob.voxels01.Dispose();
        this.sunLightJob.voxels11.Dispose();
        this.sunLightJob.voxels21.Dispose();
        this.sunLightJob.voxels02.Dispose();
        this.sunLightJob.voxels12.Dispose();
        this.sunLightJob.voxels22.Dispose();

        //
        // Light Update Job
        //

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
        // Mesh Terrain Job
        //

        this.meshTerrainJob.vertexNormals.Dispose();
        this.meshTerrainJob.breakPoints.Dispose();
        this.meshTerrainJob.voxelsMerged.Dispose();
        this.meshTerrainJob.lightsMerged.Dispose();
        this.meshTerrainJob.mcCornerPositions.Dispose();
        this.meshTerrainJob.mcCellClasses.Dispose();
        this.meshTerrainJob.mcCellGeometryCounts.Dispose();
        this.meshTerrainJob.mcCellIndices.Dispose();
        this.meshTerrainJob.mcCellVertexData.Dispose();
        this.meshTerrainJob.vertices.Dispose();
        this.meshTerrainJob.normals.Dispose();
        this.meshTerrainJob.indices.Dispose();
        this.meshTerrainJob.lights.Dispose();
        this.meshTerrainJob.mats1234.Dispose();
        this.meshTerrainJob.mats5678.Dispose();
        this.meshTerrainJob.weights1234.Dispose();
        this.meshTerrainJob.weights5678.Dispose();
        this.meshTerrainJob.cornerPositions.Dispose();
        this.meshTerrainJob.cornerVoxels.Dispose();
        this.meshTerrainJob.cornerLights.Dispose();
        this.meshTerrainJob.cornerIndices.Dispose();
        this.meshTerrainJob.cellIndices.Dispose();
        this.meshTerrainJob.mappedIndices.Dispose();
        this.meshTerrainJob.voxels00.Dispose();
        this.meshTerrainJob.lights00.Dispose();
        this.meshTerrainJob.voxels10.Dispose();
        this.meshTerrainJob.lights10.Dispose();
        this.meshTerrainJob.voxels20.Dispose();
        this.meshTerrainJob.lights20.Dispose();
        this.meshTerrainJob.voxels01.Dispose();
        this.meshTerrainJob.lights01.Dispose();
        this.meshTerrainJob.voxels11.Dispose();
        this.meshTerrainJob.lights11.Dispose();
        this.meshTerrainJob.voxels21.Dispose();
        this.meshTerrainJob.lights21.Dispose();
        this.meshTerrainJob.voxels02.Dispose();
        this.meshTerrainJob.lights02.Dispose();
        this.meshTerrainJob.voxels12.Dispose();
        this.meshTerrainJob.lights12.Dispose();
        this.meshTerrainJob.voxels22.Dispose();
        this.meshTerrainJob.lights22.Dispose();

        //
        // Mesh Update Job
        //

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