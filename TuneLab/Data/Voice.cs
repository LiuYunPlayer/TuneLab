using System;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;

using TuneLab.Extensions.Voices;
namespace TuneLab.Data;

internal class Voice : DataObject, IVoice
{
    public string Type => mType;
    public string ID => mID;
    public string Name => mName;
    public string DefaultLyric => mSession?.DefaultLyric ?? "a";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs => mPiecewiseAutomationConfigs;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => mSession?.GetPartPropertyConfig(context) ?? EmptyConfig;
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => mSession?.GetNotePropertyConfig(context) ?? EmptyConfig;

    public Voice(DataObject parent, VoiceInfo info) : base(parent)
    {
        WriteInfo(info);
        RefreshDeclarations(null, PartPropertyContext.Empty);
    }

    public VoiceInfo GetInfo()
    {
        return new VoiceInfo()
        {
            Type = mType,
            ID = mID
        };
    }

    // 原子复合：状态是普通字段（无子数据对象可扇出），故保留复合 ModifyCommand + 私有 raw SetInfo（写自身字段，无跨实例访问墙）。
    public void SetInfo(VoiceInfo info)
    {
        var before = GetInfo();
        if (Equals(before, info))
            return;

        PushAndDo(new ModifyCommand(this, before, info));
    }

    // 会话重建后由 part 注入声明来源（不触发 Notify：本方法在 Voice.Modified 的 part 侧
    // handler 内被调，part 在构造期最早订阅、先于 UI 刷新执行，UI 读到的即新声明）。
    public void RefreshDeclarations(ISynthesisSession? session, IPartPropertyContext context)
    {
        mSession = session;
        mName = VoicesManager.TryGetVoiceInfo(mType, mID, out var info)
            ? info.Name
            : (string.IsNullOrEmpty(mID) ? "Empty Voice" : mID);
        RebuildAutomationConfigs(context);
    }

    // 按当前 part 参数值重算自动化轨集合（轨集合 = f(当前值)）：会话固定、part 参数 commit 时由 part 调用。
    // 仅重算材料化缓存（PreCommon + 会话条件声明 + PostCommon），不动 mSession/mName。变更检测由 part 侧承担。
    public void RebuildAutomationConfigs(IPartPropertyContext context)
    {
        mAutomationConfigs.Clear();
        foreach (var kvp in ConstantDefine.PreCommonAutomationConfigs)
        {
            mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        if (mSession != null)
        {
            foreach (var kvp in mSession.GetAutomationConfigs(context))
            {
                if (!mAutomationConfigs.ContainsKey(kvp.Key))
                    mAutomationConfigs.Add(kvp.Key, kvp.Value);
            }
        }
        foreach (var kvp in ConstantDefine.PostCommonAutomationConfigs)
        {
            if (!mAutomationConfigs.ContainsKey(kvp.Key))
                mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }

        // 分段轨声明（无 Pre/Post 公共项——内置分段轨 Pitch 是 part 专属常驻通道，不经此 map）。
        mPiecewiseAutomationConfigs.Clear();
        if (mSession != null)
        {
            foreach (var kvp in mSession.GetPiecewiseAutomationConfigs(context))
            {
                if (!mPiecewiseAutomationConfigs.ContainsKey(kvp.Key))
                    mPiecewiseAutomationConfigs.Add(kvp.Key, kvp.Value);
            }
        }
    }

    [MemberNotNull(nameof(mType))]
    [MemberNotNull(nameof(mID))]
    void WriteInfo(VoiceInfo info)
    {
        mType = info.Type;
        mID = info.ID;
    }

    class ModifyCommand(Voice voice, VoiceInfo before, VoiceInfo after) : ICommand
    {
        public void Redo() { voice.WriteInfo(after); voice.Notify(); }
        public void Undo() { voice.WriteInfo(before); voice.Notify(); }
    }

    static readonly ObjectConfig EmptyConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };

    string mType;
    string mID;
    string mName = string.Empty;

    ISynthesisSession? mSession;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();
}
