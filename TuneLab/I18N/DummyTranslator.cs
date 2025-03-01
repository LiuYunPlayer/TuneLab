using System.Collections.Generic;

namespace TuneLab.I18N;

internal class DummyTranslator : ITranslator
{
    public string Translate(string text, IEnumerable<string> context)
    {
        return text;
    }
}
