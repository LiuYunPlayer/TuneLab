using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

public interface IVoiceSynthesisContext
{
    string VoiceID { get; }
    IReadOnlyMap<string, IReadOnlyPropertyValue> Properties { get; }
}
