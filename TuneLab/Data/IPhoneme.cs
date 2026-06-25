using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal interface IPhoneme : IDataObject<PhonemeInfo>
{
    // 音素标称时长（秒）。辅音(StretchWeight=0)固定时长；元音(StretchWeight>0)时长为派生填充量、布局时忽略此值。
    // 位置不存：由布局按「元音起点对齐 note 起点、前辅音往左累积、元音填充到满末」派生（见 INote）。
    IDataProperty<double> Duration { get; }
    IDataProperty<string> Symbol { get; }
    // 弹性伸缩权重：0 = 刚性辅音（长度固定）；>0 = 可伸元音（吸收 note 伸缩 / 压缩、按权重分摊先让）。
    IDataProperty<double> StretchWeight { get; }
    // 是否前置音素（音节核之前的引导辅音）：true 从前置分界线往左累积；false（核 + 后辅音）往右、核填充。
    IDataProperty<bool> IsLead { get; }
}
