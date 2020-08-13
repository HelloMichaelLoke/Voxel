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
    public NativeArray<sbyte> densities;
    public NativeArray<byte> materials;

    public int2 chunkCoordinate;
    public int3 chunkSize;

    public void Execute()
    {
        int3 startPosition;
        startPosition.x = chunkCoordinate.x * chunkSize.x;
        startPosition.y = 0;
        startPosition.z = chunkCoordinate.y * chunkSize.z;

        int3 endPosition = startPosition + chunkSize - new int3(1);

        // Generate Heights
        int i = 0;
        for (int z = startPosition.z; z <= endPosition.z; z++)
        {
            for (int x = startPosition.x; x <= endPosition.x; x++)
            {
                heights[i] = GetHeight(new float2(x, z));
                i++;
            }
        }

        // Fill densities based on heights & determine materials
        i = 0;
        int xLocal, zLocal;
        sbyte density;
        byte material;
        for (int y = startPosition.y; y <= endPosition.y; y++)
        {
            zLocal = 0;
            for (int z = startPosition.z; z <= endPosition.z; z++)
            {
                xLocal = 0;
                for (int x = startPosition.x; x <= endPosition.x; x++)
                {
                    /*
                    float height = heights[xLocal + zLocal * chunkSize.x];

                    density = 127;
                    if ((float)y < math.floor(height))
                    {
                        density = -128;
                    }
                    else if ((float)y == math.floor(height))
                    {
                        float offset = height - math.floor(height);
                        float value = math.round(offset * 127.0f);
                        value = -1.0f - value;
                        density = (sbyte)value;
                    }
                    else if ((float)y == math.ceil(height))
                    {
                        float offset = height - math.floor(height);
                        float value = math.round(offset * 127.0f);
                        value = 127.0f - value;
                        density = (sbyte)value;
                    }
                    else if ((float)y > math.ceil(height))
                    {
                        density = 127;
                    }

                    if ((float)y < math.floor(height - 2.0f))
                    {
                        float caveNoise = GetCaves(new float3(x, y, z));

                        if (caveNoise < 0.0f)
                        {
                            density = (sbyte)-math.round(127.0f * -caveNoise);
                            density -= 1;
                        }
                        else
                        {
                            density = (sbyte)math.round(127.0f * caveNoise);
                        }
                    }

                    if (density >= 0)
                        material = 255;
                    else
                        material = (byte)((noise.snoise(new float3(x, y, z) * 0.01f) + 1.0f) / 2.0f * 3.0f);

                    if (xLocal == 8 && zLocal == 8)
                    {
                        density = 127;
                        material = 255;
                    }
                    */

                    if (y >= 70)
                    {
                        density = 127;
                        material = 255;
                    }
                    else
                    {
                        float caveNoise = GetCaves(new float3(x, y, z));

                        if (caveNoise < 0.0f)
                        {
                            density = (sbyte)math.round(127.0f * caveNoise);
                            density -= 1;
                            material = (byte)math.round((noise.snoise(new float3(x, y, z) * 0.1f) + 1.0f) / 2.0f * 3.0f);
                        }
                        else
                        {
                            density = (sbyte)math.round(127.0f * caveNoise);
                            material = 255;
                        }
                    }

                    if (y <= 1)
                    {
                        density = -128;
                        material = 0;
                    }

                    if (y >= 254)
                    {
                        density = 127;
                        material = 255;
                    }

                    densities[i] = density;
                    materials[i] = material;

                    i++;
                    xLocal++;
                }
                zLocal++;
            }
        }
    }

    private float GetHeight(float2 position)
    {
        // Settings
        float maxHeight = 200.0f;

        // Noises
        float noiseAFreq = 0.001f;
        float noiseA = noise.snoise(new float2(1000.0f, 1000.0f) + position * noiseAFreq);

        // Noise to height value
        float height = noiseA;
        height += 1.0f;
        height *= maxHeight / 2.0f;
        return height;
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
