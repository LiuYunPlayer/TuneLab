using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceEngine
{
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }
    void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties);
    void Destroy();
    IVoiceSource CreateVoiceSource(string id, IReadOnlyMap<string, IReadOnlyPropertyValue> properties);
}
