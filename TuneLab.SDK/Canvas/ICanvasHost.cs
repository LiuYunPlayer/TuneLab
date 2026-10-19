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

    // 本字段的当前值：有草稿时是草稿（见 SetValue），否则是工程里的值。多选时工程里的值是各目标的合并值：
    // 不一致处为多值标记（PropertyValue.IsMultiple），由插件决定怎么画。字段未设过时给声明的默认值。
    PropertyValue Value { get; }

    // 请求重画（合并到下一帧）。
    void Invalidate();

    // 编辑：操作期间的值只是草稿，提交时才进工程——
    // · SetValue：写草稿。宿主代为保管（不进工程、别处看不到），Value 随即返回它。有草稿期间视为面板仍在使用：
    //   所在窗口的属性面板不刷新、本实例不会被换掉。想在操作一开始就锁住画布（如按下时、还没改值），写入当前值即可。
    //   返回 false = 实例已作废，什么都没写。
    // · CommitValue：把草稿写进工程，成为一个撤销步骤（多选时写到每个目标），并结束编辑。没有草稿则什么都不做。
    // · DiscardValue：丢弃草稿，回到工程里的值，并结束编辑（如"取消"）。没有草稿则什么都不做。
    // 提交只由插件发起，宿主从不代为提交。草稿保不住时宿主将其丢弃：外部改了本字段的值（撤销、别处改了同一份数据）、
    // 面板被拆（切换 part 等）、画布离开界面（页签切走、窗口关闭）。
    bool SetValue(PropertyValue value);
    void CommitValue();
    void DiscardValue();
}
