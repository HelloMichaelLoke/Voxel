using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

//[BurstCompile(CompileSynchronously = true)]
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
    public Vector2Int chunkPosition;
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
        Debug.Log("merged chunks");

        int queueCount = this.sunLightSpreadQueue.Count;
        for (int i = 0; i < queueCount; i++)
        {
            int indexPeek = this.sunLightSpreadQueue.Dequeue();
            int maxSunLight = 0;

            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexTop(indexPeek)));
            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexBottom(indexPeek)));
            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexLeft(indexPeek)));
            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexRight(indexPeek)));
            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexFront(indexPeek)));
            maxSunLight = math.max(maxSunLight, this.GetSunLight(this.GetIndexBack(indexPeek)));

            if (this.GetSunLight(this.GetIndexTop(indexPeek)) == 15)
            {
                this.SetSunLight(indexPeek, (byte)15);
            }
            else
            {
                this.SetSunLight(indexPeek, (byte)(maxSunLight - 1));
            }

            this.sunLightSpreadQueue.Enqueue(indexPeek);
        }

        this.RemoveSunLights();
        this.SpreadSunLights();
        this.RemoveSourceLights();
        this.SpreadSourceLights();
        this.UpdateChunks();
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

    private void UpdateChunks()
    {
        int index = 0;
        int index00 = 0;
        int index10 = 0;
        int index20 = 0;
        int index01 = 0;
        int index11 = 0;
        int index21 = 0;
        int index02 = 0;
        int index12 = 0;
        int index22 = 0;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    if (x >= 0 && x < 16 && z >= 0 && z < 16)
                    {
                        lights00[index00] = (byte)lights[index];
                        index00++;
                    }
                    else if (x >= 16 && x < 32 && z >= 0 && z < 16)
                    {
                        lights10[index10] = (byte)lights[index];
                        index10++;
                    }
                    else if (x >= 32 && x < 48 && z >= 0 && z < 16)
                    {
                        lights20[index20] = (byte)lights[index];
                        index20++;
                    }
                    else if (x >= 0 && x < 16 && z >= 16 && z < 32)
                    {
                        lights01[index01] = (byte)lights[index];
                        index01++;
                    }
                    else if (x >= 16 && x < 32 && z >= 16 && z < 32)
                    {
                        lights11[index11] = (byte)lights[index];
                        index11++;
                    }
                    else if (x >= 32 && x < 48 && z >= 16 && z < 32)
                    {
                        lights21[index21] = (byte)lights[index];
                        index21++;
                    }
                    else if (x >= 0 && x < 16 && z >= 32 && z < 48)
                    {
                        lights02[index02] = (byte)lights[index];
                        index02++;
                    }
                    else if (x >= 16 && x < 32 && z >= 32 && z < 48)
                    {
                        lights12[index12] = (byte)lights[index];
                        index12++;
                    }
                    else if (x >= 32 && x < 48 && z >= 32 && z < 48)
                    {
                        lights22[index22] = (byte)lights[index];
                        index22++;
                    }

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
            Debug.Log(this.sunLightRemovalQueue.Count);
            int index = this.sunLightRemovalQueue.Dequeue();
            int sunLight = (int)this.GetSunLight(index);

            Vector3Int lightPosition = new Vector3Int(
                index % 48,
                Mathf.FloorToInt((float)index / 2304.0f),
                Mathf.FloorToInt(((float)index / 48.0f) % 48)
            );

            // Left
            if (lightPosition.x > 0)
            {
                this.RemoveSunLight(this.GetIndexLeft(index), sunLight);
            }

            // Right
            if (lightPosition.x < 47)
            {
                this.RemoveSunLight(this.GetIndexRight(index), sunLight);
            }

            // Front
            if (lightPosition.z < 47)
            {
                this.RemoveSunLight(this.GetIndexFront(index), sunLight);
            }

            // Back
            if (lightPosition.z > 0)
            {
                this.RemoveSunLight(this.GetIndexBack(index), sunLight);
            }

            // Top
            if (lightPosition.y < 255)
            {
                this.RemoveSunLight(this.GetIndexTop(index), sunLight);
            }

            // Bottom
            if (lightPosition.y > 0)
            {
                if (this.GetIndexBottom(index) == 15)
                {
                    this.RemoveSunLight(this.GetIndexBottom(index), 16);
                }
                else
                {
                    this.RemoveSunLight(this.GetIndexBottom(index), sunLight);
                }
            }

            // Set 0
            this.SetSunLight(index, 0);
        }
    }

    private void RemoveSunLight(int index, int sunLight)
    {
        if ((int)this.GetSunLight(index) < sunLight)
        {
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

            Vector3Int lightPosition = new Vector3Int(
                index % 48,
                Mathf.FloorToInt((float)index / 2304.0f),
                Mathf.FloorToInt((float)index / 48.0f) % 48
            );

            // Left
            if (lightPosition.x > 0)
            {
                this.SpreadSunLight(this.GetIndexLeft(index), sunLight);
            }

            // Right
            if (lightPosition.x < 47)
            {
                this.SpreadSunLight(this.GetIndexRight(index), sunLight);
            }

            // Front
            if (lightPosition.z < 47)
            {
                this.SpreadSunLight(this.GetIndexFront(index), sunLight);
            }

            // Back
            if (lightPosition.z > 0)
            {
                this.SpreadSunLight(this.GetIndexBack(index), sunLight);
            }

            // Top
            if (lightPosition.y < 255)
            {
                this.SpreadSunLight(this.GetIndexTop(index), sunLight);
            }

            // Bottom
            if (lightPosition.y > 0)
            {
                if (sunLight == 15)
                {
                    this.SpreadSunLight(this.GetIndexBottom(index), 16);
                }
                else
                {
                    this.SpreadSunLight(this.GetIndexBottom(index), sunLight);
                }
            }
        }
    }

    private void SpreadSunLight(int index, int sunLight)
    {
        if (sunLight > (int)this.GetSunLight(index) + 1 && this.densities[index] >= 0)
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
        while (this.sourceLightRemovalQueue.Count > 0)
        {
            this.sourceLightRemovalQueue.Dequeue();
        }
    }

    private void RemoveSourceLight(int index)
    {

    }

    private void SpreadSourceLights()
    {
        while (this.sourceLightSpreadQueue.Count > 0)
        {
            this.sourceLightSpreadQueue.Dequeue();
        }
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
        this.TouchChunk(index);
    }

    private byte GetSourceLight(int index)
    {
        return (byte)(lights[index] & 0xF);
    }

    private void SetSourceLight(int index, byte lightValue)
    {
        lights[index] = (byte)((lights[index] & 0xF) | lightValue);
        this.TouchChunk(index);
    }

    private void TouchChunk(int index)
    {
        Vector3Int lightPosition = new Vector3Int(
            index % 48,
            Mathf.FloorToInt((float)index / 2304.0f),
            Mathf.FloorToInt((float)index / 48.0f) % 48
        );

        if (lightPosition.z <= 17 && lightPosition.x <= 17)
        {
            this.chunksTouched[0] = true;
        }
        else if (lightPosition.z <= 17 && lightPosition.x >= 15 && lightPosition.x <= 33)
        {
            this.chunksTouched[1] = true;
        }
        else if (lightPosition.z <= 17 && lightPosition.x >= 31)
        {
            this.chunksTouched[2] = true;
        }
        else if (lightPosition.z >= 15 && lightPosition.z <= 33 && lightPosition.x <= 17)
        {
            this.chunksTouched[3] = true;
        }
        else if (lightPosition.z >= 15 && lightPosition.z <= 33 && lightPosition.x >= 15 && lightPosition.x <= 33)
        {
            this.chunksTouched[4] = true;
        }
        else if (lightPosition.z >= 15 && lightPosition.z <= 33 && lightPosition.x >= 31)
        {
            this.chunksTouched[5] = true;
        }
        else if (lightPosition.z >= 31 && lightPosition.x <= 17)
        {
            this.chunksTouched[6] = true;
        }
        else if (lightPosition.z >= 31 && lightPosition.x >= 15 && lightPosition.x <= 33)
        {
            this.chunksTouched[7] = true;
        }
        else if (lightPosition.z >= 31 && lightPosition.x >= 31)
        {
            this.chunksTouched[8] = true;
        }
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