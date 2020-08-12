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
    public NativeArray<sbyte>   chunkDensities;
    public NativeArray<byte>    chunkMaterials;
    public NativeArray<byte>    chunkLights;

    // Chunk Data
    public int2     chunkPosition;
    public float3   chunkSize;
    public float3   chunkSizeFull;

    // Terrain Data
    public NativeArray<sbyte>   chunk00densities;
    public NativeArray<byte>    chunk00materials;
    public NativeArray<byte>    chunk00lights;
    public NativeArray<sbyte>   chunk10densities;
    public NativeArray<byte>    chunk10materials;
    public NativeArray<byte>    chunk10lights;
    public NativeArray<sbyte>   chunk20densities;
    public NativeArray<byte>    chunk20materials;
    public NativeArray<byte>    chunk20lights;
    public NativeArray<sbyte>   chunk01densities;
    public NativeArray<byte>    chunk01materials;
    public NativeArray<byte>    chunk01lights;
    public NativeArray<sbyte>   chunk11densities;
    public NativeArray<byte>    chunk11materials;
    public NativeArray<byte>    chunk11lights;
    public NativeArray<sbyte>   chunk21densities;
    public NativeArray<byte>    chunk21materials;
    public NativeArray<byte>    chunk21lights;
    public NativeArray<sbyte>   chunk02densities;
    public NativeArray<byte>    chunk02materials;
    public NativeArray<byte>    chunk02lights;
    public NativeArray<sbyte>   chunk12densities;
    public NativeArray<byte>    chunk12materials;
    public NativeArray<byte>    chunk12lights;
    public NativeArray<sbyte>   chunk22densities;
    public NativeArray<byte>    chunk22materials;
    public NativeArray<byte>    chunk22lights;

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
    public NativeArray<sbyte> cornerDensities;
    public NativeArray<byte> cornerMaterials;
    public NativeArray<float2> cornerLights;
    public NativeArray<float3> cornerNormals;
    public NativeList<int> cellIndices;
    public NativeList<ushort> mappedIndices;

    public void Execute()
    {
        this.MergeChunks();
        this.MeshCells();
    }

    private void MergeChunks()
    {
        int index = 0;
        sbyte density = 0;
        byte material = 0;
        byte light = 0;
        int arrayPosition = 0;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 18; z++)
            {
                for (int x = 0; x <= 18; x++)
                {
                    if (x == 0 && z == 0)
                    {
                        arrayPosition = 255 + y * 256;
                        density = chunk00densities[arrayPosition];
                        material = chunk00materials[arrayPosition];
                        light = chunk00lights[arrayPosition];
                    }
                    else if (x == 0 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 15 + ((z - 1) * 16) + y * 256;
                        density = chunk01densities[arrayPosition];
                        material = chunk01materials[arrayPosition];
                        light = chunk01lights[arrayPosition];
                    }
                    else if (x == 0 && z >= 17)
                    {
                        arrayPosition = 15 + ((z - 17) * 16) + y * 256;
                        density = chunk02densities[arrayPosition];
                        material = chunk02materials[arrayPosition];
                        light = chunk02lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z == 0)
                    {
                        arrayPosition = 240 + (x - 1) + y * 256;
                        density = chunk10densities[arrayPosition];
                        material = chunk10materials[arrayPosition];
                        light = chunk10lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 0 + (x - 1) + ((z - 1) * 16) + y * 256;
                        density = chunk11densities[arrayPosition];
                        material = chunk11materials[arrayPosition];
                        light = chunk11lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z >= 17)
                    {
                        arrayPosition = 0 + (x - 1) + ((z - 17) * 16) + y * 256;
                        density = chunk12densities[arrayPosition];
                        material = chunk12materials[arrayPosition];
                        light = chunk12lights[arrayPosition];
                    }
                    else if (x >= 17 && z == 0)
                    {
                        arrayPosition = 240 + (x - 17) + y * 256;
                        density = chunk20densities[arrayPosition];
                        material = chunk20materials[arrayPosition];
                        light = chunk20lights[arrayPosition];
                    }
                    else if (x >= 17 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 0 + (x - 17) + ((z - 1) * 16) + y * 256;
                        density = chunk21densities[arrayPosition];
                        material = chunk21materials[arrayPosition];
                        light = chunk21lights[arrayPosition];
                    }
                    else if (x >= 17 && z >= 17)
                    {
                        arrayPosition = 0 + (x - 17) + ((z - 17) * 16) + y * 256;
                        density = chunk22densities[arrayPosition];
                        material = chunk22materials[arrayPosition];
                        light = chunk22lights[arrayPosition];
                    }

                    chunkDensities[index] = density;
                    chunkMaterials[index] = material;
                    chunkLights[index] = light;

                    index++;
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
        for (int i = 0; i < 8; i++)
        {
            this.cornerPositions[i] = this.mcCornerPositions[i] + new float3(x, y, z);
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerDensities[i] = chunkDensities[index];
        }

        byte caseIndex = 0;
        caseIndex |= (byte)((this.cornerDensities[0] >> 7) & 0x01);
        caseIndex |= (byte)((this.cornerDensities[1] >> 6) & 0x02);
        caseIndex |= (byte)((this.cornerDensities[2] >> 5) & 0x04);
        caseIndex |= (byte)((this.cornerDensities[3] >> 4) & 0x08);
        caseIndex |= (byte)((this.cornerDensities[4] >> 3) & 0x10);
        caseIndex |= (byte)((this.cornerDensities[5] >> 2) & 0x20);
        caseIndex |= (byte)((this.cornerDensities[6] >> 1) & 0x40);
        caseIndex |= (byte)(this.cornerDensities[7] & 0x80);

        if (caseIndex == 0x00 || caseIndex == 0xFF)
            return;

        for (int i = 0; i < 8; i++)
        {
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerMaterials[i] = this.chunkMaterials[index];
            this.cornerLights[i] = this.GetLight(index);
            this.cornerNormals[i] = this.GetNormal(index);
        }

        float matId1 = (float)cornerMaterials[0] / 255.0f;
        float matId2 = (float)cornerMaterials[1] / 255.0f;
        float matId3 = (float)cornerMaterials[2] / 255.0f;
        float matId4 = (float)cornerMaterials[3] / 255.0f;
        float matId5 = (float)cornerMaterials[4] / 255.0f;
        float matId6 = (float)cornerMaterials[5] / 255.0f;
        float matId7 = (float)cornerMaterials[6] / 255.0f;
        float matId8 = (float)cornerMaterials[7] / 255.0f;

        float4 matIds1234 = new float4(matId1, matId2, matId3, matId4);
        float4 matIds5678 = new float4(matId5, matId6, matId7, matId8);

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
            byte material0 = this.cornerMaterials[index0];
            byte material1 = this.cornerMaterials[index1];
            sbyte density0 = this.cornerDensities[index0];
            sbyte density1 = this.cornerDensities[index1];
            float sunLight0 = this.cornerLights[index0].x;
            float sunLight1 = this.cornerLights[index1].x;
            float sourceLight0 = this.cornerLights[index0].y;
            float sourceLight1 = this.cornerLights[index1].y;

            int t = (density1 << 8) / (density1 - density0);
            int u = 256 - t;

            float weight0 = (float)t / 256.0f;
            float weight1 = (float)u / 256.0f;

            float3 vertex;
            if ((t & 0x00FF) == 0)
            {
                if (t == 0)
                    vertex = position1;
                else
                    vertex = position0;
            }
            else
            {
                vertex = weight0 * position0 + weight1 * position1;
            }

            float3 normal = weight0 * normal0 + weight1 * normal1;
            float sunLight = weight0 * sunLight0 + weight1 * sunLight1;

            float sourceLight = weight0 * sourceLight0 + weight1 * sourceLight1;

            float materialId;
            if (density0 < density1)
            {
                materialId = (float)material0 / 255.0f;
            }
            else
            {
                materialId = (float)material1 / 255.0f;
            }

            this.lights.Add(new float2(sunLight, sourceLight));
            this.vertices.Add(vertex - new float3(1.0f, 1.0f, 1.0f));
            this.normals.Add(normal);

            this.mats1234.Add(matIds1234);
            this.mats5678.Add(matIds5678);

            bool isMaterialSet = false;
            if (matIds1234.x == materialId) { weights1234.x = 1.0f; isMaterialSet = true; } else weights1234.x = 0.0f;
            if (matIds1234.y == materialId && !isMaterialSet) { weights1234.y = 1.0f; isMaterialSet = true; } else weights1234.y = 0.0f;
            if (matIds1234.z == materialId && !isMaterialSet) { weights1234.z = 1.0f; isMaterialSet = true; } else weights1234.z = 0.0f;
            if (matIds1234.w == materialId && !isMaterialSet) { weights1234.w = 1.0f; isMaterialSet = true; } else weights1234.w = 0.0f;
            if (matIds5678.x == materialId && !isMaterialSet) { weights5678.x = 1.0f; isMaterialSet = true; } else weights5678.x = 0.0f;
            if (matIds5678.y == materialId && !isMaterialSet) { weights5678.y = 1.0f; isMaterialSet = true; } else weights5678.y = 0.0f;
            if (matIds5678.z == materialId && !isMaterialSet) { weights5678.z = 1.0f; isMaterialSet = true; } else weights5678.z = 0.0f;
            if (matIds5678.w == materialId && !isMaterialSet) { weights5678.w = 1.0f; isMaterialSet = true; } else weights5678.w = 0.0f;

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
        int sunLight = 0;

        sunLight = math.max(sunLight, (int)((chunkLights[index - 381] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 362] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 343] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 20] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 1] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 18] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 341] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 360] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 379] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 380] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 361] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 342] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 19] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 19] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 342] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 361] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 380] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 379] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 360] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 341] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 18] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 1] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 20] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 343] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 362] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 381] >> 4) & 0xF));

        /*
        sunLight = math.max(sunLight, (int)((chunkLights[index + 1] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 1] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 19] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 19] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index + 361] >> 4) & 0xF));
        sunLight = math.max(sunLight, (int)((chunkLights[index - 361] >> 4) & 0xF));
        */

        int sourceLight = 0;

        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 381] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 362] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 343] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 20] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 1] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 18] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 341] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 360] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 379] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 380] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 361] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 342] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 19] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 19] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 342] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 361] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 380] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 379] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 360] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 341] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index - 18] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 1] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 20] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 343] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 362] & 0xF));
        sourceLight = math.max(sourceLight, (int)(chunkLights[index + 381] & 0xF));

        return new float2((float)sunLight / 15.0f, (float)sourceLight / 15.0f);
    }

    private float3 GetNormal(int index)
    {
        float3 normal = new float3(0.0f, 0.0f, 0.0f);

        normal += new float3(-1.0f, -1.0f, -1.0f) * (float)chunkDensities[index - 381];
        normal += new float3(-1.0f, -1.0f, 0.0f) * (float)chunkDensities[index - 362];
        normal += new float3(-1.0f, -1.0f, 1.0f) * (float)chunkDensities[index - 343];
        normal += new float3(-1.0f, 0.0f, -1.0f) * (float)chunkDensities[index - 20];
        normal += new float3(-1.0f, 0.0f, 0.0f) * (float)chunkDensities[index - 1];
        normal += new float3(-1.0f, 0.0f, 1.0f) * (float)chunkDensities[index + 18];
        normal += new float3(-1.0f, 1.0f, -1.0f) * (float)chunkDensities[index + 341];
        normal += new float3(-1.0f, 1.0f, 0.0f) * (float)chunkDensities[index + 360];
        normal += new float3(-1.0f, 1.0f, 1.0f) * (float)chunkDensities[index + 379];
        normal += new float3(0.0f, -1.0f, -1.0f) * (float)chunkDensities[index - 380];
        normal += new float3(0.0f, -1.0f, 0.0f) * (float)chunkDensities[index - 361];
        normal += new float3(0.0f, -1.0f, 1.0f) * (float)chunkDensities[index - 342];
        normal += new float3(0.0f, 0.0f, -1.0f) * (float)chunkDensities[index - 19];
        normal += new float3(0.0f, 0.0f, 1.0f) * (float)chunkDensities[index + 19];
        normal += new float3(0.0f, 1.0f, -1.0f) * (float)chunkDensities[index + 342];
        normal += new float3(0.0f, 1.0f, 0.0f) * (float)chunkDensities[index + 361];
        normal += new float3(0.0f, 1.0f, 1.0f) * (float)chunkDensities[index + 380];
        normal += new float3(1.0f, -1.0f, -1.0f) * (float)chunkDensities[index - 379];
        normal += new float3(1.0f, -1.0f, 0.0f) * (float)chunkDensities[index - 360];
        normal += new float3(1.0f, -1.0f, 1.0f) * (float)chunkDensities[index - 341];
        normal += new float3(1.0f, 0.0f, -1.0f) * (float)chunkDensities[index - 18];
        normal += new float3(1.0f, 0.0f, 0.0f) * (float)chunkDensities[index + 1];
        normal += new float3(1.0f, 0.0f, 1.0f) * (float)chunkDensities[index + 20];
        normal += new float3(1.0f, 1.0f, -1.0f) * (float)chunkDensities[index + 343];
        normal += new float3(1.0f, 1.0f, 0.0f) * (float)chunkDensities[index + 362];
        normal += new float3(1.0f, 1.0f, 1.0f) * (float)chunkDensities[index + 381];

        return math.normalize(normal);
    }
}
