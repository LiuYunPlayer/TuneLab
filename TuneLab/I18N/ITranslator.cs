using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.I18N;

internal interface ITranslator
{
    string Translate(string text, IEnumerable<string> context);
}
