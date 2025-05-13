using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Core.Environment;

public interface ITuneLabContext
{
    IEnumerable<string> ExtensionDirectories { get; }
    int SampleRate { get; }
}
