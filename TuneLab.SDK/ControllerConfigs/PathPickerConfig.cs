using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 选择目标：文件 / 文件夹。宿主按它决定开哪种系统选择器。
// 加成员是加性（产出方是插件、消费方只有宿主自己）——宿主侧 switch 一律留 default 兜底、按 File 处理，
// 这样老宿主遇到新目标类型也只是退化成文件选择器，不会崩。
public enum PathPickerTarget
{
    File,
    Folder,
}

// 文件类型过滤器：一条"显示名 + 通配"的过滤项（如 name="Executable"、patterns=["*.exe"]）。
// Name 由插件按当前语言自译（与其余 config 的 DisplayText 同约定，宿主不查表）；
// Patterns 是文件名通配（"*.exe" / "*.*"），跨平台语义由宿主的系统选择器决定。
public readonly struct FileTypeFilter(string name, IReadOnlyList<string> patterns)
{
    public string Name { get; init; } = name;
    public IReadOnlyList<string> Patterns { get; init; } = patterns;
}

// 路径选择：一个文本框 + 浏览按钮，值是路径字符串（存值形态与 TextBoxConfig 一致，仍是普通 string property）。
// 用于"引擎可执行文件 / 模型目录"这类必须让用户挑一次的设置——手敲路径易错，故给系统选择器。
// 构造函数全封，只走静态工厂 + 链式 Append/With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
//
// 宿主只负责"挑出来的路径写进属性"，**不校验路径存在性/可执行性**——插件自己在 ApplySettings / Init 时校验并报错，
// 因为合法与否只有插件知道（用户也可能手敲一个尚未安装的路径）。
public sealed class PathPickerConfig : IValueConfig<string>
{
    public string DefaultValue { get; private set; } = "";

    public PathPickerTarget Target { get; private set; } = PathPickerTarget.File;

    // 文件类型过滤器（按声明序）。空 = 不过滤（任意文件）；Target 为 Folder 时无意义、宿主忽略。
    public IReadOnlyList<FileTypeFilter> FileTypes { get; private set; } = [];

    // 选择对话框的标题（如 "Select neutrino.exe"）。null = 用系统默认标题。插件自译。
    public string? PickerTitle { get; private set; }

    private PathPickerConfig() { }
    PathPickerConfig Clone() => (PathPickerConfig)MemberwiseClone();

    public static PathPickerConfig CreateFile(string defaultValue = "") => new() { DefaultValue = defaultValue };
    public static PathPickerConfig CreateFolder(string defaultValue = "") => new() { DefaultValue = defaultValue, Target = PathPickerTarget.Folder };

    // 追加一条过滤器：CreateFile().AppendFileType("Executable", "*.exe")。隐藏 FileTypeFilter 细节。
    public PathPickerConfig AppendFileType(string name, params string[] patterns)
        => AppendFileType(new FileTypeFilter(name, patterns));
    public PathPickerConfig AppendFileType(FileTypeFilter fileType) { var c = Clone(); c.FileTypes = [.. FileTypes, fileType]; return c; }

    public PathPickerConfig WithPickerTitle(string title) { var c = Clone(); c.PickerTitle = title; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
