using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voice;

public struct VoiceExtensionEntry
{
    public string Type { get; set; }
    public IVoiceEngine VoiceEngine { get; set; }
}
