﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TerrainMaterial : byte
{
    Dirt,
    Grass,
    Stone,
    Sand,
    Snow,

    Gravel,

    Ore_Copper,
    Ore_Iron,
    Ore_Coal,
    Ore_Silver,
    Ore_Gold,

    Soil_Peat,
    Soil_Loam,
    Soil_Sand,
    Soil_Chalk,
    Soil_Clay,
    Soil_Silt,
    Soil_Saline,

    Stone_Lime,
    Stone_Marble,
    Stone_Slate,
    Stone_Granite,

    Air = 255
}

public struct TerrainMaterialDefition
{
    public TerrainMaterial terrainMaterial;
    public float minHeight;
    public float maxHeight;
    public float spawnRate;
}

public struct Biome
{
    public float minHeight;
    public float maxHeight;
    public TerrainMaterialDefition[] terrainMaterialDefinitions;
}