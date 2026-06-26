using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;

using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
namespace TuneLab.Data;

// ISoundSource 的实现：单一稳定数据对象（身份不变），按 Kind 把声明委派给 Voices / Instruments 管理器。
// 换种类只改 Kind/Type/ID（走 SetInfo + 同一 ModifyCommand），不替换对象——故外部对 Modified 的订阅不悬挂。
//
// 声明方法沿用 voice 既有 context 形态（其 VoiceId 即"音源 id"）：instrument 分支在本类内把 voice context
// 适配成 instrument context（InstrumentId = VoiceId），故调用方（MidiPart / 属性面板）无需感知种类。
internal class SoundSource : DataObject, ISoundSource
{
    public SourceKind Kind => mKind;
    public string Type => mType;
    public string ID => mID;
    public string Name => mName;
    // 新 note 默认歌词：voice 取会话级 DefaultLyric；instrument 无歌词系统，恒 "a"。
    public string DefaultLyric => mKind == SourceKind.Voice ? (mSession?.DefaultLyric ?? "a") : "a";

    // 物化缓存（非 live）：返回 RebuildAutomationConfigs 上次填好的集合。须在「建会话之前」填好
    //（见 MidiPart.RebuildSynthesisPipeline 顺序），否则会话构造期订阅自己声明的轨会读到空缓存。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs => mSynthesizedParameterConfigs;

    // 声明类 config 求值在引擎层（不依赖会话实例）：按 Kind 上行委派对应管理器，context（PartPropertyContext/NotePropertyContext）同时满足两域。
    public ObjectConfig GetPartPropertyConfig(PartPropertyContext context)
        => mKind == SourceKind.Voice
            ? VoicesManager.GetPartPropertyConfig(mType, context)
            : InstrumentsManager.GetPartPropertyConfig(mType, context);

    public ObjectConfig GetNotePropertyConfig(NotePropertyContext context)
        => mKind == SourceKind.Voice
            ? VoicesManager.GetNotePropertyConfig(mType, context)
            : InstrumentsManager.GetNotePropertyConfig(mType, context);

    public SoundSource(DataObject parent, SoundSourceInfo info) : base(parent)
    {
        WriteInfo(info);
        RefreshDeclarations(PartPropertyContext.Empty);
    }

    public SoundSourceInfo GetInfo()
    {
        return new SoundSourceInfo()
        {
            Kind = mKind,
            Type = mType,
            ID = mID
        };
    }

    // 原子复合：状态是普通字段（无子数据对象可扇出），故保留复合 ModifyCommand + 私有 raw SetInfo。
    public void SetInfo(SoundSourceInfo info)
    {
        var before = GetInfo();
        if (Equals(before, info))
            return;

        PushAndDo(new ModifyCommand(this, before, info));
    }

    // 声明刷新（不依赖会话）：重算名字 + 轨/回显集合。声明类 config 全是引擎层纯函数，故可在「建会话之前」调用。
    // 不触发 Notify：本方法在 SoundSource.Modified 的 part 侧 handler 内被调，先于 UI 刷新执行。
    public void RefreshDeclarations(PartPropertyContext context)
    {
        mName = ResolveName();
        RebuildAutomationConfigs(context);
    }

    string ResolveName()
    {
        if (mKind == SourceKind.Voice)
            return VoicesManager.TryGetVoiceInfo(mType, mID, out var v)
                ? v.Name : (string.IsNullOrEmpty(mID) ? "Empty Voice" : mID);

        return InstrumentsManager.TryGetInstrumentInfo(mType, mID, out var i)
            ? i.Name : (string.IsNullOrEmpty(mID) ? "Empty Instrument" : mID);
    }

    // 注入合成会话（建会话之后，仅 voice）：供 DefaultLyric 等会话级运行时取值；instrument 无此需求。
    public void SetSession(IVoiceSession? session)
    {
        mSession = session;
    }

    // 按当前 part 参数值重算自动化轨集合（轨集合 = f(当前值)）。
    // 通用轨：Volume（PreCommon，宿主混音应用、对 voice / instrument 皆有效）两类都并；
    // VibratoEnvelope（PostCommon）是 voice 颤音专属，仅 voice 并。
    public void RebuildAutomationConfigs(PartPropertyContext context)
    {
        mAutomationConfigs.Clear();
        foreach (var kvp in ConstantDefine.PreCommonAutomationConfigs)
        {
            mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        foreach (var kvp in DeclaredAutomationConfigs(context))
        {
            if (!mAutomationConfigs.ContainsKey(kvp.Key))
                mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        if (mKind == SourceKind.Voice)
        {
            foreach (var kvp in ConstantDefine.PostCommonAutomationConfigs)
            {
                if (!mAutomationConfigs.ContainsKey(kvp.Key))
                    mAutomationConfigs.Add(kvp.Key, kvp.Value);
            }
        }

        // 回显轨集合（独立、扁平；与可编辑轨集合各管各的，不去重不掺通用轨）。
        mSynthesizedParameterConfigs.Clear();
        foreach (var kvp in DeclaredSynthesizedParameterConfigs(context))
        {
            if (!mSynthesizedParameterConfigs.ContainsKey(kvp.Key))
                mSynthesizedParameterConfigs.Add(kvp.Key, kvp.Value);
        }
    }

    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> DeclaredAutomationConfigs(PartPropertyContext context)
        => mKind == SourceKind.Voice
            ? VoicesManager.GetAutomationConfigs(mType, context)
            : InstrumentsManager.GetAutomationConfigs(mType, context);

    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> DeclaredSynthesizedParameterConfigs(PartPropertyContext context)
        => mKind == SourceKind.Voice
            ? VoicesManager.GetSynthesizedParameterConfigs(mType, context)
            : InstrumentsManager.GetSynthesizedParameterConfigs(mType, context);

    [MemberNotNull(nameof(mType))]
    [MemberNotNull(nameof(mID))]
    void WriteInfo(SoundSourceInfo info)
    {
        mKind = info.Kind;
        mType = info.Type;
        mID = info.ID;
    }

    class ModifyCommand(SoundSource source, SoundSourceInfo before, SoundSourceInfo after) : ICommand
    {
        public void Redo() { source.WriteInfo(after); source.Notify(); }
        public void Undo() { source.WriteInfo(before); source.Notify(); }
    }

    SourceKind mKind;
    string mType;
    string mID;
    string mName = string.Empty;

    IVoiceSession? mSession;
    readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mSynthesizedParameterConfigs = new();
}
