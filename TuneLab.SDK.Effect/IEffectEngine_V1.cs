using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

public interface IEffectEngine_V1
{
    string PropertyConfig { get; }
    string AutomationConfig { get; }
    void Initialize(PropertyObject_V1 args);
    void Destroy();
    IEffectSynthesisTask_V1 CreateSynthesisTask(IEffectSynthesisInput_V1 input, IEffectSynthesisOutput_V1 output);
}
