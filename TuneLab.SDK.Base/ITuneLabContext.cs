namespace TuneLab.SDK.Base;

// 插件读宿主状态的注入式入口：host 为每个插件作用域注入一个实例。
// 当前仅暴露 Log；富成员（扩展目录 / 语言 / 采样率 等）按需增长，
// 遵循内核按需增长纪律——只在具体插件 API 真需要时克制扩展。
public interface ITuneLabContext
{
    ILog Log { get; }
}
