using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

//[BurstCompile(CompileSynchronously = true)]
public struct MeshTerrainJob : IJob
{
    // Temporary Data
    public NativeArray<Voxel> voxelsMerged;
    public NativeArray<byte> lightsMerged;

    // Chunk Data
    public int2 chunkPosition;

    // Terrain Data
    public NativeArray<Voxel>   voxels00;
    public NativeArray<byte>    lights00;
    public NativeArray<Voxel>   voxels10;
    public NativeArray<byte>    lights10;
    public NativeArray<Voxel>   voxels20;
    public NativeArray<byte>    lights20;
    public NativeArray<Voxel>   voxels01;
    public NativeArray<byte>    lights01;
    public NativeArray<Voxel>   voxels11;
    public NativeArray<byte>    lights11;
    public NativeArray<Voxel>   voxels21;
    public NativeArray<byte>    lights21;
    public NativeArray<Voxel>   voxels02;
    public NativeArray<byte>    lights02;
    public NativeArray<Voxel>   voxels12;
    public NativeArray<byte>    lights12;
    public NativeArray<Voxel>   voxels22;
    public NativeArray<byte>    lights22;

    // Mesh Data
    public NativeHashMap<Vector3, Vector3> vertexNormals;
    public NativeList<Vector3> vertices;
    public NativeList<Vector3> normals;
    public NativeList<int> indices;
    public NativeList<Vector2> lights;
    public NativeList<Vector4> mats1234;
    public NativeList<Vector4> mats5678;
    public NativeList<Vector4> weights1234;
    public NativeList<Vector4> weights5678;

    // Marching Cubes Data
    public NativeArray<float3> mcCornerPositions;
    public NativeArray<byte> mcCellClasses;
    public NativeArray<int> mcCellGeometryCounts;
    public NativeArray<int> mcCellIndices;
    public NativeArray<ushort> mcCellVertexData;

    // Temporary Variables
    public NativeArray<float3> cornerPositions;
    public NativeArray<Voxel> cornerVoxels;
    public NativeArray<float2> cornerLights;
    public NativeArray<int> cornerIndices;
    public NativeList<int> cellIndices;
    public NativeList<ushort> mappedIndices;

    // Physics Mesh Breakpoints (vertex array position, index array position)
    public NativeList<int2> breakPoints;

    public void Execute()
    {
        this.MergeChunks();
        this.MeshCells();
    }

    private void MergeChunks()
    {
        int i = 0;
        int index;

        for (int y = 0; y <= 255; y++)
        {
            for (int z = 0; z <= 18; z++)
            {
                for (int x = 0; x <= 18; x++)
                {
                    Voxel voxel = new Voxel();
                    byte light = 0;

                    if (x == 0 && z == 0)
                    {
                        index = 255 + y * 256;
                        voxel = this.voxels00[index];
                        light = this.lights00[index];
                    }
                    else if (x == 0 && z >= 1 && z <= 16)
                    {
                        index = 15 + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels01[index];
                        light = this.lights01[index];
                    }
                    else if (x == 0 && z >= 17)
                    {
                        index = 15 + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels02[index];
                        light = this.lights02[index];
                    }
                    else if (x >= 1 && x <= 16 && z == 0)
                    {
                        index = 240 + (x - 1) + y * 256;
                        voxel = this.voxels10[index];
                        light = this.lights10[index];
                    }
                    else if (x >= 1 && x <= 16 && z >= 1 && z <= 16)
                    {
                        index = 0 + (x - 1) + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels11[index];
                        light = this.lights11[index];
                    }
                    else if (x >= 1 && x <= 16 && z >= 17)
                    {
                        index = 0 + (x - 1) + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels12[index];
                        light = this.lights12[index];
                    }
                    else if (x >= 17 && z == 0)
                    {
                        index = 240 + (x - 17) + y * 256;
                        voxel = this.voxels20[index];
                        light = this.lights20[index];
                    }
                    else if (x >= 17 && z >= 1 && z <= 16)
                    {
                        index = 0 + (x - 17) + ((z - 1) * 16) + y * 256;
                        voxel = this.voxels21[index];
                        light = this.lights21[index];
                    }
                    else if (x >= 17 && z >= 17)
                    {
                        index = 0 + (x - 17) + ((z - 17) * 16) + y * 256;
                        voxel = this.voxels22[index];
                        light = this.lights22[index];
                    }

                    this.voxelsMerged[i] = voxel;
                    this.lightsMerged[i] = light;

                    i++;
                }
            }
        }
    }

    private void MeshCells()
    {
        for (int y = 1; y <= 253; y++)
        {
            for (int z = 1; z <= 16; z++)
            {
                for (int x = 1; x <= 16; x++)
                {
                    this.MeshCell(x, y, z);
                }
            }
        }
    }

    private void MeshCell(int x, int y, int z)
    {
        if (x == 1 && z == 1 && y % 16 == 1)
        {
            // TODO | Check if breakpoint was affected to avoid useless collider updates
            this.breakPoints.Add(new int2(this.vertices.Length, this.indices.Length));
        }

        bool isEmpty = true;
        bool isFull = true;

        for (int i = 0; i < 8; i++)
        {
            this.cornerPositions[i] = this.mcCornerPositions[i] + new float3(x, y, z);
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerIndices[i] = index;
            this.cornerVoxels[i] = this.voxelsMerged[index];

            if (this.cornerVoxels[i].GetMaterial() > 0)
            {
                isEmpty = false;
            }
            else
            {
                isFull = false;
            }
        }

        if (isEmpty || isFull)
        {
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            int index = (int)(this.cornerPositions[i].x + this.cornerPositions[i].z * 19.0f + this.cornerPositions[i].y * 361.0f);
            this.cornerLights[i] = this.GetLight(index);
        }

        float matId1 = (float)this.cornerVoxels[0].GetMaterial();
        float matId2 = (float)this.cornerVoxels[1].GetMaterial();
        float matId3 = (float)this.cornerVoxels[2].GetMaterial();
        float matId4 = (float)this.cornerVoxels[3].GetMaterial();
        float matId5 = (float)this.cornerVoxels[4].GetMaterial();
        float matId6 = (float)this.cornerVoxels[5].GetMaterial();
        float matId7 = (float)this.cornerVoxels[6].GetMaterial();
        float matId8 = (float)this.cornerVoxels[7].GetMaterial();

        float4 matIds1234 = new float4(matId1 - 1.0f, matId2 - 1.0f, matId3 - 1.0f, matId4 - 1.0f);
        float4 matIds5678 = new float4(matId5 - 1.0f, matId6 - 1.0f, matId7 - 1.0f, matId8 - 1.0f);

        byte caseIndex = 0;

        if (matId1 > 0)
        {
            caseIndex |= 0b_0000_0001;
        }
        if (matId2 > 0)
        {
            caseIndex |= 0b_0000_0010;
        }
        if (matId3 > 0)
        {
            caseIndex |= 0b_0000_0100;
        }
        if (matId4 > 0)
        {
            caseIndex |= 0b_0000_1000;
        }
        if (matId5 > 0)
        {
            caseIndex |= 0b_0001_0000;
        }
        if (matId6 > 0)
        {
            caseIndex |= 0b_0010_0000;
        }
        if (matId7 > 0)
        {
            caseIndex |= 0b_0100_0000;
        }
        if (matId8 > 0)
        {
            caseIndex |= 0b_1000_0000;
        }

        byte cellClass = mcCellClasses[caseIndex];
        int geometryCounts = mcCellGeometryCounts[cellClass];
        int vertexCount = geometryCounts >> 4;
        int triangleCount = geometryCounts & 0x0F;

        this.cellIndices.Clear();
        int cellIndicesArrayPosition = cellClass * 15;
        int countIndices = 0;
        while (this.mcCellIndices[cellIndicesArrayPosition] != -1)
        {
            this.cellIndices.Add(this.mcCellIndices[cellIndicesArrayPosition]);
            cellIndicesArrayPosition++;
            countIndices++;

            if (countIndices == 15)
                break;
        }

        this.mappedIndices.Clear();
        float4 weights1234 = float4.zero;
        float4 weights5678 = float4.zero;

        for (int i = 0; i < vertexCount; i++)
        {
            ushort edgeCode = this.mcCellVertexData[caseIndex * 12 + i];

            ushort cornerIndex0 = (byte)((edgeCode >> 4) & 0x0F);
            ushort cornerIndex1 = (byte)((edgeCode) & 0x0F);

            float3 position0 = this.cornerPositions[cornerIndex0];
            float3 position1 = this.cornerPositions[cornerIndex1];
            Voxel voxel0 = this.cornerVoxels[cornerIndex0];
            Voxel voxel1 = this.cornerVoxels[cornerIndex1];
            bool solid0 = (this.cornerVoxels[cornerIndex0].GetMaterial() > 0);
            bool solid1 = (this.cornerVoxels[cornerIndex1].GetMaterial() > 0);
            float sunLight0 = this.cornerLights[cornerIndex0].x;
            float sunLight1 = this.cornerLights[cornerIndex1].x;
            float sourceLight0 = this.cornerLights[cornerIndex0].y;
            float sourceLight1 = this.cornerLights[cornerIndex1].y;
            int index0 = this.cornerIndices[cornerIndex0];
            int index1 = this.cornerIndices[cornerIndex1];

            float weight0 = 0.0f;
            float weight1 = 0.0f;
            float materialId = 0.0f;

            if (solid0)
            {
                materialId = voxel0.GetMaterial();

                if (position0.x < position1.x) weight1 = voxel0.GetRight();
                else if (position0.x > position1.x) weight1 = voxel0.GetLeft();
                else if (position0.y < position1.y) weight1 = voxel0.GetTop();
                else if (position0.y > position1.y) weight1 = voxel0.GetBottom();
                else if (position0.z < position1.z) weight1 = voxel0.GetFront();
                else if (position0.z > position1.z) weight1 = voxel0.GetBack();

                weight0 = 1.0f - weight1;
            }
            else
            {
                materialId = voxel1.GetMaterial();

                if (position0.x < position1.x) weight0 = voxel1.GetLeft();
                else if (position0.x > position1.x) weight0 = voxel1.GetRight();
                else if (position0.y < position1.y) weight0 = voxel1.GetBottom();
                else if (position0.y > position1.y) weight0 = voxel1.GetTop();
                else if (position0.z < position1.z) weight0 = voxel1.GetBack();
                else if (position0.z > position1.z) weight0 = voxel1.GetFront();

                weight1 = 1.0f - weight0;
            }

            float3 vertex;
            vertex = weight0 * position0 + weight1 * position1;

            float sunLight = 0.0f;

            if (solid0)
            {
                sunLight = sunLight1 - weight0;
            }
            else
            {
                sunLight = sunLight0 - weight1;
            }

            // TODO
            float sourceLight = 0.0f;

            //
            // Normal
            //

            float3 normal = new float3(0.0f, 0.0f, 0.0f);

            float3 right = new float3(1.0f, 0.0f, 0.0f);
            float3 left = new float3(-1.0f, 0.0f, 0.0f);
            float3 top = new float3(0.0f, 1.0f, 0.0f);
            float3 bottom = new float3(0.0f, -1.0f, 0.0f);
            float3 front = new float3(0.0f, 0.0f, 1.0f);
            float3 back = new float3(0.0f, 0.0f, -1.0f);

            Voxel voxel0Right = this.voxelsMerged[index0 + 1];
            Voxel voxel0Left = this.voxelsMerged[index0 - 1];
            Voxel voxel0Top = this.voxelsMerged[index0 + 361];
            Voxel voxel0Bottom = this.voxelsMerged[index0 - 361];
            Voxel voxel0Front = this.voxelsMerged[index0 + 19];
            Voxel voxel0Back = this.voxelsMerged[index0 - 19];

            Voxel voxel0LeftTop = this.voxelsMerged[index0 - 1 + 361];
            Voxel voxel0RightTop = this.voxelsMerged[index0 + 1 + 361];
            Voxel voxel0FrontTop = this.voxelsMerged[index0 + 19 + 361];
            Voxel voxel0BackTop = this.voxelsMerged[index0 - 19 + 361];
            Voxel voxel0LeftBottom = this.voxelsMerged[index0 - 1 - 361];
            Voxel voxel0RightBottom = this.voxelsMerged[index0 + 1 - 361];
            Voxel voxel0FrontBottom = this.voxelsMerged[index0 + 19 - 361];
            Voxel voxel0BackBottom = this.voxelsMerged[index0 - 19 - 361];

            Voxel voxel1Right = this.voxelsMerged[index1 + 1];
            Voxel voxel1Left = this.voxelsMerged[index1 - 1];
            Voxel voxel1Top = this.voxelsMerged[index1 + 361];
            Voxel voxel1Bottom = this.voxelsMerged[index1 - 361];
            Voxel voxel1Front = this.voxelsMerged[index1 + 19];
            Voxel voxel1Back = this.voxelsMerged[index1 - 19];

            bool isTowardsRight = (position0.x != position1.x);
            bool isTowardsTop = (position0.y != position1.y);
            bool isTowardsFront = (position0.z != position1.z);

            if (isTowardsTop)
            {
                if (voxel0.IsSolid())
                {
                    // Right
                    if (voxel0Right.IsSolid())
                    {
                        if (!voxel0RightTop.IsSolid())
                            normal += math.normalize(top + (voxel0.GetTop() - voxel0Right.GetTop()) * right);
                        else
                            normal += math.normalize(voxel0RightTop.GetLeft() * left + voxel0.GetTop() * top);
                    }
                    else
                    {
                        normal += math.normalize(voxel0.GetRight() * top + voxel0.GetTop() * right);
                    }

                    // Left
                    if (voxel0Left.IsSolid())
                    {
                        if (!voxel0LeftTop.IsSolid())
                            normal += math.normalize(top + (voxel0.GetTop() - voxel0Left.GetTop()) * left);
                        else
                            normal += math.normalize(voxel0LeftTop.GetRight() * right + voxel0.GetTop() * top);
                    }
                    else
                    {
                        normal += math.normalize(voxel0.GetLeft() * top + voxel0.GetTop() * left);
                    }

                    // Front
                    if (voxel0Front.IsSolid())
                    {
                        if (!voxel0FrontTop.IsSolid())
                            normal += math.normalize(top + (voxel0.GetTop() - voxel0Front.GetTop()) * front);
                        else
                            normal += math.normalize(voxel0FrontTop.GetBack() * back + voxel0.GetTop() * top);
                    }
                    else
                    {
                        normal += math.normalize(voxel0.GetFront() * top + voxel0.GetTop() * front);
                    }

                    // Back
                    if (voxel0Back.IsSolid())
                    {
                        if (!voxel0BackTop.IsSolid())
                            normal += math.normalize(top + (voxel0.GetTop() - voxel0Back.GetTop()) * back);
                        else
                            normal += math.normalize(voxel0BackTop.GetFront() * front + voxel0.GetTop() * top);
                    }
                    else
                    {
                        normal += math.normalize(voxel0.GetBack() * top + voxel0.GetTop() * back);
                    }
                }
                
                //if (voxel1.IsSolid())
                //{
                //    // Bottom Right | Not Solid
                //    if (!voxel1Right.IsSolid()) normal += math.normalize(voxel1.GetRight() * bottom + voxel1.GetBottom() * right);
                //    // Bottom Left | Not Solid
                //    if (!voxel1Left.IsSolid()) normal += math.normalize(voxel1.GetLeft() * bottom + voxel1.GetBottom() * left);
                //    // Bottom Front | Not Solid
                //    if (!voxel1Front.IsSolid()) normal += math.normalize(voxel1.GetFront() * bottom + voxel1.GetBottom() * front);
                //    // Bottom Back | Not Solid
                //    if (!voxel1Back.IsSolid()) normal += math.normalize(voxel1.GetBack() * bottom + voxel1.GetBottom() * back);
                //}
            }

            //if (voxel0.IsSolid())
            //{
            //    if (isTowardsTop)
            //    {
            //        if (!voxel0Right.IsSolid() && !voxel0Left.IsSolid())
            //        {
            //            float3 directionRight = voxel0.GetRight() * top + voxel0.GetTop() * right;
            //            float3 directionLeft = voxel0.GetLeft() * top + voxel0.GetTop() * left;
            //            normal += directionRight + directionLeft;
            //        }

            //        if (!voxel0Front.IsSolid() && !voxel0Back.IsSolid())
            //        {
            //            float3 directionFront = voxel0.GetFront() * top + voxel0.GetTop() * front;
            //            float3 directionBack = voxel0.GetBack() * top + voxel0.GetTop() * back;
            //            normal += directionFront + directionBack;
            //        }
            //    }

            //    if (isTowardsRight)
            //    {
            //        if (!voxel0Top.IsSolid() && !voxel0Bottom.IsSolid())
            //        {
            //            float3 directionTop = voxel0.GetTop() * right + voxel0.GetRight() * top;
            //            float3 directionBottom = voxel0.GetBottom() * right + voxel0.GetRight() * bottom;
            //            normal += directionTop + directionBottom;
            //        }
            //    }

            //    if (isTowardsFront)
            //    {
            //        if (!voxel0Top.IsSolid() && !voxel0Bottom.IsSolid())
            //        {
            //            float3 directionTop = voxel0.GetTop() * front + voxel0.GetFront() * top;
            //            float3 directionBottom = voxel0.GetBottom() * back + voxel0.GetBack() * bottom;
            //            normal += directionTop + directionBottom;
            //        }
            //    }
            //}

            //
            // Set Mesh Data
            //

            this.lights.Add(new float2(sunLight / 15.0f, sourceLight / 15.0f));
            this.vertices.Add(vertex - new float3(1.0f, 0.0f, 1.0f));
            this.normals.Add(math.normalize(normal));
            this.mats1234.Add(matIds1234);
            this.mats5678.Add(matIds5678);

            bool isMaterialSet = false;
            if (matId1 == materialId) { weights1234.x = 1.0f; isMaterialSet = true; } else weights1234.x = 0.0f;
            if (matId2 == materialId && !isMaterialSet) { weights1234.y = 1.0f; isMaterialSet = true; } else weights1234.y = 0.0f;
            if (matId3 == materialId && !isMaterialSet) { weights1234.z = 1.0f; isMaterialSet = true; } else weights1234.z = 0.0f;
            if (matId4 == materialId && !isMaterialSet) { weights1234.w = 1.0f; isMaterialSet = true; } else weights1234.w = 0.0f;
            if (matId5 == materialId && !isMaterialSet) { weights5678.x = 1.0f; isMaterialSet = true; } else weights5678.x = 0.0f;
            if (matId6 == materialId && !isMaterialSet) { weights5678.y = 1.0f; isMaterialSet = true; } else weights5678.y = 0.0f;
            if (matId7 == materialId && !isMaterialSet) { weights5678.z = 1.0f; isMaterialSet = true; } else weights5678.z = 0.0f;
            if (matId8 == materialId && !isMaterialSet) { weights5678.w = 1.0f; isMaterialSet = true; } else weights5678.w = 0.0f;

            this.weights1234.Add(weights1234);
            this.weights5678.Add(weights5678);

            mappedIndices.Add((ushort)(this.vertices.Length - 1));
        }

        for (int i = 0; i < triangleCount; i++)
        {
            int tm = i * 3;
            this.indices.Add(mappedIndices[cellIndices[tm + 2]]);
            this.indices.Add(mappedIndices[cellIndices[tm + 1]]);
            this.indices.Add(mappedIndices[cellIndices[tm]]);
        }
    }

    private float2 GetLight(int index)
    {
        float sunLight = 0.0f;
        sunLight = (float)((this.lightsMerged[index] >> 4) & 0xF);

        float sourceLight = 0.0f;
        sourceLight = (float)(this.lightsMerged[index] & 0xF);

        return new float2(sunLight, sourceLight);
    }
}
