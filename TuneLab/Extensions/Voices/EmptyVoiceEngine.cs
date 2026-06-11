using System.Collections.Generic;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Format.DataInfo;

using TuneLab.SDK.Voice;
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
        public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
        public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

        public string DefaultLyric { get; } = "a";

        public EmptyVoiceSource(string id)
        {
            mID = id;
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
    static OrderedMap<string, IControllerConfig> mPartProperties = new();
    static OrderedMap<string, IControllerConfig> mNoteProperties = new();
    static VoiceSourceInfo mVoiceSourceInfo = new() { Name = "Empty Voice", Description = "" };
}
