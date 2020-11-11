using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct GenerateMapsJob : IJob
{
    public int2 chunkPosition;

    public NativeArray<float> heightMapTemp;

    public NativeArray<float> heightMap;
    public NativeArray<float> rainMap;
    public NativeArray<float> heatMap;

    public void Execute()
    {
        int startPositionX = chunkPosition.x * 16 - 2;
        int endPositionX = startPositionX + 16 + 4;

        int startPositionZ = chunkPosition.y * 16 - 2;
        int endPositionZ = startPositionZ + 16 + 4;

        float2 currentPosition = new float2(0.0f, 0.0f);

        int index = 0;
        for (int z = startPositionZ; z < endPositionZ; z++)
        {
            for (int x = startPositionX; x < endPositionX; x++)
            {
                currentPosition.x = x;
                currentPosition.y = z;

                this.heightMapTemp[index] = this.GetHeight(currentPosition);

                index++;
            }
        }

        index = 0;
        int indexTemp = 0;
        float finalHeight = 0.0f;

        for (int z = 0; z < 20; z++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (x >= 2 && x <= 17 && z >= 2 && z <= 17)
                {
                    finalHeight = 0.0f;

                    for (int iZ = -2; iZ <= 2; iZ++)
                    {
                        for (int iX = -2; iX <= 2; iX++)
                        {
                            finalHeight += this.heightMapTemp[indexTemp + iX + iZ * 20];
                        }
                    }

                    this.heightMap[index] = finalHeight / 25.0f;

                    index++;
                }

                indexTemp++;
            }
        }

        startPositionX = chunkPosition.x * 16;
        startPositionZ = chunkPosition.y * 16;
        endPositionX = startPositionX + 16;
        endPositionZ = startPositionZ + 16;
        currentPosition = new float2(0.0f, 0.0f);
        index = 0;

        for (int z = startPositionZ; z < endPositionZ; z++)
        {
            for (int x = startPositionX; x < endPositionX; x++)
            {
                currentPosition.x = x;
                currentPosition.y = z;

                this.rainMap[index] = this.GetRain(currentPosition);
                this.heatMap[index] = this.GetHeat(currentPosition);

                index++;
            }
        }
    }

    ////////////////////////////////////////////////////////////////////
    /// NOISE //////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////

    private float GetHeight(float2 position)
    {
        position *= 1.0f;

        // DOMAIN WARPING 1
        float frequency1 = 0.00003f;
        int octaves1 = 12;
        float2 p = position * frequency1;
        float2 q = new float2(
            fbm(p, octaves1),
            fbm(p + new float2(1.2f, 2.3f), octaves1)
        );
        float2 r = new float2(
            fbm(p + q + new float2(-0.7f, 1.2f), octaves1),
            fbm(p + q + new float2(0.8f, -0.8f), octaves1)
        );
        float dmNoise1 = snoise(p + r);

        // FINAL HEIGHT
        float mixedNoises = 0.9f * dmNoise1 + 0.1f * this.fbm(position * 0.004f, 12);
        float height = mixedNoises;

        return 100.0f + height * 80.0f;
    }

    private float GetRain(float2 position)
    {
        float rain = 0.0f;

        //float2 offset = new float2(1000.0f, 1000.0f);
        //rain = noise.snoise(offset + (position * 0.001f));
        //rain = 0.5f * rain + 0.5f;

        float2 p = position * 0.00001f;
        float2 q = new float2(
            fbm(p, 12),
            fbm(p + new float2(1.1f, 0.3f), 12)
        );

        float2 r = new float2(
            fbm(p + q + new float2(0.8f, 1.1f), 12),
            fbm(p + q + new float2(0.5f, 0.4f), 12)
        );

        rain = fbm(p + r, 12) + 1.0f;

        return rain;
    }

    private float GetHeat(float2 position)
    {
        float heat = 0.0f;

        //float2 offset = new float2(-1000.0f, -1000.0f);
        //heat = noise.snoise(offset + (position * 0.001f));
        //heat = 0.5f * heat + 0.5f;

        float2 p = position * 0.00001f + new float2(-1283.0f, 1138.0f);
        float2 q = new float2(
            fbm(p, 12),
            fbm(p + new float2(2.1f, 0.7f), 12)
        );

        float2 r = new float2(
            fbm(p + q + new float2(1.8f, -1.4f), 12),
            fbm(p + q + new float2(2.5f, -1.4f), 12)
        );

        heat = fbm(p + r, 12) + 1.0f;

        return heat;
    }

    ////////////////////////////////////////////////////////////////////
    /// FUNCTIONS //////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////

    private float3 permute(float3 h, float hash = 2.0f)
    {
        float3 value = (h * (h * 34.0f + hash));
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
        float hashA = 4358.0f;
        float hashB = 2486.0f;
        float3 hashes = permute(permute(new float3(vertex1.x, vertex2.x, vertex3.x), hashA) + new float3(vertex1.y, vertex2.y, vertex3.y), hashB);
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

    private float fbm(float2 position, int octaves)
    {
        return onoise(position, octaves);
    }
}