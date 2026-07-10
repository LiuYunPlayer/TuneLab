using TuneLab.Foundation;

namespace TuneLab.SDK;

// 钉死音素的存储形（per note，列表非空 = 整 note 钉死）。
// 用「时长 + 弹性权重」表示，而非锚/偏差或绝对时间：
//   · Duration：该音素的标称时长（秒）。辅音(StretchWeight=0)为固定时长；元音(StretchWeight>0)为原长——布局按缩放比
//     len/d = r^w 分配（单核时原长被抵消、恒填满核空间；多核时原长定彼此基准比例）。
//   · StretchWeight：弹性伸缩权重。0 = 刚性（辅音，长度固定）；>0 = 可伸（元音），缩放比 len/d = r^w（同权重等比保形、w 越大越剧烈）。
// 位置由布局派生、不存：音节的「元音起点」对齐 note 起点（歌唱对拍惯例），前辅音从元音起点往左依次累积
//   （故前辅音可任意加长、向 note 前自由伸展），元音 + 后辅音往右、元音填充到 note 满末（含 melisma）。
//   编辑某音素时只改其 Duration，累积布局令相邻音素整体平移（推挤）而非互相挤占长度。
//   跨 note 去重叠（后盖前 / 辅音簇压缩）由全局布局算法 PhonemeLayout 统一求解。
public class PhonemeInfo
{
    public string Symbol { get; set; } = string.Empty;
    public double Duration { get; set; }
    public double StretchWeight { get; set; }

    // per-phoneme 引擎自定义属性（GetPhonemePropertyConfigs 声明的轨）。无属性时为 null、不序列化（pay-as-you-go）。
    public PropertyObject? Properties { get; set; }
}
