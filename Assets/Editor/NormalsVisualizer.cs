using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeshFilter))]
public class NormalsVisualizer : Editor
{

    private Mesh mesh;
    private MeshFilter meshFilter;
    private Vector3[] vertices;
    private Vector3[] normals;
    private int[] triangles;

    void OnEnable()
    {
        this.meshFilter = target as MeshFilter;
        this.mesh = this.meshFilter.sharedMesh;
        this.vertices = this.mesh.vertices;
        this.normals = this.mesh.normals;
        this.triangles = this.mesh.triangles;
    }

    void OnSceneGUI()
    {
        int max = 1000;
        int length = this.vertices.Length;
        for (int i = 0; i < length; i++)
        {
            Handles.matrix = this.meshFilter.transform.localToWorldMatrix;
            Handles.color = Color.yellow;
            Handles.DrawLine(
                this.vertices[length - i - 1],
                this.vertices[length - i - 1] + this.normals[length - i - 1]
            );

            if (i == max)
                break;
        }
    }
}