using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 自动化轨配置(名称/默认值/范围/颜色)。voice 与 effect 共用：声明一条可编辑自动化轨。
public class AutomationConfig : SliderConfig
{
    public required string Name { get; init; }
    public required string Color { get; init; }
}
