using System.Collections.Generic;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 宿主交给画布控件的能力（宿主实现，随控件实例同生共死；实例作废后再写回一律不生效）。全部只在主线程上用。
// 宿主不把值读回给控件：值在声明时就能取到（声明 context 带着每个选中对象各自的值），由工厂捕获交给控件——一个实例的
// 生命周期内工程里的值不变（变了就作废实例、按新声明重建，见 CanvasConfig）。编辑中的值（草稿）控件本就自己持有。
public interface ICanvasHost
{
    // 画布区域的当前尺寸（逻辑像素）。宽度随面板变化，变化后宿主会重新调用 Render。
    double Width { get; }
    double Height { get; }

    // 设备像素比：一个逻辑像素对应多少个物理像素（随所在屏幕的缩放设置，如 1.5、2）。自己生成位图（FromPixelData）时
    // 按 Width × DevicePixelRatio 的像素宽度生成才清晰。变化后（窗口拖到另一块屏幕）宿主会重新调用 Render。
    double DevicePixelRatio { get; }

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
    // · SetValue：写草稿。宿主代为保管（不进工程、别处看不到），提交时写进工程；控件照常画自己手里的这个值。只收真实的值：
    //   Multiple 不是值（多选不一致只是读出时的状态），写它返回 false 且什么都不写（宿主记一条错误）；没改值就别写。
    //   有草稿也视为面板仍在使用（与 BeginEdit 同时起作用、互不替代）。返回 false 也可能是实例已作废。
    // · CommitValue：把草稿写进工程，成为一个撤销步骤（多选时写到每个目标）。没有草稿则什么都不做。
    // · DiscardValue：丢弃草稿，回到工程里的值（如"取消"）。没有草稿则什么都不做。
    // 两者都只处理值，不结束 BeginEdit 开始的编辑。提交只由插件发起，宿主从不代为提交。
    // 实例作废时（外部改了本字段的值——撤销、别处改了同一份数据；面板被拆——切换 part 等；画布离开界面——页签切走、
    // 窗口关闭）草稿与编辑状态一并作废，此后的调用都不生效（EndEdit 也不再记错），控件也不再收到事件。
    bool SetValue(PropertyValue value);
    void CommitValue();
    void DiscardValue();

    // 弹出（就地的轻量输入，不模态）——打开期间也视为面板仍在使用（与 BeginEdit、草稿是三个独立的状态，任一存在即锁住画布；
    // 不占 BeginEdit 的计数）。同一时刻至多一个：再弹一个会先关掉前一个（前一个返回 null）。实例作废时弹出随之关闭、返回 null。
    // 返回值只交给插件，宿主不写值：要改值照常 SetValue + CommitValue。
    //
    // · PopupTextBox：在画布上弹出文本框，初始内容 text。框就是给定的矩形（画布坐标，同 Render），文字按 style 的对齐排在框内、
    //   字号同 style、字体随界面——与 ICanvasDrawing.DrawText 同一套（style 不传同样是默认样式），把画这段字的矩形与样式
    //   原样传过来，框就正好盖在字上。
    //   矩形应给"这段文字可用的地方"（如整个格子），而非当前文字的宽度：框不随内容变大，装不下时框内滚动（不自动折行）。
    //   太小时按对齐的锚点扩到一行高与最小宽度，宽高给 0 即"以该点为锚"。弹出后若超出屏幕，宿主挪进来。
    //   按键同宿主文本框：Enter 或点别处 = 确认，返回文字；Esc = 取消，返回 null。multiline 时 Shift+Enter 换行（文字里是 \n）。
    // · PopupMenu：在给定矩形下方弹出菜单（下方放不下则翻到上方；宽高给 0 即在该点弹出，如右键菜单）。选项同下拉框
    //  （ComboBoxItem：分组为子菜单、分隔线、叶子多时自动加搜索框）。选中返回该项的值，点别处或 Esc 关掉返回 null。
    //   既能当下拉选项，也能当操作菜单（值写成命令名，拿到后自己分派）。
    Task<string?> PopupTextBox(string text, Rect bounds, CanvasTextStyle? style = null, bool multiline = false);
    Task<PropertyValue?> PopupMenu(Rect anchor, IReadOnlyList<ComboBoxItem> items);

    // 弹窗服务（提示 / 确认 / 进度 / 输入表单）：与按钮点击拿到的是同一套（见 IButtonClickContext.DialogService），以画布
    // 所在的窗口为父窗口，弹窗上标明来源插件。本实例有效期内都能用（画布没有"一次点击"的边界），实例作废后再调抛
    // InvalidOperationException；弹窗打开期间同弹出一样视为面板仍在使用。
    IDialogService DialogService { get; }
}
