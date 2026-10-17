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
    public bool WaveformVisible { get; set; } = true;
    // 打开着的侧栏面（按打开顺序，存页签名）与各面拖出来的宽度（含已关的面：重开时沿用）。
    public List<string> SidePanels { get; set; } = [];
    public Dictionary<string, double> SidePanelWidths { get; set; } = [];
}
