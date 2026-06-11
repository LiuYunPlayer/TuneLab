using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base.ControllerConfigs;

// 自动化轨配置(显示名/默认值/范围/颜色)。voice 与 effect 共用：声明一条可编辑自动化轨。
// 显示名走继承自 SliderConfig 的 DisplayText（缺省回退到该轨的 map key）。
public class AutomationConfig : SliderConfig
{
    public required string Color { get; init; }
}
