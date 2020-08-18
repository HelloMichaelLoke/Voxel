using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct MeshTerrainJob : IJob
{
    // Temporary Data
    public NativeArray<Voxel> voxelsMerged;
    public NativeArray<byte> lightsMerged;

    // Chunk Data
    public int2 chunkPosition;

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
    public NativeArray<float3> cornerNormals;
    public NativeList<int> cellIndices;
    public NativeList<ushort> mappedIndices;

    // Physics Mesh Breakpoints (vertex array position, index array position)
    public NativeList<int2> breakPoints;

    public void Execute()
    {
        this.MergeChunks();
        this.MeshCells();
    }

    private void MergeChunks()
    {
        int i = 0;
        int index;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 18; z++)
            {
                for (int x = 0; x <= 18; x++)
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

    private void MeshCells()
    {
        for (int y = 1; y <= 253; y++)
        {
            for (int z = 1; z <= 16; z++)
            {
                for (int x = 1; x <= 16; x++)
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
            // TODO | Check if breakpoint was affected to avoid useless collider updates
            this.breakPoints.Add(new int2(this.vertices.Length, this.indices.Length));
        }

        bool isEmpty = true;
        bool isFull = true;

        for (int i = 0; i < 8; i++)
        {
            this.cornerPositions[i] = this.mcCornerPositions[i] + new float3(x, y, z);
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerVoxels[i] = this.voxelsMerged[index];

            if (this.cornerVoxels[i].GetMaterial() > 0)
            {
                isEmpty = false;
            }
            else
            {
                isFull = false;
            }
        }

        if (isEmpty || isFull)
        {
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerLights[i] = this.GetLight(index);
            this.cornerNormals[i] = this.GetNormal(index);
        }

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

        byte caseIndex = 0;

        if (matId1 > 0)
        {
            caseIndex |= 0b_0000_0001;
        }
        if (matId2 > 0)
        {
            caseIndex |= 0b_0000_0010;
        }
        if (matId3 > 0)
        {
            caseIndex |= 0b_0000_0100;
        }
        if (matId4 > 0)
        {
            caseIndex |= 0b_0000_1000;
        }
        if (matId5 > 0)
        {
            caseIndex |= 0b_0001_0000;
        }
        if (matId6 > 0)
        {
            caseIndex |= 0b_0010_0000;
        }
        if (matId7 > 0)
        {
            caseIndex |= 0b_0100_0000;
        }
        if (matId8 > 0)
        {
            caseIndex |= 0b_1000_0000;
        }

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

        for (int i = 0; i < vertexCount; i++)
        {
            ushort edgeCode = this.mcCellVertexData[caseIndex * 12 + i];

            ushort index0 = (byte)((edgeCode >> 4) & 0x0F);
            ushort index1 = (byte)((edgeCode) & 0x0F);

            float3 position0 = this.cornerPositions[index0];
            float3 position1 = this.cornerPositions[index1];
            float3 normal0 = this.cornerNormals[index0];
            float3 normal1 = this.cornerNormals[index1];
            Voxel voxel0 = this.cornerVoxels[index0];
            Voxel voxel1 = this.cornerVoxels[index1];
            bool solid0 = (this.cornerVoxels[index0].GetMaterial() > 0);
            bool solid1 = (this.cornerVoxels[index1].GetMaterial() > 0);
            float sunLight0 = this.cornerLights[index0].x;
            float sunLight1 = this.cornerLights[index1].x;
            float sourceLight0 = this.cornerLights[index0].y;
            float sourceLight1 = this.cornerLights[index1].y;

            float weight0 = 0.0f;
            float weight1 = 0.0f;
            float materialId = 0.0f;

            if (solid0)
            {
                materialId = voxel0.GetMaterial();

                if (position0.x < position1.x) weight1 = voxel0.GetRight();
                else if (position0.x > position1.x) weight1 = voxel0.GetLeft();
                else if (position0.y < position1.y) weight1 = voxel0.GetTop();
                else if (position0.y > position1.y) weight1 = voxel0.GetBottom();
                else if (position0.z < position1.z) weight1 = voxel0.GetFront();
                else if (position0.z > position1.z) weight1 = voxel0.GetBack();

                weight0 = 1.0f - weight1;
            }
            else
            {
                materialId = voxel1.GetMaterial();

                if (position0.x < position1.x) weight0 = voxel1.GetLeft();
                else if (position0.x > position1.x) weight0 = voxel1.GetRight();
                else if (position0.y < position1.y) weight0 = voxel1.GetBottom();
                else if (position0.y > position1.y) weight0 = voxel1.GetTop();
                else if (position0.z < position1.z) weight0 = voxel1.GetBack();
                else if (position0.z > position1.z) weight0 = voxel1.GetFront();

                weight1 = 1.0f - weight0;
            }

            float3 vertex;
            vertex = weight0 * position0 + weight1 * position1;
            float3 normal = weight0 * normal0 + weight1 * normal1;

            float sunLight = 0.0f;

            if (solid0)
            {
                sunLight = sunLight1 - weight0;
            }
            else
            {
                sunLight = sunLight0 - weight1;
            }

            // TODO
            float sourceLight = 0.0f;

            this.lights.Add(new float2(sunLight / 15.0f, sourceLight / 15.0f));
            this.vertices.Add(vertex - new float3(1.0f, 0.0f, 1.0f));
            this.normals.Add(normal);

            this.mats1234.Add(matIds1234);
            this.mats5678.Add(matIds5678);

            bool isMaterialSet = false;
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
        sunLight = (float)((this.lightsMerged[index] >> 4) & 0xF);

        float sourceLight = 0.0f;
        sourceLight = (float)(this.lightsMerged[index] & 0xF);

        return new float2(sunLight, sourceLight);
    }

    private float3 GetNormal(int index)
    {
        float3 normal = new float3(0.0f, 0.0f, 0.0f);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (math.abs(x) == 1.0f && math.abs(z) == 1.0f)
                        continue;

                    int arrayPosition = x + y * 361 + z * 19;
                    arrayPosition = index + arrayPosition;

                    if (this.voxelsMerged[arrayPosition].GetMaterial() > 0)
                    {
                        normal -= new float3(x, y, z);
                    }
                }
            }
        }

        if (!(normal.x == 0.0f && normal.y == 0.0f && normal.z == 0.0f))
        {
            normal = math.normalize(normal);
        }

        return normal;
    }
}
