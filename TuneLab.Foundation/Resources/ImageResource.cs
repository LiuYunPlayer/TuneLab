namespace TuneLab.Foundation;

// 格式无关的图像资源引用：封闭层次——构造器 private protected，变体只能在本程序集内新增
// （宿主无法解释的外部自造变体在类型上不存在），宿主按变体 pattern match、未知变体走兜底。
// 变体按"数据形态"分型（如何取到数据：路径/字节流/URI…，纯加性扩展）；内容动不动（动图、
// 序列帧）由宿主解码定夺，不进类型。变体保持可序列化的数据形态（跨进程友好），不带行为成员。
public abstract class ImageResource
{
    private protected ImageResource() { }
}

// 路径变体：绝对路径（插件按自身包目录拼出），可指向图像文件或序列帧目录，宿主按需解码渲染。
public sealed class FileImageResource(string path) : ImageResource
{
    public string Path { get; } = path;
}
