namespace TuneLab.Primitives.Resources;

// 格式无关的图像资源引用：v1 仅文件路径（绝对路径，插件按自身包目录拼出），宿主按需解码渲染。
// 路径形态天然可序列化（跨进程友好）；动图(GIF/APNG)支持是宿主渲染能力、可增量添加，不动本类型；
// 将来要内嵌字节流等其他来源时纯加性扩展（加属性/工厂），不破坏既有用法。
public sealed class ImageResource(string path)
{
    public string Path { get; } = path;
}
