using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct GenerateMapsJob : IJob
{
    public int2 chunkPosition;

    public NativeArray<float> heightMap;
    public NativeArray<float> rainMap;
    public NativeArray<float> heatMap;

    public void Execute()
    {
        int startPositionX = chunkPosition.x * 16;
        int startPositionZ = chunkPosition.y * 16;
        int endPositionX = startPositionX + 16;
        int endPositionZ = startPositionZ + 16;

        float2 currentPosition = new float2(0.0f, 0.0f);

        int index = 0;
        for (int z = startPositionZ; z < endPositionZ; z++)
        {
            for (int x = startPositionX; x < endPositionX; x++)
            {
                currentPosition.x = x;
                currentPosition.y = z;

                this.heightMap[index] = this.GetHeight(currentPosition);
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
        position += new float2(10000.0f, -10000.0f);
        position *= 0.001f;

        // DOMAIN WARPING 1
        float2 p = position * 0.008f;
        float2 q = new float2(fbm(p), fbm(p + new float2(5.2f, 1.3f)));
        float2 r = new float2(fbm(p + 2.0f * q + new float2(1.7f, 9.2f)), fbm(p + 2.0f * q + new float2(8.3f, 2.8f)));
        float dmNoise1 = snoise(p + 2.0f * r);

        // DOMAIN WARPING 2
        p = position * 0.01f;
        q = new float2(fbm(p), fbm(p + new float2(1.2f, 1.3f)));
        r = new float2(fbm(p + 2.0f * q + new float2(2.7f, 3.2f)), fbm(p + 2.0f * q + new float2(-2.3f, 6.8f)));
        float dmNoise2 = snoise(p + 2.0f * r);

        // DOMAIN WARPING 3
        p = position * 0.03f;
        q = new float2(fbm(p), fbm(p + new float2(1.2f, 1.3f)));
        r = new float2(fbm(p + 2.0f * q + new float2(2.7f, 3.2f)), fbm(p + 2.0f * q + new float2(-2.3f, 6.8f)));
        float dmNoise3 = snoise(p + 2.0f * r);

        // FINAL HEIGHT
        float mixedNoises = 0.6f * dmNoise1 + 0.3f * dmNoise2 + 0.1f * dmNoise3;
        float height = mixedNoises;

        return 100.0f + height * 80.0f;
    }

    private float GetRain(float2 position)
    {
        float rain = 0.0f;

        float2 offset = new float2(1000.0f, 1000.0f);

        rain = noise.snoise(offset + (position * 0.001f));

        rain = 0.5f * rain + 0.5f;

        return rain;
    }

    private float GetHeat(float2 position)
    {
        float heat = 0.0f;

        float2 offset = new float2(-1000.0f, -1000.0f);

        heat = noise.snoise(offset + (position * 0.001f));

        heat = 0.5f * heat + 0.5f;

        return heat;
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
        return onoise(position, 13);
    }
}