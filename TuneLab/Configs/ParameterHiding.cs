using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 标签栏上常驻参数的隐藏：音源 / 各效果器的自动化轨与时间轴画布默认都在标签栏上，用户只用其中几个时可以把别的收起来。
// 【与钉选互为两类】原本常驻的记"用户显式设过显示 / 隐藏的"，没设过的按声明：自动化轨、时间轴 lane 画布看 config 的 Visibility
//   （插件声明的初始状态，默认显示——插件新增的参数自然出现），时间轴画布看 TimelineCanvasConfig.ShownByDefault；原本不显示的可钉属性记"钉了哪些"
//   （ParameterPinning）。标签栏右键的勾选列表把两类合在一起，打勾 = 在标签栏上。有没有数据不参与判定。
// 【只传值】声明为 ControllerVisibility.Never 的条目没有任何控件：恒隐藏、不能显示（IsHideable = false，不进勾选列表与动作参数），
//   用户以前对它设过的显式状态不生效（记录留着，插件改回能显示的档位后照旧）。
// 【隐藏 = 藏掉这一条的所有控件】标签栏、侧栏的默认值滑条都不出现；数据照常参与合成；面板不画它（激活不了、也不作背景）；
//   命令与 agent 照样读写它。
//   侧栏时间轴画布那一行的编辑按钮跳向一条隐藏的画布，就是要看它——那一下把它显示出来（见 PianoWindow.EditInFocusedPanel）。
// 【存法】同钉选：界面偏好、跟声源 / 效果器类型走（跨工程生效），自带独立 JSON（Configs/ParameterHidden.json），改后即时存盘。
//   键 "kind:Type:类别:id" → 是否隐藏（true = 用户显式隐藏，false = 用户显式显示）；没有键 = 用户没设过、按声明的默认值。
//   自动化与时间轴 lane 画布按声源或效果器类型记（两族分开记，可以同 id），时间轴画布按声源 + scope + 路径记。
internal static class ParameterHiding
{
    // 隐藏集合变了（任何声源）：标签栏重建按钮、面板重新判定激活与背景。
    public static IActionEvent Changed => mChanged;

    public static void Init(string path)
    {
        Dictionary<string, bool>? explicitStates = null;
        if (File.Exists(path))
        {
            try
            {
                explicitStates = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize hidden parameters: " + ex);
            }
        }
        mExplicit = explicitStates ?? new();
        mPath = path;
    }

    static void Save()
    {
        if (string.IsNullOrEmpty(mPath))
            return;

        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mExplicit, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save hidden parameters: " + ex);
        }
    }

    // 能不能隐藏 / 显示：自动化轨、时间轴 lane 画布与时间轴画布能；只传值（Never）的不能——它没有控件可显示；
    // 钉选的 lane 不归这里（不想要就解钉）。
    public static bool IsHideable(IMidiPart part, AutomationKey key)
        => PreferenceKey(part, key) != null && DeclaredVisibility(part, key) != ControllerVisibility.Never;

    // 只传值：声明为 ControllerVisibility.Never（没有任何控件，数据照常参与合成）。
    public static bool IsValueOnly(IMidiPart part, AutomationKey key)
        => DeclaredVisibility(part, key) == ControllerVisibility.Never;

    // 只传值的恒隐藏；其余用户显式设过的按用户的，否则按声明的初始状态。
    public static bool IsHidden(IMidiPart part, AutomationKey key)
    {
        if (PreferenceKey(part, key) is not { } hiddenKey)
            return false;
        var declared = DeclaredVisibility(part, key);
        if (declared == ControllerVisibility.Never)
            return true;
        return mExplicit.TryGetValue(hiddenKey, out var hidden) ? hidden : declared == ControllerVisibility.HiddenByDefault;
    }

    // 【隐藏 / 显示一次】的收口：标签栏右键勾选列表、命令面的 parameter.hideFromTabBar / showInTabBar、侧栏编辑按钮，
    // 按下去是同一件事。终态语义、幂等：已在目标态什么都不做（也不记成显式——没设过的仍跟着声明的默认值走）；
    // 真改了就记成用户显式的状态。返回是否真的动了。
    public static bool SetHidden(IMidiPart part, AutomationKey key, bool hidden)
    {
        if (!IsHideable(part, key) || PreferenceKey(part, key) is not { } hiddenKey)
            return false;
        if (IsHidden(part, key) == hidden)
            return false;

        mExplicit[hiddenKey] = hidden;
        Save();
        mChanged.Invoke();
        return true;
    }

    // 声明的可见性：自动化轨、时间轴 lane 画布看 config 的 Visibility；时间轴画布看物化时记下的 ShownByDefault（没有 Never）。
    // 不认识的枚举值按 ShownByDefault（宁可多显示，不让数据悄悄失去入口）。
    static ControllerVisibility DeclaredVisibility(IMidiPart part, AutomationKey key)
    {
        var visibility = ControllerVisibility.ShownByDefault;
        switch (key.Source)
        {
            case AutomationSource.Voice:
            case AutomationSource.Effect:
                if (part.TryGetAutomationConfig(key, out var automationConfig))
                    visibility = automationConfig.Visibility;
                break;
            case AutomationSource.VoiceCanvas:
            case AutomationSource.EffectCanvas:
                if (part.TryGetTimelineLaneCanvas(key, out var canvasConfig, out _))
                    visibility = canvasConfig.Visibility;
                break;
            case AutomationSource.NoteLane:
            case AutomationSource.PhonemeLane:
                if ((key.IsNoteLane ? part.NoteLaneConfigs : part.PhonemeLaneConfigs).TryGetValue(key.Id, out var lane) && !lane.ShownByDefault)
                    visibility = ControllerVisibility.HiddenByDefault;
                break;
        }
        return visibility is ControllerVisibility.HiddenByDefault or ControllerVisibility.Never ? visibility : ControllerVisibility.ShownByDefault;
    }

    // 这一条在界面偏好里的键（跟声源 / 效果器类型走、不跟工程走）：标签栏隐藏与叠到钢琴窗上（PianoOverlay）都按它记。
    // 只有能常驻标签栏的条目有键（自动化轨、时间轴 lane 画布、时间轴画布）；钉选的 lane 没有（它们按钉选存储记）。
    public static string? PreferenceKey(IMidiPart part, AutomationKey key)
    {
        switch (key.Source)
        {
            case AutomationSource.Voice:
                return SourcePrefix(part.SoundSource) + ":automation:" + key.Id;
            case AutomationSource.Effect:
                return key.EffectIndex >= 0 && key.EffectIndex < part.Effects.Count
                    ? "effect:" + part.Effects[key.EffectIndex].Type + ":automation:" + key.Id
                    : null;
            case AutomationSource.NoteLane:
            case AutomationSource.PhonemeLane:
                var lanes = key.IsNoteLane ? part.NoteLaneConfigs : part.PhonemeLaneConfigs;
                if (!lanes.TryGetValue(key.Id, out var lane) || lane.Kind != LaneKind.TimelineCanvas)
                    return null;
                return SourcePrefix(part.SoundSource) + (key.IsNoteLane ? ":note-lane:" : ":phoneme-lane:") + key.Id;
            case AutomationSource.VoiceCanvas:
                return SourcePrefix(part.SoundSource) + ":timeline-lane-canvas:" + key.Id;
            case AutomationSource.EffectCanvas:
                return key.EffectIndex >= 0 && key.EffectIndex < part.Effects.Count
                    ? "effect:" + part.Effects[key.EffectIndex].Type + ":timeline-lane-canvas:" + key.Id
                    : null;
            default:
                return null;
        }
    }

    static string SourcePrefix(ISoundSource source)
        => (source.Kind == SourceKind.Voice ? "voice:" : "instrument:") + source.Type;

    static Dictionary<string, bool> mExplicit = new();
    static string mPath = string.Empty;
    static readonly ActionEvent mChanged = new();
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
