using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

public interface IEffectSynthesisInput_V1
{
    MonoAudio_V1 Audio { get; }
    PropertyObject_V1 Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter_V1? automation);
}
