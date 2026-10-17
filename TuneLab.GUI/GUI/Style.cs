using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;

namespace TuneLab.GUI;

internal static class Style
{
    public static readonly Color TRANSPARENT = new(0, 0, 0, 0);
    public static readonly Color WHITE = new(255, 21, 21, 30);
    public static readonly Color LIGHT_WHITE = new(255, 52, 53, 66);
    public static readonly Color BLACK = new(255, 0, 0, 0);
    public static readonly Color TOOL_TIP_BACK = new(255, 255, 255, 255);
    public static readonly Color DARK = new(255, 198, 200, 212);
    public static readonly Color BACK = new(255, 212, 213, 224);
    public static readonly Color INTERFACE = new(255, 244, 244, 248);
    public static readonly Color LINE = new((int)(0.12 * 255), 20, 20, 40);
    public static readonly Color ITEM = new(255, 96, 108, 240);
    public static readonly Color HIGH_LIGHT = new(255, 52, 62, 206);
    public static readonly Color TEXT_NORMAL = new((int)(0.8 * 255), 21, 21, 30);
    public static readonly Color TEXT_LIGHT = new(255, 21, 21, 30);
    // 属性面板的字段标签：比值（LIGHT_WHITE）暗一档，与白色加粗的分组标题拉开层级——标签暗、值亮、分组最亮。
    public static readonly Color TEXT_LABEL = new((int)(0.9 * 255), 52, 53, 66);
    public static readonly Color WHITE_KEY = new(255, 250, 250, 252);
    public static readonly Color BLACK_KEY = new(255, 236, 237, 243);
    public static readonly Color BUTTON_PRIMARY = new(255, 96, 96, 200);
    public static readonly Color BUTTON_NORMAL = new(255, 206, 208, 226);
    public static readonly Color BUTTON_PRIMARY_HOVER = new(255, 78, 78, 184);
    public static readonly Color BUTTON_NORMAL_HOVER = new(255, 192, 195, 216);

    // 自绘标题栏的关闭键：悬浮红底 + 按下更深，随系统窗口关闭键的惯例（红底白叉）。
    public static readonly Color CLOSE_HOVER = new(255, 232, 17, 35);
    public static readonly Color CLOSE_PRESSED = new(255, 197, 15, 31);

    public static readonly Color AMP_NORMAL = new(255, 40, 176, 72);
    public static readonly Color AMP_DELAY = new((int)(0.5 * 255), 40, 176, 72);
    public static readonly Color AMP_WARN = new(255, 222, 165, 0);
    public static readonly Color AMP_PEAK = new(255, 226, 56, 56);

    // 合成状态条语义色（沿用习惯：灰=待合成 橙=合成中 绿=已合成 红=失败）；
    // 实色不透明，压在音符上也读得清。
    public static readonly Color SYNTHESIS_PENDING = new(255, 160, 164, 178);
    // 编排区 part 状态条的脏区间灰：比钢琴窗的 PENDING 更亮——part 选中时会叠白罩(@0.16)冲淡，且底色是轨道色，需更跳。
    public static readonly Color SYNTHESIS_DIRTY_PART = new(255, 188, 192, 204);
    public static readonly Color SYNTHESIS_SYNTHESIZING = new(255, 222, 140, 48);
    public static readonly Color SYNTHESIS_SYNTHESIZED = new(255, 60, 158, 98);
    public static readonly Color SYNTHESIS_FAILED = new(255, 210, 80, 80);
    // 编排区 part 状态条的失败红：同灰色脏区间一样需更跳——选中叠白罩冲淡 + 底是轨道色，故用高饱和的亮红。
    public static readonly Color SYNTHESIS_FAILED_PART = new(255, 255, 64, 64);
    // 降级琥珀（可播但非最终，如某级 effect 失败 passthrough）：黄于合成中橙、别于失败红，警示而非错误。
    public static readonly Color SYNTHESIS_DEGRADED = new(255, 214, 168, 40);
    // 软绿（声称完成/待下游的非最终内容，及合成中段的纵向进度水位）：暗于最终亮绿一档——
    // 明度档承载「声称/最终」语义，亮绿只能来自链尾音频事实。
    public static readonly Color SYNTHESIS_INTERIM = new(255, 156, 200, 172);
    // 编排区 part 状态条的降级琥珀：同上需更跳，用高饱和亮黄。
    public static readonly Color SYNTHESIS_DEGRADED_PART = new(255, 255, 204, 0);

    public static readonly string[] TRACK_COLORS =
    [
        "#737CE5",
        "#73B5E5",
        "#73E5DB",
        "#73E5A2",
        "#7DE573",
        "#B5E573",
        "#E5DB73",
        "#E5A273",
        "#E5737D",
        "#E573B5",
        "#DB73E5",
        "#A273E5",
    ];

    public static Color DefaultTrackColor => Color.Parse(TRACK_COLORS[0]);

    public static string GetNewColor(int index)
    {
        return TRACK_COLORS[index % TRACK_COLORS.Length];
    }
}
