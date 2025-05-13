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

    public IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
    {
        return new EmptyVoiceSource();
    }

    public void Destroy()
    {

    }

    public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
    {

    }

    class EmptyVoiceSource : IVoiceSource
    {
        public string DefaultLyric { get; } = "a";
        public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; } = [];
        public ObjectConfig PropertyConfig { get; } = new();

        public IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
        {
            return new EmptyVoiceSynthesisSegment(input, output);
        }

        public ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes)
        {
            return PropertyConfig;
        }

        public IReadOnlyList<IReadOnlyList<ISynthesisNote>> Segment(IEnumerable<ISynthesisNote> notes)
        {
            return this.SimpleSegment(notes);
        }

        class EmptyVoiceSynthesisSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output) : IVoiceSynthesisSegment
        {
            public event Action<double>? Progress;
            public event Action<SynthesisError?>? Finished;

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
