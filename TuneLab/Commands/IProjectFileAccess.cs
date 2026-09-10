namespace TuneLab.Commands;

// 「把用户正看着的那份文档换成磁盘上的另一个工程」——`project open` 用的宿主能力。
//
// 【为什么不是直接在命令里读文件建工程】"当前文档"这个概念只存在于**有窗口的宿主**里：换工程要连带
// 换掉撤销栈、播放头、钢琴窗里开着的 part、崩溃恢复态，而这些全归 Editor 管。命令自己造一个 Project
// 塞进 ctx 只会得到一个"命令面以为换了、界面还是旧的"的分裂状态。
//
// headless 里整个为 null：那儿的工程在启动时就定了（`--project`），进程里也没有第二份文档可切。
// 命令据此如实拒绝，而不是造一个"这次调用之后 ctx.Project 就指向别处"的半截状态（`ctx.Project`
// 在 headless 是启动时捕获的那一个引用，换不掉）。
internal interface IProjectFileAccess
{
    // 当前文档有没有未保存的改动。`project open` 据此在**问用户之前**就拒绝——把用户没保存的活儿
    // 换掉是撤销栈救不回的事，不该只靠一次授权确认。
    bool IsSaved { get; }

    // 当前文档的落地路径；从未保存过则为空。
    string? Path { get; }

    // 有没有可以直接存回去的目标（路径存在且是工程格式）。`project save` 据此在动手前就拒绝，
    // 并指向 save-as——菜单那条路在这种情况下会转去弹文件选择器，而命令面没人应答那个框。
    bool HasSaveTarget { get; }

    // 打开一个工程文件。成功返回 null，失败返回**为什么**（不弹窗——回报走命令的结果）。
    string? Open(string path);

    // 保存。path 为空 = 存回当前路径；给了 path = 另存为，**工程的保存路径随之改到那里**
    // （与界面上的另存为同义，不是 `project export` 那种只写一份副本）。成功返回 null，失败返回原因。
    string? Save(string? path);
}
