using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct GenerateLightsJob : IJob
{
    // Final light data output
    public NativeArray<byte> lights;

    // Temporary data (48 * 48 * 256)
    public NativeArray<bool> isSolid;
    public NativeArray<byte> sunLights;
    public NativeQueue<int3> lightQueue;

    // Used data
    public NativeArray<Voxel> voxels00;
    public NativeArray<Voxel> voxels10;
    public NativeArray<Voxel> voxels20;
    public NativeArray<Voxel> voxels01;
    public NativeArray<Voxel> voxels11;
    public NativeArray<Voxel> voxels21;
    public NativeArray<Voxel> voxels02;
    public NativeArray<Voxel> voxels12;
    public NativeArray<Voxel> voxels22;

    // Information
    public int2 chunkPosition;

    public void Execute()
    {
        this.MergeChunks();
        this.SpreadSunLights();
        this.UpdateChunk();
    }

    public void SpreadSunLights()
    {
        while (this.lightQueue.Count > 0)
        {
            this.SpreadSunLight(this.lightQueue.Dequeue());
        }
    }

    public void SpreadSunLight(int3 position)
    {
        int index = position.x + position.z * 48 + position.y * 2304;
        int lightValue = this.sunLights[index];

        // -X Voxel (Left)
        if (position.x > 0)
        {
            int indexLeft = index - 1;
            if (!this.isSolid[indexLeft])
            {
                int lightValueLeft = this.sunLights[indexLeft];

                if (lightValue - lightValueLeft > 1)
                {
                    lightValueLeft = lightValue - 1;
                    this.sunLights[indexLeft] = (byte)lightValueLeft;

                    if (lightValueLeft > 1) this.lightQueue.Enqueue(position + new int3(-1, 0, 0));
                }
            }
        }

        // +X Voxel (Right)
        if (position.x < 48 - 1)
        {
            int indexRight = index + 1;
            if (!this.isSolid[indexRight])
            {
                int lightValueRight = this.sunLights[indexRight];

                if (lightValue - lightValueRight > 1)
                {
                    lightValueRight = lightValue - 1;
                    this.sunLights[indexRight] = (byte)lightValueRight;

                    if (lightValueRight > 1)
                        this.lightQueue.Enqueue(position + new int3(1, 0, 0));
                }
            }
        }

        // -Z Voxel (Front)
        if (position.z > 0)
        {
            int indexFront = index - 48;
            if (!this.isSolid[indexFront])
            {
                int lightValueFront = this.sunLights[indexFront];

                if (lightValue - lightValueFront > 1)
                {
                    lightValueFront = lightValue - 1;
                    this.sunLights[indexFront] = (byte)lightValueFront;

                    if (lightValueFront > 1)
                        this.lightQueue.Enqueue(position + new int3(0, 0, -1));
                }
            }
        }

        // +Z Voxel (Back)
        if (position.z < 48 - 1)
        {
            int indexBack = index + 48;
            if (!this.isSolid[indexBack])
            {
                int lightValueBack = this.sunLights[indexBack];

                if (lightValue - lightValueBack > 1)
                {
                    lightValueBack = lightValue - 1;
                    this.sunLights[indexBack] = (byte)lightValueBack;

                    if (lightValueBack > 1)
                        this.lightQueue.Enqueue(position + new int3(0, 0, 1));
                }
            }
        }

        // -Y Voxel (Bottom)
        if (position.y > 0)
        {
            int indexBottom = index - 2304;
            if (!this.isSolid[indexBottom])
            {
                int lightValueBottom = this.sunLights[indexBottom];

                if (lightValue == 15)
                {
                    this.sunLights[indexBottom] = 15;
                    this.lightQueue.Enqueue(position + new int3(0, -1, 0));
                }
                else if (lightValue - lightValueBottom > 1)
                {
                    lightValueBottom = lightValue - 1;
                    this.sunLights[indexBottom] = (byte)lightValueBottom;

                    if (lightValueBottom > 1)
                        this.lightQueue.Enqueue(position + new int3(0, -1, 0));
                }
            }
        }

        // +Y Voxel (Top)
        if (position.y < 256 - 1)
        {
            int indexTop = index + 2304;
            if (!this.isSolid[indexTop])
            {
                int lightValueTop = this.sunLights[indexTop];
                if (lightValue - lightValueTop > 1)
                {
                    lightValueTop = lightValue - 1;
                    this.sunLights[indexTop] = (byte)lightValueTop;

                    if (lightValueTop > 1)
                        this.lightQueue.Enqueue(position + new int3(0, 1, 0));
                }
            }
        }
    }

    public void MergeChunks()
    {
        int i = 0;
        bool isSolid;
        int index;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    isSolid = false;

                    if (x <= 15 && z <= 15)
                    {
                        index = x + (z * 16) + y * 256;
                        if (voxels00[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 16 && x <= 31 && z <= 15)
                    {
                        index = (x - 16) + (z * 16) + y * 256;
                        if (voxels10[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 32 && z <= 15)
                    {
                        index = (x - 32) + (z * 16) + y * 256;
                        if (voxels20[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x <= 15 && z >= 16 && z <= 31)
                    {
                        index = x + ((z - 16) * 16) + y * 256;
                        if (voxels01[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 16 && x <= 31 && z >= 16 && z <= 31)
                    {
                        index = x - 16 + ((z - 16) * 16) + y * 256;
                        if (voxels11[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 32 && z >= 16 && z <= 31)
                    {
                        index = (x - 32) + ((z - 16) * 16) + y * 256;
                        if (voxels21[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x <= 15 && z >= 32)
                    {
                        index = x + ((z - 32) * 16) + y * 256;
                        if (voxels02[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 16 && x <= 31 && z >= 32)
                    {
                        index = (x - 16) + ((z - 32) * 16) + y * 256;
                        if (voxels12[index].GetMaterial() > 0) isSolid = true;
                    }
                    else if (x >= 32 && z >= 32)
                    {
                        index = (x - 32) + ((z - 32) * 16) + y * 256;
                        if (voxels22[index].GetMaterial() > 0) isSolid = true;
                    }

                    if (y == 255)
                    {
                        this.sunLights[i] = 15;
                        this.lightQueue.Enqueue(new int3(x, y, z));
                    }
                    else
                    {
                        this.sunLights[i] = 0;
                    }

                    this.isSolid[i] = isSolid;
                    i++;
                }
            }
        }
    }

    public void UpdateChunk()
    {
        int i = 0;
        int index = 0;
        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    if (x >= 16 && x <= 31 && z >= 16 && z <= 31)
                    {
                        this.lights[index] = (byte)((this.sunLights[i] & 0b_0000_1111) << 4);
                        index++;
                    }

                    i++;
                }
            }
        }
    }
}
