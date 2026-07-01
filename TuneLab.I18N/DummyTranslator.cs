using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.I18N;

internal class DummyTranslator : ITranslator
{
    public string Translate(string text, IEnumerable<string> context)
    {
        return text;
    }
}
