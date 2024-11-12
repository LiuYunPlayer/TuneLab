using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class ConditionConfig_V1(IExpression_V1<IControllerConfig_V1?> expression)
{
    public IControllerConfig_V1? Config => expression.Result;
}

public static class ConditionConfig_V1Extensions
{
    public static ConditionConfig_V1 ToControllerConfig_V1(this IExpression_V1<IControllerConfig_V1?> expression)
    {
        return new ConditionConfig_V1(expression);
    }
}
