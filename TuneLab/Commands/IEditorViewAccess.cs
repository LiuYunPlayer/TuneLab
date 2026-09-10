using TuneLab.Data;

namespace TuneLab.Commands;

// 「把用户的视野挪到这里来」——`editor reveal` 用的宿主能力。
//
// 【为什么这不是把被否掉的视口捞回来】§11 否掉的是模拟滚轮缩放：那是以鼠标位置为轴心的连续手势，
// 外部没有鼠标，且**不改变任何结果**。它同时留下了一句话——真正有用的那件事是"让用户看到我改了哪里"，
// 它要一个 tick 或一个对象作参数。这个接口就是那件事，不是那条被否的。
//
// 【为什么是命令而不是带参动作】§5.6 自己写着"滚到某个 tick 要的是一个连续量，不是闭集里的一个成员"，
// 而且带必填参数的动作不可绑手势——把 ActionParameter 扩成能接一个数，走完一圈还是只能从命令面调。
//
// 【为什么只有 Editor 给得出】视野是**两个窗口各自的轴**（编排区与钢琴窗各有时间轴，一个有纵向轨道轴、
// 一个有音高轴），只有 Editor 同时够得着它们；headless 里整个为 null，命令据此如实回答"这里没有视野"。
//
// 各方法只做"挪"，不改工程数据、不动播放头、不进撤销栈——除了 OpenPart，那一条改的是用户的编辑目标，
// 故命令默认不调它（要显式的 open 参数）。
internal interface IEditorViewAccess
{
    // 钢琴窗此刻开着的 part（null = 没开）。命令据此回报"你要看的那个 part 不在钢琴窗里"。
    IPart? EditingPart { get; }

    // 把 [startTick, endTick] 挪进**两个窗**的时间轴视野（同 transport.gotoStart 的做法：
    // 两条时间轴一起走，否则用户在另一个窗里看到的还是原处）。放不下才缩小，永不放大。
    //
    // 返回**挪完之后**编排区时间轴上实际看得见的范围。回报里给出它，调用方据此知道整段有没有真装下——
    // 目标比最小缩放档还宽时装不下，那时不该让回报读起来像"都给你看到了"。
    // 【为什么由它返回而不是另设一个属性去读】挪动是动画，挪完之前读轴读到的是**挪之前**的视野。
    (double Start, double End) RevealTicks(double startTick, double endTick);

    // 把这条轨挪进编排区的纵向视野。轨号 1-based，在实现里换成视图的 0-based 行号——与
    // IScriptSelectionWriter 是同一处边界，故两个方向的口径不会分叉。
    // 这一条**只滚不缩**：一条轨的高度是用户自己调的，为"让它进视野"去改它没有道理。
    void RevealTrack(int trackNumber);

    // 把这段音高挪进钢琴窗的纵向视野。只有钢琴窗正开着目标 part 时才有意义，由命令决定调不调。
    void RevealPitches(double minPitch, double maxPitch);

    // 把钢琴窗切到这个 part。**这一条不只是挪视野**：它改的是用户的编辑目标（Ctrl+A 选什么、
    // 下一个按键落在哪），撤销栈救不回，故命令只在显式 open 时才调。
    void OpenPart(IPart part);
}
