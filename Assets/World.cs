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
    private int chunkDistance = 5;

    // Player Information
    public GameObject playerGameObject;
    public Transform playerWorldPosition;
    private Vector3 playerWorldPositionLast;
    private Vector2Int playerChunkPosition = new Vector2Int(2147483647, 2147483647);
    private Vector2Int playerChunkPositionLast = new Vector2Int(2147483647, 2147483647);
    private bool didPlayerEnterChunk = false;

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

    // Job for generating sun lights
    private SunLightJob tempSunLightJob;
    private JobHandle tempSunLightJobHandle;
    private bool tempSunLightJobDone = true;

    // Job for generating the mesh
    private MeshTerrainJob meshTerrainJob;
    private JobHandle meshTerrainJobHandle;
    private bool meshTerrainJobDone = true;

    // World Edit Mesh Job
    private MeshTerrainJob worldEditMeshJob;
    private JobHandle worldEditMeshJobHandle;
    private bool worldEditMeshJobDone = true;
    private Queue<Vector2Int> worldEditMeshQueue = new Queue<Vector2Int>();

    private bool isUpdateChunkQueuePending = false;
    private Queue<Vector2Int> loadChunkQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateTerrainQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateLightsQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> generateMeshQueue = new Queue<Vector2Int>();

    // Clean Up
    private Queue<Vector2Int> chunksToDestroy = new Queue<Vector2Int>();
    private Queue<Vector2Int> chunksToDeactivate = new Queue<Vector2Int>();

    // Chunk Loading
    private List<Vector2Int> chunkLoadingOrder = new List<Vector2Int>();

    // Diagnostics
    private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

    private bool worldEditQueueEmpty = true;
    private System.Diagnostics.Stopwatch worldEditStopwatch = new System.Diagnostics.Stopwatch();

    private void Start()
    {
        this.InitMaterial();
        this.InitJobs();

        this.playerWorldPositionLast = this.playerWorldPosition.position;

        int arraySize = (this.chunkDistance + 2) * 2 + 1;
        arraySize = arraySize * arraySize;
        List<Vector2Int> chunkLoadingOrder = new List<Vector2Int>();

        Vector2Int currentPosition = Vector2Int.zero;
        int iterator = 1;
        int i = 0;

        while (i < arraySize)
        {
            for (int j = 1; j <= iterator; j++)
            {
                chunkLoadingOrder.Add(currentPosition);

                if (iterator % 2 == 1)
                    currentPosition.y++;
                else
                    currentPosition.y--;

                i++;
            }

            for (int j = 1; j <= iterator; j++)
            {
                chunkLoadingOrder.Add(currentPosition);

                if (iterator % 2 == 1)
                    currentPosition.x++;
                else
                    currentPosition.x--;

                i++;
            }

            iterator++;
        }

        this.chunkLoadingOrder = chunkLoadingOrder;
    }

    private void Update()
    {
        if (this.locator.activeInHierarchy)
        {
            if (Input.GetMouseButtonDown(0))
            {
                this.WorldEditDraw(locator.transform.position, -127, 1);
            }

            if (Input.GetMouseButtonDown(1))
            {
                this.WorldEditErase(locator.transform.position);
            }

            if (Input.GetMouseButtonDown(2))
            {
                this.WorldEditHeighten(locator.transform.position, 20);
            }
        }

        this.UpdatePlayerChunkPosition();

        if (!this.chunkObjects.ContainsKey(this.playerChunkPosition))
            this.playerGameObject.GetComponent<PlayerController>().Stop();

        this.UpdateChunkQueue();
        this.UnloadChunks();
        this.LoadChunks();
        this.GenerateTerrains();
        this.GenerateLights(); // 3ms (SetSunLightsFromNative)
        this.GenerateMeshes(); // 9-14ms (creating mesh 5ms & uploading meshcollider 5ms)
        this.UpdateWorldEdit();

        /*
        Debug.Log("Terrain Queue: " + this.generateTerrainQueue.Count);
        Debug.Log("Lights Queue: " + this.generateLightsQueue.Count);
        Debug.Log("Mesh Queue: " + this.generateMeshQueue.Count);
        */

        this.UpdatePlayerChunkPositionLast();
    }

    private void OnApplicationQuit()
    {
        this.DisposeJobs();
    }

    private void UpdateWorldEdit()
    {
        if (this.worldEditQueueEmpty && this.worldEditMeshQueue.Count > 0)
        {
            this.worldEditQueueEmpty = false;
            this.worldEditStopwatch.Reset();
            this.worldEditStopwatch.Start();
        }
        else if (!this.worldEditQueueEmpty && this.worldEditMeshQueue.Count == 0)
        {
            this.worldEditQueueEmpty = true;
            this.worldEditStopwatch.Stop();
            //Debug.Log("worldEdit took: " + this.worldEditStopwatch.ElapsedMilliseconds + "ms");
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

            // Update Sun Light

            this.startStopwatch();
            this.tempSunLightJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
            this.tempSunLightJob.chunk00densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetDensities());
            this.tempSunLightJob.chunk10densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, -1)].GetDensities());
            this.tempSunLightJob.chunk20densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, -1)].GetDensities());
            this.tempSunLightJob.chunk01densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 0)].GetDensities());
            this.tempSunLightJob.chunk11densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 0)].GetDensities());
            this.tempSunLightJob.chunk21densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 0)].GetDensities());
            this.tempSunLightJob.chunk02densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, 1)].GetDensities());
            this.tempSunLightJob.chunk12densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(0, 1)].GetDensities());
            this.tempSunLightJob.chunk22densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(1, 1)].GetDensities());
            this.tempSunLightJobHandle = this.tempSunLightJob.Schedule();

            this.tempSunLightJobHandle.Complete();
            this.tempSunLightJobDone = true;
            this.chunks[chunkPosition].SetSunLightsFromNative(this.tempSunLightJob.sunLights);
            this.chunks[chunkPosition].areLightsDone = true;
            this.stopStopwatch("sunLightUpdate", 0);

            // End Update Sun Light

            this.worldEditMeshJob.vertices.Clear();
            this.worldEditMeshJob.normals.Clear();
            this.worldEditMeshJob.indices.Clear();
            this.worldEditMeshJob.lights.Clear();
            this.worldEditMeshJob.mats1234.Clear();
            this.worldEditMeshJob.mats5678.Clear();
            this.worldEditMeshJob.weights1234.Clear();
            this.worldEditMeshJob.weights5678.Clear();
            this.worldEditMeshJob.chunk00densities.CopyFrom(chunk00.GetDensities());
            this.worldEditMeshJob.chunk00materials.CopyFrom(chunk00.GetMaterials());
            this.worldEditMeshJob.chunk00lights.CopyFrom(chunk00.GetLights());
            this.worldEditMeshJob.chunk10densities.CopyFrom(chunk10.GetDensities());
            this.worldEditMeshJob.chunk10materials.CopyFrom(chunk10.GetMaterials());
            this.worldEditMeshJob.chunk10lights.CopyFrom(chunk10.GetLights());
            this.worldEditMeshJob.chunk20densities.CopyFrom(chunk20.GetDensities());
            this.worldEditMeshJob.chunk20materials.CopyFrom(chunk20.GetMaterials());
            this.worldEditMeshJob.chunk20lights.CopyFrom(chunk20.GetLights());
            this.worldEditMeshJob.chunk01densities.CopyFrom(chunk01.GetDensities());
            this.worldEditMeshJob.chunk01materials.CopyFrom(chunk01.GetMaterials());
            this.worldEditMeshJob.chunk01lights.CopyFrom(chunk01.GetLights());
            this.worldEditMeshJob.chunk11densities.CopyFrom(chunk11.GetDensities());
            this.worldEditMeshJob.chunk11materials.CopyFrom(chunk11.GetMaterials());
            this.worldEditMeshJob.chunk11lights.CopyFrom(chunk11.GetLights());
            this.worldEditMeshJob.chunk21densities.CopyFrom(chunk21.GetDensities());
            this.worldEditMeshJob.chunk21materials.CopyFrom(chunk21.GetMaterials());
            this.worldEditMeshJob.chunk21lights.CopyFrom(chunk21.GetLights());
            this.worldEditMeshJob.chunk02densities.CopyFrom(chunk02.GetDensities());
            this.worldEditMeshJob.chunk02materials.CopyFrom(chunk02.GetMaterials());
            this.worldEditMeshJob.chunk02lights.CopyFrom(chunk02.GetLights());
            this.worldEditMeshJob.chunk12densities.CopyFrom(chunk12.GetDensities());
            this.worldEditMeshJob.chunk12materials.CopyFrom(chunk12.GetMaterials());
            this.worldEditMeshJob.chunk12lights.CopyFrom(chunk12.GetLights());
            this.worldEditMeshJob.chunk22densities.CopyFrom(chunk22.GetDensities());
            this.worldEditMeshJob.chunk22materials.CopyFrom(chunk22.GetMaterials());
            this.worldEditMeshJob.chunk22lights.CopyFrom(chunk22.GetLights());

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

            // Update Colliders TODO!!!
        }
    }

    private void UpdateChunkQueue()
    {
        if (this.didPlayerEnterChunk)
        {
            this.generateTerrainQueue.Clear();
            this.generateLightsQueue.Clear();
            this.generateMeshQueue.Clear();
            this.isUpdateChunkQueuePending = true;
        }

        if (this.isUpdateChunkQueuePending)
        {
            if (this.generateTerrainJobDone == false) return;
            if (this.sunLightJobDone == false) return;
            if (this.meshTerrainJobDone == false) return;
        }

        if (!this.isUpdateChunkQueuePending)
            return;

        this.generateTerrainQueue.Clear();
        this.generateLightsQueue.Clear();
        this.generateMeshQueue.Clear();

        this.isUpdateChunkQueuePending = false;

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
            Vector2Int chunkPosition = this.playerChunkPosition + offset;
            this.loadChunkQueue.Enqueue(chunkPosition);
        }
    }

    private void UnloadChunks()
    {
        while (this.chunksToDestroy.Count > 0)
        {
            Vector2Int chunkPosition = this.chunksToDestroy.Dequeue();

            if (this.chunks[chunkPosition].areMeshesDone)
            {
                this.chunkObjects[chunkPosition].Destroy();
                this.chunkObjects.Remove(chunkPosition);
            }
            this.chunks.Remove(chunkPosition);
        }

        while (this.chunksToDeactivate.Count > 0)
        {
            Vector2Int chunkPosition = this.chunksToDeactivate.Dequeue();
            this.chunkObjects[chunkPosition].Deactivate();
        }
    }

    private void LoadChunks()
    {
        while (this.loadChunkQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.loadChunkQueue.Dequeue();

            if (!this.chunks.ContainsKey(chunkPosition))
            {
                this.generateTerrainQueue.Enqueue(chunkPosition);
                continue;
            }

            if (!this.chunks[chunkPosition].areLightsDone)
            {
                if (this.IsInChunkDistance(chunkPosition, 1))
                {
                    this.generateLightsQueue.Enqueue(chunkPosition);
                }

                continue;
            }

            if (!this.chunks[chunkPosition].areMeshesDone)
            {
                if (this.IsInChunkDistance(chunkPosition))
                {
                    this.generateMeshQueue.Enqueue(chunkPosition);
                }
                continue;
            }

            this.chunkObjects[chunkPosition].Activate();

            if (!this.chunkObjects[chunkPosition].IsActive())
            {
                this.chunkObjects[chunkPosition].Activate();
            }
        }
    }

    private void GenerateTerrains()
    {
        // Dequeue and schedule terrain generation of the chunk (densities & materials)
        if (this.generateTerrainJobDone && this.generateTerrainQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.generateTerrainQueue.Dequeue();

            this.generateTerrainJob.chunkCoordinate = new int2(chunkPosition.x, chunkPosition.y);
            this.generateTerrainJobHandle = this.generateTerrainJob.Schedule();
            this.generateTerrainJobDone = false;
        }

        // Save chunk with densities and material to the chunk dictionary and enqueue chunk for light generation
        if (!this.generateTerrainJobDone && this.generateTerrainJobHandle.IsCompleted)
        {
            this.generateTerrainJobHandle.Complete();
            this.generateTerrainJobDone = true;
            Chunk chunk = new Chunk();
            chunk.SetDensitiesFromNative(this.generateTerrainJob.densities);
            chunk.SetMaterialsFromNative(this.generateTerrainJob.materials);
            chunk.areDensitiesDone = true;
            Vector2Int chunkPosition = new Vector2Int(this.generateTerrainJob.chunkCoordinate.x, this.generateTerrainJob.chunkCoordinate.y);
            this.chunks.Add(chunkPosition, chunk);

            if (this.IsInChunkDistance(chunkPosition, 1))
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

                this.sunLightJob.chunk00densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1, -1)].GetDensities());
                this.sunLightJob.chunk10densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 0, -1)].GetDensities());
                this.sunLightJob.chunk20densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 1, -1)].GetDensities());
                this.sunLightJob.chunk01densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1,  0)].GetDensities());
                this.sunLightJob.chunk11densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 0,  0)].GetDensities());
                this.sunLightJob.chunk21densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 1,  0)].GetDensities());
                this.sunLightJob.chunk02densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int(-1,  1)].GetDensities());
                this.sunLightJob.chunk12densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 0,  1)].GetDensities());
                this.sunLightJob.chunk22densities.CopyFrom(this.chunks[chunkPosition + new Vector2Int( 1,  1)].GetDensities());

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
            this.chunks[chunkPosition].SetSunLightsFromNative(this.sunLightJob.sunLights);
            this.chunks[chunkPosition].areLightsDone = true;

            if (this.IsInChunkDistance(chunkPosition))
            {
                this.generateMeshQueue.Enqueue(chunkPosition);
            }
        }
    }

    private void GenerateMeshes()
    {
        if (this.meshTerrainJobDone && this.generateMeshQueue.Count > 0)
        {
            Vector2Int chunkPosition = this.generateMeshQueue.Peek();

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
                        if (!this.chunks[neighborPosition].areLightsDone)
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
                Chunk chunk10 = this.chunks[chunkPosition + new Vector2Int( 0, -1)];
                Chunk chunk20 = this.chunks[chunkPosition + new Vector2Int( 1, -1)];
                Chunk chunk01 = this.chunks[chunkPosition + new Vector2Int(-1,  0)];
                Chunk chunk11 = this.chunks[chunkPosition + new Vector2Int( 0,  0)];
                Chunk chunk21 = this.chunks[chunkPosition + new Vector2Int( 1,  0)];
                Chunk chunk02 = this.chunks[chunkPosition + new Vector2Int(-1,  1)];
                Chunk chunk12 = this.chunks[chunkPosition + new Vector2Int( 0,  1)];
                Chunk chunk22 = this.chunks[chunkPosition + new Vector2Int( 1,  1)];

                this.meshTerrainJob.vertices.Clear();
                this.meshTerrainJob.normals.Clear();
                this.meshTerrainJob.indices.Clear();
                this.meshTerrainJob.lights.Clear();
                this.meshTerrainJob.mats1234.Clear();
                this.meshTerrainJob.mats5678.Clear();
                this.meshTerrainJob.weights1234.Clear();
                this.meshTerrainJob.weights5678.Clear();
                this.meshTerrainJob.breakPoints.Clear();

                this.meshTerrainJob.chunk00densities.CopyFrom(chunk00.GetDensities());
                this.meshTerrainJob.chunk00materials.CopyFrom(chunk00.GetMaterials());
                this.meshTerrainJob.chunk00lights.CopyFrom(chunk00.GetLights());

                this.meshTerrainJob.chunk10densities.CopyFrom(chunk10.GetDensities());
                this.meshTerrainJob.chunk10materials.CopyFrom(chunk10.GetMaterials());
                this.meshTerrainJob.chunk10lights.CopyFrom(chunk10.GetLights());

                this.meshTerrainJob.chunk20densities.CopyFrom(chunk20.GetDensities());
                this.meshTerrainJob.chunk20materials.CopyFrom(chunk20.GetMaterials());
                this.meshTerrainJob.chunk20lights.CopyFrom(chunk20.GetLights());

                this.meshTerrainJob.chunk01densities.CopyFrom(chunk01.GetDensities());
                this.meshTerrainJob.chunk01materials.CopyFrom(chunk01.GetMaterials());
                this.meshTerrainJob.chunk01lights.CopyFrom(chunk01.GetLights());

                this.meshTerrainJob.chunk11densities.CopyFrom(chunk11.GetDensities());
                this.meshTerrainJob.chunk11materials.CopyFrom(chunk11.GetMaterials());
                this.meshTerrainJob.chunk11lights.CopyFrom(chunk11.GetLights());

                this.meshTerrainJob.chunk21densities.CopyFrom(chunk21.GetDensities());
                this.meshTerrainJob.chunk21materials.CopyFrom(chunk21.GetMaterials());
                this.meshTerrainJob.chunk21lights.CopyFrom(chunk21.GetLights());

                this.meshTerrainJob.chunk02densities.CopyFrom(chunk02.GetDensities());
                this.meshTerrainJob.chunk02materials.CopyFrom(chunk02.GetMaterials());
                this.meshTerrainJob.chunk02lights.CopyFrom(chunk02.GetLights());

                this.meshTerrainJob.chunk12densities.CopyFrom(chunk12.GetDensities());
                this.meshTerrainJob.chunk12materials.CopyFrom(chunk12.GetMaterials());
                this.meshTerrainJob.chunk12lights.CopyFrom(chunk12.GetLights());

                this.meshTerrainJob.chunk22densities.CopyFrom(chunk22.GetDensities());
                this.meshTerrainJob.chunk22materials.CopyFrom(chunk22.GetMaterials());
                this.meshTerrainJob.chunk22lights.CopyFrom(chunk22.GetLights());

                this.meshTerrainJob.chunkPosition = new int2(chunkPosition.x, chunkPosition.y);
                this.meshTerrainJobHandle = this.meshTerrainJob.Schedule();
                this.meshTerrainJobDone = false;
            }
        }
        
        if (!this.meshTerrainJobDone && this.meshTerrainJobHandle.IsCompleted)
        {
            this.meshTerrainJobHandle.Complete();
            this.meshTerrainJobDone = true;

            this.startStopwatch();

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

            this.chunks[chunkPosition].areMeshesDone = true;

            this.stopStopwatch("Create ChunkObject", 1);
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
        // Terrain Job
        this.generateTerrainJob.chunkSize = new int3(16, 256, 16);
        this.generateTerrainJob.densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.generateTerrainJob.heights = new NativeArray<float>(256, Allocator.Persistent);
        this.generateTerrainJob.materials = new NativeArray<byte>(65536, Allocator.Persistent);

        // Light Job
        this.sunLightJob.sunLights = new NativeArray<byte>(65536, Allocator.Persistent);
        this.sunLightJob.lightQueue = new NativeQueue<int3>(Allocator.Persistent);
        this.sunLightJob.densities = new NativeArray<sbyte>(589824, Allocator.Persistent);
        this.sunLightJob.lights = new NativeArray<int>(589824, Allocator.Persistent);
        this.sunLightJob.chunk00densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk10densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk20densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk01densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk11densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk21densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk02densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk12densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.sunLightJob.chunk22densities = new NativeArray<sbyte>(65536, Allocator.Persistent);

        // Light Job
        this.tempSunLightJob.sunLights = new NativeArray<byte>(65536, Allocator.Persistent);
        this.tempSunLightJob.lightQueue = new NativeQueue<int3>(Allocator.Persistent);
        this.tempSunLightJob.densities = new NativeArray<sbyte>(589824, Allocator.Persistent);
        this.tempSunLightJob.lights = new NativeArray<int>(589824, Allocator.Persistent);
        this.tempSunLightJob.chunk00densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk10densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk20densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk01densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk11densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk21densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk02densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk12densities = new NativeArray<sbyte>(65536, Allocator.Persistent);
        this.tempSunLightJob.chunk22densities = new NativeArray<sbyte>(65536, Allocator.Persistent);

        // Mesh Job
        this.meshTerrainJob = new MeshTerrainJob()
        {
            breakPoints = new NativeList<int2>(Allocator.Persistent),

            chunkDensities = new NativeArray<sbyte>(92416, Allocator.Persistent),
            chunkMaterials = new NativeArray<byte>(92416, Allocator.Persistent),
            chunkLights = new NativeArray<byte>(92416, Allocator.Persistent),
            chunkSize = new float3(16, 256, 16),
            chunkSizeFull = new float3(19, 256, 19),
            mcCornerPositions = new NativeArray<float3>(Tables.cornerPositions.Length, Allocator.Persistent),
            mcCellClasses = new NativeArray<byte>(Tables.cellClasses.Length, Allocator.Persistent),
            mcCellGeometryCounts = new NativeArray<int>(Tables.cellGeometryCounts.Length, Allocator.Persistent),
            mcCellIndices = new NativeArray<int>(Tables.cellIndices.Length, Allocator.Persistent),
            mcCellVertexData = new NativeArray<ushort>(Tables.cellVertexData.Length, Allocator.Persistent),

            chunk00densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk00materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk00lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk10densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk10materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk10lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk20densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk20materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk20lights = new NativeArray<byte>(65536, Allocator.Persistent),

            chunk01densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk01materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk01lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk11densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk11materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk11lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk21densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk21materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk21lights = new NativeArray<byte>(65536, Allocator.Persistent),

            chunk02densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk02materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk02lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk12densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk12materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk12lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk22densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk22materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk22lights = new NativeArray<byte>(65536, Allocator.Persistent),

            vertices = new NativeList<Vector3>(Allocator.Persistent),
            normals = new NativeList<Vector3>(Allocator.Persistent),
            indices = new NativeList<int>(Allocator.Persistent),
            lights = new NativeList<Vector2>(Allocator.Persistent),
            mats1234 = new NativeList<Vector4>(Allocator.Persistent),
            mats5678 = new NativeList<Vector4>(Allocator.Persistent),
            weights1234 = new NativeList<Vector4>(Allocator.Persistent),
            weights5678 = new NativeList<Vector4>(Allocator.Persistent),
            cornerPositions = new NativeArray<float3>(8, Allocator.Persistent),
            cornerDensities = new NativeArray<sbyte>(8, Allocator.Persistent),
            cornerMaterials = new NativeArray<byte>(8, Allocator.Persistent),
            cornerNormals = new NativeArray<float3>(8, Allocator.Persistent),
            cornerLights = new NativeArray<float2>(8, Allocator.Persistent),
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
            breakPoints = new NativeList<int2>(Allocator.Persistent),

            chunkDensities = new NativeArray<sbyte>(92416, Allocator.Persistent),
            chunkMaterials = new NativeArray<byte>(92416, Allocator.Persistent),
            chunkLights = new NativeArray<byte>(92416, Allocator.Persistent),

            chunkSize = new float3(16, 256, 16),
            chunkSizeFull = new float3(19, 256, 19),
            mcCornerPositions = new NativeArray<float3>(Tables.cornerPositions.Length, Allocator.Persistent),
            mcCellClasses = new NativeArray<byte>(Tables.cellClasses.Length, Allocator.Persistent),
            mcCellGeometryCounts = new NativeArray<int>(Tables.cellGeometryCounts.Length, Allocator.Persistent),
            mcCellIndices = new NativeArray<int>(Tables.cellIndices.Length, Allocator.Persistent),
            mcCellVertexData = new NativeArray<ushort>(Tables.cellVertexData.Length, Allocator.Persistent),

            chunk00densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk00materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk00lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk10densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk10materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk10lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk20densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk20materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk20lights = new NativeArray<byte>(65536, Allocator.Persistent),

            chunk01densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk01materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk01lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk11densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk11materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk11lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk21densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk21materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk21lights = new NativeArray<byte>(65536, Allocator.Persistent),

            chunk02densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk02materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk02lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk12densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk12materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk12lights = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk22densities = new NativeArray<sbyte>(65536, Allocator.Persistent),
            chunk22materials = new NativeArray<byte>(65536, Allocator.Persistent),
            chunk22lights = new NativeArray<byte>(65536, Allocator.Persistent),

            vertices = new NativeList<Vector3>(Allocator.Persistent),
            normals = new NativeList<Vector3>(Allocator.Persistent),
            indices = new NativeList<int>(Allocator.Persistent),
            lights = new NativeList<Vector2>(Allocator.Persistent),
            mats1234 = new NativeList<Vector4>(Allocator.Persistent),
            mats5678 = new NativeList<Vector4>(Allocator.Persistent),
            weights1234 = new NativeList<Vector4>(Allocator.Persistent),
            weights5678 = new NativeList<Vector4>(Allocator.Persistent),
            cornerPositions = new NativeArray<float3>(8, Allocator.Persistent),
            cornerDensities = new NativeArray<sbyte>(8, Allocator.Persistent),
            cornerMaterials = new NativeArray<byte>(8, Allocator.Persistent),
            cornerNormals = new NativeArray<float3>(8, Allocator.Persistent),
            cornerLights = new NativeArray<float2>(8, Allocator.Persistent),
            cellIndices = new NativeList<int>(Allocator.Persistent),
            mappedIndices = new NativeList<ushort>(Allocator.Persistent)
        };

        this.worldEditMeshJob.mcCornerPositions.CopyFrom(Tables.cornerPositions);
        this.worldEditMeshJob.mcCellClasses.CopyFrom(Tables.cellClasses);
        this.worldEditMeshJob.mcCellGeometryCounts.CopyFrom(Tables.cellGeometryCounts);
        this.worldEditMeshJob.mcCellIndices.CopyFrom(Tables.cellIndices);
        this.worldEditMeshJob.mcCellVertexData.CopyFrom(Tables.cellVertexData);
    }

    private void DisposeJobs()
    {
        if (!this.generateTerrainJobDone) this.generateTerrainJobHandle.Complete();
        if (!this.sunLightJobDone) this.sunLightJobHandle.Complete();
        if (!this.meshTerrainJobDone) this.meshTerrainJobHandle.Complete();
        if (!this.worldEditMeshJobDone) this.worldEditMeshJobHandle.Complete();

        this.generateTerrainJob.densities.Dispose();
        this.generateTerrainJob.heights.Dispose();
        this.generateTerrainJob.materials.Dispose();

        this.sunLightJob.sunLights.Dispose();
        this.sunLightJob.lightQueue.Dispose();
        this.sunLightJob.densities.Dispose();
        this.sunLightJob.lights.Dispose();
        this.sunLightJob.chunk00densities.Dispose();
        this.sunLightJob.chunk10densities.Dispose();
        this.sunLightJob.chunk20densities.Dispose();
        this.sunLightJob.chunk01densities.Dispose();
        this.sunLightJob.chunk11densities.Dispose();
        this.sunLightJob.chunk21densities.Dispose();
        this.sunLightJob.chunk02densities.Dispose();
        this.sunLightJob.chunk12densities.Dispose();
        this.sunLightJob.chunk22densities.Dispose();

        this.tempSunLightJob.sunLights.Dispose();
        this.tempSunLightJob.lightQueue.Dispose();
        this.tempSunLightJob.densities.Dispose();
        this.tempSunLightJob.lights.Dispose();
        this.tempSunLightJob.chunk00densities.Dispose();
        this.tempSunLightJob.chunk10densities.Dispose();
        this.tempSunLightJob.chunk20densities.Dispose();
        this.tempSunLightJob.chunk01densities.Dispose();
        this.tempSunLightJob.chunk11densities.Dispose();
        this.tempSunLightJob.chunk21densities.Dispose();
        this.tempSunLightJob.chunk02densities.Dispose();
        this.tempSunLightJob.chunk12densities.Dispose();
        this.tempSunLightJob.chunk22densities.Dispose();

        this.meshTerrainJob.breakPoints.Dispose();
        this.meshTerrainJob.chunkDensities.Dispose();
        this.meshTerrainJob.chunkMaterials.Dispose();
        this.meshTerrainJob.chunkLights.Dispose();
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
        this.meshTerrainJob.cornerDensities.Dispose();
        this.meshTerrainJob.cornerMaterials.Dispose();
        this.meshTerrainJob.cornerNormals.Dispose();
        this.meshTerrainJob.cornerLights.Dispose();
        this.meshTerrainJob.cellIndices.Dispose();
        this.meshTerrainJob.mappedIndices.Dispose();
        this.meshTerrainJob.chunk00densities.Dispose();
        this.meshTerrainJob.chunk00materials.Dispose();
        this.meshTerrainJob.chunk00lights.Dispose();
        this.meshTerrainJob.chunk10densities.Dispose();
        this.meshTerrainJob.chunk10materials.Dispose();
        this.meshTerrainJob.chunk10lights.Dispose();
        this.meshTerrainJob.chunk20densities.Dispose();
        this.meshTerrainJob.chunk20materials.Dispose();
        this.meshTerrainJob.chunk20lights.Dispose();
        this.meshTerrainJob.chunk01densities.Dispose();
        this.meshTerrainJob.chunk01materials.Dispose();
        this.meshTerrainJob.chunk01lights.Dispose();
        this.meshTerrainJob.chunk11densities.Dispose();
        this.meshTerrainJob.chunk11materials.Dispose();
        this.meshTerrainJob.chunk11lights.Dispose();
        this.meshTerrainJob.chunk21densities.Dispose();
        this.meshTerrainJob.chunk21materials.Dispose();
        this.meshTerrainJob.chunk21lights.Dispose();
        this.meshTerrainJob.chunk02densities.Dispose();
        this.meshTerrainJob.chunk02materials.Dispose();
        this.meshTerrainJob.chunk02lights.Dispose();
        this.meshTerrainJob.chunk12densities.Dispose();
        this.meshTerrainJob.chunk12materials.Dispose();
        this.meshTerrainJob.chunk12lights.Dispose();
        this.meshTerrainJob.chunk22densities.Dispose();
        this.meshTerrainJob.chunk22materials.Dispose();
        this.meshTerrainJob.chunk22lights.Dispose();

        this.worldEditMeshJob.breakPoints.Dispose();
        this.worldEditMeshJob.chunkDensities.Dispose();
        this.worldEditMeshJob.chunkMaterials.Dispose();
        this.worldEditMeshJob.chunkLights.Dispose();
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
        this.worldEditMeshJob.cornerDensities.Dispose();
        this.worldEditMeshJob.cornerMaterials.Dispose();
        this.worldEditMeshJob.cornerNormals.Dispose();
        this.worldEditMeshJob.cornerLights.Dispose();
        this.worldEditMeshJob.cellIndices.Dispose();
        this.worldEditMeshJob.mappedIndices.Dispose();
        this.worldEditMeshJob.chunk00densities.Dispose();
        this.worldEditMeshJob.chunk00materials.Dispose();
        this.worldEditMeshJob.chunk00lights.Dispose();
        this.worldEditMeshJob.chunk10densities.Dispose();
        this.worldEditMeshJob.chunk10materials.Dispose();
        this.worldEditMeshJob.chunk10lights.Dispose();
        this.worldEditMeshJob.chunk20densities.Dispose();
        this.worldEditMeshJob.chunk20materials.Dispose();
        this.worldEditMeshJob.chunk20lights.Dispose();
        this.worldEditMeshJob.chunk01densities.Dispose();
        this.worldEditMeshJob.chunk01materials.Dispose();
        this.worldEditMeshJob.chunk01lights.Dispose();
        this.worldEditMeshJob.chunk11densities.Dispose();
        this.worldEditMeshJob.chunk11materials.Dispose();
        this.worldEditMeshJob.chunk11lights.Dispose();
        this.worldEditMeshJob.chunk21densities.Dispose();
        this.worldEditMeshJob.chunk21materials.Dispose();
        this.worldEditMeshJob.chunk21lights.Dispose();
        this.worldEditMeshJob.chunk02densities.Dispose();
        this.worldEditMeshJob.chunk02materials.Dispose();
        this.worldEditMeshJob.chunk02lights.Dispose();
        this.worldEditMeshJob.chunk12densities.Dispose();
        this.worldEditMeshJob.chunk12materials.Dispose();
        this.worldEditMeshJob.chunk12lights.Dispose();
        this.worldEditMeshJob.chunk22densities.Dispose();
        this.worldEditMeshJob.chunk22materials.Dispose();
        this.worldEditMeshJob.chunk22lights.Dispose();
    }

    //
    // World Editor (Draw, Fill, Erase, Replace)
    //

    private bool IsWorldEditBlocked()
    {
        return (this.worldEditMeshQueue.Count > 0);
    }

    public bool WorldEditDraw(Vector3 worldPosition, sbyte density, byte material)
    {
        if (density >= 0)
            return false;

        if (this.IsWorldEditBlocked())
            return false;

        Vector3Int editPosition = this.WorldToEditPosition(worldPosition);
        Vector2Int chunkPosition = new Vector2Int(editPosition.x, editPosition.y);
        int arrayPosition = editPosition.z;

        if (!this.chunkObjects.ContainsKey(chunkPosition) || !this.chunks.ContainsKey(chunkPosition))
            return false;

        this.chunks[chunkPosition].SetDensity(arrayPosition, density);
        this.chunks[chunkPosition].SetMaterial(arrayPosition, material);

        sbyte densityAbove = this.chunks[chunkPosition].GetDensity(arrayPosition + 256);
        if (densityAbove >= 0)
        {
            sbyte newDensityAbove = (sbyte)Mathf.Clamp((int)density + 128, 0, 127);
            this.chunks[chunkPosition].SetDensity(arrayPosition + 256, newDensityAbove);
        }

        sbyte densityBelow = this.chunks[chunkPosition].GetDensity(arrayPosition - 256);
        if (densityBelow <= -1)
        {
            this.chunks[chunkPosition].SetDensity(arrayPosition - 256, -127);
        }

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int offset = new Vector2Int(x, z);
                this.worldEditMeshQueue.Enqueue(chunkPosition + offset);
            }
        }

        return true;
    }

    public bool WorldEditErase(Vector3 worldPosition)
    {
        if (this.IsWorldEditBlocked())
            return false;

        Vector3Int worldEditPosition = this.WorldToEditPosition(worldPosition);
        Vector2Int chunkPosition = new Vector2Int(worldEditPosition.x, worldEditPosition.y);
        int arrayPosition = worldEditPosition.z;

        if (!this.chunks.ContainsKey(chunkPosition))
            return false;

        Chunk chunk = this.chunks[chunkPosition];

        sbyte densityBelow = chunk.GetDensity(arrayPosition - 256);

        if (densityBelow >= 0)
        {
            chunk.SetDensity(arrayPosition, 127);
        }
        else
        {
            chunk.SetDensity(arrayPosition, (sbyte)((int)densityBelow + 128));
        }

        chunk.SetMaterial(arrayPosition, 255);

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int offset = new Vector2Int(x, z);
                this.worldEditMeshQueue.Enqueue(chunkPosition + offset);
            }
        }

        return true;
    }

    public bool WorldEditHeighten(Vector3 worldPosition, int strength = 1)
    {
        if (this.IsWorldEditBlocked())
            return false;

        if (strength < 1 || strength > 127)
            return false;

        Vector3Int editPosition = this.WorldToEditPosition(worldPosition);
        Vector2Int chunkPosition = new Vector2Int(editPosition.x, editPosition.y);
        int arrayPosition = editPosition.z;

        if (!this.chunks.ContainsKey(chunkPosition))
            return false;

        Chunk chunk = this.chunks[chunkPosition];
        int density = (int)chunk.GetDensity(arrayPosition);

        if (density >= 0 || density == -128)
            return false;

        int densityAbove = (int)chunk.GetDensity(arrayPosition + 256);
        int newDensity = density;

        newDensity -= strength;
        newDensity = Mathf.Clamp(newDensity, -128, -1);

        chunk.SetDensity(arrayPosition, (sbyte)newDensity);

        if (densityAbove >= 0)
        {
            chunk.SetDensity(arrayPosition + 256, (sbyte)(newDensity + 128));
        }

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int offset = new Vector2Int(x, z);
                this.worldEditMeshQueue.Enqueue(chunkPosition + offset);
            }
        }

        return true;
    }

    //
    // Player Information
    //

    private void UpdatePlayerChunkPosition()
    {
        this.playerChunkPosition = this.WorldToChunkPosition(this.playerWorldPosition.position);
        this.didPlayerEnterChunk = (this.playerChunkPosition != this.playerChunkPositionLast);

        if (this.didPlayerEnterChunk)
        {
            bool preventEntering = false;
            if (this.playerChunkPositionLast.x != 2147483647)
            {
                if (!this.chunkObjects.ContainsKey(this.playerChunkPosition))
                {
                    preventEntering = true;
                }
                else
                {
                    if (!this.chunkObjects[this.playerChunkPosition].IsActive())
                    {
                        this.chunkObjects[this.playerChunkPosition].Activate();
                    }
                }
            }

            if (preventEntering)
            {
                this.playerChunkPosition = this.playerChunkPositionLast;
                this.playerGameObject.GetComponent<PlayerController>().Stop();
                this.playerGameObject.transform.position = this.playerWorldPositionLast;
                this.didPlayerEnterChunk = false;
            }
        }
    }

    private void UpdatePlayerChunkPositionLast()
    {
        this.playerChunkPositionLast = this.playerChunkPosition;
        this.playerWorldPositionLast = this.playerWorldPosition.position;
    }

    //
    // Chunk Helpers
    //

    private bool IsInChunkDistance(Vector2Int chunkPosition, int overflow = 0)
    {
        if (Mathf.Abs(chunkPosition.x - this.playerChunkPosition.x) <= this.chunkDistance + overflow && Mathf.Abs(chunkPosition.y - this.playerChunkPosition.y) <= this.chunkDistance + overflow)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool IsInChunkDistanceLast(Vector2Int chunkPosition, int overflow = 0)
    {
        if (Mathf.Abs(chunkPosition.x - this.playerChunkPositionLast.x) <= this.chunkDistance + overflow && Mathf.Abs(chunkPosition.y - this.playerChunkPositionLast.y) <= this.chunkDistance + overflow)
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

    private Vector3Int WorldToEditPosition(Vector3 worldPosition)
    {
        // Voxel Position Relative To Chunk
        Vector3Int relativePosition = new Vector3Int(0, 0, 0);
        relativePosition.x = Mathf.RoundToInt(worldPosition.x) % 16;
        relativePosition.y = Mathf.RoundToInt(worldPosition.y + 1.0f);
        relativePosition.z = Mathf.RoundToInt(worldPosition.z) % 16;
        relativePosition.y = Mathf.Clamp(relativePosition.y, 2, 253);

        if (relativePosition.x < 0)
        {
            relativePosition.x = relativePosition.x + 15;
        }
        if (relativePosition.z < 0)
        {
            relativePosition.z = relativePosition.z + 15;
        }

        // Chunk Position
        Vector2Int chunkPosition = new Vector2Int(0, 0);

        chunkPosition.x = Mathf.FloorToInt(worldPosition.x / 16.0f);

        if (Mathf.FloorToInt(worldPosition.x) % 16 == 15 && relativePosition.x == 0)
        {
            chunkPosition.x += 1;
        }
        else if (Mathf.FloorToInt(worldPosition.x) % 16 == -1 && relativePosition.x == 0)
        {
            chunkPosition.x += 1;
        }

        chunkPosition.y = Mathf.FloorToInt(worldPosition.z / 16.0f);

        if (Mathf.FloorToInt(worldPosition.z) % 16 == 15 && relativePosition.z == 0)
        {
            chunkPosition.y += 1;
        }
        else if (Mathf.FloorToInt(worldPosition.z) % 16 == -1 && relativePosition.z == 0)
        {
            chunkPosition.y += 1;
        }

        // Array Position
        int arrayPosition = 0;
        arrayPosition = relativePosition.x + relativePosition.z * 16 + relativePosition.y * 256;

        Vector3Int editPosition = new Vector3Int(0, 0, 0);
        editPosition.x = chunkPosition.x;
        editPosition.y = chunkPosition.y;
        editPosition.z = arrayPosition;

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

    public float GetLightValue(Vector3 worldPosition)
    {
        float lightValue = 1.0f;

        Vector3Int editPositon = this.WorldToEditPosition(worldPosition);
        Vector2Int chunkPosition = new Vector2Int(editPositon.x, editPositon.y);
        int arrayPosition = editPositon.z;

        if (this.chunks.ContainsKey(chunkPosition))
        {
            lightValue = (float)this.chunks[chunkPosition].GetSunLight(arrayPosition) / 15.0f;
        }

        return lightValue;
    }
}