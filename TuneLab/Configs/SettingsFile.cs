using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Configs;

internal class SettingsFile
{
    public string Language { get; set; } = string.Empty;
    public double MasterGain { get; set; } = 0;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundImageOpacity { get; set; } = 0.5;
    public double ParameterBoundaryExtension { get; set; } = 5;
    public string PianoKeySamplesPath { get; set; } = string.Empty;
    public int AutoSaveInterval { get; set; } = 10;
    public int BufferSize { get; set; } = 1024;
    public int SampleRate { get; set; } = 44100;
    public string AudioDriver { get; set; } = string.Empty;
    public string AudioDevice { get; set; } = string.Empty;
    public double TrackHueChangeRate { get; set; } = 0;
    public bool IsAutoDownloadUpdate { get; set; } = true;
}
