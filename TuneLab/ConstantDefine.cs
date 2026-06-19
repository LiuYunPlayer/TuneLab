using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.I18N;

namespace TuneLab;

internal static class ConstantDefine
{
    public const string DefaultProjectExtension = "tlpx";

    public static readonly string PitchID = "Pitch";
    public static readonly string PitchName = "Pitch";
    public static readonly string PitchColor = "#FFCF40";
    public static readonly string VolumeID = "Volume";
    public static readonly string VibratoEnvelopeID = "VibratoEnvelope";
    public static readonly IReadOnlyOrderedMap<PropertyKey, AutomationConfig> PreCommonAutomationConfigs = new OrderedMap<PropertyKey, AutomationConfig>()
    {
        { (VolumeID, "Volume".Tr(TC.Property)), new AutomationConfig { DefaultValue = 0, MinValue = -12, MaxValue = +12, Color = "#737CE5" } }
    };
    public static readonly IReadOnlyOrderedMap<PropertyKey, AutomationConfig> PostCommonAutomationConfigs = new OrderedMap<PropertyKey, AutomationConfig>()
    {
        { (VibratoEnvelopeID, "VibratoEnvelope".Tr(TC.Property)), new AutomationConfig { DefaultValue = 1, MinValue = 0, MaxValue = 2, Color = "#73DBE5" } }
    };
}
