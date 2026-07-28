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
    // agent 写操作的授权级别（枚举名 ReadOnlyAdvice/Confirm/Auto，见 AgentAuthorization）；默认 Confirm。
    public string AgentAuthorization { get; set; } = "Confirm";
    // 单次工具结果回灌模型的字符数上限（中央兜底，防某工具输出淹没上下文）。默认设宽（普通机器十几个音源/结果远小于此，
    // 不受影响；只拦成百上千的畸形案例）；<=0 = 不限。见 AgentRunner 的 clamp。
    public int AgentMaxToolResultChars { get; set; } = 40000;
}
