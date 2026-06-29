using TuneLab.Foundation;

namespace TuneLab.SDK;

// 钉死音素的存储形（per note，列表非空 = 整 note 钉死）。
// 用「时长 + 弹性权重」表示，而非锚/偏差或绝对时间：
//   · Duration：该音素的标称时长（秒）。辅音(StretchWeight=0)为固定时长；元音(StretchWeight>0)的时长是派生填充量
//     （= note 可用空间 − Σ辅音时长，按权重分摊），故元音的 Duration 仅作记录、布局时忽略。
//   · StretchWeight：弹性伸缩权重。0 = 刚性（辅音，长度固定）；>0 = 可伸（元音，吸收 note 伸缩 / 压缩，按权重分摊）。
// 位置由布局派生、不存：音节的「元音起点」对齐 note 起点（歌唱对拍惯例），前辅音从元音起点往左依次累积
//   （故前辅音可任意加长、向 note 前自由伸展），元音 + 后辅音往右、元音填充到 note 满末（含 melisma）。
//   编辑某音素时只改其 Duration，累积布局令相邻音素整体平移（推挤）而非互相挤占长度。
//   跨 note 去重叠（后盖前 / 辅音簇压缩）由全局布局算法 PhonemeLayout 统一求解。
public class PhonemeInfo
{
    public string Symbol { get; set; } = string.Empty;
    public double Duration { get; set; }
    public double StretchWeight { get; set; }
    // 是否前置音素（音节核之前的引导辅音）：true 从「前置分界线」往左累积；false（核 + 后辅音）往右、核填充。
    // 显式标记（不再用 w>0 推断），把「前后归属」与「弹性 StretchWeight」解耦。
    public bool IsLead { get; set; }

    // per-phoneme 引擎自定义属性（GetPhonemePropertyConfigs 声明的轨）。无属性时为 null、不序列化（pay-as-you-go）。
    public PropertyObject? Properties { get; set; }
}
