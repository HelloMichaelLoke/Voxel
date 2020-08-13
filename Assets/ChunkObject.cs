using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

public struct ChunkObject
{
    public GameObject rendererObject;
    public GameObject[] colliderObjects;
    public Mesh rendererMesh;
    public Mesh[] colliderMeshes;

    public ChunkObject(Vector2Int chunkPosition, Material material)
    {
        this.rendererObject = new GameObject();
        this.rendererObject.name = "Chunk Renderer (" + chunkPosition.x.ToString() + ", " + chunkPosition.y.ToString() + ")";
        this.rendererObject.transform.position = new Vector3(chunkPosition.x * 16.0f, 0.0f, chunkPosition.y * 16.0f);
        this.rendererObject.AddComponent<MeshFilter>();
        this.rendererObject.AddComponent<MeshRenderer>();
        this.rendererObject.GetComponent<MeshRenderer>().material = material;
        this.rendererMesh = new Mesh();
        this.rendererMesh.name = "Chunk Renderer Mesh (" + chunkPosition.x.ToString() + ", " + chunkPosition.y.ToString() + ")";
        this.rendererMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        this.colliderObjects = new GameObject[16];
        this.colliderMeshes = new Mesh[16];
        for (int i = 0; i < 16; i++)
        {
            this.colliderObjects[i] = new GameObject();
            this.colliderObjects[i].name = "Chunk Collider (" + chunkPosition.x.ToString() + ", " + i + ", " + chunkPosition.y.ToString() + ")";
            this.colliderObjects[i].transform.position = new Vector3(chunkPosition.x * 16.0f, i * 16.0f, chunkPosition.y * 16.0f);
            this.colliderObjects[i].AddComponent<MeshCollider>();
            this.colliderObjects[i].SetActive(false);
            this.colliderMeshes[i] = new Mesh();
            this.colliderMeshes[i].name = "Chunk Collider Mesh (" + chunkPosition.x.ToString() + ", " + i + ", " + chunkPosition.y.ToString() + ")";
        }
    }

    public void SetRenderer(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<int> indices, NativeArray<Vector4> weights1234, NativeArray<Vector4> weights5678, NativeArray<Vector4> mats1234, NativeArray<Vector4> mats5678, NativeArray<Vector2> lights)
    {
        this.rendererMesh.SetVertices(vertices);
        this.rendererMesh.SetNormals(normals);
        this.rendererMesh.SetIndices(indices, MeshTopology.Triangles, 0);
        this.rendererMesh.SetUVs(0, weights1234);
        this.rendererMesh.SetUVs(1, weights5678);
        this.rendererMesh.SetUVs(2, mats1234);
        this.rendererMesh.SetUVs(3, mats5678);
        this.rendererMesh.SetUVs(4, lights);
        this.rendererMesh.RecalculateBounds();
    }

    public void SetCollider(int index, Vector3[] vertices, int[] indices)
    {
        this.colliderMeshes[index].Clear();
        this.colliderMeshes[index].SetVertices(vertices);
        this.colliderMeshes[index].SetIndices(indices, MeshTopology.Triangles, 0);
        this.colliderMeshes[index].Optimize();
        this.colliderObjects[index].GetComponent<MeshCollider>().sharedMesh = this.colliderMeshes[index];
        this.colliderObjects[index].SetActive(true);
    }

    public void Destroy()
    {
        Object.Destroy(this.rendererObject);
        Object.Destroy(this.rendererMesh);

        for (int i = 0; i < 16; i++)
        {
            Object.Destroy(this.colliderObjects[i]);
            Object.Destroy(this.colliderMeshes[i]);
        }
    }
};