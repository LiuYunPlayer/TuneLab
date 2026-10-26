namespace TuneLab.SDK;

// 一次按钮点击的上下文（宿主实现）：ButtonConfig.OnClick 的参数，只在这次点击执行期间有效——
// 从回调开始到它返回的 Task 完成为止；之后再用其中的服务会抛 InvalidOperationException。
public interface IButtonClickContext
{
    // 弹窗服务：插件**只能**在按钮点击期间弹窗，入口只有这里（见 IDialogService）。
    IDialogService DialogService { get; }
}
