﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

public interface IEffectPropertyDirtyEvent_V1 : IEffectDirtyEvent_V1
{
    EffectDirtyType_V1 IEffectDirtyEvent_V1.DirtyType => EffectDirtyType_V1.Property;

    
}
