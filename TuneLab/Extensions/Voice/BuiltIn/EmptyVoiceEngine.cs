using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice.BuiltIn;

class EmptyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; } = new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, new VoiceSourceInfo() { Name = "Empty Voice" } } };

    public void Init()
    {

    }

    public void Destroy()
    {

    }

    public IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
    {
        return new EmptyVoiceSource();
    }

    public ObjectConfig GetContextPropertyConfig(IEnumerable<IVoiceSynthesisContext> contexts)
    {
        return PropertyConfig;
    }

    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEnumerable<IVoiceSynthesisContext> contexts)
    {
        return [];
    }

    readonly static ObjectConfig PropertyConfig = new();

    class EmptyVoiceSource : IVoiceSource
    {
        public string DefaultLyric { get; } = "a";
        public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; } = [];

        public IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
        {
            return new EmptyVoiceSynthesisSegment(input, output);
        }

        public ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes)
        {
            return PropertyConfig;
        }

        public IEnumerable<IReadOnlyList<ISynthesisNote>> Segment(IEnumerable<ISynthesisNote> notes)
        {
            return this.SimpleSegment(notes);
        }

        IEnumerable<IReadOnlyList<ISynthesisNote>> IVoiceSource.Segment(IEnumerable<ISynthesisNote> notes)
        {
            return Segment(notes);
        }

        class EmptyVoiceSynthesisSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output) : IVoiceSynthesisSegment
        {
            public event Action? ProgressUpdated;
            public event Action<SynthesisError?>? Finished;

            public double Progress => 1;
            public string Status => "Done.";

            public void OnDirtyEvent(VoiceDirtyEvent dirtyEvent)
            {

            }

            public void StartSynthesis()
            {
                Finished?.Invoke(null);
            }

            public void StopSynthesis()
            {

            }
        }
    }
}

class EmptyVoiceContext : IVoiceSynthesisContext
{
    public string VoiceID => string.Empty;
    public IReadOnlyMap<string, IReadOnlyPropertyValue> Properties => [];
}
