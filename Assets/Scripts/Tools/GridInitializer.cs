using UnityEngine;
using System.Collections.Generic;

public static class GridInitializer
{
    public static void ApplyPreset(TileData[] gridData, GridMapPreset preset, int resolutionX, int resolutionZ)
    {
        if (preset == null) return;

        foreach (var layer in preset.Layers)
        {
            ApplyTextureLayer(gridData, layer, resolutionX, resolutionZ);
        }
    }

    private static void ApplyTextureLayer(TileData[] gridData, GridTextureLayer layer, int resX, int resZ)
    {
        if (layer.Texture == null) return;
        
        Texture2D tex = layer.Texture;
        Color32[] pixels = tex.GetPixels32(); 
        int texW = tex.width;
        int texH = tex.height;

        // -----------------------------------------------------------------------
        // PASS 1: Calculate Total Density Weights
        // -----------------------------------------------------------------------
        Dictionary<int, float> distributionSums = new Dictionary<int, float>();
        List<int> activeIndices = new List<int>();

        if (layer.DistributionMappings != null && layer.DistributionMappings.Count > 0)
        {
            for (int i = 0; i < layer.DistributionMappings.Count; i++)
            {
                if (layer.DistributionMappings[i].Enabled)
                {
                    distributionSums[i] = 0f;
                    activeIndices.Add(i);
                }
            }

            if (activeIndices.Count > 0)
            {
                for (int y = 0; y < resZ; y++)
                {
                    for (int x = 0; x < resX; x++)
                    {
                        Color32 c = SampleColor(x, y, resX, resZ, texW, texH, pixels);

                        for (int i = 0; i < activeIndices.Count; i++)
                        {
                            int index = activeIndices[i];
                            var mapping = layer.DistributionMappings[index];
                            distributionSums[index] += GetChannelValue(c, mapping.SourceChannel);
                        }
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // PASS 2: Assign Values
        // -----------------------------------------------------------------------
        for (int y = 0; y < resZ; y++)
        {
            for (int x = 0; x < resX; x++)
            {
                int gridIndex = (y * resX) + x;
                TileData data = gridData[gridIndex];
                Color32 c = SampleColor(x, y, resX, resZ, texW, texH, pixels);

                // A. Apply Linear Mappings
                if (layer.LinearMappings != null)
                {
                    foreach (var map in layer.LinearMappings)
                    {
                        if (!map.Enabled) continue;

                        byte rawVal = GetChannelValue(c, map.SourceChannel);
                        float t = rawVal / 255f;
                        float val = Mathf.Lerp(map.MinValue, map.MaxValue, t);

                        ApplyLinearValue(ref data, map.TargetField, val);
                    }
                }

                // B. Apply Distribution Mappings
                if (layer.DistributionMappings != null)
                {
                    for (int i = 0; i < layer.DistributionMappings.Count; i++)
                    {
                        var map = layer.DistributionMappings[i];
                        if (!map.Enabled) continue;
                        
                        if (distributionSums.TryGetValue(i, out float totalWeight) && totalWeight > 0)
                        {
                            byte rawVal = GetChannelValue(c, map.SourceChannel);
                            float share = rawVal / totalWeight;
                            float assignedAmount = share * map.TotalAmount;
                            
                            ApplyDistributionValue(ref data, map.TargetField, assignedAmount);
                        }
                        else
                        {
                            ApplyDistributionValue(ref data, map.TargetField, 0);
                        }
                    }
                }

                // C. Normalize Ratios
                NormalizeRatios(ref data);

                gridData[gridIndex] = data;
            }
        }
    }

    private static Color32 SampleColor(int x, int y, int resX, int resZ, int w, int h, Color32[] pixels)
    {
        float u = (x + 0.5f) / (float)resX;
        float v = (y + 0.5f) / (float)resZ;
        int tx = Mathf.Clamp(Mathf.FloorToInt(u * w), 0, w - 1);
        int ty = Mathf.Clamp(Mathf.FloorToInt(v * h), 0, h - 1);
        return pixels[ty * w + tx];
    }

    private static byte GetChannelValue(Color32 c, TextureChannel channel)
    {
        switch (channel)
        {
            case TextureChannel.Red: return c.r;
            case TextureChannel.Green: return c.g;
            case TextureChannel.Blue: return c.b;
            case TextureChannel.Alpha: return c.a;
            default: return 0;
        }
    }

    private static void ApplyLinearValue(ref TileData data, LinearGridTarget target, float val)
    {
        switch (target)
        {
            case LinearGridTarget.Traffic:          data.Traffic = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.Demand:           data.Demand = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.ResidentialRatio: data.ResidentialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.CommercialRatio:  data.CommercialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.IndustrialRatio:  data.IndustrialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.EconomicClass:    data.EcoClass = (EconomicClass)Mathf.Clamp(Mathf.RoundToInt(val), 0, 2); break;
        }
    }

    private static void ApplyDistributionValue(ref TileData data, DistributionGridTarget target, float val)
    {
        switch (target)
        {
            case DistributionGridTarget.Population: 
                data.Population = (ushort)Mathf.Clamp(val, 0, 65535); 
                break;
        }
    }

    private static void NormalizeRatios(ref TileData data)
    {
        int total = data.ResidentialRatio + data.CommercialRatio + data.IndustrialRatio;
        if (total > 0 && total != 100)
        {
            float scale = 100f / total;
            int r = Mathf.RoundToInt(data.ResidentialRatio * scale);
            int c = Mathf.RoundToInt(data.CommercialRatio * scale);
            int i = 100 - r - c;
            
            if (i < 0) 
            { 
                i = 0; 
                if (r > c) r += (100 - r - c); 
                else c += (100 - r - c); 
            }

            data.ResidentialRatio = (byte)r;
            data.CommercialRatio = (byte)c;
            data.IndustrialRatio = (byte)i;
        }
    }
}