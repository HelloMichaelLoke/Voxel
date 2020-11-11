using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public struct BakePhysicsMeshData
{
    public Vector2Int chunkPosition;
    public int colliderIndex;
    public int meshInstanceID;

    public BakePhysicsMeshData(Vector2Int chunkPosition, int colliderIndex, int meshInstanceID)
    {
        this.chunkPosition = chunkPosition;
        this.colliderIndex = colliderIndex;
        this.meshInstanceID = meshInstanceID;
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct BakePhysicsMeshJob : IJob
{
    /// <summary>
    /// int4(chunkPosition.x, chunkPosition.z, colliderPosition.y, meshInstanceID)
    /// </summary>
    public NativeList<BakePhysicsMeshData> bakeMeshData;

    public void Execute()
    {
        for (int i = 0; i < this.bakeMeshData.Length; i++)
        {
            Physics.BakeMesh(bakeMeshData[i].meshInstanceID, false);
        }
    }
}