using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct GenerateMeshJob : IJob
{
    // Temporary Data
    public NativeArray<Voxel> voxelsMerged;
    public NativeArray<byte> lightsMerged;

    public NativeArray<float3> voxelGradients;
    public NativeArray<float2> voxelLights;

    // Chunk Data
    public int2 chunkPosition;

    // Helpers
    private bool isEmpty;
    private bool isFull;

    // Terrain Data
    public NativeArray<Voxel>   voxels00;
    public NativeArray<byte>    lights00;
    public NativeArray<Voxel>   voxels10;
    public NativeArray<byte>    lights10;
    public NativeArray<Voxel>   voxels20;
    public NativeArray<byte>    lights20;
    public NativeArray<Voxel>   voxels01;
    public NativeArray<byte>    lights01;
    public NativeArray<Voxel>   voxels11;
    public NativeArray<byte>    lights11;
    public NativeArray<Voxel>   voxels21;
    public NativeArray<byte>    lights21;
    public NativeArray<Voxel>   voxels02;
    public NativeArray<byte>    lights02;
    public NativeArray<Voxel>   voxels12;
    public NativeArray<byte>    lights12;
    public NativeArray<Voxel>   voxels22;
    public NativeArray<byte>    lights22;

    // Mesh Data
    public NativeList<Vector3> vertices;
    public NativeList<Vector3> normals;
    public NativeList<int> indices;
    public NativeList<Vector2> lights;
    public NativeList<Vector4> mats1234;
    public NativeList<Vector4> mats5678;
    public NativeList<Vector4> weights1234;
    public NativeList<Vector4> weights5678;

    // Marching Cubes Data
    public NativeArray<float3> mcCornerPositions;
    public NativeArray<byte> mcCellClasses;
    public NativeArray<int> mcCellGeometryCounts;
    public NativeArray<int> mcCellIndices;
    public NativeArray<ushort> mcCellVertexData;

    // Temporary Variables
    public NativeArray<float3> cornerPositions;
    public NativeArray<Voxel> cornerVoxels;
    public NativeArray<float2> cornerLights;
    public NativeArray<float3> cornerGradients;
    public NativeList<int> cellIndices;
    public NativeList<ushort> mappedIndices;

    // Physics Mesh Breakpoints (vertex array position, index array position)
    public NativeList<int2> breakPoints;

    public void Execute()
    {
        this.MergeChunks();
        this.PrepareGradientsAndLights();
        this.MeshCells();
    }

    private void MergeChunks()
    {
        int i = 0;
        int index;

        for (int y = 0; y < 256; y++)
        {
            for (int z = 0; z < 19; z++)
            {
                for (int x = 0; x < 19; x++)
                {
                    Voxel voxel = new Voxel();
                    byte light = 0;

                    if (x == 0 && z == 0)
                    {
                        index = 255 + y * 256;
                        voxel = this.voxels00[index];
                        light = this.lights00[index];
                    }
                    else if (x == 0 && z >= 1 && z <= 16)
                    {
                        index = 15 + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels01[index];
                        light = this.lights01[index];
                    }
                    else if (x == 0 && z >= 17)
                    {
                        index = 15 + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels02[index];
                        light = this.lights02[index];
                    }
                    else if (x >= 1 && x <= 16 && z == 0)
                    {
                        index = 240 + (x - 1) + y * 256;
                        voxel = this.voxels10[index];
                        light = this.lights10[index];
                    }
                    else if (x >= 1 && x <= 16 && z >= 1 && z <= 16)
                    {
                        index = 0 + (x - 1) + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels11[index];
                        light = this.lights11[index];
                    }
                    else if (x >= 1 && x <= 16 && z >= 17)
                    {
                        index = 0 + (x - 1) + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels12[index];
                        light = this.lights12[index];
                    }
                    else if (x >= 17 && z == 0)
                    {
                        index = 240 + (x - 17) + y * 256;
                        voxel = this.voxels20[index];
                        light = this.lights20[index];
                    }
                    else if (x >= 17 && z >= 1 && z <= 16)
                    {
                        index = 0 + (x - 17) + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels21[index];
                        light = this.lights21[index];
                    }
                    else if (x >= 17 && z >= 17)
                    {
                        index = 0 + (x - 17) + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels22[index];
                        light = this.lights22[index];
                    }

                    this.voxelsMerged[i] = voxel;
                    this.lightsMerged[i] = light;

                    i++;
                }
            }
        }
    }

    private void PrepareGradientsAndLights()
    {
        int i = 0;
        for (int y = 1; y < 255; y++)
        {
            for (int z = 1; z < 18; z++)
            {
                for (int x = 1; x < 18; x++)
                {
                    int index = x + y * 361 + z * 19;
                    this.voxelLights[i] = this.GetLight(index);
                    this.voxelGradients[i] = this.GetGradient(index);
                    i++;
                }
            }
        }
    }

    private void MeshCells()
    {
        for (int y = 1; y < 254; y++)
        {
            for (int z = 1; z < 17; z++)
            {
                for (int x = 1; x < 17; x++)
                {
                    this.MeshCell(x, y, z);
                }
            }
        }
    }

    private void MeshCell(int x, int y, int z)
    {
        if (x == 1 && z == 1 && y % 16 == 1)
        {
            this.breakPoints.Add(new int2(this.vertices.Length, this.indices.Length));
        }

        int index = x + y * 361 + z * 19;

        this.cornerVoxels[0] = this.voxelsMerged[index];
        this.cornerVoxels[1] = this.voxelsMerged[index + 1];
        this.cornerVoxels[2] = this.voxelsMerged[index + 19];
        this.cornerVoxels[3] = this.voxelsMerged[index + 20];
        this.cornerVoxels[4] = this.voxelsMerged[index + 361];
        this.cornerVoxels[5] = this.voxelsMerged[index + 362];
        this.cornerVoxels[6] = this.voxelsMerged[index + 380];
        this.cornerVoxels[7] = this.voxelsMerged[index + 381];

        this.isEmpty = true;
        this.isFull = true;

        if (this.cornerVoxels[0].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[1].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[2].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[3].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[4].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[5].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[6].IsSolid()) this.isEmpty = false; else this.isFull = false;
        if (this.cornerVoxels[7].IsSolid()) this.isEmpty = false; else this.isFull = false;

        if (isEmpty || isFull)
        {
            return;
        }

        int index0 = (x - 1) + (y - 1) * 289 + (z - 1) * 17;
        int index1 = index0 + 1;
        int index2 = index0 + 17;
        int index3 = index0 + 18;
        int index4 = index0 + 289;
        int index5 = index0 + 290;
        int index6 = index0 + 306;
        int index7 = index0 + 307;

        this.cornerLights[0] = this.voxelLights[index0];
        this.cornerLights[1] = this.voxelLights[index1];
        this.cornerLights[2] = this.voxelLights[index2];
        this.cornerLights[3] = this.voxelLights[index3];
        this.cornerLights[4] = this.voxelLights[index4];
        this.cornerLights[5] = this.voxelLights[index5];
        this.cornerLights[6] = this.voxelLights[index6];
        this.cornerLights[7] = this.voxelLights[index7];

        this.cornerGradients[0] = this.voxelGradients[index0];
        this.cornerGradients[1] = this.voxelGradients[index1];
        this.cornerGradients[2] = this.voxelGradients[index2];
        this.cornerGradients[3] = this.voxelGradients[index3];
        this.cornerGradients[4] = this.voxelGradients[index4];
        this.cornerGradients[5] = this.voxelGradients[index5];
        this.cornerGradients[6] = this.voxelGradients[index6];
        this.cornerGradients[7] = this.voxelGradients[index7];

        this.cornerPositions[0] = new float3(x, y, z);
        this.cornerPositions[1] = new float3(x + 1.0f, y, z);
        this.cornerPositions[2] = new float3(x, y, z + 1.0f);
        this.cornerPositions[3] = new float3(x + 1.0f, y, z + 1.0f);
        this.cornerPositions[4] = new float3(x, y + 1.0f, z);
        this.cornerPositions[5] = new float3(x + 1.0f, y + 1.0f, z);
        this.cornerPositions[6] = new float3(x, y + 1.0f, z + 1.0f);
        this.cornerPositions[7] = new float3(x + 1.0f, y + 1.0f, z + 1.0f);

        float matId1 = (float)this.cornerVoxels[0].GetMaterial();
        float matId2 = (float)this.cornerVoxels[1].GetMaterial();
        float matId3 = (float)this.cornerVoxels[2].GetMaterial();
        float matId4 = (float)this.cornerVoxels[3].GetMaterial();
        float matId5 = (float)this.cornerVoxels[4].GetMaterial();
        float matId6 = (float)this.cornerVoxels[5].GetMaterial();
        float matId7 = (float)this.cornerVoxels[6].GetMaterial();
        float matId8 = (float)this.cornerVoxels[7].GetMaterial();
        float4 matIds1234 = new float4(matId1 - 1.0f, matId2 - 1.0f, matId3 - 1.0f, matId4 - 1.0f);
        float4 matIds5678 = new float4(matId5 - 1.0f, matId6 - 1.0f, matId7 - 1.0f, matId8 - 1.0f);

        // Determine the marching cubes case
        byte caseIndex = 0;
        if (this.cornerVoxels[0].IsSolid()) caseIndex |= 0b_0000_0001;
        if (this.cornerVoxels[1].IsSolid()) caseIndex |= 0b_0000_0010;
        if (this.cornerVoxels[2].IsSolid()) caseIndex |= 0b_0000_0100;
        if (this.cornerVoxels[3].IsSolid()) caseIndex |= 0b_0000_1000;
        if (this.cornerVoxels[4].IsSolid()) caseIndex |= 0b_0001_0000;
        if (this.cornerVoxels[5].IsSolid()) caseIndex |= 0b_0010_0000;
        if (this.cornerVoxels[6].IsSolid()) caseIndex |= 0b_0100_0000;
        if (this.cornerVoxels[7].IsSolid()) caseIndex |= 0b_1000_0000;

        // Set vertex count and triangle count
        byte cellClass = mcCellClasses[caseIndex];
        int geometryCounts = mcCellGeometryCounts[cellClass];
        int vertexCount = geometryCounts >> 4;
        int triangleCount = geometryCounts & 0x0F;

        this.cellIndices.Clear();
        int cellIndicesArrayPosition = cellClass * 15;
        int countIndices = 0;
        while (this.mcCellIndices[cellIndicesArrayPosition] != -1)
        {
            this.cellIndices.Add(this.mcCellIndices[cellIndicesArrayPosition]);
            cellIndicesArrayPosition++;
            countIndices++;

            if (countIndices == 15)
                break;
        }

        this.mappedIndices.Clear();
        float4 weights1234 = float4.zero;
        float4 weights5678 = float4.zero;
        float materialId;
        float3 vertex;
        Vector3 normal;
        ushort edgeCode;
        ushort cornerIndex0;
        ushort cornerIndex1;
        float density0;
        float density1;
        float distance;
        float weight0;
        float weight1;
        float sunLight;
        float sourceLight;
        bool isMaterialSet;

        for (int i = 0; i < vertexCount; i++)
        {
            edgeCode = this.mcCellVertexData[caseIndex * 12 + i];
            cornerIndex0 = (byte)((edgeCode >> 4) & 0x0F);
            cornerIndex1 = (byte)((edgeCode) & 0x0F);

            // Weights
            density0 = (float)this.cornerVoxels[cornerIndex0].GetDensity();
            density1 = (float)this.cornerVoxels[cornerIndex1].GetDensity();
            if (density0 >= 0.0f) density0 += 1.0f;
            if (density1 >= 0.0f) density1 += 1.0f;
            distance = math.abs(density1 - density0);
            density0 /= distance;
            density1 /= distance;

            weight0 = math.abs(density1);
            weight1 = 1.0f - weight0;

            // Vertex
            vertex = weight0 * this.cornerPositions[cornerIndex0] + weight1 * this.cornerPositions[cornerIndex1];

            // Normal
            normal = weight0 * this.cornerGradients[cornerIndex0] + weight1 * this.cornerGradients[cornerIndex1];
            if (normal != Vector3.zero) normal = math.normalize(normal);

            // Material
            if (this.cornerVoxels[cornerIndex0].IsSolid())
            {
                materialId = (float)this.cornerVoxels[cornerIndex0].GetMaterial();
            }
            else
            {
                materialId = (float)this.cornerVoxels[cornerIndex1].GetMaterial();
            }

            // Lights
            sunLight = weight0 * this.cornerLights[cornerIndex0].x + weight1 * this.cornerLights[cornerIndex1].x;
            sourceLight = weight0 * this.cornerLights[cornerIndex0].y + weight1 * this.cornerLights[cornerIndex1].y;

            this.lights.Add(new float2(sunLight / 15.0f, sourceLight / 15.0f));
            this.vertices.Add(vertex - new float3(1.0f, 0.0f, 1.0f));
            this.normals.Add(normal);
            this.mats1234.Add(matIds1234);
            this.mats5678.Add(matIds5678);

            isMaterialSet = false;
            if (matId1 == materialId) { weights1234.x = 1.0f; isMaterialSet = true; } else weights1234.x = 0.0f;
            if (matId2 == materialId && !isMaterialSet) { weights1234.y = 1.0f; isMaterialSet = true; } else weights1234.y = 0.0f;
            if (matId3 == materialId && !isMaterialSet) { weights1234.z = 1.0f; isMaterialSet = true; } else weights1234.z = 0.0f;
            if (matId4 == materialId && !isMaterialSet) { weights1234.w = 1.0f; isMaterialSet = true; } else weights1234.w = 0.0f;
            if (matId5 == materialId && !isMaterialSet) { weights5678.x = 1.0f; isMaterialSet = true; } else weights5678.x = 0.0f;
            if (matId6 == materialId && !isMaterialSet) { weights5678.y = 1.0f; isMaterialSet = true; } else weights5678.y = 0.0f;
            if (matId7 == materialId && !isMaterialSet) { weights5678.z = 1.0f; isMaterialSet = true; } else weights5678.z = 0.0f;
            if (matId8 == materialId && !isMaterialSet) { weights5678.w = 1.0f; isMaterialSet = true; } else weights5678.w = 0.0f;

            this.weights1234.Add(weights1234);
            this.weights5678.Add(weights5678);

            mappedIndices.Add((ushort)(this.vertices.Length - 1));
        }

        for (int i = 0; i < triangleCount; i++)
        {
            int tm = i * 3;
            this.indices.Add(mappedIndices[cellIndices[tm + 2]]);
            this.indices.Add(mappedIndices[cellIndices[tm + 1]]);
            this.indices.Add(mappedIndices[cellIndices[tm]]);
        }
    }

    private float2 GetLight(int index)
    {
        float sunLight = 0.0f;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    sunLight = math.max(sunLight, (float)((this.lightsMerged[index + x + (y * 361) + (z * 19)] >> 4) & 0xF));
                }
            }
        }

        float sourceLight = 0.0f;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    sourceLight = math.max(sourceLight, (float)((this.lightsMerged[index + x + (y * 361) + (z * 19)]) & 0b_0000_1111));
                }
            }
        }

        return new float2(sunLight, sourceLight);
    }

    private float3 GetGradient(int index)
    {
        float3 gradient = new float3(0.0f, 0.0f, 0.0f);

        int offset = 0;
        for (int z = -1; z <= 1; z++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    offset = x + y * 361 + z * 19;
                    gradient += (float)this.voxelsMerged[index + offset].GetDensity() * new float3(x, y, z);
                }
            }
        }

        if (!gradient.Equals(new float3(0.0f, 0.0f, 0.0f)))
        {
            gradient = math.normalize(gradient);
        }

        return gradient;
    }
}
