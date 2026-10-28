using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;

namespace TuneLab.Data;

// 参数面板 lane 的身份 = 字段在属性声明树里的路径。属性树里任何位置的字段都能进参数面板：对象 / 键控容器的成员（按键）、
// 定长数组 / 列表的元素（按下标），任意嵌套。
//
// 编码：各段之间 '/'，段内的 '~' 与 '/' 转义成 "~0" / "~1"（顶层字段的路径就是它的 id 本身——既有的钉选、工程里存的面板布局
// 原样认得）。下标段写成十进制数；一段是键还是下标不在编码里，按所在容器的声明（数组 / 列表 = 下标）解读。
// 列表下标的含义随用户增删、调序而变：值跟着元素走，lane 按下标排——有意接受（画布是属性树的一员，树怎么编由用户）。
internal static class LanePath
{
    public static string Encode(IEnumerable<string> segments)
        => string.Join("/", segments.Select(segment => segment.Replace("~", "~0").Replace("/", "~1")));

    public static string[] Decode(string path)
        => path.Split('/').Select(segment => segment.Replace("~1", "/").Replace("~0", "~")).ToArray();

    // 一段路径在标签名里的样子：键 = 它的显示名，下标 = 第几个（从 1 数）。
    public readonly record struct LabelPart(string Name, int? Number);

    // 声明树里的一个叶子字段：路径（编码好的）、各段的标签名、叶子的声明。
    public readonly record struct Leaf(string Path, IReadOnlyList<LabelPart> Label, IControllerConfig Config);

    // 遍历声明树里的全部叶子（不是对象 / 数组 / 列表 / 键控容器的字段）。按声明序，深度优先。按钮没有值，不算。
    public static IEnumerable<Leaf> Leaves(ObjectConfig root)
    {
        var segments = new List<string>();
        var label = new List<LabelPart>();
        foreach (var leaf in Walk(root.Properties, segments, label))
            yield return leaf;
    }

    static IEnumerable<Leaf> Walk(IReadOnlyOrderedMap<PropertyKey, IControllerConfig> members, List<string> segments, List<LabelPart> label)
    {
        foreach (var kvp in members)
        {
            segments.Add(kvp.Key.Id);
            label.Add(new(kvp.Key.DisplayText ?? kvp.Key.Id, null));
            foreach (var leaf in WalkNode(kvp.Value, segments, label))
                yield return leaf;
            segments.RemoveAt(segments.Count - 1);
            label.RemoveAt(label.Count - 1);
        }
    }

    static IEnumerable<Leaf> WalkNode(IControllerConfig config, List<string> segments, List<LabelPart> label)
    {
        switch (config)
        {
            case ObjectConfig obj:
                foreach (var leaf in Walk(obj.Properties, segments, label))
                    yield return leaf;
                break;
            case ExtensibleObjectConfig extensible:
                foreach (var leaf in Walk(extensible.Properties, segments, label))
                    yield return leaf;
                break;
            case ArrayConfig array:
                foreach (var leaf in WalkElements(array.Elements, segments, label))
                    yield return leaf;
                break;
            case ListConfig list:
                foreach (var leaf in WalkElements(list.Elements, segments, label))
                    yield return leaf;
                break;
            case ButtonConfig:
                break;
            default:
                yield return new Leaf(Encode(segments), label.ToArray(), config);
                break;
        }
    }

    static IEnumerable<Leaf> WalkElements(IReadOnlyList<IControllerConfig> elements, List<string> segments, List<LabelPart> label)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            segments.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            label.Add(new(string.Empty, i + 1));
            foreach (var leaf in WalkNode(elements[i], segments, label))
                yield return leaf;
            segments.RemoveAt(segments.Count - 1);
            label.RemoveAt(label.Count - 1);
        }
    }

    // —— 按路径在一份声明里找到叶子，并给出读写这条路径要走的每一步 ——

    // 一步：按键进对象（Key），或按下标进数组 / 列表的元素（Index，SeedDefaults = 这个数组各元素的默认值，写入时补齐用）。
    // Leaf 标出这一步之后就是叶子（读写落在这一步上）。
    public readonly record struct Step(string? Key, int Index, IReadOnlyList<PropertyValue>? SeedDefaults, bool IntoArray);

    // 一条已解析的路径：叶子的声明 + 从根往下的每一步。
    public sealed record Resolved(IControllerConfig Leaf, IReadOnlyList<Step> Steps);

    public static Resolved? Resolve(ObjectConfig root, string path)
    {
        var segments = Decode(path);
        var steps = new List<Step>();
        IControllerConfig node = root;
        foreach (var segment in segments)
        {
            switch (node)
            {
                case ObjectConfig obj when obj.Properties.TryGetValue(segment, out var member):
                    steps.Add(new(segment, -1, null, member is ArrayConfig or ListConfig));
                    node = member;
                    break;
                case ExtensibleObjectConfig extensible when extensible.Properties.TryGetValue(segment, out var entry):
                    steps.Add(new(segment, -1, null, entry is ArrayConfig or ListConfig));
                    node = entry;
                    break;
                case ArrayConfig or ListConfig when int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index):
                    var elements = node is ArrayConfig array ? array.Elements : ((ListConfig)node).Elements;
                    if (index >= elements.Count)
                        return null;
                    var element = elements[index];
                    steps.Add(new(null, index, elements.Select(e => e.GetDefaultValue()).ToList(), element is ArrayConfig or ListConfig));
                    node = element;
                    break;
                default:
                    return null;
            }
        }
        return node is ObjectConfig or ExtensibleObjectConfig or ArrayConfig or ListConfig or ButtonConfig ? null : new Resolved(node, steps);
    }

    // —— 沿路径读写一个属性对象（note.Properties / phoneme.Properties）——

    // 读：路径上缺的节点（没存过的对象、越界的元素）= 默认值。读不创建任何东西。
    public static PropertyValue Read(IDataPropertyObject root, IReadOnlyList<Step> steps, PropertyValue defaultValue)
    {
        var (parent, key) = Navigate(root, steps, create: false);
        return parent != null && key != null ? parent.GetValue(key, defaultValue) : defaultValue;
    }

    // 写：路径上缺的数组 / 列表元素先按元素默认值补齐（同侧栏数组的懒补齐：首次写入时整段落地），再写到叶子上。
    public static void Write(IDataPropertyObject root, IReadOnlyList<Step> steps, PropertyValue value)
    {
        var (parent, key) = Navigate(root, steps, create: true);
        if (parent != null && key != null)
            parent.SetValue(key, value);
    }

    // 清除：只移除叶子上存的值（回到默认值），不动路径上的容器。路径上缺东西 = 本就没有值，什么都不做。
    public static void Remove(IDataPropertyObject root, IReadOnlyList<Step> steps)
    {
        var (parent, key) = Navigate(root, steps, create: false);
        if (parent != null && key != null)
            parent.RemoveValue(key);
    }

    // 走到叶子的父节点，给出叶子在父节点里的键（数组元素的键 = 它的 token）。不创建时遇到缺的节点返回 (null, null)。
    static (IDataPropertyObject? Parent, string? Key) Navigate(IDataPropertyObject root, IReadOnlyList<Step> steps, bool create)
    {
        IDataPropertyObject node = root;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            bool last = i == steps.Count - 1;
            string key;
            if (step.Key != null)
            {
                key = step.Key;
            }
            else
            {
                // 进到数组 / 列表的第 Index 个元素：node 此刻就是那个数组。
                var array = (IDataPropertyArray)node;
                if (step.Index >= array.Count)
                {
                    if (!create)
                        return (null, null);
                    var seeds = step.SeedDefaults!;
                    for (int n = array.Count; n <= step.Index; n++)
                        array.Add(n < seeds.Count ? seeds[n] : PropertyValue.Null);
                }
                key = array.Tokens[step.Index];
            }

            if (last)
                return (node, key);
            // 导航是惰性的：读不存在的节点给空外观（读出默认值）、不创建；写时才按需建路径。
            node = step.IntoArray ? node.Array(key) : node.Object(key);
        }
        return (null, null);
    }

    // —— 标签名：最短不重名 ——
    // 默认 = 末段的显示名 + 路径上的序号（「音素混合 2」「曲线 2」「曲线 2-3」）；与别的标签重名才往前补一层祖先名，补到不重名
    //（「音素混合 2 · 曲线」，序号的位置见 Format）。顶层字段就是它的显示名本身。
    public static IReadOnlyList<string> ShortestLabels(IReadOnlyList<IReadOnlyList<LabelPart>> labels)
    {
        var depth = labels.Select(_ => 1).ToArray();
        var result = labels.Select((label, i) => Format(label, depth[i])).ToArray();
        for (int round = 0; round < 16; round++)
        {
            bool changed = false;
            foreach (var group in Enumerable.Range(0, result.Length).GroupBy(i => result[i]).Where(g => g.Count() > 1))
            {
                foreach (int i in group)
                {
                    if (depth[i] < Names(labels[i]).Count)
                    {
                        depth[i]++;
                        result[i] = Format(labels[i], depth[i]);
                        changed = true;
                    }
                }
            }
            if (!changed)
                break;
        }
        return result;
    }

    // 完整的标签名（悬停提示、动作面的取值说明用）：各段的显示名全部拼起来。
    public static string FullLabel(IReadOnlyList<LabelPart> label) => Format(label, int.MaxValue);

    static List<string> Names(IReadOnlyList<LabelPart> label) => label.Where(p => p.Number == null).Select(p => p.Name).ToList();

    // 序号跟在它所属的那一段后面（"Mix 2 · Curve"：2 是 Mix 的第 2 项）；没显示出来的祖先段的序号并到第一段显示的段上，
    // 一个都不丢（"Curve 2"、两层数组 "Shape 2-3"）——序号是区分各项的唯一依据。
    static string Format(IReadOnlyList<LabelPart> label, int names)
    {
        var segments = new List<(string Name, List<int> Numbers)>();
        var carried = new List<int>();
        foreach (var part in label)
        {
            if (part.Number is not { } number)
                segments.Add((part.Name, new List<int>()));
            else if (segments.Count > 0)
                segments[^1].Numbers.Add(number);
            else
                carried.Add(number);
        }

        int skip = Math.Max(0, segments.Count - names);
        for (int i = 0; i < skip; i++)
            carried.AddRange(segments[i].Numbers);

        var text = new StringBuilder();
        for (int i = skip; i < segments.Count; i++)
        {
            var numbers = i == skip ? carried.Concat(segments[i].Numbers).ToList() : segments[i].Numbers;
            if (text.Length > 0)
                text.Append(" · ");
            text.Append(segments[i].Name);
            if (numbers.Count > 0)
                text.Append(' ').Append(string.Join("-", numbers.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        return text.ToString();
    }
}
