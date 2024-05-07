using System.Collections.Generic;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Voices;

[VoiceEngine("")]
internal class EmptyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, mVoiceSourceInfo } };

    public IVoiceSource CreateVoiceSource(string id)
    {
        return new EmptyVoiceSource(id);
    }

    public void Destroy()
    {
        mAutomationConfigs.Clear();
    }

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        return true;
    }

    class EmptyVoiceSource : IVoiceSource
    {
        public string Name => string.IsNullOrEmpty(mID) ? mVoiceSourceInfo.Name : mID;

        public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
        public IReadOnlyOrderedMap<string, IPropertyConfig> PartProperties => mPartProperties;
        public IReadOnlyOrderedMap<string, IPropertyConfig> NoteProperties => mNoteProperties;

        public string DefaultLyric => "a";


        public EmptyVoiceSource(string id)
        {
            mID = id;
        }

        public VoiceInfo GetInfo()
        {
            return new VoiceInfo() { ID = mID };
        }

        public void SetInfo(VoiceInfo info)
        {
            mID = info.ID;
        }

        public string GraphemeToPhoneme(string lyric)
        {
            return lyric;
        }

        public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        {
            return this.SimpleSegment(segment);
        }

        public ISynthesisTask CreateSynthesisTask(ISynthesisData data)
        {
            return new EmptyVoiceSynthesisTask(data);
        }

        string mID;
    }

    static OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    static OrderedMap<string, IPropertyConfig> mPartProperties = new();
    static OrderedMap<string, IPropertyConfig> mNoteProperties = new();
    static VoiceSourceInfo mVoiceSourceInfo = new() { Name = "Empty Voice", Description = "" };
}
