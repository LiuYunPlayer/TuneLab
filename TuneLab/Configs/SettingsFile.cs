using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Configs;

internal class SettingsFile
{
    public string Language { get; set; } = string.Empty;
    public string AutoScrollTarget { get; set; } = "None";
    public int MainWindowX { get; set; } = int.MinValue;
    public int MainWindowY { get; set; } = int.MinValue;
    public double MainWindowWidth { get; set; } = 1000;
    public double MainWindowHeight { get; set; } = 768;
    public bool MainWindowMaximized { get; set; } = false;
    public double TrackWindowHeight { get; set; } = 240;
    public double ParameterPanelHeight { get; set; } = 200;
    public double ParameterPanelHeightNormal { get; set; } = 200;
    public double ParameterPanelHeightMaximized { get; set; } = 200;
    public double MasterGain { get; set; } = 0;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundImageOpacity { get; set; } = 0.5;
    public double ParameterBoundaryExtension { get; set; } = 5;
    public bool ParameterSyncMode { get; set; } = false;
    public string PianoKeySamplesPath { get; set; } = string.Empty;
    public int AutoSaveInterval { get; set; } = 10;
    public int BufferSize { get; set; } = 1024;
    public int SampleRate { get; set; } = 44100;
    public string AudioDriver { get; set; } = string.Empty;
    public string AudioDevice { get; set; } = string.Empty;
    public double TrackHueChangeRate { get; set; } = 0;
}
