using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.SDK.Voice;

using TuneLab.Extensions.Voices;
namespace TuneLab.Data;

internal class Voice : DataObject, IVoice
{
    public string Name => mVoiceSource.Name;
    public string DefaultLyric => mVoiceSource.DefaultLyric;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public ObjectConfig GetPartConfig(IPropertyContext context) => mVoiceSource.GetPartConfig(context);
    public ObjectConfig GetNoteConfig(IPropertyContext context) => mVoiceSource.GetNoteConfig(context);

    public Voice(DataObject parent, VoiceInfo info) : base(parent)
    {
        WriteInfo(info);
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

    [MemberNotNull(nameof(mType))]
    [MemberNotNull(nameof(mID))]
    [MemberNotNull(nameof(mVoiceSource))]
    void WriteInfo(VoiceInfo info)
    {
        mType = info.Type;
        mID = info.ID;
        mVoiceSource = VoicesManager.Create(info.Type, info.ID);
        mAutomationConfigs.Clear();
        foreach (var kvp in ConstantDefine.PreCommonAutomationConfigs.Concat(mVoiceSource.AutomationConfigs).Concat(ConstantDefine.PostCommonAutomationConfigs))
        {
            mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
    }

    class ModifyCommand(Voice voice, VoiceInfo before, VoiceInfo after) : ICommand
    {
        public void Redo() { voice.WriteInfo(after); voice.Notify(); }
        public void Undo() { voice.WriteInfo(before); voice.Notify(); }
    }

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
    {
        return mVoiceSource.Segment(segment);
    }

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data)
    {
        return mVoiceSource.CreateSynthesisTask(data);
    }

    string mType;
    string mID;

    IVoiceSource mVoiceSource;
    OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
}
