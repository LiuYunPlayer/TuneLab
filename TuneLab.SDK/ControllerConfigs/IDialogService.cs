using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 宿主弹窗服务（宿主实现）：插件只描述弹什么，宿主按自身风格画出来——主题一致、确定 / 取消等按钮文字由宿主翻译，
// 标题与正文由插件自译。只在两处可用：按钮点击期间（经 IButtonClickContext.DialogService）、画布控件实例有效期内
//（经 ICanvasHost.DialogService）；以按钮 / 画布所在的窗口为父窗口模态显示，弹窗上标明来源插件。
//
// · 任意线程可调：宿主切到 UI 线程执行。
// · **不要同步阻塞等待**返回的 Task（.Result / .Wait()）：弹窗要用 UI 线程，在 UI 线程上阻塞即死锁。
// · 插件自己的界面（自己布局、自己管数据）与宿主无关：做成独立进程，用按钮启动即可，宿主不提供父窗口或模态。
public interface IDialogService
{
    // 提示（同时写日志）。
    Task ShowMessage(string title, string message);

    // 确认：用户点确定返回 true，取消 / 关窗返回 false。
    Task<bool> Confirm(string title, string message);

    // 进度：宿主弹出模态进度窗并调用 work，work 返回即关窗。进度窗恒带取消按钮：用户取消或应用退出时 token 取消
    //（不支持取消的 work 忽略它即可）。work 经 reporter 汇报进度；work 抛出的异常原样传回调用方。
    Task RunWithProgress(string title, Func<IProgressReporter, CancellationToken, Task> work);

    // 向用户索取输入（与脚本工具的 inputs 同一个概念、同一个表单窗）：宿主用 config 渲染对话框（与属性面板同一套
    // 控件）。确定返回**完整的**值——用户没改的字段为 config 声明的默认值（初始值就写在默认值里）；取消 / 关窗返回 null。
    // 表单里的数据与工程隔离、不进撤销。
    Task<PropertyObject?> PromptInputs(string title, ObjectConfig config);

    // 同上，但表单随填写的值重新声明（与属性面板一样：值一变就重新取 config）——用于条件字段（某项选了 X 才出现）等。
    // config 收到表单当前的值：只含用户改过的字段，读不到即默认值（同声明 context 的口径）。
    // 在主线程上调用；抛异常时宿主记日志、表单保持上一份 config。
    Task<PropertyObject?> PromptInputs(string title, Func<PropertyObject, ObjectConfig> config);
}

// 进度汇报（宿主实现）。任意线程可调、可高频，宿主按帧合并。
public interface IProgressReporter
{
    // fraction ∈ [0, 1]，null = 不确定进度（滚动条）——fraction 没有"保持不变"的写法：只更新文字时传入当前进度。
    // status = 进度条下方的一行文字（插件自译），null / 省略 = 保持上一次的文字（想清空传 ""）。
    void Report(double? fraction, string? status = null);

    // 当前阶段能否由用户取消（默认可以）：进入不能打断的关键段（替换文件、写索引……）前置 false，取消按钮变灰；
    // 出关键段再置 true。只管用户的取消按钮——应用退出时 token 照样取消，关键段仍须能应对进程被结束。
    void SetCancellable(bool cancellable);
}
