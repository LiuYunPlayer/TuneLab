using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;

namespace TuneLab.Data;

internal class Voice : DataObject, IVoice
{
    public string Name => mVoiceSource.Name;
    public string DefaultLyric => mVoiceSource.DefaultLyric;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public ObjectConfig PartProperties => new(mVoiceSource.PartProperties);
    public ObjectConfig NoteProperties => new(mVoiceSource.NoteProperties);

    public Voice(DataObject parent, VoiceInfo info) : base(parent)
    {
        IDataObject<VoiceInfo>.SetInfo(this, info);
    }

    public VoiceInfo GetInfo()
    {
        return new VoiceInfo()
        {
            Type = mType,
            ID = mID
        };
    }

    public bool isEmptyVoice { get => (mVoiceSource.GetType().Name.Equals("EmptyVoiceSource")); }

    [MemberNotNull(nameof(mType))]
    [MemberNotNull(nameof(mID))]
    [MemberNotNull(nameof(mVoiceSource))]
    void IDataObject<VoiceInfo>.SetInfo(VoiceInfo info)
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
