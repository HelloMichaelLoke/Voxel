using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct LightRemovalJob : IJob
{
    // Temporary Data
    public NativeArray<sbyte> densities;
    public NativeArray<byte> lights;

    // Queues
    public NativeQueue<int> sunLightSpreadQueue;
    public NativeQueue<int> sunLightRemovalQueue;
    public NativeQueue<int> sourceLightRemovalQueue;
    public NativeQueue<int> sourceLightSpreadQueue;

    // Informational Data
    public NativeArray<bool> chunksTouched;

    /*
        ----------------
        | 02 | 12 | 22 |
        ----------------
        | 01 | 11 | 21 |
        ----------------
        | 00 | 10 | 20 |
        ----------------
    */

    // Input Densities
    public NativeArray<sbyte> densities00;
    public NativeArray<sbyte> densities10;
    public NativeArray<sbyte> densities20;
    public NativeArray<sbyte> densities01;
    public NativeArray<sbyte> densities11;
    public NativeArray<sbyte> densities21;
    public NativeArray<sbyte> densities02;
    public NativeArray<sbyte> densities12;
    public NativeArray<sbyte> densities22;

    // Input Lights
    public NativeArray<byte> lights00;
    public NativeArray<byte> lights10;
    public NativeArray<byte> lights20;
    public NativeArray<byte> lights01;
    public NativeArray<byte> lights11;
    public NativeArray<byte> lights21;
    public NativeArray<byte> lights02;
    public NativeArray<byte> lights12;
    public NativeArray<byte> lights22;

    public void Execute()
    {
        for (int i = 0; i < 9; i++)
        {
            this.chunksTouched[i] = false;
        }

        this.MergeChunks();

        this.RemoveSunLights();
        this.SpreadSunLights();

        this.RemoveSourceLights();
        this.SpreadSourceLights();
    }

    private void MergeChunks()
    {
        int index = 0;

        sbyte density = 0;
        byte light = 0;
        int arrayPosition = 0;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    if (x <= 15 && z <= 15)
                    {
                        arrayPosition = x + (z * 16) + y * 256;
                        density = densities00[arrayPosition];
                        light = lights00[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z <= 15)
                    {
                        arrayPosition = (x - 16) + (z * 16) + y * 256;
                        density = densities10[arrayPosition];
                        light = lights10[arrayPosition];
                    }
                    else if (x >= 32 && z <= 15)
                    {
                        arrayPosition = (x - 32) + (z * 16) + y * 256;
                        density = densities20[arrayPosition];
                        light = lights20[arrayPosition];
                    }
                    else if (x <= 15 && z >= 16 && z <= 31)
                    {
                        arrayPosition = x + ((z - 16) * 16) + y * 256;
                        density = densities01[arrayPosition];
                        light = lights01[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z >= 16 && z <= 31)
                    {
                        arrayPosition = x - 16 + ((z - 16) * 16) + y * 256;
                        density = densities11[arrayPosition];
                        light = lights11[arrayPosition];
                    }
                    else if (x >= 32 && z >= 16 && z <= 31)
                    {
                        arrayPosition = (x - 32) + ((z - 16) * 16) + y * 256;
                        density = densities21[arrayPosition];
                        light = lights21[arrayPosition];
                    }
                    else if (x <= 15 && z >= 32)
                    {
                        arrayPosition = x + ((z - 32) * 16) + y * 256;
                        density = densities02[arrayPosition];
                        light = lights02[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z >= 32)
                    {
                        arrayPosition = (x - 16) + ((z - 32) * 16) + y * 256;
                        density = densities12[arrayPosition];
                        light = lights12[arrayPosition];
                    }
                    else if (x >= 32 && z >= 32)
                    {
                        arrayPosition = (x - 32) + ((z - 32) * 16) + y * 256;
                        density = densities22[arrayPosition];
                        light = lights22[arrayPosition];
                    }

                    densities[index] = density;
                    lights[index] = light;

                    index++;
                }
            }
        }
    }

    //
    // Sun Lights
    //

    private void RemoveSunLights()
    {
        while (this.sunLightRemovalQueue.Count > 0)
        {
            int index = this.sunLightRemovalQueue.Dequeue();
            int sunLight = (int)this.GetSunLight(index);

            // Left
            if (index % 48 != 0)
            {
                this.RemoveSunLight(this.GetIndexLeft(index), sunLight);
            }

            // Right
            if (index % 48 != 47)
            {
                this.RemoveSunLight(this.GetIndexRight(index), sunLight);
            }

            // Front
            if (index % 2304 < 2256)
            {
                this.RemoveSunLight(this.GetIndexFront(index), sunLight);
            }

            // Back
            if (index % 2304 >= 48)
            {
                this.RemoveSunLight(this.GetIndexBack(index), sunLight);
            }

            // Top
            if (index < 9434880)
            {
                this.RemoveSunLight(this.GetIndexTop(index), sunLight);
            }

            // Bottom
            if (index >= 2304)
            {
                this.RemoveSunLight(this.GetIndexBottom(index), sunLight);
            }

            // Set 0
            this.SetSunLight(index, 0);
        }
    }

    private void RemoveSunLight(int index, int sunLight)
    {
        if (sunLight >= (int)this.GetSunLight(index))
        {
            this.SetSunLight(index, 0);
            this.sunLightRemovalQueue.Enqueue(index);
        }
        else
        {
            this.sunLightSpreadQueue.Enqueue(index);
        }
    }

    private void SpreadSunLights()
    {
        while (this.sunLightSpreadQueue.Count > 0)
        {
            int index = this.sunLightSpreadQueue.Dequeue();
            int sunLight = this.GetSunLight(index);

            // Left
            if (index % 48 != 0)
            {
                this.SpreadSunLight(this.GetIndexLeft(index), sunLight);
            }

            // Right
            if (index % 48 != 47)
            {
                this.SpreadSunLight(this.GetIndexRight(index), sunLight);
            }

            // Front
            if (index % 2304 < 2256)
            {
                this.SpreadSunLight(this.GetIndexFront(index), sunLight);
            }

            // Back
            if (index % 2304 >= 48)
            {
                this.SpreadSunLight(this.GetIndexBack(index), sunLight);
            }

            // Top
            if (index < 9434880)
            {
                this.SpreadSunLight(this.GetIndexTop(index), sunLight);
            }

            // Bottom
            if (index % 2304 > 0)
            {
                this.SpreadSunLight(this.GetIndexBottom(index), sunLight);
            }
        }
    }

    private void SpreadSunLight(int index, int sunLight)
    {
        if (sunLight > (int)this.GetSunLight(index) + 1)
        {
            this.SetSunLight(index, (byte)(sunLight - 1));
            this.sunLightSpreadQueue.Enqueue(index);
        }
    }

    //
    // Source Lights
    //

    private void RemoveSourceLights()
    {

    }

    private void RemoveSourceLight(int index)
    {

    }

    private void SpreadSourceLights()
    {

    }

    private void SpreadSourceLight(int index)
    {

    }

    //
    // Helpers
    //

    private byte GetSunLight(int index)
    {
        return (byte)((lights[index] >> 4) & 0xF);
    }

    private void SetSunLight(int index, byte lightValue)
    {
        lights[index] = (byte)((lights[index] & 0xF) | (lightValue << 4));
    }

    private byte GetSourceLight(int index)
    {
        return (byte)(lights[index] & 0xF);
    }

    private void SetSourceLight(int index, byte lightValue)
    {
        lights[index] = (byte)((lights[index] & 0xF) | lightValue);
    }

    // +X
    private int GetIndexRight(int index)
    {
        return index + 1;
    }

    // -X
    private int GetIndexLeft(int index)
    {
        return index - 1;
    }

    // +Y
    private int GetIndexTop(int index)
    {
        return index + 2304;
    }

    // -Y
    private int GetIndexBottom(int index)
    {
        return index - 2304;
    }

    // +Z
    private int GetIndexFront(int index)
    {
        return index + 48;
    }

    // -Z
    private int GetIndexBack(int index)
    {
        return index - 48;
    }
}