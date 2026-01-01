using UnityEngine;
using System.Collections.Generic;

// Targets that rely on simple 0-1 normalization (Min/Max)
public enum LinearGridTarget
{
    Traffic,            // byte (0-100)
    ResidentialRatio,   // byte (0-100)
    CommercialRatio,    // byte (0-100)
    IndustrialRatio,    // byte (0-100)
    EconomicClass       // Enum (0, 1, 2)
}

// Targets that rely on density distribution (Total Amount)
public enum DistributionGridTarget
{
    Population,
    Jobs 
}

public enum TextureChannel
{
    Red,
    Green,
    Blue,
    Alpha
}

[System.Serializable]
public struct LinearChannelMapping
{
    public bool Enabled;
    public LinearGridTarget TargetField;
    public TextureChannel SourceChannel;

    [Tooltip("Value when pixel is black (0).")]
    public float MinValue;
    [Tooltip("Value when pixel is white (255).")]
    public float MaxValue;
}

[System.Serializable]
public struct DistributionChannelMapping
{
    public bool Enabled;
    public DistributionGridTarget TargetField;
    public TextureChannel SourceChannel;

    [Tooltip("The total sum of this value across the ENTIRE grid.")]
    public int TotalAmount;
}

[System.Serializable]
public struct GridTextureLayer
{
    [Tooltip("Input Texture. Read/Write must be enabled.")]
    public Texture2D Texture;

    [Header("Linear Mappings (Intensity -> Value)")]
    public List<LinearChannelMapping> LinearMappings;

    [Header("Distribution Mappings (Density -> Share of Total)")]
    public List<DistributionChannelMapping> DistributionMappings;
}

[CreateAssetMenu(fileName = "NewGridMapPreset", menuName = "Grid Map Preset")]
public class GridMapPreset : ScriptableObject
{
    [Header("Composition Layers")]
    public List<GridTextureLayer> Layers;
}