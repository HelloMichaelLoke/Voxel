using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

public class Chunk
{
    private sbyte[] densities;
    private byte[] lights;
    private byte[] materials;

    public bool areDensitiesDone = false;
    public bool areMaterialsDone = false;
    public bool areLightsDone = false;
    public bool areMeshesDone = false;

    public Chunk()
    {
        this.densities = new sbyte[65536];
        this.materials = new byte[65536];
        this.lights = new byte[65536];
    }

    //
    // Densities
    //

    public sbyte[] GetDensities()
    {
        return this.densities;
    }

    public void SetDensities(sbyte[] densities)
    {
        this.densities = densities;
    }

    public void SetDensitiesFromNative(NativeArray<sbyte> densities)
    {
        densities.CopyTo(this.densities);
    }

    public sbyte GetDensity(int arrayPosition)
    {
        return this.densities[arrayPosition];
    }

    public void SetDensity(int arrayPosition, sbyte density)
    {
        this.densities[arrayPosition] = density;
    }

    //
    // Materials
    //

    public byte[] GetMaterials()
    {
        return this.materials;
    }

    public void SetMaterials(byte[] materials)
    {
        this.materials = materials;
    }

    public void SetMaterialsFromNative(NativeArray<byte> materials)
    {
        materials.CopyTo(this.materials);
    }

    public byte GetMaterial(int arrayPosition)
    {
        return this.materials[arrayPosition];
    }

    public void SetMaterial(int arrayPosition, byte material)
    {
        this.materials[arrayPosition] = material;
    }

    //
    // Lights
    //

    public byte[] GetLights()
    {
        return this.lights;
    }

    //
    // Sun Lights
    //

    public byte[] GetSunLights()
    {
        byte[] sunLights = new byte[65536];
        for (int i = 0; i < 65536; i++)
        {
            sunLights[i] = this.GetSunLight(i);
        }
        return sunLights;
    }

    public void SetSunLights(byte[] sunLights)
    {
        for (int i = 0; i < 65536; i++)
        {
            this.SetSunLight(i, sunLights[i]);
        }
    }

    public void SetSunLightsFromNative(NativeArray<byte> sunLights)
    {
        for (int i = 0; i < 65536; i++)
        {
            this.SetSunLight(i, sunLights[i]);
        }
    }

    public byte GetSunLight(int arrayPosition)
    {
        return (byte)((lights[arrayPosition] >> 4) & 0xF);
    }

    public void SetSunLight(int arrayPosition, byte lightValue)
    {
        lights[arrayPosition] = (byte)((lights[arrayPosition] & 0xF) | (lightValue << 4));
    }

    //
    // Source Lights
    //

    public byte[] GetSourceLights()
    {
        byte[] sourceLights = new byte[65536];
        for (int i = 0; i < 65536; i++)
        {
            sourceLights[i] = this.GetSourceLight(i);
        }
        return sourceLights;
    }

    public void SetSourceLights(byte[] sourceLights)
    {
        for (int i = 0; i < 65536; i++)
        {
            this.SetSourceLight(i, sourceLights[i]);
        }
    }

    public byte GetSourceLight(int arrayPosition)
    {
        return (byte)(lights[arrayPosition] & 0xF);
    }

    public void SetSourceLight(int arrayPosition, byte lightValue)
    {
        lights[arrayPosition] = (byte)((lights[arrayPosition] & 0xF) | lightValue);
    }
}