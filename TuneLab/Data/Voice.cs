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
    // 合成参数回显轨声明（独立扁平集合，不掺 Pre/PostCommon）：会话产出的只读回显轨自带 config，宿主据此显隐/绘制。
    public IReadOnlyOrderedMap<string, AutomationConfig> SynthesizedParameterConfigs => mSynthesizedParameterConfigs;
    // 声明类 config 求值移到引擎层（不依赖会话实例）：经 VoicesManager 按 type 解析活引擎，context 携带 voiceId。
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => VoicesManager.GetPartPropertyConfig(mType, context);
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => VoicesManager.GetNotePropertyConfig(mType, context);

    public Voice(DataObject parent, VoiceInfo info) : base(parent)
    {
        WriteInfo(info);
        RefreshDeclarations(new PartPropertyContext(mID, PropertyObject.Empty));
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

    // 声明刷新（不依赖会话）：重算名字 + 轨/回显集合。声明类 config 全是引擎层纯函数，故宿主可在
    // 「建会话之前」调用，使会话构造期声明（AutomationConfigs）即已就绪——插件构造函数里就能订阅自己声明的轨。
    // 不触发 Notify：本方法在 Voice.Modified 的 part 侧 handler 内被调，part 构造期最早订阅、先于 UI 刷新执行。
    public void RefreshDeclarations(IPartPropertyContext context)
    {
        mName = VoicesManager.TryGetVoiceInfo(mType, mID, out var info)
            ? info.Name
            : (string.IsNullOrEmpty(mID) ? "Empty Voice" : mID);
        RebuildAutomationConfigs(context);
    }

    // 注入合成会话（建会话之后）：仅供 DefaultLyric 等会话级运行时取值；声明不经此（已走引擎层）。
    public void SetSession(ISynthesisSession? session)
    {
        mSession = session;
    }

    // 按当前 part 参数值重算自动化轨集合（轨集合 = f(当前值)）：会话固定、part 参数 commit 时由 part 调用。
    // 仅重算材料化缓存（PreCommon + 会话条件声明 + PostCommon，及回显轨集合），不动 mSession/mName。变更检测由 part 侧承担。
    public void RebuildAutomationConfigs(IPartPropertyContext context)
    {
        mAutomationConfigs.Clear();
        foreach (var kvp in ConstantDefine.PreCommonAutomationConfigs)
        {
            mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        foreach (var kvp in VoicesManager.GetAutomationConfigs(mType, context))
        {
            if (!mAutomationConfigs.ContainsKey(kvp.Key))
                mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        foreach (var kvp in ConstantDefine.PostCommonAutomationConfigs)
        {
            if (!mAutomationConfigs.ContainsKey(kvp.Key))
                mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }

        // 回显轨集合（独立、扁平；与可编辑轨集合各管各的，不去重不掺通用轨）。
        mSynthesizedParameterConfigs.Clear();
        foreach (var kvp in VoicesManager.GetSynthesizedParameterConfigs(mType, context))
        {
            if (!mSynthesizedParameterConfigs.ContainsKey(kvp.Key))
                mSynthesizedParameterConfigs.Add(kvp.Key, kvp.Value);
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

    string mType;
    string mID;
    string mName = string.Empty;

    ISynthesisSession? mSession;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, AutomationConfig> mSynthesizedParameterConfigs = new();
}
