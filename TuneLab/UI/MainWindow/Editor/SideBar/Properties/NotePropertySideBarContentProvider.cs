using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Configs;

namespace TuneLab.UI;

// Note 作用域属性侧栏：Note / Phoneme。与 part 作用域面板（见 PartPropertySideBarContentProvider）拆为两个独立页签，
// 两者各自订阅当前 part 的数据层事件、互不引用。note config 依赖 part 值，故本面板也订阅 part 属性 commit 沿链重算。
internal class NotePropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Note.GetImage(Style.LIGHT_WHITE), Name = "Note".Tr(TC.Property), Items = [mNotePanel, mPhonemePanel] };

    public NotePropertySideBarContentProvider()
    {
        var noteName = new Label() { Content = "Note".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mNotePanel.Title = noteName;
        mNoteContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mNoteContent.Children.Add(mNotePropertiesController);
        mNoteContentPanel.Children.Add(mNoteContent);
        mNoteContentPanel.Children.Add(mNoteContentMask);
        mNotePanel.Content = mNoteContentPanel;

        var phonemeName = new Label() { Content = "Phoneme".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPhonemePanel.Title = phonemeName;
        mPhonemeContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPhonemeContent.Children.Add(mPhonemeRowsPanel);
        mPhonemePanel.Content = mPhonemeContent;
        // 有音素声明了属性（逐音素 config 非空、至少一行）才显示本面板；默认隐藏。
        mPhonemePanel.IsVisible = false;
    }

    public void SetPart(IMidiPart? part)
    {
        if (mPart != null)
        {
            s.DisposeAll();

            Terminate();
        }

        mPart = part;

        if (mPart != null)
        {
            mPart.SoundSource.Modified.Subscribe(OnConfigChnaged, s);
            mPart.Notes.SelectionChanged.Subscribe(OnNoteSelectionChanged, s);
            // part 属性 commit（结果态）→ 沿链重算 note 面板（note config 依赖 part 值）。
            mPart.Properties.Modified.Subscribe(OnPartPropertiesModified, s);

            Setup(mPart);
        }
    }

    void Setup(IMidiPart part)
    {
        RefreshNoteController();
        RefreshPhonemeController();
    }

    void Terminate()
    {
        mNotePropertiesController.ResetConfig();
        mNoteSub.DisposeAll();
        mNoteData = null;
        mPhonemeSub.DisposeAll();
        mPhonemeRowsPanel.Children.Clear();
        mPhonemePanel.IsVisible = false;
    }

    // part 值 commit：沿链触发 note 面板重算（note config 依赖 part 值），音素面板直接重建（part 值变可能改 schema/对齐）。
    void OnPartPropertiesModified()
    {
        if (mPart == null)
            return;

        ReconcileNoteController();
        RefreshPhonemeController();   // 音素面板：part 值变可能改 schema/对齐，直接重建（编辑为 settled 触发，无活拖拽）
    }

    // ---- 条件属性面板：config = f(context)，按当前值重算并 keyed-diff 到控件树 ----
    // note config 依赖 part 值 + 当前选中 note 的三态合并值。

    void ReconcileNoteController()
    {
        if (mNoteReconcilePending)
            return;
        mNoteReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mNoteReconcilePending = false;
            if (mPart == null || mNoteData == null)
                return;
            mNotePropertiesController.Reconcile(mPart.SoundSource.GetNotePropertyConfig(BuildNoteContext()));
        });
    }

    // 声明面活视图壳（引擎无关、调用级）：note 面板 → 单个所属 part + 各选中 note 列表
    //（宿主不替插件合并，插件按需遍历 .Merge()）。复用数据层 PartContext/PartNote（TuneLab.Data）。
    NotePropertyContext BuildNoteContext()
        => new(new PartContext(mPart!),
            mPart!.Notes.AllSelectedItems().Select(n => new PartContext.PartNote(n)).ToList());

    void OnConfigChnaged()
    {
        Terminate();
        if (mPart == null)
            return;

        Setup(mPart);
    }

    void OnNoteSelectionChanged()
    {
        RefreshNoteController();
        RefreshPhonemeController();   // 音素面板 scope = 选中 note 的音素
    }

    // 把 note 属性面板绑定到当前选中 note 集合（多选合一）。无选中则盖遮罩。
    // 值的下发/写回/撤销刷新由逐字段绑定承担，选中变化时整体重绑（数据对象变 → SetConfig 重建）。
    // 选中不变期间 note 值 commit 触发 ReconcileNoteController（数据对象不变 → keyed-diff 复用控件）。
    //
    // 重绑 defer 到下一 UI 调度并合并：框选过程中选中集每帧都变，逐次同步全量重建（SetConfig 清空+重建整棵控件树）
    // 会令数组/列表等变高控件每帧重排、视觉抖动。pending 标志把一拍内的多次选中变化并成一次重建。
    void RefreshNoteController()
    {
        if (mNoteRefreshPending)
            return;
        mNoteRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mNoteRefreshPending = false;
            RefreshNoteControllerNow();
        });
    }

    void RefreshNoteControllerNow()
    {
        mNoteSub.DisposeAll();
        if (mPart == null)
        {
            mNotePropertiesController.ResetConfig();
            mNoteData = null;
            mNoteContentMask.IsVisible = true;
            return;
        }

        // 无选中也绑空数据源（0 对象），让控件在遮罩下呈 Invalid 态而非被清空；
        // 遮罩仅压暗 + 挡交互、提示去选音符。
        var dataObjects = mPart.Notes.AllSelectedItems().Select(note => note.Properties).ToList();
        mNoteData = new MultipleDataPropertyObject(dataObjects);
        mNotePropertiesController.SetConfig(mPart.SoundSource.GetNotePropertyConfig(BuildNoteContext()), mNoteData);
        mNoteContentMask.IsVisible = dataObjects.Count == 0;
        mNoteData.Modified.Subscribe(ReconcileNoteController, mNoteSub);
    }

    // ---- 音素属性面板（scope = 选中 note 的钉死音素；引擎未声明音素属性即整面板隐藏）----
    // config 空（引擎默认不声明 phoneme 属性）→ 隐藏面板、不物化任何音素 Properties（pay-as-you-go）。
    // config 非空 → 绑定选中 note 的音素 Properties（多音素合一）、无音素则盖遮罩。
    void RefreshPhonemeController()
    {
        if (mPhonemeRefreshPending)
            return;
        mPhonemeRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mPhonemeRefreshPending = false;
            RefreshPhonemeControllerNow();
        });
    }

    // 一个选中 note 的音素声明信息：显示音素（钉死则 IPhoneme、否则合成）+ 逐音素 config + 核位置（leadCount）。
    readonly record struct PhonemeNoteInfo(INote Note, bool Pinned, int Count, int LeadCount, ObjectConfig?[] Configs);

    void RefreshPhonemeControllerNow()
    {
        mPhonemeSub.DisposeAll();
        mPhonemeRowsPanel.Children.Clear();
        if (mPart == null)
        {
            mPhonemePanel.IsVisible = false;
            return;
        }

        // 一次成批求 config（复用 note 声明上下文）：与各 note 显示音素扁平展开索引对齐，拆回各 note 的逐音素 config。
        // 同时算各 note 的核位置 leadCount（首个 IsLead=false 的下标；无非 lead 则 = count）。
        var configs = mPart.SoundSource.GetPhonemePropertyConfigs(BuildNoteContext());
        int flat = 0;
        var perNote = new List<PhonemeNoteInfo>();
        foreach (var note in mPart.Notes.AllSelectedItems())
        {
            bool pinned = note.Phonemes.Count > 0;
            int count = pinned ? note.Phonemes.Count : (note.SynthesizedPhonemes?.Length ?? 0);
            var cfgs = new ObjectConfig?[count];
            int leadCount = count;
            for (int i = 0; i < count; i++)
            {
                cfgs[i] = flat < configs.Count ? configs[flat] : null;
                flat++;
                bool isLead = pinned ? note.Phonemes[i].IsLead.Value : note.SynthesizedPhonemes![i].IsLead;
                if (!isLead && leadCount == count)
                    leadCount = i;   // 第一个非 lead = 核 = 对齐 0
            }
            perNote.Add(new PhonemeNoteInfo(note, pinned, count, leadCount, cfgs));
            // 钉死/清除音素结构变化 → 重建（即便当前无行也订阅，使后续钉死/清除刷新面板）。
            note.Phonemes.MembershipModified.Subscribe(RefreshPhonemeController, mPhonemeSub);
        }

        // 对齐索引 = 音素位置 − leadCount（核 = 0、前置辅音离核越近越靠 −1、核后 = +1+）。各 note 按此有符号索引对齐成 slot。
        int minA = 0, maxA = 0;
        foreach (var n in perNote)
        {
            if (n.Count == 0) continue;
            minA = Math.Min(minA, -n.LeadCount);
            maxA = Math.Max(maxA, n.Count - 1 - n.LeadCount);
        }

        for (int a = minA; a <= maxA; a++)
        {
            // 该 slot 各 note 在对齐位 a 处的音素成员（config 非空者）。
            var members = new List<(PhonemeNoteInfo Note, int Index)>();
            foreach (var n in perNote)
            {
                int idx = n.LeadCount + a;
                if (idx < 0 || idx >= n.Count) continue;
                if (n.Configs[idx] is not { } c || c.Properties.Count == 0) continue;
                members.Add((n, idx));
            }
            if (members.Count == 0) continue;

            // IsLead 用标签底色区分：前置辅音（a<0，核前）= 非选中 note 色铺垫；核及核后（a>=0，核=0=音符头锚点）= 选中 note 高亮色。
            bool isLead = a < 0;

            var config = members[0].Note.Configs[members[0].Index]!;   // 代表 config（首成员）

            // 符号三态：各成员符号全等显该符号、否则 (...)。
            var symbols = members.Select(m => m.Note.Pinned ? m.Note.Note.Phonemes[m.Index].Symbol.Value : m.Note.Note.SynthesizedPhonemes![m.Index].Symbol).Distinct().ToList();
            string symbol = symbols.Count == 1 ? (string.IsNullOrEmpty(symbols[0]) ? "-" : symbols[0]) : "(...)";
            var label = new Label() { Content = symbol, Width = 36, MinHeight = 28, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Foreground = Brushes.White, Background = (isLead ? Style.ITEM : Style.HIGH_LIGHT).ToBrush(), Padding = new(0) };
            var controller = new PropertyObjectController();

            if (members.All(m => m.Note.Pinned))
            {
                // 全钉死：各 note 该位音素真 .Properties → MultipleDataPropertyObject 三态合并 / 写扇出 / Head 委托首成员。
                var data = members.Select(m => (IDataPropertyObject)m.Note.Note.Phonemes[m.Index].Properties).ToList();
                controller.SetConfig(config, data.Count == 1 ? data[0] : new MultipleDataPropertyObject(data));
                foreach (var m in members)
                    m.Note.Note.Phonemes[m.Index].Properties.Modified.Subscribe(RefreshPhonemeController, mPhonemeSub);
            }
            else
            {
                // 含合成音素：每成员一个 buffer（共享一个 throwaway DataDocument 拿 Head）；pinned 成员 seed 真值、合成成员留空
                // → MultipleDataPropertyObject 三态（缺位=默认，参考 ListConfig）。松手提交时各 note 钉死（取不到就创建）+ buffer 写回。
                var doc = new DataDocument();
                var bufs = new List<IDataPropertyObject>(members.Count);
                var apply = new List<(INote Note, int Index, DataPropertyObject Buffer)>(members.Count);
                foreach (var m in members)
                {
                    var buf = new DataPropertyObject(doc);
                    if (m.Note.Pinned)
                    {
                        var ph = m.Note.Note.Phonemes[m.Index];
                        if (ph.HasProperties)
                            buf.SetInfo(ph.Properties.GetInfo());
                    }
                    bufs.Add(buf);
                    apply.Add((m.Note.Note, m.Index, buf));
                }
                IDataPropertyObject data = bufs.Count == 1 ? bufs[0] : new MultipleDataPropertyObject(bufs);
                controller.SetConfig(config, data);
                data.Modified.Subscribe(() => PinAndApply(apply), mPhonemeSub);
            }

            // 左右排布：音素符号标签固定宽列在左、属性控制器占满右侧（控制器多行时标签竖向居中拉伸对齐）。
            var row = new Grid() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(controller, 1);
            row.Children.Add(label);
            row.Children.Add(controller);
            mPhonemeRowsPanel.Children.Add(row);
        }

        mPhonemePanel.IsVisible = mPhonemeRowsPanel.Children.Count > 0;
    }

    // 编辑（松手提交）含合成音素的 slot：对涉及的每个 note 先钉死（LockPhonemes 幂等、保几何——"取不到就创建"=钉死），
    // 再把该成员 buffer 值写回该位音素属性，整体提交为一个撤销步。随后 Phonemes.MembershipModified 触发重建 → 该 slot 转真绑定。
    void PinAndApply(IReadOnlyList<(INote Note, int Index, DataPropertyObject Buffer)> members)
    {
        if (mPart == null)
            return;
        foreach (var (note, idx, buf) in members)
        {
            note.LockPhonemes();
            if (idx < note.Phonemes.Count)
                note.Phonemes[idx].Properties.SetInfo(buf.GetInfo());
        }
        mPart.Commit();
    }

    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPhonemeContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mNotePanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPhonemePanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNoteContentPanel = new();

    readonly PropertyObjectController mNotePropertiesController = new();
    // 音素属性逐 slot 呈现：按 IsLead 分界、核=0 的有符号对齐索引把各 note 音素合并成 slot，每 slot 一行（符号标签 + 控制器）。
    readonly StackPanel mPhonemeRowsPanel = new() { Orientation = Orientation.Vertical };

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    IMidiPart? mPart = null;
    MultipleDataPropertyObject? mNoteData = null;
    bool mNoteReconcilePending = false;
    bool mNoteRefreshPending = false;
    bool mPhonemeRefreshPending = false;
    readonly DisposableManager s = new();
    readonly DisposableManager mNoteSub = new();
    readonly DisposableManager mPhonemeSub = new();
}
