using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Effect;

public interface IEffectDirtyEvent_V1
{
    EffectDirtyType_V1 DirtyType { get; }
    void Accept();
}
