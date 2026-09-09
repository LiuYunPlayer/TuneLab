using System;

namespace TuneLab.Input;

// 一条【动作】：用户在界面上能做的一次操作，加上外部触发它所需的全部判据。
// 注册表见 ActionRegistry；触发入口见 TuneLab/Commands/Handlers/ActionCommands.cs。
//
// 【为什么与快捷键分成两层】两个集合的判据不同：快捷键是**筛选**（这件事值不值得占一个手势），
// 动作面是**穷尽**（用户够得着的事是不是都够得着）。拿筛选过的集合当穷尽面的真源只会二选一地坏掉——
// 要么快捷键设置页被几百条没人想绑的东西灌满、"值得做快捷键"这层筛选死掉，要么命令面永远停在
// 能绑的那二十几条。故这里是穷尽面，Keymap 只**引用**动作 id 再补上手势与作用域。
// 「可绑」不另设布尔：**在 Keymap 里有条目就是可绑**（一份数据一处真源）。
//
// Execute 是捕获拥有者（Editor / PianoWindow / MainWindow）的闭包，直接读实时状态——与 KeyCommand
// 时代同一个理由：同一动作在不同现场自适应（Delete 删锚点还是删音符由 Execute 内部按实时状态分支）。
internal sealed class EditorAction
{
    // 稳定点分 id（`<域>.<动作>`，见 docs/keybinding-system.md §1.1）：Keybindings.json 的键、菜单绑定、
    // 命令面 `action run --id` 共用的唯一锚点，一经发布不改。显示名走翻译、可随时改；id 不动。
    public required string Id { get; init; }

    public required Func<string> DisplayName { get; init; }

    public required ActionKind Kind { get; init; }

    // 无参动作的执行。带**选择器参数**的动作把执行放在 Parameter.Execute（那一个接值），故这里可空：
    // 一条动作恰有一条执行路径，注册时校验（见 ActionRegistry.Register）。
    public Action? Execute { get; init; }

    // 非 null = 这条动作要一个【选择器参数】（一个动词 × 一个闭集里的成员），见 ActionParameter。
    // 【不给手势】带参动作不进 Keymap：一条绑定只有手势没有参数，v1 刻意不做"绑定携带参数"
    //（keybinding-system.md §10 把它留给 v2）。故这批动作只从命令面与界面入口触发。
    public ActionParameter? Parameter { get; init; }

    // 现在跑得动吗：null = 跑得动；否则返回**为什么跑不动**的一句英文（原样进 `action list` 与 `action run` 的回报）。
    //
    // 【为什么必须有这一层】Execute 全是自带静默守卫的闭包：instrument part 下的颤音工具直接 return、
    // 撤销栈空时什么也不做、剪贴板动词在两个编辑面都没焦点时是空操作。少了这一层，外部触发就会回报
    // "已执行"而实际什么都没发生——正是最该避免的假装成功。
    //
    // 判据【只写在这里】：ActionRegistry.Execute 先问它，不可用就不跑。故键盘路径与命令面看到的是同一份
    // 判据，不可能漂移（原先散在 Execute 里的守卫已上移到这里）。能与菜单的 IsEnabled 同源就同源
    // （如撤销/重做共用 ProjectDocument.Undoable()）。
    public Func<string?>? Unavailable { get; init; }

    // 这一刻触发会不会停在**需要人应答**的模态上（文件选择器、未保存确认、脚本入参窗）。
    // 外部驱动把这种框弹出来而无人回答，那件事就永远走不完（保存/打开都没发生），而框一直挂在用户
    // 屏幕上等着——故命令面默认拒绝、要显式声明"有人在场"才放行。
    // 【实测记录】它并不会连带挡住别的动作：原生文件选择器与自家 Dialog 都不阻塞 Dispatcher，经命令桥
    // 来的动作照样执行。别把理由写成"卡死整个界面"，那是不实的。
    // 条件性的（file.new 只在工程未保存时才问）故取判据而非常量。
    public Func<bool>? Prompts { get; init; }

    // 非 null = 这条动作**不从命令面触发**，这句话说清该走哪条命令。唯一现例：脚本工具（`script:*`）——
    // 它的执行早已由 `script run-saved` 承担（能传参数、走预览-回退闸门），动作面再开一条无参入口
    // 就是第二套写通道，两边语义迟早漂移。
    public string? RunElsewhere { get; init; }
}
