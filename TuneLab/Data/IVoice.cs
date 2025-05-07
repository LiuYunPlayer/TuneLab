using System.Collections.Generic;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IVoice : IDataObject<VoiceInfo>
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    ObjectConfig PropertyConfig { get; }
    ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes);
    IReadOnlyList<IReadOnlyList<INote>> Segment(IEnumerable<INote> notes);
    IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output);
}
