using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.I18N;

internal class TranslationContext(IEnumerable<string> context) : ITranslationContext
{
    public static implicit operator TranslationContext(string[] context) => new(context);
    public static implicit operator TranslationContext(string context) => new([context]);

    public IEnumerable<string> TranslationContextKeys => context;
}
