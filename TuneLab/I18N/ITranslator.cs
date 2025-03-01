using System.Collections.Generic;

namespace TuneLab.I18N;

internal interface ITranslator
{
    string Translate(string text, IEnumerable<string> context);
}
