using System;
using System.Collections.Generic;

namespace TuneLab.Extensions.Formats.DataInfo;

/// <summary>
/// Plugin information for serialization
/// </summary>
public class TrackPluginInfo
{
    /// <summary>
    /// Plugin unique identifier (used to find and load the plugin)
    /// </summary>
    public string PluginUid { get; set; } = string.Empty;
    
    /// <summary>
    /// Plugin file path (fallback if UID lookup fails)
    /// </summary>
    public string PluginPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Plugin display name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the plugin is bypassed
    /// </summary>
    public bool Bypassed { get; set; } = false;
    
    /// <summary>
    /// Plugin state data (Base64 encoded)
    /// </summary>
    public string StateData { get; set; } = string.Empty;
}
