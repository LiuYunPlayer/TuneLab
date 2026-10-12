using System.Threading;

namespace TuneLab.Data;

// 运行期身份发号（宿主进程内单调计数、不持久）：note / part 对象建时各领一个，作为 SDK 面 note Id / PartId 的来源。
// 全局计数而非按会话计：身份要跨会话重建（换声源 / 时基变更）保持不变，插件才能把会话里算出的派生状态与声明求值
// 对上号（见 IVoiceSynthesisContext.InvalidateNotes）。note 与 part 共用一个计数器，两类号码互不重叠——误把一种 id
// 当另一种用时是查无此号，而不是张冠李戴。对象可能在非数据线程构造（如工程加载），故原子递增。
internal static class RuntimeId
{
    public static string Next() => Interlocked.Increment(ref sNext).ToString();

    static long sNext;
}
