using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Core.Environment;
using TuneLab.I18N;

namespace TuneLab;

internal class TuneLabContextGlobal : ITuneLabContext
{
    public IEnumerable<string> ExtensionDirectories => Directory.GetDirectories(PathManager.ExtensionsFolder);
    public string Language => TranslationManager.CurrentLanguage.Value;
    public int SampleRate => AudioEngine.SampleRate.Value;
}
