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
    public NativeHashMap<Vector3, Vector3> vertexNormals;
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
    public NativeArray<int> cornerIndices;
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
            this.cornerIndices[i] = index;
            this.cornerVoxels[i] = this.voxelsMerged[index];

            if (this.cornerVoxels[i].IsSolid())
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

            ushort cornerIndex0 = (byte)((edgeCode >> 4) & 0x0F);
            ushort cornerIndex1 = (byte)((edgeCode) & 0x0F);

            float3 position0 = this.cornerPositions[cornerIndex0];
            float3 position1 = this.cornerPositions[cornerIndex1];
            Voxel voxel0 = this.cornerVoxels[cornerIndex0];
            Voxel voxel1 = this.cornerVoxels[cornerIndex1];
            bool solid0 = (this.cornerVoxels[cornerIndex0].IsSolid());
            bool solid1 = (this.cornerVoxels[cornerIndex1].IsSolid());
            float sunLight0 = this.cornerLights[cornerIndex0].x;
            float sunLight1 = this.cornerLights[cornerIndex1].x;
            float sourceLight0 = this.cornerLights[cornerIndex0].y;
            float sourceLight1 = this.cornerLights[cornerIndex1].y;
            int index0 = this.cornerIndices[cornerIndex0];
            int index1 = this.cornerIndices[cornerIndex1];

            //
            // Weights
            //

            float density0 = (float)voxel0.GetDensity();
            float density1 = (float)voxel1.GetDensity();
            if (density0 >= 0.0f) density0 += 1.0f;
            if (density1 >= 0.0f) density1 += 1.0f;
            float distance = math.abs(density1) + math.abs(density0);
            density0 /= distance;
            density1 /= distance;

            float weight0 = math.abs(density1);
            float weight1 = 1.0f - weight0;

            //
            // Vertex, Normal, Material and Light
            //

            float3 vertex = weight0 * position0 + weight1 * position1;

            Vector3 normal = weight0 * this.GetGradient(index0) + weight1 * this.GetGradient(index1);
            if (normal != Vector3.zero) normal = math.normalize(normal);

            float materialId = 0.0f;
            float sunLight = 0.0f;
            float sourceLight = 0.0f;

            if (solid0)
            {
                materialId = this.GetMaterial(index0);
            }
            else
            {
                materialId = this.GetMaterial(index1);
            }

            //if (solid0 && this.lightsMerged[index1] == 0) { sunLight0 = 0.0f; sunLight1 = 0.0f; };
            //if (solid1 && this.lightsMerged[index0] == 0) { sunLight0 = 0.0f; sunLight1 = 0.0f; };

            sunLight = weight0 * sunLight0 + weight1 * sunLight1;
            sourceLight = weight0 * sourceLight0 + weight1 * sourceLight1;

            //
            // Set Mesh Data
            //

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
        float sunLight = sunLight = (float)((this.lightsMerged[index] >> 4) & 0xF);

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

        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index + 1] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index - 1] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index + 361] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index - 361] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index + 19] >> 4) & 0xF));
        //sunLight = math.max(sunLight, (float)((this.lightsMerged[index - 19] >> 4) & 0xF));

        // TODO Get Max
        float sourceLight = sourceLight = (float)(this.lightsMerged[index] & 0xF);

        return new float2(sunLight, sourceLight);
    }

    private Voxel GetVoxel(int index)
    {
        return this.voxelsMerged[index];
    }

    private Vector3 GetGradient(int index)
    {
        Vector3 gradient = Vector3.zero;

        gradient += this.GetVoxel(index + 1).GetDensity() * new Vector3(1.0f, 0.0f, 0.0f);
        gradient += this.GetVoxel(index - 1).GetDensity() * new Vector3(-1.0f, 0.0f, 0.0f);
        gradient += this.GetVoxel(index + 361).GetDensity() * new Vector3(0.0f, 1.0f, 0.0f);
        gradient += this.GetVoxel(index - 361).GetDensity() * new Vector3(0.0f, -1.0f, 0.0f);
        gradient += this.GetVoxel(index + 19).GetDensity() * new Vector3(0.0f, 0.0f, 1.0f);
        gradient += this.GetVoxel(index - 19).GetDensity() * new Vector3(0.0f, 0.0f, -1.0f);

        gradient = math.normalize(gradient);

        return gradient;
    }

    private byte GetMaterial(int index)
    {
        return this.GetVoxel(index).GetMaterial();

        byte material = 0;

        sbyte minDensity = 127;

        Voxel voxelRight = this.GetVoxel(index + 1);
        Voxel voxelLeft = this.GetVoxel(index - 1);
        Voxel voxelTop = this.GetVoxel(index + 361);
        Voxel voxelBottom = this.GetVoxel(index - 361);
        Voxel voxelFront = this.GetVoxel(index + 19);
        Voxel voxelBack = this.GetVoxel(index - 19);

        if (voxelRight.GetDensity() < minDensity)
        {
            material = voxelRight.GetMaterial();
            minDensity = voxelRight.GetDensity();
        }

        if (voxelLeft.GetDensity() < minDensity)
        {
            material = voxelLeft.GetMaterial();
            minDensity = voxelLeft.GetDensity();
        }

        if (voxelTop.GetDensity() < minDensity)
        {
            material = voxelTop.GetMaterial();
            minDensity = voxelTop.GetDensity();
        }

        if (voxelBottom.GetDensity() < minDensity)
        {
            material = voxelBottom.GetMaterial();
            minDensity = voxelBottom.GetDensity();
        }

        if (voxelFront.GetDensity() < minDensity)
        {
            material = voxelFront.GetMaterial();
            minDensity = voxelFront.GetDensity();
        }

        if (voxelBack.GetDensity() < minDensity)
        {
            material = voxelBack.GetMaterial();
            minDensity = voxelBack.GetDensity();
        }

        return material;
    }
}
