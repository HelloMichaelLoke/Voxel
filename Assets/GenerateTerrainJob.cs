using System.Collections;
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
                heights[i] = GetHeightA(new float2(x, z));
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

                    if (y <= height - 3.0f)
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
                                if (caveNoiseTop >= 0.0f)
                                {
                                    float distance = math.abs(caveNoiseCenter - caveNoiseTop);
                                    voxel.SetTop((-caveNoiseCenter) / distance);
                                }
                                else
                                {
                                    float distance = math.abs(y - height);
                                    voxel.SetLeft(distance);
                                    voxel.SetRight(distance);
                                    voxel.SetTop(distance);
                                    voxel.SetBottom(distance);
                                    voxel.SetFront(distance);
                                    voxel.SetBack(distance);
                                }
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

                    if (y < height && y > height - 3.0f)
                    {
                        voxel.SetMaterial(this.GetMaterial(new float3(x, y, z)));
                        voxel.SetTop(height - y);
                        voxel.SetRight((1.0f / (height - this.GetHeightA(new float2(x + 1.0f, z)))) * (height - y));
                        voxel.SetLeft((1.0f / (height - this.GetHeightA(new float2(x - 1.0f, z)))) * (height - y));
                        voxel.SetFront((1.0f / (height - this.GetHeightA(new float2(x, z + 1.0f)))) * (height - y));
                        voxel.SetBack((1.0f / (height - this.GetHeightA(new float2(x, z - 1.0f)))) * (height - y));
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
        float minHeight = 50.0f;
        float maxHeight = 40.0f;

        // Noises
        float noiseAFreq = 0.01f;
        float noiseA = noise.snoise(new float2(1000.0f, 1000.0f) + position * noiseAFreq);

        // Noise to height value
        float height = noiseA;
        height += 1.0f;
        height *= maxHeight / 2.0f;
        height += minHeight;
        return height;
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

    private float GetHeightA(float2 position)
    {
        float2 pos = new float2(position.x, position.y) * 0.0003f;

        float2 q = new float2(noise.snoise(pos), noise.snoise(pos + new float2(-12.700f, 11.820f)));
        float2 r = pos + 4.0f * q;
        float2 s = new float2(noise.snoise(r + new float2(5.060f, -8.830f)), noise.snoise(r + new float2(-0.270f, -0.180f)));
        float2 t = pos + 4.0f * s;

        float val = octaves(pos, 24.0f);
        float val2 = octaves(pos * 10.0f, 16.0f);
        val = 0.8f * val + 0.05f * noise.snoise(t * 0.05f) + 0.15f * val2;

        float2 seaLevelPos = pos * 0.006f + t * 0.005f;
        float seaLevel = noise.snoise(seaLevelPos);

        if (val >= seaLevel)
        {
            val = math.lerp(val, 1.0f, math.smoothstep(seaLevel, 1.0f, val));
        }
        else
        {
            val = math.lerp(val, 0.0f, math.smoothstep(seaLevel, -1.0f, val));
        }

        float amplitude = (1.0f + noise.snoise(pos * 0.0015f)) / 2.0f;

        return 20.0f + amplitude * val * 220.0f;
    }

    public float octaves(float2 pos, float octaves)
    {
        float val = 0.0f;
        float valMax = 0.0f;
        for (float i = 1.0f; i <= octaves; i++)
        {
            float mult = 1.0f / i;
            valMax += mult;
            val += mult * noise.snoise(pos * i);
        }
        return val / valMax;
    }

    public float rand(float2 pos)
    {
        return -1.0f + 2.0f * math.frac(math.sin(math.dot(pos, new float2(12.9898f, 78.233f))) * 43758.5453123f);
    }

    public float2 rand2(float2 pos)
    {
        return -1.0f + 2.0f * math.frac(math.sin(new float2(math.dot(pos, new float2(127.1f, 311.7f)), math.dot(pos, new float2(269.5f, 183.3f)))) * 43758.5453f);
    }

    public float3 rand3(float2 pos)
    {
        return -1.0f + 2.0f * math.frac(math.sin(new float3(math.dot(pos, new float2(127.1f, 311.7f)), math.dot(pos, new float2(269.5f, 183.3f)), math.dot(pos, new float2(532.1f, 66.3f)))) * 43758.5453f);
    }
}
