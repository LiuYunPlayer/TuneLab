using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

// 自动化轨按时间取值的服务接口。voice 与 effect 共用：宿主按插件请求的时间点，
// 提供该自动化轨(含 vibrato/默认值等最终合成结果)的采样值。
public interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
