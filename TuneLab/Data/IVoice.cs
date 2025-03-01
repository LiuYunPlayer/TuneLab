using System.Collections.Generic;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IVoice : IDataObject<VoiceInfo>
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    ObjectConfig PartProperties { get; }
    ObjectConfig NoteProperties { get; }
    IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote;
    ISynthesisTask CreateSynthesisTask(ISynthesisData data);
}
