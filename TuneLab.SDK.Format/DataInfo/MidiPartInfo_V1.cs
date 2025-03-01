using TuneLab.SDK.Base;

namespace TuneLab.SDK.Format.DataInfo;

public class MidiPartInfo_V1 : PartInfo_V1
{
    public double Gain { get; set; } = 0;
    public VoiceInfo_V1 Voice { get; set; } = new VoiceInfo_V1();
    public List<EffectInfo_V1> Effects { get; set; } = [];
    public List<NoteInfo_V1> Notes { get; set; } = [];
    public Dictionary<string, AutomationInfo_V1> Automations { get; set; } = [];
    public List<List<AutomationPointInfo_V1>> Pitch { get; set; } = [];
    public List<VibratoInfo_V1> Vibratos { get; set; } = [];
    public PropertyObject_V1 Properties { get; set; } = [];
}
