using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Format.DataInfo;

public class PhonemeInfo
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string Symbol { get; set; } = string.Empty;
    // 伸缩权重：锁定音素时随时长一并从合成产物固定下来（用户意图的一部分），
    // 宿主拖伸 note 时据此分配长度变化。旧工程缺省 0 → 宿主公式 Σw≤0 退化均匀缩放。
    public double Weight { get; set; }
}
