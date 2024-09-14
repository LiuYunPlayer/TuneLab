using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;

namespace TuneLab.Extensions.Effect;

public interface IEffectEngine
{
    string PropertyConfig { get; }
    string AutomationConfig { get; }
    void Initialize(PropertyObject args);
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);
}
