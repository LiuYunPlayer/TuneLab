using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Effect;

public interface IEffectEngine
{
    string PropertyConfig { get; }
    string AutomationConfig { get; }
    void Initialize(string args);
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisData data);
}
