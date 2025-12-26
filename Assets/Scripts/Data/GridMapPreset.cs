using UnityEngine;
using System.Collections.Generic;

// Enum to select which Grid Data field we are targeting
public enum GridTargetField
{
    Traffic,            // byte (0-100)
    Population,         // ushort (0-65535)
    Demand,             // byte (0-100)
    ResidentialRatio,   // byte (0-100)
    CommercialRatio,    // byte (0-100)
    IndustrialRatio,    // byte (0-100)
    EconomicClass       // Enum (0, 1, 2)
}

// Enum for Texture Channels
public enum TextureChannel
{
    Red,
    Green,
    Blue,
    Alpha
}

[System.Serializable]
public struct ChannelMapping
{
    [Tooltip("Enable to write this value to the grid.")]
    public bool Enabled;

    [Tooltip("The field in TileData to update.")]
    public GridTargetField TargetField;

    [Tooltip("Which channel of the texture to read from.")]
    public TextureChannel SourceChannel;

    [Header("Range Mapping")]
    [Tooltip("Value when the texture channel is 0.")]
    public float MinValue;

    [Tooltip("Value when the texture channel is 255 (Max).")]
    public float MaxValue;
}

[System.Serializable]
public struct GridTextureLayer
{
    [Tooltip("The texture to read data from. Ensure 'Read/Write' is enabled in Import Settings.")]
    public Texture2D Texture;

    [Tooltip("List of mappings from this texture's channels to grid data.")]
    public List<ChannelMapping> Mappings;
}

[CreateAssetMenu(fileName = "NewGridMapPreset", menuName = "Grid Map Preset")]
public class GridMapPreset : ScriptableObject
{
    [Header("Grid Initialization Layers")]
    [Tooltip("Layers are applied in order. Later layers can overwrite earlier ones.")]
    public List<GridTextureLayer> Layers;
}