using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 标签栏上常驻参数的隐藏：音源 / 各效果器的自动化轨与时间轴画布默认都在标签栏上，用户只用其中几个时可以把别的收起来。
// 【与钉选互为两类】原本常驻的记"隐藏了哪些"（默认显示：插件新增的参数自然出现）；原本不显示的可钉属性记"钉了哪些"
//   （ParameterPinning）。标签栏右键的勾选列表把两类合在一起，打勾 = 在标签栏上。
// 【隐藏只管标签栏】数据照常参与合成；面板不画它（激活不了、也不作背景）；命令与 agent 照样读写它。
//   侧栏时间轴画布那一行的编辑按钮跳向一条隐藏的画布，就是要看它——那一下把它显示出来（见 PianoWindow.EditInFocusedPanel）。
// 【存法】同钉选：界面偏好、跟声源 / 效果器类型走（跨工程生效），自带独立 JSON（Configs/ParameterHidden.json），改后即时存盘。
//   键 "kind:Type:类别:id"——自动化按声源或效果器类型记，时间轴画布按声源 + scope + 路径记。
internal static class ParameterHiding
{
    // 隐藏集合变了（任何声源）：标签栏重建按钮、面板重新判定激活与背景。
    public static IActionEvent Changed => mChanged;

    public static void Init(string path)
    {
        HashSet<string>? hidden = null;
        if (File.Exists(path))
        {
            try
            {
                hidden = JsonSerializer.Deserialize<HashSet<string>>(File.OpenRead(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize hidden parameters: " + ex);
            }
        }
        mHidden = hidden ?? new();
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
            File.WriteAllText(mPath, JsonSerializer.Serialize(mHidden, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save hidden parameters: " + ex);
        }
    }

    // 能不能隐藏：自动化轨与时间轴画布能；钉选的 lane 不归这里（不想要就解钉）。
    public static bool IsHideable(IMidiPart part, AutomationKey key) => HiddenKey(part, key) != null;

    public static bool IsHidden(IMidiPart part, AutomationKey key)
        => HiddenKey(part, key) is { } hiddenKey && mHidden.Contains(hiddenKey);

    // 【隐藏 / 显示一次】的收口：标签栏右键勾选列表、命令面的 parameter.hideFromTabBar / showInTabBar、侧栏编辑按钮，
    // 按下去是同一件事。终态语义、幂等；返回是否真的动了。
    public static bool SetHidden(IMidiPart part, AutomationKey key, bool hidden)
    {
        if (HiddenKey(part, key) is not { } hiddenKey)
            return false;
        if (!(hidden ? mHidden.Add(hiddenKey) : mHidden.Remove(hiddenKey)))
            return false;

        Save();
        mChanged.Invoke();
        return true;
    }

    static string? HiddenKey(IMidiPart part, AutomationKey key)
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
            default:
                return null;
        }
    }

    static string SourcePrefix(ISoundSource source)
        => (source.Kind == SourceKind.Voice ? "voice:" : "instrument:") + source.Type;

    static HashSet<string> mHidden = new();
    static string mPath = string.Empty;
    static readonly ActionEvent mChanged = new();
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
