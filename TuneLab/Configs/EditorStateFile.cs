using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Configs;

internal class EditorStateFile
{
    public int MainWindowX { get; set; } = int.MinValue;
    public int MainWindowY { get; set; } = int.MinValue;
    public double MainWindowWidth { get; set; } = 0;
    public double MainWindowHeight { get; set; } = 0;
    public bool MainWindowMaximized { get; set; } = false;
    public double TrackWindowHeight { get; set; } = 240;
    public double ParameterPanelHeight { get; set; } = 200;
    public double ParameterPanelHeightNormal { get; set; } = 200;
    public double ParameterPanelHeightMaximized { get; set; } = 200;
}
