using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

public class MidiPartInfo : PartInfo
{
    public double Gain { get; set; } = 0;
    public VoiceInfo Voice { get; set; } = new VoiceInfo();
    public List<EffectInfo> Effects { get; set; } = new();
    public List<NoteInfo> Notes { get; set; } = new();
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    public List<List<Point>> Pitch { get; set; } = new();
    public List<VibratoInfo> Vibratos { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
