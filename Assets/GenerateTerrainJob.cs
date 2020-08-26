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
                    float height = heights[heightIndex];
                    float density = 0.0f;
                    byte material = 0;
                    float3 position = new float3(x, y, z);
                    float2 positionXZ = new float2(x, z);
                    float temperature = this.GetTemperature(positionXZ);
                    float rainfall = this.GetRainfall(positionXZ);

                    if (y < 75.0f || y < height - 10.0f)
                    {
                        float caveNoise = GetCaves(new float3(x, y, z));

                        if (caveNoise < 0.0f)
                        {
                            density = (sbyte)math.round(127.0f * caveNoise);
                            density -= 1;
                            material = 4;
                        }
                        else
                        {
                            density = (sbyte)math.round(127.0f * caveNoise);
                            material = 0;
                        }
                    }
                    else
                    {
                        if (y > height + 1.0f)
                        {
                            density = 127.0f;
                            material = 0;
                        }
                        else
                        {
                            density = -128.0f;

                            float desert = temperature + (1.0f - rainfall);
                            float snow = (1.0f - temperature) + rainfall;
                            float grass = temperature + rainfall;
                            float dirt = (1.0f - temperature) + (1.0f - rainfall);

                            float maxBiome1 = 0.0f;
                            float maxBiome2 = 0.0f;
                            int biome1 = 0;
                            int biome2 = 0;
                            
                            if (desert > maxBiome1)
                            {
                                maxBiome1 = desert;
                                biome1 = 1;
                            }

                            if (snow > maxBiome1)
                            {
                                maxBiome1 = snow;
                                biome1 = 2;
                            }
                            else if (snow > maxBiome2)
                            {
                                maxBiome2 = snow;
                                biome2 = 2;
                            }

                            if (grass > maxBiome1)
                            {
                                maxBiome1 = grass;
                                biome1 = 3;
                            }
                            else if (grass > maxBiome2)
                            {
                                maxBiome2 = grass;
                                biome2 = 3;
                            }

                            if (dirt > maxBiome1)
                            {
                                maxBiome1 = dirt;
                                biome1 = 4;
                            }
                            else if (dirt > maxBiome2)
                            {
                                maxBiome2 = dirt;
                                biome2 = 4;
                            }

                            if (biome1 == 1)
                            {
                                material = 2;
                            }
                            else if (biome1 == 2)
                            {
                                material = 5;
                            }
                            else if (biome1 == 3)
                            {
                                material = 3;
                            }
                            else if (biome1 == 4)
                            {
                                material = 1;
                            }

                            float distance = math.distance(maxBiome1, maxBiome2);
                            if (distance < 0.1f)
                            {
                                float chance = noise.snoise(positionXZ);
                                if (chance > 0.0f + (distance / 0.1f))
                                {
                                    if (biome2 == 1)
                                    {
                                        material = 2;
                                    }
                                    else if (biome2 == 2)
                                    {
                                        material = 5;
                                    }
                                    else if (biome2 == 3)
                                    {
                                        material = 3;
                                    }
                                    else if (biome2 == 4)
                                    {
                                        material = 1;
                                    }
                                }
                            }

                            if (height < 120.0f)
                            {
                                material = 4;
                            }
                        }
                    }

                    if (y < height && y + 1 >= height && density < 0)
                    {
                        float distanceToSurface = height % 1.0f;
                        density = -127.0f * distanceToSurface - 1.0f;
                    }
                    else if (y >= height && y - 1 < height)
                    {
                        float distanceToSurface = height % 1.0f;
                        density = -127.0f * distanceToSurface - 1.0f;
                        density += 128.0f;
                        material = 0;
                    }


                    if (y <= 2)
                    {
                        density = -128.0f;
                        material = 4;
                    }

                    if (y >= 254)
                    {
                        density = 127.0f;
                        material = 0;
                    }

                    this.voxels[i] = new Voxel((sbyte)density, material);

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

    /// <summary>
    /// Returns a rainfall value between 0.0f and 1.0f
    /// </summary>
    /// <param name="position">The x,z position to generate.</param>
    /// <returns>Returns a rainfall value between 0.0f and 1.0f</returns>
    private float GetRainfall(float2 position)
    {
        float rainfall = 0.0f;

        float2 offset = new float2(1000.0f, 1000.0f);

        rainfall = noise.snoise(offset + (position * 0.001f));

        rainfall = 0.5f * rainfall + 0.5f;

        return rainfall;
    }

    /// <summary>
    /// Returns a temperature value between 0.0f and 1.0f
    /// </summary>
    /// <param name="position">The x,z position to generate.</param>
    /// <returns></returns>
    private float GetTemperature(float2 position)
    {
        float temperature = 0.0f;

        float2 offset = new float2(-1000.0f, -1000.0f);

        temperature = noise.snoise(offset + (position * 0.001f));

        temperature = 0.5f * temperature + 0.5f;

        return temperature;
    }

    private float GetHeightA(float2 position)
    {
        float2 pos = new float2(position.x + 15000.0f, position.y - 20000.0f) * 0.0010f;

        float2 q = new float2(noise.snoise(pos), noise.snoise(pos + new float2(-12.700f, 11.820f)));
        float2 r = pos + 4.0f * q;
        float2 s = new float2(noise.snoise(r + new float2(5.060f, -8.830f)), noise.snoise(r + new float2(-0.270f, -0.180f)));
        float2 t = pos + 4.0f * s;

        float val = octaves(pos, 16.0f);
        float val2 = octaves(pos * 10.0f, 8.0f);
        val = 0.8f * val + 0.05f * noise.snoise(t * 0.05f) + 0.15f * val2;

        /*
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
        */

        float2 offset = new float2(13000.0f, 5000.0f);
        float amplitude = (1.0f + noise.snoise(offset + pos * 0.001f)) / 2.0f;

        val = 0.95f * val + 0.05f * octaves(pos * 0.04f, 2.0f);

        return 120.0f + amplitude * val * 70.0f;
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
