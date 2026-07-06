using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal interface IPart : IReadOnlyDataObject<PartInfo>, ITimeline, IDuration, IAudioSource, ISelectable, ILinkedNode<IPart>
{
    ITrack Track { get; set; }
    new IPart? Next { get; }
    new IPart? Last { get; }
    IDataProperty<string> Name { get; }
    // part 几何锚点模型：Pos = 锚点（内容原点），StartOffset/EndOffset = 起点/终点相对锚点的有符号偏移。
    // 三个原始字段各对应一个原子操作：移动改 Pos、拖左边缘改 StartOffset、拖右边缘改 EndOffset；
    // Dur（可见长度）派生 = EndOffset - StartOffset。内容坐标恒以 Pos 为原点，裁剪只改偏移、不重排内容。
    IDataProperty<double> Pos { get; }
    IDataProperty<double> StartOffset { get; }
    IDataProperty<double> EndOffset { get; }
    double Dur { get; }
    void Activate();
    void Deactivate();
}

internal static class IPartExtension
{
    public static double StartPos(this IPart part)
    {
        return part.Pos.Value + part.StartOffset.Value;
    }

    public static double EndPos(this IPart part)
    {
        return part.Pos.Value + part.EndOffset.Value;
    }
}
