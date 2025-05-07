using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Formats.DataInfo;

public class MidiPartInfo : PartInfo
{
    public double Gain { get; set; } = 0;
    public VoiceInfo Voice { get; set; } = new VoiceInfo();
    public List<EffectInfo> Effects { get; set; } = new();
    public List<NoteInfo> Notes { get; set; } = new();
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    public List<List<Point>> Pitch { get; set; } = new();
    public List<VibratoInfo> Vibratos { get; set; } = new();
    public PropertyObject Properties { get; set; } = [];
}
