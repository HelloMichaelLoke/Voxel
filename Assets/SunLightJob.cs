using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct SunLightJob : IJob
{
    public NativeArray<byte> sunLights;

    public NativeArray<sbyte> densities;
    public NativeArray<int> lights;
    public NativeQueue<int3> lightQueue;

    public NativeArray<sbyte> chunk00densities;
    public NativeArray<sbyte> chunk10densities;
    public NativeArray<sbyte> chunk20densities;
    public NativeArray<sbyte> chunk01densities;
    public NativeArray<sbyte> chunk11densities;
    public NativeArray<sbyte> chunk21densities;
    public NativeArray<sbyte> chunk02densities;
    public NativeArray<sbyte> chunk12densities;
    public NativeArray<sbyte> chunk22densities;

    public int2 chunkPosition;

    public void Execute()
    {
        int index = 0;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    sbyte density = 0;
                    int arrayPosition = 0;

                    if (x <= 15 && z <= 15)
                    {
                        arrayPosition = x + (z * 16) + y * 256;
                        density = chunk00densities[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z <= 15)
                    {
                        arrayPosition = (x - 16) + (z * 16) + y * 256;
                        density = chunk10densities[arrayPosition];
                    }
                    else if (x >= 32 && z <= 15)
                    {
                        arrayPosition = (x - 32) + (z * 16) + y * 256;
                        density = chunk20densities[arrayPosition];
                    }
                    else if (x <= 15 && z >= 16 && z <= 31)
                    {
                        arrayPosition = x + ((z - 16) * 16) + y * 256;
                        density = chunk01densities[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z >= 16 && z <= 31)
                    {
                        arrayPosition = x - 16 + ((z - 16) * 16) + y * 256;
                        density = chunk11densities[arrayPosition];
                    }
                    else if(x >= 32 && z >= 16 && z <= 31)
                    {
                        arrayPosition = (x - 32) + ((z - 16) * 16) + y * 256;
                        density = chunk21densities[arrayPosition];
                    }
                    else if (x <= 15 && z >= 32)
                    {
                        arrayPosition = x + ((z - 32) * 16) + y * 256;
                        density = chunk02densities[arrayPosition];
                    }
                    else if (x >= 16 && x <= 31 && z >= 32)
                    {
                        arrayPosition = (x - 16) + ((z - 32) * 16) + y * 256;
                        density = chunk12densities[arrayPosition];
                    }
                    else if (x >= 32 && z >= 32)
                    {
                        arrayPosition = (x - 32) + ((z - 32) * 16) + y * 256;
                        density = chunk22densities[arrayPosition];
                    }

                    if (y == 255)
                    {
                        this.lights[index] = 15;
                        this.lightQueue.Enqueue(new int3(x, y, z));
                    }
                    else
                    {
                        this.lights[index] = 0;
                    }

                    densities[index] = density;
                    index++;
                }
            }
        }

        while (this.lightQueue.Count > 0)
        {
            int3 position = this.lightQueue.Dequeue();

            index = position.x + position.z * 48 + position.y * 2304;
            int lightValue = lights[index];

            // -X Voxel (Left)
            if (position.x > 0)
            {
                int indexLeft = index - 1;
                if (densities[indexLeft] >= 0)
                {
                    int lightValueLeft = lights[indexLeft];

                    if (lightValue - lightValueLeft > 1)
                    {
                        lightValueLeft = lightValue - 1;
                        lights[indexLeft] = lightValueLeft;

                        if (lightValueLeft > 1)
                            this.lightQueue.Enqueue(position + new int3(-1, 0, 0));
                    }
                }
            }

            // +X Voxel (Right)
            if (position.x < 48 - 1)
            {
                int indexRight = index + 1;
                if (densities[indexRight] >= 0)
                {
                    int lightValueRight = lights[indexRight];

                    if (lightValue - lightValueRight > 1)
                    {
                        lightValueRight = lightValue - 1;
                        lights[indexRight] = lightValueRight;

                        if (lightValueRight > 1)
                            this.lightQueue.Enqueue(position + new int3(1, 0, 0));
                    }
                }
            }

            // -Z Voxel (Front)
            if (position.z > 0)
            {
                int indexFront = index - 48;
                if (densities[indexFront] >= 0)
                {
                    int lightValueFront = lights[indexFront];

                    if (lightValue - lightValueFront > 1)
                    {
                        lightValueFront = lightValue - 1;
                        lights[indexFront] = lightValueFront;

                        if (lightValueFront > 1)
                            this.lightQueue.Enqueue(position + new int3(0, 0, -1));
                    }
                } 
            }

            // +Z Voxel (Back)
            if (position.z < 48 - 1)
            {
                int indexBack = index + 48;
                if (densities[indexBack] >= 0)
                {
                    int lightValueBack = lights[indexBack];

                    if (lightValue - lightValueBack > 1)
                    {
                        lightValueBack = lightValue - 1;
                        lights[indexBack] = lightValueBack;

                        if (lightValueBack > 1)
                            this.lightQueue.Enqueue(position + new int3(0, 0, 1));
                    }
                } 
            }

            // -Y Voxel (Bottom)
            if (position.y > 0)
            {
                int indexBottom = index - 2304;
                if (densities[indexBottom] >= 0)
                {
                    int lightValueBottom = lights[indexBottom];

                    if (lightValue == 15)
                    {
                        this.lights[indexBottom] = 15;
                        this.lightQueue.Enqueue(position + new int3(0, -1, 0));
                    }
                    else if (lightValue - lightValueBottom > 1)
                    {
                        lightValueBottom = lightValue - 1;
                        lights[indexBottom] = lightValueBottom;

                        if (lightValueBottom > 1)
                            this.lightQueue.Enqueue(position + new int3(0, -1, 0));
                    }
                }
            }

            // +Y Voxel (Top)
            if (position.y < 256 - 1)
            {
                int indexTop = index + 2304;
                if (densities[indexTop] >= 0)
                {
                    int lightValueTop = lights[indexTop];
                    if (lightValue - lightValueTop > 1)
                    {
                        lightValueTop = lightValue - 1;
                        lights[indexTop] = lightValueTop;

                        if (lightValueTop > 1)
                            this.lightQueue.Enqueue(position + new int3(0, 1, 0));
                    }
                }
            }
        }

        index = 0;
        int finalIndex = 0;
        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 47; z++)
            {
                for (int x = 0; x <= 47; x++)
                {
                    if (x >= 16 && x <= 31 && z >= 16 && z <= 31)
                    {
                        this.sunLights[finalIndex] = (byte)lights[index];
                        finalIndex++;
                    }

                    index++;
                }
            }
        }
    }
}
