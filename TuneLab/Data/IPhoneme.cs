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

    // 引擎声明的 per-phoneme 自定义属性（GetPhonemePropertyConfig）。**lazy 物化**：未编辑过的音素零开销
    // （绝大多数音素无属性）。读 Properties 即按需物化（编辑路径用）；只读消费（快照 / 合并）走 HasProperties
    // 闸门避免无谓物化。空容器 ≡ 无属性（HasProperties=false、持久化省略）。
    bool HasProperties { get; }
    DataPropertyObject Properties { get; }
}
