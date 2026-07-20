using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

public abstract class PartInfo
{
    public string Name { get; set; } = string.Empty;
    // 与数据层 Part 同形的几何模型（三原始字段 + 派生 Dur），三者单位均 = tick（PPQ 480）：
    // Pos = 锚点在全局时间线的位置（内容坐标原点，part 内 note/pitch/vibrato/automation 位置均以它为原点）；
    // StartOffset/EndOffset = 起点/终点相对锚点的有符号偏移。起点 = Pos + StartOffset，终点 = Pos + EndOffset。
    public double Pos { get; set; } = 0;
    // 0 = 锚点即起点（默认/未前向裁剪），>0 前向裁剪，<0 前向扩展。
    public double StartOffset { get; set; } = 0;
    public double EndOffset { get; set; } = 0;
}
