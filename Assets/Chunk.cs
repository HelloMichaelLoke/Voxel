using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

public class Chunk
{
    private Voxel[] voxels;
    private byte[] lights;

    public bool hasVoxels = false;
    public bool hasLights = false;
    public bool hasObjects = false;

    public Chunk()
    {
        this.voxels = new Voxel[65536];
        this.lights = new byte[65536];
    }

    //
    // Set Voxels
    //

    public void SetVoxels(Voxel[] voxels)
    {
        this.voxels = voxels;
    }

    public void SetVoxelsFromNative(NativeArray<Voxel> voxels)
    {
        voxels.CopyTo(this.voxels);
    }

    public void SetVoxel(int index, Voxel voxel)
    {
        this.voxels[index] = voxel;
    }

    //
    // Get Voxels
    //

    public Voxel[] GetVoxels()
    {
        return this.voxels;
    }

    public Voxel GetVoxel(int index)
    {
        return this.voxels[index];
    }

    //
    // Set Lights
    //

    public void SetLights(byte[] lights)
    {
        this.lights = lights;
    }

    public void SetLightsFromNative(NativeArray<byte> lights)
    {
        lights.CopyTo(this.lights);
    }

    public void SetLight(int index, byte light)
    {
        this.lights[index] = light;
    }

    //
    // Get Lights
    //

    public byte[] GetLights()
    {
        return this.lights;
    }

    public byte GetLight(int index)
    {
        return this.lights[index];
    }

    //
    // Set Sun Lights
    //

    public void SetSunLight(int index, byte sunLight)
    {
        this.lights[index] = (byte)((this.lights[index] & 0b_0000_1111) | ((sunLight & 0b_0000_1111) << 4));
    }

    //
    // Get Sun Lights
    //

    public byte GetSunLight(int index)
    {
        return (byte)(this.lights[index] >> 4);
    }

    //
    // Set Source Lights
    //

    public void SetSourceLight(int index, byte sourceLight)
    {
        this.lights[index] = (byte)((this.lights[index] & 0b_1111_0000) | (sourceLight & 0b_0000_1111));
    }

    //
    // Get Source Lights
    //

    public byte GetSourceLight(int index)
    {
        return (byte)(this.lights[index] & 0b_0000_1111);
    }
}