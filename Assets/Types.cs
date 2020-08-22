using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct EditPosition
{
    public Vector3 worldPosition;
    public Vector3Int roundedPosition;
    public Vector3Int relativePosition;
    public Vector2Int chunkPosition;
    public int index;

    public EditPosition(Vector3 worldPosition, Vector3Int roundedPosition, Vector3Int relativePosition, Vector2Int chunkPosition, int index)
    {
        this.worldPosition = worldPosition;
        this.roundedPosition = roundedPosition;
        this.relativePosition = relativePosition;
        this.chunkPosition = chunkPosition;
        this.index = index;
    }
}

public struct VoxelChange
{
    public Vector2Int chunkPosition;
    public int index;
    public Voxel oldVoxel;
    public Voxel newVoxel;

    public VoxelChange(Vector2Int chunkPosition, int index, Voxel oldVoxel, Voxel newVoxel)
    {
        this.chunkPosition = chunkPosition;
        this.index = index;
        this.oldVoxel = oldVoxel;
        this.newVoxel = newVoxel;
    }
}

public struct WorldEditData
{
    public EditPosition editPosition;
    public List<VoxelChange> voxelChanges;

    public WorldEditData(EditPosition editPosition, List<VoxelChange> voxelChanges)
    {
        this.editPosition = editPosition;
        this.voxelChanges = voxelChanges;
    }
}

public struct Voxel
{
    private sbyte density;
    private byte material;

    public Voxel(sbyte density, byte material)
    {
        this.density = density;
        this.material = material;
    }

    // Set Voxel

    public void SetVoxel(sbyte density, byte material)
    {
        this.density = density;
        this.material = material;
    }

    //
    // Set & Get Material
    //

    public void SetMaterial(byte material)
    {
        this.material = material;
    }

    public byte GetMaterial()
    {
        return this.material;
    }

    //
    // Set & Get Density
    //

    public void SetDensity(sbyte density)
    {
        this.density = density;
    }

    public sbyte GetDensity()
    {
        return this.density;
    }

    //
    // Helpers
    //

    public bool IsSolid()
    {
        return (this.density < 0);
    }
}