using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Configs;

internal class SettingsFile
{
    public string Language { get; set; } = string.Empty;
    // 界面字体家族名（空 = 系统默认，走 Inter + 平台回退链）。空值外的选中字体作 FontManager 默认家族。
    public string InterfaceFontFamily { get; set; } = string.Empty;
    public string AutoScrollTarget { get; set; } = "None";
    public double MasterGain { get; set; } = 0;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundImageOpacity { get; set; } = 0.5;
    public double ParameterBoundaryExtension { get; set; } = 5;
    public bool ParameterSyncMode { get; set; } = false;
    public string PianoKeySamplesPath { get; set; } = string.Empty;
    public int AutoSaveInterval { get; set; } = 10;
    public int AutoSaveMaxCount { get; set; } = 5;
    public int BufferSize { get; set; } = 1024;
    public int MaxParallelSynthesisTasks { get; set; } = 0;   // 合成/效果器并行任务数上限；<=0 = 按核数自动
    public int SampleRate { get; set; } = 44100;
    public string AudioDriver { get; set; } = string.Empty;
    public string AudioDevice { get; set; } = string.Empty;
    public double TrackHueChangeRate { get; set; } = 0;
    public string AgentModelProvider { get; set; } = string.Empty;   // agent 选中的模型 provider（引擎 id）；各 provider 的配置另存 ExtensionSettings.json
    // 扩展冲突消解的用户选择（routeKey="kind:identity" → 选中的 packageId）：同一身份 id/扩展名被多包提供时用户选用哪个包。
    // 扁平小映射、无密钥，与 AgentModelProvider 同属「用户选择」类，故直接随 app 设置存盘，不另开配置文件。
    public Dictionary<string, string> ExtensionRouting { get; set; } = new();
}
