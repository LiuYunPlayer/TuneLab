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

// Part 音源选择：单个下拉直接绑 part.SoundSource（非 IDataProperty，用户选 → SetInfo+Commit 扇出到所有目标 part、
// SoundSource.Modified → 回显）。按钮显示当前 voice 名；展开后：
//   顶部 = 当前引擎的各音源（快速在当前引擎内切）；
//   ──Voices── 带字分隔线下 = 其余各 voice 引擎（二级子菜单列其音源）；
//   ──Instruments── 带字分隔线下 = 各 instrument 引擎（二级子菜单）。
// 引擎只要加载正常(infos!=null)就展示（哪怕无音源=空子菜单，点引擎头无效，传达"引擎在、缺音源"的信息）。
// 选项值用"在 mEntries 表里的下标"编码（避开任意 type/id 字符串拼接），选中后按下标查回 (Kind,Type,ID)。
// 仅在单选或同引擎多选时由 provider 展示（current 引擎须一致；voice 可不同显 "-"）。
internal class PartVoiceController : StackPanel
{
    public PartVoiceController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Orientation.Vertical;

        mVoiceController.Margin = new(24, 12);
        mVoiceController.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Children.Add(mVoiceController);
        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mVoiceController.ValueCommitted.Subscribe(OnVoiceCommitted);
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
    readonly record struct Entry(SourceKind Kind, string Type, string Id);

    void Refresh()
    {
        if (mParts.Count == 0)
        {
            mVoiceController.DisplayNull();
            return;
        }

        var source = mParts[0].SoundSource;
        mVoiceController.SetConfig(BuildConfig(source.Kind, source.Type));

        // 各 part 音源全等才高亮对应项，否则三态 "-"。
        bool allSame = true;
        for (int i = 1; i < mParts.Count; i++)
        {
            var other = mParts[i].SoundSource;
            if (other.Kind != source.Kind || other.Type != source.Type || other.ID != source.ID)
            {
                allSame = false;
                break;
            }
        }
        if (!allSame)
        {
            mVoiceController.DisplayMultiple();
            return;
        }
        int index = mEntries.FindIndex(e => e.Kind == source.Kind && e.Type == source.Type && e.Id == source.ID);
        if (index < 0)
            mVoiceController.DisplayNull();
        else
            mVoiceController.Display(PropertyValue.Create((double)index));
    }

    // 构建选项树并同步填充 mEntries（叶子 value = 其在 mEntries 的下标）。
    ComboBoxConfig BuildConfig(SourceKind currentKind, string currentType)
    {
        mEntries.Clear();
        var options = new List<ComboBoxOption>();

        // 顶部：当前引擎的各音源（含当前 voice，便于直接切换 / 高亮）。
        if (TryEnumerateSources(currentKind, currentType, out var currentSources))
            foreach (var src in currentSources)
                options.Add(Leaf(currentKind, currentType, src));

        AddEngineSection(options, "Voices".Tr(TC.Property), SourceKind.Voice, VoicesManager.GetAllVoiceEngines(), currentKind, currentType);
        AddEngineSection(options, "Instruments".Tr(TC.Property), SourceKind.Instrument, InstrumentsManager.GetAllInstrumentEngines(), currentKind, currentType);

        return new ComboBoxConfig() { Options = options };
    }

    // 某一类(kind)的各引擎做成二级子菜单分组（排除当前引擎，其音源已在顶部）；跳过加载失败(infos==null)的引擎，
    // 空音源引擎仍展示为空子菜单。整段前置一条带字分隔线；该类无可展示引擎则整段省略。
    void AddEngineSection(List<ComboBoxOption> options, string label, SourceKind kind, IReadOnlyList<string> engines, SourceKind currentKind, string currentType)
    {
        var groups = new List<ComboBoxOption>();
        foreach (var type in engines)
        {
            if (kind == currentKind && type == currentType)
                continue;
            if (!TryEnumerateSources(kind, type, out var sources))
                continue;
            var leaves = sources.Select(src => Leaf(kind, type, src)).ToList();
            groups.Add(new ComboBoxOption(EngineName(kind, type), leaves));
        }
        if (groups.Count == 0)
            return;
        options.Add(ComboBoxOption.Separator(label));
        options.AddRange(groups);
    }

    // 登记一个音源叶子：记入 mEntries 并以其下标为 value。
    ComboBoxOption Leaf(SourceKind kind, string type, Source source)
    {
        int index = mEntries.Count;
        mEntries.Add(new Entry(kind, type, source.Id));
        return new ComboBoxOption(PropertyValue.Create((double)index), source.Name);
    }

    static string EngineName(SourceKind kind, string type)
    {
        if (string.IsNullOrEmpty(type))
            return "Built-In".Tr(TC.Property);
        return kind == SourceKind.Voice ? VoicesManager.GetDisplayName(type) : InstrumentsManager.GetDisplayName(type);
    }

    // 列某引擎的音源；infos==null（引擎未加载/不可用）返回 false 表示该引擎应跳过；非 null 但空则返回空列表（空子菜单）。
    static bool TryEnumerateSources(SourceKind kind, string type, out List<Source> sources)
    {
        sources = new List<Source>();
        if (kind == SourceKind.Voice)
        {
            var infos = VoicesManager.GetAllVoiceInfos(type);
            if (infos == null)
                return false;
            foreach (var kvp in infos)
                sources.Add(new Source(kvp.Key, kvp.Value.Name));
        }
        else
        {
            var infos = InstrumentsManager.GetAllInstrumentInfos(type);
            if (infos == null)
                return false;
            foreach (var kvp in infos)
                sources.Add(new Source(kvp.Key, kvp.Value.Name));
        }
        return true;
    }

    void OnVoiceCommitted()
    {
        if (mParts.Count == 0)
            return;
        if (!mVoiceController.Value.ToDouble(out var d))
            return;
        int index = (int)d;
        if ((uint)index >= (uint)mEntries.Count)
            return;
        var entry = mEntries[index];
        ApplyToAll(entry.Kind, entry.Type, entry.Id);
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

    readonly ComboBoxController mVoiceController = new();
    readonly List<Entry> mEntries = new();
    IReadOnlyList<IMidiPart> mParts = [];
    readonly DisposableManager s = new();
}
