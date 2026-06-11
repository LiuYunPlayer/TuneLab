using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.SDK.Voice;

namespace TuneLab.Data;

internal interface IVoice : IDataObject<VoiceInfo>
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    ObjectConfig GetPartConfig(IPropertyContext context);
    ObjectConfig GetNoteConfig(IPropertyContext context);
    IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote;
    ISynthesisTask CreateSynthesisTask(ISynthesisData data);
}
