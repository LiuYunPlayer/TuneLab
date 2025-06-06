using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceSource(TuneLab.Extensions.Voices.IVoiceSource voiceSource, IVoiceSynthesisContext context) : IVoiceSource
{
    public string DefaultLyric => voiceSource.DefaultLyric;

    public IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
    {
        throw new NotImplementedException();
        //return new VoiceSynthesisSegment(voiceSource.CreateSynthesisTask()(input, output), context);
    }

    public ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes)
    {
        throw new NotImplementedException();
        //return voiceSource.NoteProperties;
    }

    public IEnumerable<IReadOnlyList<ISynthesisNote>> Segment(IEnumerable<ISynthesisNote> notes)
    {
        throw new NotImplementedException();
        //return voiceSource.Segment(new TuneLab.Extensions.Voices.SynthesisSegment<SynthesisNote>() { PartProperties = context.Properties, Notes = notes.Select(note => new SynthesisNote(note)));
    }
}
