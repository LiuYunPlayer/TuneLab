using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// 能力位摘要的生成：把一份 introduction 变成一句话，写进内容寻址缓存（ExtensionSummaryCache）。
// 由 list_extensions 在渲染前调用——缺哪几条就补哪几条，补完再返回，故对 agent 而言 summary 就是
// 能力位自带的一个属性，它感知不到生成过程（也就没有对应的工具）。
//
// 【短文档直接采用作者原话，不调模型】introduction 归一化后已经足够短（≤ MaxSummaryChars）时，它本身
//   就是一句话说明——再让模型转述一遍既费钱又只会更差（转述必然丢信息，还引入编造的可能）。
//   这类条目标记 Verbatim=true，呈现时说"作者原话"而非"自动转述"——**出处比转述强，不该一律标成转述**。
//   实测这一条能消掉大多数请求：真正需要调模型的只剩长文档。
//
// 【不合批】曾考虑把几份 introduction 拼进一次请求以减少往返，否掉了：
//   · **串味**——同时喂多个同类插件（措辞高度相似），模型很容易把 A 的特性写进 B 的摘要；而这种错
//     "看起来完全合理"，是最难被发现的一类，与"宁可没有也不要错的"直接冲突；
//   · **失败从丢一条变成丢一批**——合批必须要结构化输出，模型稍有偏差整批解析失败；
//   · **长输入本身降质**——靠后的那几份注意力权重明显下降，常表现为越往后越笼统；
//   · 省的只是往返、不是 token（输入总量一样），而限流已由串行解决。
//   何况有了"短文档直接采用"之后，真需要调模型的恰恰是长文档——最不该合批的那种。
//
// 【串行】即便在一次 list_extensions 里要补十几条也不并发：那很容易撞上 provider 的频率限制，一旦 429
//   就整批白跑。串行 + 预算，超了如实回报还剩多少，让模型转告用户"稍后再问一次"。
internal static class ExtensionSummaryFiller
{
    // 「把这组消息发给当前模型，回我正文」。由 agent 侧栏提供（它持有会话）。
    public delegate Task<string?> Summarizer(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken);

    // 单次调用内的生成预算。超了就停手并如实报还剩多少——**不静默给半份**：模型得知道自己拿到的
    // 是不是全的，才谈得上转告用户"待会儿再问一次"。
    const int BudgetMilliseconds = 60000;

    // 模型回复里的取值标记。**只取这行标记之后的内容，没有标记就整条丢弃**——模型很爱先来一句
    // "我来帮你总结："或 "Sure, here is a one-line summary."，那种话长度合规、也不以 { 开头，
    // 只靠"prompt 里写了 no preamble"根本挡不住，会一路混进缓存被后来的每个会话读到。
    const string Marker = "SUMMARY:";

    // 逐个补齐这些 introduction 的摘要（已有缓存的跳过）。返回 (本次新增数, 仍缺失数)。
    public static async Task<(int Filled, int Remaining)> FillAsync(
        Summarizer? summarize, IReadOnlyList<string> introductionPaths, CancellationToken cancellationToken)
    {
        // 读一次快照再逐条查（别对每条都读一遍盘）。按内容键去重：两个条目指向同一份文案
        // （同一文档被两个包分发）时只该做一次。
        var cached = ExtensionSummaryCache.Read();
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in introductionPaths)
        {
            var key = ExtensionSummaryCache.ContentKey(path);
            if (key == null || cached.Get(key) != null)
                continue;   // 读不出内容 / 已有有效缓存（内容变过的话键就变了，等同失效）
            pending[key] = path;
        }
        if (pending.Count == 0)
        {
            ExtensionSummaryCache.Prune();   // 没什么可补也顺手清一次：卸载过的包留下的死条目该走了
            return (0, 0);
        }

        int filled = 0, remaining = 0;
        var clock = Stopwatch.StartNew();
        foreach (var path in pending.Values)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Log.Info("Failed to read introduction " + path + ": " + ex.Message); remaining++; continue; }

            // ① 够短 → 作者原话即摘要，零调用零风险。
            var verbatim = AsVerbatimSummary(text);
            if (verbatim != null)
            {
                if (ExtensionSummaryCache.Set(path, verbatim, true, LabelOf(path)))
                    filled++;
                else
                    remaining++;
                continue;
            }

            // ② 长文档才交给模型；没连模型 / 超预算 / 被取消 → 留着下次
            if (summarize == null || cancellationToken.IsCancellationRequested || clock.ElapsedMilliseconds >= BudgetMilliseconds)
            {
                remaining++;
                continue;
            }
            if (await FillOneAsync(summarize, path, text, cancellationToken))
                filled++;
            else
                remaining++;
        }

        // 收尾清一次死条目（不放进 Set：那样 N 条写入要扫 N 遍全部 introduction）。
        ExtensionSummaryCache.Prune();
        return (filled, remaining);
    }

    // introduction 本身就在预算内 → 它**原样**就是 summary（一行不删、一个标记不改）；否则 null，交给模型。
    //
    // 【判据只有"装不装得下"】与文体无关：短的结构化文档（几条要点、一张小参数表）本来就是极好的索引
    // 条目。曾错判成"出现列表/表格就交给模型"，那是拿文体当判据；也曾把整篇拍成一行，那是把结构当噪音
    // ——markdown 对模型不是噪音，它读得懂，而拍平会丢掉"这是几个并列项"和表格里名与值的对应。
    // 【为什么连"剔掉用不上的行"也不做】曾想删图片引用/代码块/锚点这类 agent 用不上的东西，其实多余：
    //   **预算本身就是过滤器**——噪音让文档变长，长了就装不下、自然交给模型。再叠一层"我认为哪些行
    //   没用"的判断，只会引入丢掉承重信息的风险；而且标着"作者原话"却已被删过几行，那个标签就不准了。
    static string? AsVerbatimSummary(string introduction)
    {
        var text = introduction.Replace("\r", string.Empty).Trim();
        return text.Length > 0 && text.Length <= ExtensionSummaryCache.MaxSummaryChars ? text : null;
    }

    static async Task<bool> FillOneAsync(Summarizer summarize, string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            // 与 agent 自己能看到的正文同一口径（含超长时的截断标记）——绝不用更少的信息去总结。
            if (text.Length > GetExtensionIntroductionTool.MaxIntroductionChars)
                text = text.Substring(0, GetExtensionIntroductionTool.MaxIntroductionChars)
                     + "\n\n… (introduction truncated; " + (text.Length - GetExtensionIntroductionTool.MaxIntroductionChars) + " more characters)";

            var reply = await summarize(BuildMessages(text), cancellationToken);
            var summary = Extract(reply);
            if (summary == null)
                return false;

            ExtensionSummaryCache.Set(path, summary, false, LabelOf(path));
            return true;
        }
        catch (Exception ex)
        {
            // 单份失败不拖累其余（限流/网络抖动都在此）；不做负缓存，下次调用自然重试。
            Log.Info("Failed to summarize " + path + ": " + ex.Message);
            return false;
        }
    }

    static string LabelOf(string path)
        => Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty) + "/" + Path.GetFileName(path);

    // 独立的一次性请求：不带工具声明、不带对话历史、不带主循环的系统提示（同会话标题的生成）。
    // 【要的是关键信息，不是潦草概括】这段文字是 agent **只凭它就摸清这个插件有哪些能力**的钥匙：
    //   它据此判断"这个插件能不能解决用户的问题"，确定要用了才回去读全文。压成一句空泛的
    //   "一个声源插件"反而会让它误判、进而白读全文——所以宁可多几句，把参数名、限制、前置条件留住。
    // 【不给硬性字数，只给预算】把字数写死常换来"数着字写"的生硬句子；说明预算与用途，让它自己权衡。
    // 【要求标记收尾】见 Marker：让客套话有地方去，我们只取标记之后那段。
    static IReadOnlyList<AgentMessage> BuildMessages(string introduction) =>
    [
        new AgentMessage
        {
            Role = AgentRole.System,
            Content = "You condense the documentation of ONE capability of a music editor plugin into an index entry that another AI assistant will read.\n"
                    + "That assistant uses your text to judge whether this capability can solve a user's problem, and only then goes and reads the full document. "
                    + "So keep the load-bearing facts: what it does, what it is for, the capabilities/parameters it exposes (names and defaults if given), "
                    + "and any requirement or limitation that would rule it in or out. Drop marketing, examples, code, install boilerplate and repetition. "
                    + "A few compact sentences are fine — being vague to be short is the one real failure here; do not exceed about "
                    + ExtensionSummaryCache.MaxSummaryChars + " characters.\n"
                    + "Write in the same language as the document. Short lines listing parameters or requirements are welcome where that is clearer than prose "
                    + "(the reader is another model — structure helps it); just keep it plain, with no headings, images, code or links.\n"
                    + "End your reply with:\n"
                    + Marker + " <the entry>\n"
                    + "Nothing after it. Anything you write before that marker is ignored. "
                    + "If the document says too little to be worth an entry, end with exactly: " + Marker + " NONE",
        },
        new AgentMessage { Role = AgentRole.User, Content = introduction },
    ];

    // 取标记之后的一句话并过防线。宁可没有摘要也不要一句错的——它会被后来的会话当作事实读到。
    // 【只丢弃、绝不截断】截出来的半句话，agent 之后每次读到都会困惑，而用户和开发者都不知情。
    static string? Extract(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // 取【最后一个】标记：模型偶尔会先复述一遍格式要求，取最后一个才是它真正的作答。
        var at = raw.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return null;   // 没按格式来 → 整条丢弃，客套话/长篇大论都在此被挡下

        // 不拍平换行：模型若用短行分点列出关键信息，那结构本身就是信息（呈现处按行缩进，见 list_extensions）。
        var text = raw.Substring(at + Marker.Length).Replace("\r", string.Empty).Trim();
        text = text.Trim('"', '\'', '“', '”', '「', '」', ' ');
        // 用宽于预算的 RejectOverChars 判，别拿告诉模型的那个数当拒收线——见其注释（1020 被永远丢弃的坑）。
        if (text.Length == 0 || text.Length > ExtensionSummaryCache.RejectOverChars)
            return null;
        if (text[0] == '{' || text[0] == '[')
            return null;
        if (text.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return null;   // 模型自己判定"这份文档没什么可总结的"
        return text;
    }
}
