using System;
using System.Threading.Tasks;

namespace TuneLab.SDK;

// 按钮：点击执行插件代码（清理缓存、测试连接、启动插件自己的程序……）。config 族里第一个**没有值**的成员——
// 不实现 IValueConfig、不读写任何属性值：恢复默认 / preset / 设置落盘一律跳过它，CLI / agent 也设不了它。
// 构造函数全封，只走静态工厂（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。不带任何布局属性——
// 布局将来是独立的一层，不散落在叶子 config 上。
//
// 只能作为 ObjectConfig 的成员（任意嵌套深度，含列表元素里的对象）；直接当 Array / List 元素或
// ExtensibleObject 的可加键时宿主不支持（那些位置每一格都是一个值，按钮没有值可占）。
//
// 契约：
// · 闭包里**只捕获值**（PartId、note Id、已读出的属性值……），不要捕获声明 context / 视图——视图只在声明调用期间
//   有效，点击可能发生在几分钟以后。需要什么，在 GetXxxConfig 里先取出来再让闭包捕获。
// · 按钮不改工程数据（SDK 不给写入口）：它只动插件自己的状态。若因此改变了声明，经会话 context 的
//   InvalidatePart / InvalidateNotes 宣告；点击所在的面板宿主会在点击结束后自动重新询问，不必宣告。
// · 线程：宿主在主线程上调用 OnClick（与 Init / CreateSession / 声明函数 / 会话事件同线程），await 后默认回到主线程——
//   只在主线程访问的资源无需加锁。await 是交错点：期间宿主可能调用插件的其他方法（合成调度、声明、会话事件），
//   插件须保证状态跨 await 一致。别阻塞主线程（宿主拦不住）：瞬间操作同步做即可，耗时的 await 或放进 RunWithProgress。
// · 执行期间宿主照常可操作（非模态）：只有该按钮置灰、不可重入；用户可能编辑 / 删除工程内容，闭包捕获的 id 可能已失效。
// · 抛出的异常由宿主记日志并提示用户（标明来源插件）。需要弹窗用 click.DialogService（见 IButtonClickContext）。
public sealed class ButtonConfig : IControllerConfig
{
    // 按钮上的文字（插件自译）。行标题仍由 PropertyKey.DisplayText 提供。
    public string Text { get; private init; } = "";

    public Func<IButtonClickContext, Task> OnClick { get; private init; } = null!;

    private ButtonConfig() { }

    public static ButtonConfig Create(string text, Func<IButtonClickContext, Task> onClick)
        => new() { Text = text, OnClick = onClick ?? throw new ArgumentNullException(nameof(onClick)) };
}
