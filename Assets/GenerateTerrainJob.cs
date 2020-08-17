﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct GenerateTerrainJob : IJob
{
    public NativeArray<float> heights;
    public NativeArray<Voxel> voxels;

    public int2 chunkPosition;

    public void Execute()
    {
        int3 startPosition = new int3(chunkPosition.x * 16, 0, chunkPosition.y * 16);
        int3 endPosition = new int3(startPosition.x + 15, 255, startPosition.z + 15);

        //
        // Generate Heights
        //

        int i = 0;
        for (int z = startPosition.z; z <= endPosition.z; z++)
        {
            for (int x = startPosition.x; x <= endPosition.x; x++)
            {
                heights[i] = GetHeight(new float2(x, z));
                i++;
            }
        }

        //
        // Generate Voxels
        //

        i = 0;
        int heightIndex = 0;
        for (int y = startPosition.y; y <= endPosition.y; y++)
        {
            heightIndex = 0;

            for (int z = startPosition.z; z <= endPosition.z; z++)
            {
                for (int x = startPosition.x; x <= endPosition.x; x++)
                {
                    Voxel voxel = new Voxel();
                    float height = heights[heightIndex];

                    if (y <= height)
                    {
                        float caveNoiseCenter = this.GetCaves(new float3(x, y, z));

                        if (caveNoiseCenter < 0.0f)
                        {
                            float caveNoiseRight = this.GetCaves(new float3(x + 1, y, z));
                            float caveNoiseLeft = this.GetCaves(new float3(x - 1, y, z));
                            float caveNoiseTop = this.GetCaves(new float3(x, y + 1, z));
                            float caveNoiseBottom = this.GetCaves(new float3(x, y - 1, z));
                            float caveNoiseFront = this.GetCaves(new float3(x, y, z + 1));
                            float caveNoiseBack = this.GetCaves(new float3(x, y, z - 1));

                            voxel.SetVoxel(255, 255, 255, 255, 255, 255, this.GetMaterial(new float3(x, y, z)));

                            if (y + 1 >= height)
                            {
                                float distance = math.abs(y - height);
                                voxel.SetLeft(distance);
                                voxel.SetRight(distance);
                                voxel.SetTop(distance);
                                voxel.SetBottom(distance);
                                voxel.SetFront(distance);
                                voxel.SetBack(distance);
                            }
                            else
                            {
                                if (caveNoiseRight >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseRight);
                                    voxel.SetRight((-caveNoiseCenter) / distance);
                                }
                                if (caveNoiseLeft >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseLeft);
                                    voxel.SetLeft((-caveNoiseCenter) / distance);
                                }
                                if (caveNoiseTop >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseTop);
                                    voxel.SetTop((-caveNoiseCenter) / distance);
                                }
                                if (caveNoiseBottom >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseBottom);
                                    voxel.SetBottom((-caveNoiseCenter) / distance);
                                }
                                if (caveNoiseFront >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseFront);
                                    voxel.SetFront((-caveNoiseCenter) / distance);
                                }
                                if (caveNoiseBack >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseBack);
                                    voxel.SetBack((-caveNoiseCenter) / distance);
                                }
                            }
                        }
                    }

                    if (y <= 2)
                    {
                        voxel.SetVoxel(255, 255, 255, 255, 255, 255, 1);
                    }

                    this.voxels[i] = voxel;

                    i++;
                    heightIndex++;
                }
            }
        }
    }

    private float GetHeight(float2 position)
    {
        // Settings
        float maxHeight = 70.0f;

        // Noises
        float noiseAFreq = 0.01f;
        float noiseA = noise.snoise(new float2(1000.0f, 1000.0f) + position * noiseAFreq);

        // Noise to height value
        float height = noiseA;
        height += 1.0f;
        height *= maxHeight / 2.0f;
        return height;
    }

    private byte GetMaterial(float3 position)
    {
        float material = noise.snoise(new float3(position.x, position.y, position.z + 10000.0f) * 0.1f);
        material += 1.0f; // Set range to (0, 2)
        material /= 2.0f; // Set range to (0, 1)
        material *= 3.0f; // Set range to (0, 3)
        material += 1.0f; // Set range to (1, 4) so it can't be 0

        return (byte)material;
    }

    private float GetCaves(float3 position)
    {
        // Noises
        float noiseAFreq = 0.05f;
        float noiseA = noise.snoise(position * noiseAFreq);

        // Noise Mixing etc.
        float value = noiseA;
        return value;
    }
}
