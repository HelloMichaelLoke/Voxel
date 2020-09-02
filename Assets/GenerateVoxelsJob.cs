using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct GenerateVoxelsJob : IJob
{
    public NativeArray<float> heightMap;
    public NativeArray<float> rainMap;
    public NativeArray<float> heatMap;
    public NativeArray<Voxel> voxels;

    public int2 chunkPosition;

    public void Execute()
    {
        int3 startPosition = new int3(chunkPosition.x * 16, 0, chunkPosition.y * 16);
        int3 endPosition = new int3(startPosition.x + 15, 255, startPosition.z + 15);

        // materials
        byte air = 0;
        byte dirt = 1;
        byte sand = 2;
        byte grass = 3;
        byte stone = 4;
        byte snow = 5;

        int i = 0;
        int heightIndex = 0;
        for (int y = startPosition.y; y <= endPosition.y; y++)
        {
            heightIndex = 0;

            for (int z = startPosition.z; z <= endPosition.z; z++)
            {
                for (int x = startPosition.x; x <= endPosition.x; x++)
                {
                    float height = heightMap[heightIndex];

                    float density = 0.0f;
                    byte material = 0;

                    if (y < height)
                    {
                        density = -128.0f;
                        material = stone;
                    }

                    if (y > height)
                    {
                        density = 127.0f;
                        material = air;
                    }

                    if (y < height && y + 1.0f > height)
                    {
                        density = -127.0f * (height % 1.0f) - 1.0f;

                        if (height < 80.0f)
                        {
                            material = sand;
                        }
                        else
                        {
                            material = grass;
                        }
                    }

                    if (y > height && y - 1.0f < height)
                    {
                        density = -127.0f * (height % 1.0f) - 1.0f + 128.0f;
                        material = air;
                    }

                    //if (y < height - 10.0f)
                    //{
                    //    if (this.GetCaves(new float3(x, y, z)) < 0.0f)
                    //    {
                    //        density = -128.0f;
                    //        material = stone;
                    //    }
                    //    else
                    //    {
                    //        density = 127.0f;
                    //        material = air;
                    //    }
                    //}

                    if (y <= 2)
                    {
                        density = -128.0f;
                        material = stone;
                    }

                    if (y >= 254)
                    {
                        density = 127.0f;
                        material = air;
                    }

                    this.voxels[i] = new Voxel((sbyte)density, material);

                    i++;
                    heightIndex++;
                }
            }
        }
    }

    private byte GetMaterial(float3 position)
    {
        float material = noise.snoise(new float3(position.x, position.y, position.z + 10000.0f) * 0.05f);
        material += 1.0f; // Set range to (0, 2)
        material /= 2.0f; // Set range to (0, 1)
        material *= 4.0f; // Set range to (0, 3)
        material += 1.0f; // Set range to (1, 4) so it can't be 0

        return (byte)material;
    }

    private float GetCaves(float3 position)
    {
        // Noises
        float value = 0.0f;

        float noiseAFreq = 0.05f;
        float noiseA = noise.snoise(position * noiseAFreq);
        value = noiseA;

        if (position.y <= 20.0f)
        {
            value = position.y / 20.0f * value + (1.0f - (position.y / 20.0f)) * -1.0f;
        }

        // Noise Mixing etc.
        return value;
    }
}
