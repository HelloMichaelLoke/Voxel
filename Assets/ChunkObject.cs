using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

public class ChunkObject
{
    public GameObject wrapperObject;
    public GameObject rendererObject;
    public GameObject[] colliderObjects;
    public Mesh rendererMesh;
    public Mesh[] colliderMeshes;
    private bool[] hasCollider = new bool[16];

    private bool isActive;

    public ChunkObject(Vector2Int chunkPosition, Material material)
    {
        this.isActive = true;
        this.wrapperObject = new GameObject();
        this.wrapperObject.transform.position = new Vector3(chunkPosition.x * 16.0f, 0.0f, chunkPosition.y * 16.0f);
        this.rendererObject = new GameObject();
        this.rendererObject.transform.SetParent(this.wrapperObject.transform);
        this.rendererObject.transform.localPosition = Vector3.zero;
        this.rendererObject.AddComponent<MeshFilter>();
        this.rendererObject.AddComponent<MeshRenderer>();
        this.rendererObject.GetComponent<MeshRenderer>().material = material;
        //this.rendererObject.GetComponent<MeshRenderer>().allowOcclusionWhenDynamic = false;
        this.rendererMesh = new Mesh();
        this.rendererMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        this.rendererObject.GetComponent<MeshFilter>().sharedMesh = this.rendererMesh;

        this.colliderObjects = new GameObject[16];
        this.colliderMeshes = new Mesh[16];
        for (int i = 0; i < 16; i++)
        {
            this.hasCollider[i] = false;
        }
    }

    public bool IsActive()
    {
        return this.isActive;
    }

    public void Activate()
    {
        this.isActive = true;
        this.wrapperObject.SetActive(true);
    }

    public void Deactivate()
    {
        this.isActive = false;
        this.wrapperObject.SetActive(false);
    }

    public void SetRenderer(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<int> indices, NativeArray<Vector4> weights1234, NativeArray<Vector4> weights5678, NativeArray<Vector4> mats1234, NativeArray<Vector4> mats5678, NativeArray<Vector2> lights)
    {
        this.rendererMesh.Clear();
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

    /// <summary>
    /// Sets the collider mesh based on the Y index (0-15) but DOES NOT apply it to the MeshCollider component yet,
    /// because it needs threaded baking first.
    /// </summary>
    /// <param name="index">Y array position of the collider mesh.</param>
    /// <param name="vertices">Array of mesh vertices.</param>
    /// <param name="indices">Array of mesh indices.</param>
    public void SetColliderMesh(int index, Vector3[] vertices, int[] indices)
    {
        //System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        //stopwatch.Start();

        if (!this.hasCollider[index])
        {
            this.hasCollider[index] = true;

            this.colliderObjects[index] = new GameObject();
            this.colliderObjects[index].transform.SetParent(this.wrapperObject.transform);
            this.colliderObjects[index].tag = "Terrain";
            this.colliderObjects[index].layer = 8;
            this.colliderObjects[index].transform.localPosition = new Vector3(0.0f, index * 16.0f, 0.0f);
            this.colliderObjects[index].AddComponent<MeshCollider>();
            this.colliderMeshes[index] = new Mesh();
        }

        this.colliderMeshes[index].Clear();
        this.colliderMeshes[index].SetVertices(vertices);
        this.colliderMeshes[index].SetIndices(indices, MeshTopology.Triangles, 0);

        //stopwatch.Stop();
        //Debug.Log("setting shared mesh: " + stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Sets the MeshCollider.sharedMesh to the ALREADY Physics.BakeMesh()'d collider mesh.
    /// </summary>
    /// <param name="index">Y array position (0-15) of the collider inside the chunk.</param>
    public void SetCollider(int index)
    {
        this.colliderObjects[index].GetComponent<MeshCollider>().sharedMesh = this.colliderMeshes[index];
        this.colliderObjects[index].SetActive(true);
    }

    public int GetMeshInstanceID(int index)
    {
        return this.colliderMeshes[index].GetInstanceID();
    }

    public void Destroy()
    {
        UnityEngine.Object.Destroy(this.wrapperObject);

        UnityEngine.Object.Destroy(this.rendererMesh);
        for (int i = 0; i < 16; i++)
        {
            UnityEngine.Object.Destroy(this.colliderMeshes[i]);
        }
    }
};