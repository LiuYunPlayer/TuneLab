using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;

namespace TuneLab;

internal static class ConstantDefine
{
    public static readonly string PitchID = "Pitch";
    public static readonly string PitchName = "Pitch";
    public static readonly string PitchColor = "#FFCF40";
    public static readonly string VolumeID = "Volume";
    public static readonly string VibratoEnvelopeID = "VibratoEnvelope";
    public static readonly IReadOnlyOrderedMap<string, AutomationConfig> PreCommonAutomationConfigs = new OrderedMap<string, AutomationConfig>()
    {
        { VolumeID, new AutomationConfig() { Name = "Volume", DefaultValue = 0, MinValue = -12, MaxValue = +12, Color = "#737CE5" } }
    };
    public static readonly IReadOnlyOrderedMap<string, AutomationConfig> PostCommonAutomationConfigs = new OrderedMap<string, AutomationConfig>()
    {
        { VibratoEnvelopeID, new AutomationConfig() { Name = "VibratoEnvelope", DefaultValue = 0, MinValue = 0, MaxValue = 2, Color = "#73DBE5" } }
    };
}
