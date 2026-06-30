using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// merge 通知的上行语义钉死：merge 期间「结果态(settled) Modified」不再以中间态身份漏给祖先，
// 而是在 merge 收口时沿父链补发一次——使「settled = 用户提交」对每个观察者（含域外祖先）都成立。
// 全量通道(canIgnore)仍逐次可见中间态。嵌套（子 merge 深于父）按计数处理：仅最外层收口才触发。
public class DataObjectMergeNotifyTests
{
    // 最小容器结点：仅作父/祖先承载变更上行，自身不持值。
    sealed class Node : DataObject
    {
        public Node(IDataObject? parent = null) : base(parent) { }
    }

    // 订阅一个结点的两条通道，分别计数（settled = 无参；全量 = 带 canIgnore，记录每次的 canIgnore）。
    sealed class Probe
    {
        public int Settled;
        public int All;
        public readonly List<bool> CanIgnores = new();

        public Probe(DataObject node)
        {
            node.Modified.Subscribe(() => Settled++);
            node.Modified.AsEverytime().Subscribe((bool canIgnore) => { All++; CanIgnores.Add(canIgnore); });
        }
    }

    // 滑条式：merge 开在被编辑结点本身（叶）。祖先 settled 应在收口时一次，而非拖拽期每步。
    [Fact]
    public void Merge_OnEditedLeaf_AncestorSettledFiresOnceAtCommit()
    {
        var root = new Node();
        var mid = new Node(root);
        var leaf = new DataStruct<double>();
        leaf.Attach(mid);

        var midProbe = new Probe(mid);
        var leafProbe = new Probe(leaf);

        leaf.BeginMergeNotify();
        leaf.Set(1);
        leaf.Set(2);
        leaf.Set(3);
        Assert.Equal(0, midProbe.Settled);    // 拖拽期：祖先 settled 不触发
        Assert.Equal(0, leafProbe.Settled);

        leaf.EndMergeNotify();
        Assert.Equal(1, midProbe.Settled);     // 收口：祖先 settled 恰一次
        Assert.Equal(1, leafProbe.Settled);
        Assert.Equal(3.0, leaf.Value);
    }

    // SetInfo 式：merge 开在祖先(mid)、编辑其子(leaf)，L≠X。各级 settled 仍在收口各一次。
    [Fact]
    public void Merge_OnAncestor_EditChild_AllLevelsSettledOnceAtCommit()
    {
        var root = new Node();
        var mid = new Node(root);
        var leaf = new DataStruct<double>();
        leaf.Attach(mid);

        var rootProbe = new Probe(root);
        var midProbe = new Probe(mid);
        var leafProbe = new Probe(leaf);

        mid.BeginMergeNotify();
        leaf.Set(1);
        leaf.Set(2);
        Assert.Equal(0, rootProbe.Settled);
        Assert.Equal(0, midProbe.Settled);
        Assert.Equal(0, leafProbe.Settled);

        mid.EndMergeNotify();
        Assert.Equal(1, leafProbe.Settled);    // 域内：下行补发
        Assert.Equal(1, midProbe.Settled);     // 域内根：下行补发
        Assert.Equal(1, rootProbe.Settled);    // 域外祖先：上行补发
    }

    // 嵌套：父 merge 一次后子再 merge 一次（子 flag=2 > 父=1）。解子 merge 时父仍在 merge，不应触发；
    // 仅最外层（父）收口才补发一次。
    [Fact]
    public void NestedMerge_ChildDeeperThanParent_FiresOnlyAtOutermostClose()
    {
        var root = new Node();
        var mid = new Node(root);
        var leaf = new DataStruct<double>();
        leaf.Attach(mid);

        var rootProbe = new Probe(root);
        var midProbe = new Probe(mid);

        mid.BeginMergeNotify();      // mid、leaf flag = 1
        leaf.BeginMergeNotify();     // leaf flag = 2
        leaf.Set(1);
        leaf.Set(2);

        leaf.EndMergeNotify();       // leaf flag 2→1：仍在父 merge 下，不触发
        Assert.Equal(0, midProbe.Settled);
        Assert.Equal(0, rootProbe.Settled);

        mid.EndMergeNotify();        // 最外层收口：全 flag→0，补发一次
        Assert.Equal(1, midProbe.Settled);
        Assert.Equal(1, rootProbe.Settled);
    }

    // 全量通道：祖先逐次收到中间态(canIgnore=true)，收口收到一次结果态(false)。
    [Fact]
    public void Merge_FullChannel_SeesIntermediatesThenSettled()
    {
        var root = new Node();
        var leaf = new DataStruct<double>();
        leaf.Attach(root);

        var rootProbe = new Probe(root);

        leaf.BeginMergeNotify();
        leaf.Set(1);
        leaf.Set(2);
        leaf.EndMergeNotify();

        Assert.Equal(new[] { true, true, false }, rootProbe.CanIgnores);   // 两次中间态 + 一次结果态
        Assert.Equal(1, rootProbe.Settled);                                // settled 仅末尾一次
    }

    // 无 merge：普通编辑的 settled 立即逐次上行（保持原语义）。
    [Fact]
    public void NoMerge_NormalEdit_BubblesSettledImmediately()
    {
        var root = new Node();
        var leaf = new DataStruct<double>();
        leaf.Attach(root);

        var rootProbe = new Probe(root);

        leaf.Set(1);
        leaf.Set(2);
        Assert.Equal(2, rootProbe.Settled);    // 每次提交各一发
    }

    // undo/redo 重放：合并编辑提交后，撤销/重做各应使祖先 settled 恰触发一次（收口经命令重放正确补发）。
    [Fact]
    public void Merge_UndoRedoReplay_FiresSettledOncePerDirection()
    {
        var doc = new DataDocument();
        var mid = new Node(doc);
        var leaf = new DataStruct<double>();
        leaf.Attach(mid);

        leaf.BeginMergeNotify();
        leaf.Set(1);
        leaf.Set(2);
        leaf.EndMergeNotify();
        doc.Commit();
        Assert.Equal(2.0, leaf.Value);

        var midProbe = new Probe(mid);

        doc.Undo();
        Assert.Equal(0.0, leaf.Value);
        Assert.Equal(1, midProbe.Settled);     // 撤销：收口补发一次

        doc.Redo();
        Assert.Equal(2.0, leaf.Value);
        Assert.Equal(2, midProbe.Settled);     // 重做：再一次
    }
}
