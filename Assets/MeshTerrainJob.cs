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
    public NativeArray<sbyte> chunkDensities;
    public NativeArray<byte> chunkMaterials;
    public NativeArray<byte> chunkLights;

    // Chunk Data
    public int2 chunkPosition;
    public float3 chunkSize;
    public float3 chunkSizeFull;

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
    public NativeArray<byte> cornerLightVoxels;
    public NativeArray<float3> cornerNormals;
    public NativeList<int> cellIndices;
    public NativeList<ushort> mappedIndices;

    public void Execute()
    {
        int index = 0;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 18; z++)
            {
                for (int x = 0; x <= 18; x++)
                {
                    sbyte density = 0;
                    byte material = 0;
                    byte light = 0;
                    int arrayPosition = 0;

                    if (x == 0 && z == 0)
                    {
                        arrayPosition = 255 + y * 256;
                        density =   chunk00densities[arrayPosition];
                        material =  chunk00materials[arrayPosition];
                        light =     chunk00lights[arrayPosition];
                    }
                    else if (x == 0 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 15 + ((z - 1) * 16) + y * 256;
                        density =   chunk01densities[arrayPosition];
                        material =  chunk01materials[arrayPosition];
                        light =     chunk01lights[arrayPosition];
                    }
                    else if (x == 0 && z >= 17)
                    {
                        arrayPosition = 15 + ((z - 17) * 16) + y * 256;
                        density =   chunk02densities[arrayPosition];
                        material =  chunk02materials[arrayPosition];
                        light =     chunk02lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z == 0)
                    {
                        arrayPosition = 240 + (x - 1) + y * 256;
                        density =   chunk10densities[arrayPosition];
                        material =  chunk10materials[arrayPosition];
                        light =     chunk10lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 0 + (x - 1) + ((z - 1) * 16) + y * 256;
                        density =   chunk11densities[arrayPosition];
                        material =  chunk11materials[arrayPosition];
                        light =     chunk11lights[arrayPosition];
                    }
                    else if (x >= 1 && x <= 16 && z >= 17)
                    {
                        arrayPosition = 0 + (x - 1) + ((z - 17) * 16) + y * 256;
                        density =   chunk12densities[arrayPosition];
                        material =  chunk12materials[arrayPosition];
                        light =     chunk12lights[arrayPosition];
                    }
                    else if (x >= 17 && z == 0)
                    {
                        arrayPosition = 240 + (x - 17) + y * 256;
                        density =   chunk20densities[arrayPosition];
                        material =  chunk20materials[arrayPosition];
                        light =     chunk20lights[arrayPosition];
                    }
                    else if (x >= 17 && z >= 1 && z <= 16)
                    {
                        arrayPosition = 0 + (x - 17) + ((z - 1) * 16) + y * 256;
                        density =   chunk21densities[arrayPosition];
                        material =  chunk21materials[arrayPosition];
                        light =     chunk21lights[arrayPosition];
                    }
                    else if (x >= 17 && z >= 17)
                    {
                        arrayPosition = 0 + (x - 17) + ((z - 17) * 16) + y * 256;
                        density =   chunk22densities[arrayPosition];
                        material =  chunk22materials[arrayPosition];
                        light =     chunk22lights[arrayPosition];
                    }

                    chunkDensities[index] = density;
                    chunkMaterials[index] = material;
                    chunkLights[index] = light;

                    index++;
                }
            }
        }

        for (float y = 1.0f; y <= 253.0f; y++)
        {
            for (float z = 1.0f; z <= 16.0f; z++)
            {
                for (float x = 1.0f; x <= 16.0f; x++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        this.cornerPositions[i] = mcCornerPositions[i] + new float3(x, y, z);
                        index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * this.chunkSizeFull.x + this.cornerPositions[i].y * this.chunkSizeFull.x * this.chunkSizeFull.z);
                        this.cornerDensities[i] = chunkDensities[index];
                        this.cornerMaterials[i] = chunkMaterials[index];

                        int light = (int)((chunkLights[index] >> 4) & 0xF);

                        for (float aX = -1.0f; aX <= 1.0f; aX++)
                        {
                            for (float aY = -1.0f; aY <= 1.0f; aY++)
                            {
                                for (float aZ = -1.0f; aZ <= 1.0f; aZ++)
                                {
                                    int newIndex = index + (int)aX + (int)aZ * 19 + (int)aY * 19 * 19;
                                    int newLight = (int)((chunkLights[newIndex] >> 4) & 0xF);
                                    light = math.max(light, newLight);
                                }
                            }
                        }

                        this.cornerLightVoxels[i] = (byte)light;
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

                    byte caseIndex = 0;
                    caseIndex |= (byte)((cornerDensities[0] >> 7) & 0x01);
                    caseIndex |= (byte)((cornerDensities[1] >> 6) & 0x02);
                    caseIndex |= (byte)((cornerDensities[2] >> 5) & 0x04);
                    caseIndex |= (byte)((cornerDensities[3] >> 4) & 0x08);
                    caseIndex |= (byte)((cornerDensities[4] >> 3) & 0x10);
                    caseIndex |= (byte)((cornerDensities[5] >> 2) & 0x20);
                    caseIndex |= (byte)((cornerDensities[6] >> 1) & 0x40);
                    caseIndex |= (byte)(cornerDensities[7] & 0x80);

                    if (caseIndex == 0x00 || caseIndex == 0xFF)
                        continue;

                    for (int i = 0; i < 8; i++)
                    {
                        float3 cornerPosition = cornerPositions[i];
                        this.cornerNormals[i] = float3.zero;

                        for (float nz = -1.0f; nz <= 1.0f; nz++)
                        {
                            for (float ny = -1.0f; ny <= 1.0f; ny++)
                            {
                                for (float nx = -1.0f; nx <= 1.0f; nx++)
                                {
                                    if (math.abs(nx) == 1 && math.abs(ny) == 1 && math.abs(z) == 1)
                                        continue;

                                    this.cornerNormals[i] += new float3(nx, ny, nz) * chunkDensities[(int)((cornerPosition.x + nx)
                                                          + (cornerPosition.z + nz) * this.chunkSizeFull.x
                                                          + (cornerPosition.y + ny) * this.chunkSizeFull.x * this.chunkSizeFull.z)];
                                }
                            }
                        }
                        
                        this.cornerNormals[i] = math.normalize(this.cornerNormals[i]);
                    }

                    byte cellClass = mcCellClasses[caseIndex];
                    int geometryCounts = mcCellGeometryCounts[cellClass];
                    int vertexCount = geometryCounts >> 4;
                    int triangleCount = geometryCounts & 0x0F;

                    this.cellIndices.Clear();
                    int cellIndicesArrayPosition = cellClass * 15;
                    int countIndices = 0;
                    while (mcCellIndices[cellIndicesArrayPosition] != -1)
                    {
                        this.cellIndices.Add(mcCellIndices[cellIndicesArrayPosition]);
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
                        ushort edgeCode = mcCellVertexData[caseIndex * 12 + i];
                        ushort v0 = (byte)((edgeCode >> 4) & 0x0F);
                        ushort v1 = (byte)((edgeCode) & 0x0F);
                        float3 p0 = cornerPositions[v0];
                        float3 p1 = cornerPositions[v1];
                        float3 n0 = cornerNormals[v0];
                        float3 n1 = cornerNormals[v1];
                        byte matv0 = cornerMaterials[v0];
                        byte matv1 = cornerMaterials[v1];
                        sbyte d0 = cornerDensities[v0];
                        sbyte d1 = cornerDensities[v1];
                        byte sl0 = cornerLightVoxels[v0]; // (byte)((cornerLightVoxels[v0] >> 4) & 0xF);
                        byte sl1 = cornerLightVoxels[v1]; // (byte)((cornerLightVoxels[v1] >> 4) & 0xF);

                        int t = (d1 << 8) / (d1 - d0);
                        int u = 256 - t;

                        float w0 = (float)t / 256.0f;
                        float w1 = (float)u / 256.0f;

                        float3 vertex;
                        if ((t & 0x00FF) == 0)
                        {
                            if (t == 0)
                                vertex = p1;
                            else
                                vertex = p0;
                        }
                        else
                        {
                            vertex = w0 * p0 + w1 * p1;
                        }

                        float3 normal = w0 * n0 + w1 * n1;
                        normal = math.normalize(normal);

                        float materialId;
                        float sunLightValue;

                        sunLightValue = w1 * ((float)sl1) / 15.0f + w0 * ((float)sl0) / 15.0f;

                        if (d0 < d1)
                        {
                            materialId = (float)matv0 / 255.0f;
                        }
                        else
                        {
                            materialId = (float)matv1 / 255.0f;
                        }

                        this.lights.Add(new float2(sunLightValue, 0.0f));
                        this.vertices.Add(vertex - new float3(1.0f));
                        this.normals.Add(normal);

                        this.mats1234.Add(matIds1234);
                        this.mats5678.Add(matIds5678);

                        if (matIds1234.x == materialId) weights1234.x = 1.0f; else weights1234.x = 0.0f;
                        if (matIds1234.y == materialId) weights1234.y = 1.0f; else weights1234.y = 0.0f;
                        if (matIds1234.z == materialId) weights1234.z = 1.0f; else weights1234.z = 0.0f;
                        if (matIds1234.w == materialId) weights1234.w = 1.0f; else weights1234.w = 0.0f;
                        if (matIds5678.x == materialId) weights5678.x = 1.0f; else weights5678.x = 0.0f;
                        if (matIds5678.y == materialId) weights5678.y = 1.0f; else weights5678.y = 0.0f;
                        if (matIds5678.z == materialId) weights5678.z = 1.0f; else weights5678.z = 0.0f;
                        if (matIds5678.w == materialId) weights5678.w = 1.0f; else weights5678.w = 0.0f;

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
            }
        }
    }
}
