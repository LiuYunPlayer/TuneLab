using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Property;

namespace TuneLab.Data;

internal class Voice : DataObject, IVoice, IVoiceSynthesisContext
{
    public string VoiceID => mID;
    public IReadOnlyMap<string, IReadOnlyPropertyValue> Properties => mPart.Properties;

    public string Name => VoiceManager.GetAllVoiceInfos(mType)?.TryGetValue(mID, out var info) ?? false ? info.Name : mID;
    public string DefaultLyric => mVoiceSource.DefaultLyric;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public ObjectConfig PropertyConfig => mVoiceSource.PropertyConfig;

    public Voice(IMidiPart parent, VoiceInfo info) : base(parent)
    {
        mPart = parent;
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

    [MemberNotNull(nameof(mType))]
    [MemberNotNull(nameof(mID))]
    [MemberNotNull(nameof(mName))]
    [MemberNotNull(nameof(mVoiceSource))]
    void IDataObject<VoiceInfo>.SetInfo(VoiceInfo info)
    {
        mType = info.Type;
        mID = info.ID;

        mVoiceSource = VoiceManager.Create(info.Type, this);
        mAutomationConfigs.Clear();
        foreach (var kvp in ConstantDefine.PreCommonAutomationConfigs.Concat(mVoiceSource.AutomationConfigs).Concat(ConstantDefine.PostCommonAutomationConfigs))
        {
            mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
    }

    public ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes)
    {
        return mVoiceSource.GetNotePropertyConfig(notes);
    }

    public IReadOnlyList<IReadOnlyList<INote>> Segment(IEnumerable<INote> segment)
    {
        return mVoiceSource.Segment(segment).Convert(list => list.Convert(note => (INote)note));
    }

    public IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
    {
        return mVoiceSource.CreateSegment(input, output);
    }

    string mType;
    string mID;
    string mName;

    readonly IMidiPart mPart;
    IVoiceSource mVoiceSource;
    OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
}
