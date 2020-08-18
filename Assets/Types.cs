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
    private byte right;  // +X
    private byte left;   // -X
    private byte top;    // +Y
    private byte bottom; // -Y
    private byte front;  // +Z
    private byte back;   // -Z
    private byte material;

    public Voxel(byte right, byte left, byte top, byte bottom, byte front, byte back, byte material)
    {
        this.right = right;
        this.left = left;
        this.top = top;
        this.bottom = bottom;
        this.front = front;
        this.back = back;
        this.material = material;
    }

    // Set Voxel

    public void SetVoxel(byte right, byte left, byte top, byte bottom, byte front, byte back, byte material)
    {
        this.right = right;
        this.left = left;
        this.top = top;
        this.bottom = bottom;
        this.front = front;
        this.back = back;
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
    // Set Directions
    //

    public void SetRight(float right)
    {
        this.right = (byte)(math.clamp(right, 0.0f, 1.0f) * 255.0f);
    }

    public void SetLeft(float left)
    {
        this.left = (byte)(math.clamp(left, 0.0f, 1.0f) * 255.0f);
    }

    public void SetTop(float top)
    {
        this.top = (byte)(math.clamp(top, 0.0f, 1.0f) * 255.0f);
    }

    public void SetBottom(float bottom)
    {
        this.bottom = (byte)(math.clamp(bottom, 0.0f, 1.0f) * 255.0f);
    }

    public void SetFront(float front)
    {
        this.front = (byte)(math.clamp(front, 0.0f, 1.0f) * 255.0f);
    }

    public void SetBack(float back)
    {
        this.back = (byte)(math.clamp(back, 0.0f, 1.0f) * 255.0f);
    }

    //
    // Get Directions
    //

    public float GetRight()
    {
        return (float)right / 255.0f;
    }

    public float GetLeft()
    {
        return (float)left / 255.0f;
    }

    public float GetTop()
    {
        return (float)top / 255.0f;
    }

    public float GetBottom()
    {
        return (float)bottom / 255.0f;
    }

    public float GetFront()
    {
        return (float)front / 255.0f;
    }

    public float GetBack()
    {
        return (float)back / 255.0f;
    }

    //
    // Helpers
    //

    public bool IsDensityEqualTo(Voxel otherVoxel)
    {
        bool isEqual = true;

        if (otherVoxel.GetRight() != this.GetRight()) isEqual = false;
        if (otherVoxel.GetLeft() != this.GetLeft()) isEqual = false;
        if (otherVoxel.GetTop() != this.GetTop()) isEqual = false;
        if (otherVoxel.GetBottom() != this.GetBottom()) isEqual = false;
        if (otherVoxel.GetFront() != this.GetFront()) isEqual = false;
        if (otherVoxel.GetBack() != this.GetBack()) isEqual = false;

        return isEqual;
    }
}