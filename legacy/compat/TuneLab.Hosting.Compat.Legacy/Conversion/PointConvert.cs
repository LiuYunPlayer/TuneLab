using System.Collections.Generic;
using System.Linq;
using LStruct = TuneLab.Base.Structures;
using PStruct = TuneLab.Foundation;

namespace TuneLab.Hosting.Compat.Legacy.Conversion;

// Point 跨代转换。Legacy/V1 Point 布局相同（两个连续 double），故热缓冲可零拷贝重解释；
// 但跨类型重新暴露为 IReadOnlyList<Point> 无法零分配（数组元素类型不可原地改），故曲线点逐点拷贝——
// 它落在冷设置路径（每 part/合成结果一次），可忽略。真正热路径 audio float[] 同类型直接共享引用、零拷贝。
internal static class PointConvert
{
    public static PStruct.Point ToV1(this LStruct.Point p) => new(p.X, p.Y);
    public static LStruct.Point ToLegacy(this PStruct.Point p) => new(p.X, p.Y);

    public static List<PStruct.Point> ToV1(this List<LStruct.Point> list)
        => list.Select(ToV1).ToList();

    public static List<LStruct.Point> ToLegacy(this List<PStruct.Point> list)
        => list.Select(ToLegacy).ToList();
}
