using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Voices;

namespace TuneLab;

internal static class ConstantDefine
{
    public static readonly string PitchID = "Pitch";
    public static readonly string PitchName = "Pitch";
    public static readonly string PitchColor = "#FFCF40";
    public static readonly string VolumeID = "Volume";
    public static readonly string XvsLineID = "CrossVoiceSynth";
    public static readonly string VibratoEnvelopeID = "VibratoEnvelope";
    public static readonly IReadOnlyOrderedMap<string, AutomationConfig> PreCommonAutomationConfigs = new OrderedMap<string, AutomationConfig>()
    {
        { VolumeID, new AutomationConfig("Volume", 0, -12, +12, "#737CE5") },
        { XvsLineID, new AutomationConfig("CrossVoiceSynth", 0, 0.0, 1.0, "#737CE5") },
    };
    public static readonly IReadOnlyOrderedMap<string, AutomationConfig> PostCommonAutomationConfigs = new OrderedMap<string, AutomationConfig>()
    {
        { VibratoEnvelopeID, new AutomationConfig("VibratoEnvelope", 1, 0, 2, "#73DBE5") }
    };
}
