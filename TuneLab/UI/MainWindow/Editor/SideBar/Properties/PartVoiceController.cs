using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using TuneLab.Data;
using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.I18N;

namespace TuneLab.UI;

// Part 音源选择：两个下拉（引擎 + voice）直接绑 part.SoundSource（Type=引擎 id、ID=voice id、Kind 区分 voice/instrument）。
// SoundSource 不是 IDataProperty（要 SetInfo 整体写），故不走属性绑定：用户选 → SetInfo+Commit 扇出到所有目标 part，
// part.SoundSource.Modified → 回显。
//
// 引擎下拉按当前 Kind 分层：当前是 voice 则平铺所有 voice 引擎 + 一个「Instrument」二级菜单收所有 instrument 引擎；
// 当前是 instrument 则反之。选项值编码 (Kind,Type)。仅在单选或同引擎多选时由 provider 展示（engine 必一致，voice 可不同显 "-"）。
internal class PartVoiceController : StackPanel
{
    public PartVoiceController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Orientation.Vertical;

        AddController(mEngineController, "Engine".Tr(TC.Property));
        AddController(mVoiceController, "Voice".Tr(TC.Property));

        mEngineController.ValueCommitted.Subscribe(OnEngineCommitted);
        mVoiceController.ValueCommitted.Subscribe(OnVoiceCommitted);
    }

    void AddController(Control control, string name)
    {
        Children.Add(new Label()
        {
            Height = 30,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Content = name,
            Padding = new(24, 0),
        });
        Children.Add(control);
        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
    }

    public void SetParts(IReadOnlyList<IMidiPart> parts)
    {
        s.DisposeAll();
        mParts = parts;
        foreach (var part in mParts)
            part.SoundSource.Modified.Subscribe(Refresh, s);
        Refresh();
    }

    readonly record struct Source(string Id, string Name);

    // 回显：按当前 Kind 重建引擎下拉（分层结构随 Kind 变），显当前引擎；voice 三态（各 part ID 全等显之、否则 "-"）。
    void Refresh()
    {
        if (mParts.Count == 0)
        {
            mEngineController.DisplayNull();
            mVoiceController.DisplayNull();
            return;
        }

        var source = mParts[0].SoundSource;
        mEngineController.SetConfig(BuildEngineConfig(source.Kind));
        mEngineController.Display(PropertyValue.Create(EngineKey(source.Kind, source.Type)));

        mVoiceController.SetConfig(BuildVoiceConfig(source.Kind, source.Type));
        var firstId = source.ID;
        bool allSame = true;
        for (int i = 1; i < mParts.Count; i++)
        {
            if (mParts[i].SoundSource.ID != firstId)
            {
                allSame = false;
                break;
            }
        }
        if (allSame)
            mVoiceController.Display(PropertyValue.Create(firstId));
        else
            mVoiceController.DisplayMultiple();
    }

    // 当前 Kind 的引擎平铺在前，另一 Kind 的引擎收进一个二级菜单组。
    static ComboBoxConfig BuildEngineConfig(SourceKind currentKind)
    {
        var voiceEngines = VoicesManager.GetAllVoiceEngines()
            .Select(type => new ComboBoxOption(PropertyValue.Create(EngineKey(SourceKind.Voice, type)), EngineName(SourceKind.Voice, type))).ToList();
        var instrumentEngines = InstrumentsManager.GetAllInstrumentEngines()
            .Select(type => new ComboBoxOption(PropertyValue.Create(EngineKey(SourceKind.Instrument, type)), EngineName(SourceKind.Instrument, type))).ToList();

        var primary = currentKind == SourceKind.Voice ? voiceEngines : instrumentEngines;
        var other = currentKind == SourceKind.Voice ? instrumentEngines : voiceEngines;
        var otherName = (currentKind == SourceKind.Voice ? "Instrument" : "Voice").Tr(TC.Property);

        var options = new List<ComboBoxOption>(primary);
        if (other.Count > 0)
            options.Add(new ComboBoxOption(otherName, other));
        return new ComboBoxConfig() { Options = options };
    }

    static ComboBoxConfig BuildVoiceConfig(SourceKind kind, string type)
    {
        var options = EnumerateSources(kind, type)
            .Select(source => new ComboBoxOption(PropertyValue.Create(source.Id), source.Name)).ToList();
        return new ComboBoxConfig() { Options = options };
    }

    static string EngineName(SourceKind kind, string type)
    {
        if (string.IsNullOrEmpty(type))
            return "Built-In".Tr(TC.Property);
        return kind == SourceKind.Voice ? VoicesManager.GetDisplayName(type) : InstrumentsManager.GetDisplayName(type);
    }

    static IEnumerable<Source> EnumerateSources(SourceKind kind, string type)
    {
        if (kind == SourceKind.Voice)
        {
            var infos = VoicesManager.GetAllVoiceInfos(type);
            if (infos != null)
                foreach (var kvp in infos)
                    yield return new Source(kvp.Key, kvp.Value.Name);
        }
        else
        {
            var infos = InstrumentsManager.GetAllInstrumentInfos(type);
            if (infos != null)
                foreach (var kvp in infos)
                    yield return new Source(kvp.Key, kvp.Value.Name);
        }
    }

    // 换引擎：旧 ID 多半不在新引擎，取新引擎首个 source 作默认 voice。
    void OnEngineCommitted()
    {
        if (mParts.Count == 0)
            return;
        var key = mEngineController.Value.ToString();
        if (string.IsNullOrEmpty(key))
            return;
        var (kind, type) = DecodeEngineKey(key);
        var first = EnumerateSources(kind, type).FirstOrDefault();
        ApplyToAll(kind, type, first.Id ?? "");
    }

    void OnVoiceCommitted()
    {
        if (mParts.Count == 0)
            return;
        var id = mVoiceController.Value.ToString() ?? "";
        var source = mParts[0].SoundSource;
        ApplyToAll(source.Kind, source.Type, id);
    }

    // 写回扇出到所有目标 part，归为一个撤销步（共享文档，统一 BeginMergeDirty/EndMergeDirty + commit 一次）。
    void ApplyToAll(SourceKind kind, string type, string id)
    {
        foreach (var part in mParts)
            part.BeginMergeDirty();
        foreach (var part in mParts)
            part.SoundSource.SetInfo(new SoundSourceInfo() { Kind = kind, Type = type, ID = id });
        foreach (var part in mParts)
            part.EndMergeDirty();
        mParts[0].Commit();
    }

    // 引擎选项值编码 (Kind,Type)：首字符 v/i 标 Kind，其余为 Type（type 可含任意字符，含空串）。
    static string EngineKey(SourceKind kind, string type) => (kind == SourceKind.Voice ? "v" : "i") + type;
    static (SourceKind Kind, string Type) DecodeEngineKey(string key)
    {
        var kind = key.Length > 0 && key[0] == 'v' ? SourceKind.Voice : SourceKind.Instrument;
        var type = key.Length > 0 ? key.Substring(1) : "";
        return (kind, type);
    }

    readonly ComboBoxController mEngineController = new() { Margin = new(24, 12), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    readonly ComboBoxController mVoiceController = new() { Margin = new(24, 12), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    IReadOnlyList<IMidiPart> mParts = [];
    readonly DisposableManager s = new();
}
