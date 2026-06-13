using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Data;

internal interface IPhoneme : IDataObject<PhonemeInfo>
{
    IDataProperty<double> StartTime { get; }
    IDataProperty<double> EndTime { get; }
    IDataProperty<string> Symbol { get; }
    // 伸缩权重（锁定时随时长一并固定的用户数据，宿主拖伸 note 分配长度变化用）。
    IDataProperty<double> Weight { get; }
}
