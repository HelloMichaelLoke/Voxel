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
    public NativeArray<float> tempHeights;
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
        for (int z = startPosition.z - 1; z <= endPosition.z + 1; z++)
        {
            for (int x = startPosition.x - 1; x <= endPosition.x + 1; x++)
            {
                tempHeights[i] = GetHeight1(new float2(x, z));
                i++;
            }
        }

        i = 0;
        for (int z = 1; z <= 16; z++)
        {
            for (int x = 1; x <= 16; x++)
            {
                int index = x + z * 18;
                float finalHeight = 0.0f;
                finalHeight += tempHeights[index - 19];
                finalHeight += tempHeights[index - 18];
                finalHeight += tempHeights[index + 19];
                finalHeight += tempHeights[index - 1];
                finalHeight += tempHeights[index];
                finalHeight += tempHeights[index + 1];
                finalHeight += tempHeights[index - 19];
                finalHeight += tempHeights[index + 18];
                finalHeight += tempHeights[index + 19];
                finalHeight /= 9.0f;
                heights[i] = finalHeight;
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

                    if (y < 60.0f || y < height - 10.0f)
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

                            if (height < 80.0f)
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
        float2 pos = new float2(position.x + 15000.0f, position.y - 20000.0f) * 0.001f;

        float2 q = new float2(noise.snoise(pos), noise.snoise(pos + new float2(-12.700f, 11.820f)));
        float2 r = pos + 4.0f * q;
        float2 s = new float2(noise.snoise(r + new float2(5.060f, -8.830f)), noise.snoise(r + new float2(-0.270f, -0.180f)));
        float2 t = pos + 4.0f * s;

        float val = octaves(pos * 0.1f, 16.0f);
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

    ////////////////////////////////////////////////////////////////////
    /// NOISE //////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////
    
    private float GetHeight1(float2 position)
    {
        position += new float2(1000.0f, 1000.0f);
        position *= 0.001f;

        // NOISE 1
        float frequencyNoise1 = 0.5f;
        float noise1 = onoise(position * frequencyNoise1, 8);

        // DOMAIN WARPING 1
        float2 p = position * 0.005f;
        float2 q = new float2(fbm(p), fbm(p + new float2(5.2f, 1.3f)));
        float2 r = new float2(fbm(p + 2.0f * q + new float2(1.7f, 9.2f)), fbm(p + 2.0f * q + new float2(8.3f, 2.8f)));
        float dmNoise1 = snoise(p + 2.0f * r);

        // DOMAIN WARPING 2
        p = position * 0.02f;
        q = new float2(fbm(p), fbm(p + new float2(1.2f, 1.3f)));
        r = new float2(fbm(p + 2.0f * q + new float2(2.7f, 3.2f)), fbm(p + 2.0f * q + new float2(-2.3f, 6.8f)));
        float dmNoise2 = snoise(p + 2.0f * r);

        // DOMAIN WARPING 3
        p = position * 0.08f;
        q = new float2(fbm(p), fbm(p + new float2(1.2f, 1.3f)));
        r = new float2(fbm(p + 2.0f * q + new float2(2.7f, 3.2f)), fbm(p + 2.0f * q + new float2(-2.3f, 6.8f)));
        float dmNoise3 = snoise(p + 2.0f * r);

        // FINAL HEIGHT
        float mixedNoises = 0.1f * noise1 + 0.45f * dmNoise1 + 0.35f * dmNoise2 + 0.1f * dmNoise3;
        float height = 0.5f * mixedNoises + 0.5f;

        return 30.0f + height * 100.0f;
    }

    ////////////////////////////////////////////////////////////////////
    /// FUNCTIONS //////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////

    private float3 permute(float3 h)
    {
        float3 value = (h * (h * 34.0f + 2.0f));
        value.x = value.x % 289.0f;
        value.y = value.y % 289.0f;
        value.z = value.z % 289.0f;
        return value;
    }

    private float grad(float hash, float2 vRelative)
    {
        float2 gradient = new float2(math.sin(hash), math.cos(hash));
        return math.dot(vRelative, gradient);
    }

    private float snoise(float2 v)
    {
        float4 C = new float4(0.366025403784439f, -0.211324865405187f, 1.0f - 2.0f * 0.211324865405187f, 1.0f / 0.010080202759123733f);
        float skew = (v.x + v.y) * C.x;
        float2 vSkew = v + skew;
        float2 vSkewBase = math.floor(vSkew);
        float2 vSkewRelative = vSkew - vSkewBase;

        float2 p = vSkewRelative.y > vSkewRelative.x ? new float2(0.0f, 1.0f) : new float2(1.0f, 0.0f);
        float2 vertex1 = vSkewBase;
        float2 vertex2 = vSkewBase + p;
        float2 vertex3 = vSkewBase + new float2(1.0f, 1.0f);
        float3 hashes = permute(permute(new float3(vertex1.x, vertex2.x, vertex3.x)) + new float3(vertex1.y, vertex2.y, vertex3.y));
        hashes *= 6.283185307179586f / 289.0f;

        float unskew = (vSkewRelative.x + vSkewRelative.y) * C.y;
        float2 vRelative = vSkewRelative + unskew;
        float2 vRelative2 = vRelative - p - C.yy;
        float2 vRelative3 = vRelative - C.zz;

        float3 kernels = math.max(new float3(0.0f), 0.5f - new float3(math.dot(vRelative, vRelative), math.dot(vRelative2, vRelative2), math.dot(vRelative3, vRelative3)));
        kernels *= kernels; kernels *= kernels;
        float3 grads = new float3(grad(hashes.x, vRelative), grad(hashes.y, vRelative2), grad(hashes.z, vRelative3));

        return math.dot(kernels, grads) * C.w;
    }

    private float onoise(float2 position, int octaves)
    {
        float sum = 0.0f;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float max = 0.0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += snoise(position * frequency) * amplitude;
            max += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.0f;
        }
        return sum / max;
    }

    private float fbm(float2 position)
    {
        return onoise(position, 16);
    }
}
