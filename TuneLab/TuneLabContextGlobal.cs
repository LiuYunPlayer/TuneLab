using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Core.Environment;

namespace TuneLab;

internal class TuneLabContextGlobal : ITuneLabContext
{
    public IEnumerable<string> ExtensionDirectories => Directory.GetDirectories(PathManager.ExtensionsFolder);
    public int SampleRate => AudioEngine.SampleRate.Value;
}
