using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 能力位一句话摘要的本地缓存——由宿主从作者的 introduction 生成（见 ExtensionSummaryFiller），
// 不是作者写的、也不给用户看。
//
// 【它解决什么】作者只写 introduction 全文（刻意没设 summary 字段：作者不知道模型要什么，写出来多半是
//   产品文案）。于是模型每要"扫一眼这台机器上都有些什么能力"，就得逐个把全文拉进上下文——装了十几个
//   插件时这一步比它真正要做的事还贵。宿主提前备好一句话，list_extensions 直接带上，只有真需要细节时
//   才回去拉全文。对 agent 而言 summary 就是能力位自带的属性，它感知不到生成过程。
//
// 【内容寻址】键 = introduction **文件内容**的哈希，不是包 id + 条目身份。三个好处都是白得的：
//   · 插件更新换了文案 → 哈希变 → 自动失效，绝不会拿旧摘要描述新版本（这是最要紧的一条：
//     一份过期的摘要比没有摘要更糟，模型会照着它向用户断言）；
//   · 语言变体天然分开（localizations 让不同语言指向不同文件，内容不同即不同键）；
//   · 多后缀 format 条目共用一份说明 → 共用一条摘要；卸载重装、甚至换个包名分发同一份文档也照样命中。
//   代价是文件本身不可读——故另存一个 Label 字段纯供人排查，**永不参与查找**。
//
// 【零内存态：每次都以磁盘为准】本类**不缓存任何东西在内存里**，每次读写都直接过盘。
//   起初是"内存权威、改动才落盘"（同 ParameterPinning / ScriptInputMemory），结果连栽两次：删掉文件后
//   不重建、把文件改坏后既不报错也不重建——两次都是内存里那份完好、于是认为"一条都不缺"。按个案去补
//   （先判 File.Exists、再判解析失败、还要维护"上次见到的修改时间"印记去猜有没有人动过）只会越描越黑，
//   而**把内存层去掉，这一整类同步问题就不存在了**。代价是每个操作多读一个几 KB 的小文件（读一次拿
//   快照、再逐条查，见 Snapshot），毫秒级，换掉一整类 bug 很划算。
//   （那几个存**用户数据**的仍适合内存权威——它们不该被外部改动牵着走；而这里存的是缓存，
//   动了缓存文件的意思本来就是"别用旧的了"。）
//
// 【不进 UI】详情窗渲染的是作者写的 introduction 全文。把自动生成的句子摆到用户面前，等于让宿主替
//   插件作者背书一段它没写过的话；模型自己用没这个问题——回报里如实标注了出处。
internal static class ExtensionSummaryCache
{
    // 摘要的**字符预算**——一条 summary 允许占多大。它同时是两件事的判据，这不是巧合而是定义：
    //   · 作者的 introduction **已在预算内** ⇒ 它本身就是 summary，原样直接采用；
    //   · 超出预算 ⇒ 才请模型把关键信息压进这个预算。
    // 定得比"一句话"宽：summary 是 agent **只凭短文字就摸清这个插件有哪些能力**的钥匙，潦草的一句
    // 概括反而会让它误判、进而白读全文。真需要细节时才走 get_extension_introduction。
    public const int MaxSummaryChars = 1000;

    // 兜底拒收线，**刻意宽于预算**：预算是告诉模型的目标与"作者原文装不装得下"的判据，而这条只用来挡
    // 离谱输出（回了一整篇、dump 了一段数据）。两者若取同一个数，模型写到 1020 就被整条丢弃、下次重试
    // 大概率还是 1020——那份文档会永远补不上。超预算一点点照收，超太多才判定它根本没在按要求做。
    // 超限一律**丢弃不缓存，绝不截断**：截出来的半句话，agent 之后每次读到都会困惑，而用户与开发者
    // 都不知情——那比没有摘要糟得多。
    public const int RejectOverChars = MaxSummaryChars * 3 / 2;

    // 一次读盘得到的只读快照。**要看多条就读一次快照再逐条查**，别对每条都调一次读盘——那不只是浪费，
    // 更会让同一次操作里前后几条读到文件的不同版本（等于把刚赶走的同步问题请回来一个小号）。
    // 故本类不提供 "Get(单个路径)" 那种便利方法：只有一条要查时，Read() 一次同样便宜。
    internal sealed class Snapshot(Dictionary<string, Entry> entries)
    {
        public Entry? Get(string? contentKey)
            => contentKey != null && entries.TryGetValue(contentKey, out var entry) ? entry : null;
    }

    public static Snapshot Read() => new(Load());

    // 写入一条摘要（覆盖同内容的旧摘要）。introduction 读不了 → 无从成键，回 false。
    public static bool Set(string? introductionPath, string summary, bool verbatim, string label)
    {
        var key = ContentKey(introductionPath);
        if (key == null)
            return false;

        var entries = Load();
        entries[key] = new Entry(summary, verbatim, label);
        Save(entries);
        return true;
    }

    // 清掉指向"当前已装扩展里不存在的 introduction"的条目：卸载/更新过的旧文案不会有人再查，
    // 留着只会让这个文件无上限地长。由补齐流程收尾时调一次（不放进 Set，否则 N 条写入要扫 N 遍）。
    public static void Prune()
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in ExtensionManager.LoadResults)
            foreach (var entry in package.Entries)
            {
                var key = ContentKey(entry.IntroductionPath);
                if (key != null)
                    live.Add(key);
            }

        // 一条 live 都没有时不清：那通常意味着扩展还没加载完（而非"全都卸载了"），照做会把整份缓存抹掉。
        if (live.Count == 0)
            return;

        var entries = Load();
        var stale = new List<string>();
        foreach (var key in entries.Keys)
            if (!live.Contains(key))
                stale.Add(key);
        if (stale.Count == 0)
            return;

        foreach (var key in stale)
            entries.Remove(key);
        Save(entries);
    }

    // introduction 文件内容的哈希（前 16 字节的十六进制，足够避免这个量级下的碰撞，且文件短好读）。
    // 同样不做进程内备忘：一次 SHA256 只有几十微秒，为省它而引入"上次见到的修改时间"那类状态，
    // 正是本类要摆脱的东西。
    public static string? ContentKey(string? introductionPath)
    {
        if (string.IsNullOrEmpty(introductionPath))
            return null;
        try
        {
            if (!File.Exists(introductionPath))
                return null;
            var hash = SHA256.HashData(File.ReadAllBytes(introductionPath));
            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to hash introduction " + introductionPath + ": " + ex.Message);
            return null;
        }
    }

    // 读盘取全表。文件不存在 / 内容坏掉 → 空表（= 冷缓存，重新生成即可）。
    // 坏文件只记一条 warning：模型重新总结一遍就好，不值得为此报错打扰任何人。
    static Dictionary<string, Entry> Load()
    {
        var path = PathManager.ExtensionSummariesFilePath;
        if (!File.Exists(path))
            return new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream);
            if (loaded != null)
                return new Dictionary<string, Entry>(loaded, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to load extension summaries: " + ex.Message);
        }
        return new Dictionary<string, Entry>(StringComparer.Ordinal);
    }

    static void Save(Dictionary<string, Entry> entries)
    {
        var path = PathManager.ExtensionSummariesFilePath;
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            // 原子写：先落临时文件再改名顶替。**这份缓存重建要花钱**（每条都是一次模型调用），
            // 而直接覆写时若在写到一半崩溃/断电，整份就成了半截 JSON、下次全数作废。
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonSerializerOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save extension summaries: " + ex);
        }
    }

    // Verbatim=true 表示这句就是【作者自己写的原话】（introduction 本身在预算内，原样采用、没调模型）——
    // 出处比转述强，呈现时该照实说，不能一律标成"自动转述"。
    // Label 纯供人翻这个文件时认得出是谁的摘要（内容寻址的键本身不可读），永不参与查找。
    internal sealed record Entry(string Summary, bool Verbatim, string Label);

    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
