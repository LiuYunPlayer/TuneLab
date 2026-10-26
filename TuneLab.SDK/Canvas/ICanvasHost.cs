using TuneLab.Foundation;

namespace TuneLab.SDK;

// 宿主交给画布控件的能力（宿主实现，随控件实例同生共死；实例作废后再写回一律不生效）。全部只在主线程上用。
public interface ICanvasHost
{
    // 画布区域的当前尺寸（逻辑像素）。宽度随面板变化，变化后宿主会重新调用 Render。
    double Width { get; }
    double Height { get; }

    // 设备像素比：一个逻辑像素对应多少个物理像素（随所在屏幕的缩放设置，如 1.5、2）。自己生成位图（FromPixelData）时
    // 按 Width × DevicePixelRatio 的像素宽度生成才清晰。变化后（窗口拖到另一块屏幕）宿主会重新调用 Render。
    double DevicePixelRatio { get; }

    // 本字段的当前值：有草稿时是草稿（见 SetValue），否则是工程里的值。多选时各目标的值完全相同就是那个值，
    // 有任何不同则整个值是多值标记（PropertyValue.IsMultiple），由插件决定怎么画。字段未设过时给声明的默认值。
    PropertyValue Value { get; }

    // 请求重画（合并到下一帧）。
    void Invalidate();

    // 编辑状态（是否正在编辑只有插件知道，宿主不猜）——
    // · BeginEdit / EndEdit 成对调用：在两者之间视为面板仍在使用，所在窗口的属性面板不刷新、本实例不会被换掉。
    //   计数、可嵌套（几种操作可能同时进行，如拖着点的同时按住方向键）：每个 BeginEdit 配一个 EndEdit，全部结束才解锁。
    //   多余的 EndEdit 什么都不做（宿主记一条错误）。作用域写法见 CanvasHostExtensions.EditScope。
    // · 与值无关：只锁住画布、不改值；改值走下面的草稿。
    void BeginEdit();
    void EndEdit();

    // 值：操作期间的值只是草稿，提交时才进工程——
    // · SetValue：写草稿。宿主代为保管（不进工程、别处看不到），Value 随即返回它。只收真实的值：Multiple 只是多选读出时
    //   "各对象不一致"的标记、不是值，写它返回 false 且什么都不写（宿主记一条错误）；没改值就别写。
    //   有草稿也视为面板仍在使用（与 BeginEdit 同时起作用、互不替代）。返回 false 也可能是实例已作废。
    // · CommitValue：把草稿写进工程，成为一个撤销步骤（多选时写到每个目标）。没有草稿则什么都不做。
    // · DiscardValue：丢弃草稿，回到工程里的值（如"取消"）。没有草稿则什么都不做。
    // 两者都只处理值，不结束 BeginEdit 开始的编辑。提交只由插件发起，宿主从不代为提交。
    // 实例作废时（外部改了本字段的值——撤销、别处改了同一份数据；面板被拆——切换 part 等；画布离开界面——页签切走、
    // 窗口关闭）草稿与编辑状态一并作废，此后的调用都不生效（EndEdit 也不再记错）。
    bool SetValue(PropertyValue value);
    void CommitValue();
    void DiscardValue();
}
