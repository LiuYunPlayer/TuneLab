using System.Linq;
using TuneLab.Foundation.Utils;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 插件级（注册单位）元数据：description.json 的 extensions[] 中每个元素，
// 或单插件简写时由顶层字段兜底（ExtensionDescription 继承本类）。
//
// 字段职责分离：
//   type       —— 必填，WHAT：决定派给哪个 manager、找哪种 attribute（format/voice/effect/资源类）。
//   assemblies —— 选填，WHERE：只是缩小扫描范围的性能提示。
//                 写了 → 只 load+扫这几个；没写(代码插件) → 扫文件夹内全部 dll 找 type 对应入口；
//                 没写(资源类 type) → 不加载代码，只登记。
//   platforms  —— 选填，平台过滤（同一包内不同插件可各自声明）。
internal class ExtensionInfo
{
    public string type { get; set; } = string.Empty;
    public string[] assemblies { get; set; } = [];
    public string[] platforms { get; set; } = [];

    public bool IsPlatformAvailable()
    {
        if (platforms.IsEmpty())
            return true;

        return platforms.Contains(PlatformHelper.GetOS()) | platforms.Contains(PlatformHelper.GetPlatform());
    }
}
