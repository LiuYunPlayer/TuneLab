namespace TuneLab.Scripting;

// 脚本面写【范围选区】的那道口子（`tl.setTrackSelection` / `setPianoSelection` 及其 clear）。
//
// 【为什么要一道口子而不是直接写数据层】范围选区不在工程数据里：它是编排区与钢琴窗各自持有的一段
// 编辑器态（tick×轨道 / tick 带），随窗口走、不随工程存、撤销栈里也没有它。故脚本层只拿一个由宿主
// 实现的写口——没有编辑器的进程（headless / 探测沙箱）里整个为 null，那几个 tl 方法据此**如实报错**，
// 而不是静默无效（那会让脚本以为选上了）。
//
// 与 `IEditorStateAccess`（读那一半）对偶：读走那边的四个访问器，写走这里的四个方法。
internal interface IScriptSelectionWriter
{
    // 编排区的 tick×轨道矩形。轨道号 **1-based、含两端**（与 tl.trackSelection() 读到的口径一致）。
    void SetTrackSelection(double startTick, double endTick, int startTrackNumber, int endTrackNumber);

    void ClearTrackSelection();

    // 钢琴窗的 tick 带：限当前打开的 part、贯穿全音高（与 tl.pianoSelection() 对偶）。
    void SetPianoSelection(double startTick, double endTick);

    void ClearPianoSelection();
}
