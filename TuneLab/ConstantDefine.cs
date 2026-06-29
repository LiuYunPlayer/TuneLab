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
        { (VolumeID, "Volume".Tr(TC.Property)), AutomationConfig.Create(-12, +12).WithColor("#737CE5").WithDefault(0) }
    };
    public static readonly IReadOnlyOrderedMap<PropertyKey, AutomationConfig> PostCommonAutomationConfigs = new OrderedMap<PropertyKey, AutomationConfig>()
    {
        { (VibratoEnvelopeID, "VibratoEnvelope".Tr(TC.Property)), AutomationConfig.Create(0, 2).WithColor("#73DBE5").WithDefault(1) }
    };
}
