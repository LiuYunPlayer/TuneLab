using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Synthesizer;

namespace TuneLab.Extensions.Effect;

public interface IEffectSynthesisData
{
    Audio Audio { get; }
    PropertyObject Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);
}
