using Avalonia.Controls;
using Avalonia.Layout;
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
        // 两面板各一个"标脏→合拍重建"调度器：note 面板（结构=选中集变化整体重绑 SetConfig、值=config 沿链重算
        // keyed-diff 复用控件）、音素面板（结构=签名/成员变化整面板重建、值=时长/权重值框轻刷新，编辑中经 Suspended 抑制）。
        // 订阅回调一律只标脏不读数，读取集中在 flush 回调内（settled 状态上执行），见 ViewRefreshScheduler。
        mNoteScheduler = new(RefreshNoteControllerNow, ReconcileNoteControllerNow);
        mPhonemeScheduler = new(RefreshPhonemeControllerNow, RefreshPhonemeValues);

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
            // 重合成回填新 SynthesizedPhonemes（精确信号，不被进度/参数/音高 tick 带动）→ 音素面板按显示音素签名变化才重建。
            mPart.SynthesizedPhonemesChanged.Subscribe(OnSynthesizedPhonemesChanged, s);

            Setup(mPart);
        }
    }

    void Setup(IMidiPart part)
    {
        mNoteScheduler.InvalidateStructure();
        mPhonemeScheduler.InvalidateStructure();
    }

    void Terminate()
    {
        mNotePropertiesController.ResetConfig();
        mNoteSub.DisposeAll();
        mNoteData = null;
        mPhonemeSub.DisposeAll();
        mPhonemeRowsPanel.Children.Clear();
        mPhonemeValueRefreshers.Clear();
        mPhonemePanel.IsVisible = false;
        mPhonemeScheduler.Suspended = false;   // 在飞编辑的抑制态不跨 part 存留（提交回调随控件树废弃，可能不再触发复位）
        mPhonemeSignature = string.Empty;
    }

    // part 值 commit：沿链触发 note 面板重算（note config 依赖 part 值），音素面板直接重建（part 值变可能改 schema/对齐）。
    void OnPartPropertiesModified()
    {
        if (mPart == null)
            return;

        mNoteScheduler.InvalidateValues();        // note 面板：数据对象不变 → keyed-diff 复用控件
        mPhonemeScheduler.InvalidateStructure();  // 音素面板：part 值变可能改 schema/对齐，直接重建（编辑为 settled 触发，无活拖拽）
    }

    // ---- 条件属性面板：config = f(context)，按当前值重算并 keyed-diff 到控件树 ----
    // note config 依赖 part 值 + 当前选中 note 的三态合并值。

    // note 面板值级 flush：config 沿链重算 + keyed-diff 到控件树（数据对象不变、复用控件）。
    void ReconcileNoteControllerNow()
    {
        if (mPart == null || mNoteData == null)
            return;
        mNotePropertiesController.Reconcile(mPart.SoundSource.GetNotePropertyConfig(BuildNoteContext()));
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
        mNoteScheduler.InvalidateStructure();
        mPhonemeScheduler.InvalidateStructure();   // 音素面板 scope = 选中 note 的音素
    }

    // 合成音素回填（已是精确信号）。但它为 part 级、覆写所有 note，且每个分块完成各触发一次，
    // 故按显示音素签名（符号/IsLead/数量/钉死态）分流：签名变了才整面板重建，滤掉"非选中 note 的块完成"类空转；
    // 签名没变仍可能是"重合成产出同一音素但时长/权重变了"，降级为值刷新——典型如撤销音素长度编辑：解钉重建
    // 读到旧回声快照，稍后重合成回原值时签名未变，值框必须靠这次轻刷新纠正显示。
    // 签名读取是对当前数据的整体扫描（不经任何构建时下标快照），不违反"订阅回调只标脏不读数"的纪律。
    void OnSynthesizedPhonemesChanged()
    {
        if (mPart == null)
            return;

        var signature = PhonemeSignature();
        if (signature == mPhonemeSignature)
        {
            mPhonemeScheduler.InvalidateValues();
            return;
        }

        mPhonemeSignature = signature;
        mPhonemeScheduler.InvalidateStructure();
    }

    // 选中 note 的显示音素结构签名（决定面板布局的字段：钉死态 + 各音素符号 + IsLead）。值类属性不入签名——
    // 它们由各 slot 绑定 / Properties.Modified 单独刷新。
    string PhonemeSignature()
    {
        if (mPart == null)
            return string.Empty;

        return string.Join(";", mPart.Notes.AllSelectedItems().Select(note =>
            note.Phonemes.Count > 0
                ? "P" + string.Join("|", note.Phonemes.Select(p => p.Symbol.Value + (p.IsLead.Value ? "<" : ">")))
                : "S" + (note.SynthesizedPhonemes is { } synth ? string.Join("|", synth.Select(p => p.Symbol + (p.IsLead ? "<" : ">"))) : "")));
    }

    // 把 note 属性面板绑定到当前选中 note 集合（多选合一）。无选中则盖遮罩。
    // 值的下发/写回/撤销刷新由逐字段绑定承担，选中变化时整体重绑（数据对象变 → SetConfig 重建）。
    // 选中不变期间 note/part 值 commit 走 mNoteScheduler 值级（数据对象不变 → keyed-diff 复用控件）。
    //
    // 重绑经 mNoteScheduler 合拍：框选过程中选中集每帧都变，逐次同步全量重建（SetConfig 清空+重建整棵控件树）
    // 会令数组/列表等变高控件每帧重排、视觉抖动，调度器把一拍内的多次选中变化并成一次重建。
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
        mNoteData.Modified.Subscribe(mNoteScheduler.InvalidateValues, mNoteSub);
    }

    // ---- 音素编辑面板（scope = 选中 note 的显示音素，多 note 按核对齐合并成 slot）----
    // 每个有音素的 slot 出一行：符号列（可编、扇出）+（引擎声明了属性时）属性控制器。任一选中 note 有音素即显示面板。
    // 引擎自定义属性仍 pay-as-you-go：未声明则不物化、右侧无控制器，但符号列照常可编。
    // 一个选中 note 的音素声明信息：显示音素（钉死则 IPhoneme、否则合成）+ 逐音素 config + 核位置（leadCount）。
    readonly record struct PhonemeNoteInfo(INote Note, bool Pinned, int Count, int LeadCount, ObjectConfig?[] Configs);

    // 音素面板结构级 flush：整面板重建。编辑中抑制由 mPhonemeScheduler.Suspended 承担（保留脏位，提交复位时补排）。
    void RefreshPhonemeControllerNow()
    {
        mPhonemeSub.DisposeAll();
        mPhonemeValueRefreshers.Clear();
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
            note.Phonemes.MembershipModified.Subscribe(mPhonemeScheduler.InvalidateStructure, mPhonemeSub);
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
            // 该 slot 各 note 在对齐位 a 处的音素成员（**全部音素**，不再只取声明了属性者——符号编辑 / 增删对所有音素可用）。
            var members = new List<(PhonemeNoteInfo Note, int Index)>();
            foreach (var n in perNote)
            {
                int idx = n.LeadCount + a;
                if (idx < 0 || idx >= n.Count) continue;
                members.Add((n, idx));
            }
            if (members.Count == 0) continue;

            // IsLead 用底色区分：前置辅音（a<0，核前）= 非选中 note 色铺垫；核及核后（a>=0，核=0=音符头锚点）= 选中 note 高亮色。
            bool isLead = a < 0;

            // 符号（可编、扇出）：各成员符号全等显该符号、否则 (...)。双击编辑，提交即对各成员 LockPhonemes + set Symbol 成一个撤销步。
            var symbols = members.Select(m => m.Note.Pinned ? m.Note.Note.Phonemes[m.Index].Symbol.Value : m.Note.Note.SynthesizedPhonemes![m.Index].Symbol).Distinct().ToList();
            string displayed = symbols.Count == 1 ? symbols[0] : "(...)";
            var symbolField = BuildSymbolCell(displayed, members);

            // 引擎声明了属性的成员才建属性控制器（其余成员只有符号列 + 右键增删）。
            var propMembers = members.Where(m => m.Note.Configs[m.Index] is { } c && c.Properties.Count > 0).ToList();
            Control? controller = null;
            if (propMembers.Count > 0)
            {
                var config = propMembers[0].Note.Configs[propMembers[0].Index]!;   // 代表 config（首个有属性成员）
                var poc = new PropertyObjectController() { ShowSeparators = false };   // 分界由行单元统一拥有（见下），控制器不再自吐尾随分隔
                controller = poc;
                if (propMembers.All(m => m.Note.Pinned))
                {
                    // 全钉死：各 note 该位音素真 .Properties → MultipleDataPropertyObject 三态合并 / 写扇出 / Head 委托首成员。
                    var data = propMembers.Select(m => (IDataPropertyObject)m.Note.Note.Phonemes[m.Index].Properties).ToList();
                    poc.SetConfig(config, data.Count == 1 ? data[0] : new MultipleDataPropertyObject(data));
                    foreach (var m in propMembers)
                        m.Note.Note.Phonemes[m.Index].Properties.Modified.Subscribe(mPhonemeScheduler.InvalidateStructure, mPhonemeSub);
                }
                else
                {
                    // 含合成音素：每成员一个 buffer（共享一个 throwaway DataDocument 拿 Head）；pinned 成员 seed 真值、合成成员留空
                    // → MultipleDataPropertyObject 三态（缺位=默认，参考 ListConfig）。松手提交时各 note 钉死（取不到就创建）+ buffer 写回。
                    var doc = new DataDocument();
                    var bufs = new List<IDataPropertyObject>(propMembers.Count);
                    var apply = new List<(INote Note, int Index, DataPropertyObject Buffer)>(propMembers.Count);
                    foreach (var m in propMembers)
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
                    poc.SetConfig(config, data);
                    data.Modified.Subscribe(() => PinAndApply(apply), mPhonemeSub);
                }
            }

            // 行单元 = 顶部色条（IsLead 配色：符号靠左 + 时长/权重值框靠右，无文字标签、悬浮 tooltip）+（引擎声明属性时）其下属性面板。
            // 符号铺在音素自己面板顶部、值框紧凑靠右：长符号有整行宽度容纳；ms 单位本身已表达时长，无需文字标签；无引擎属性时右侧不再留空。
            var strip = new DockPanel() { Background = (isLead ? Style.ITEM : Style.HIGH_LIGHT).ToBrush() };
            var rightGroup = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new(0, 0, 4, 0) };
            rightGroup.Children.Add(BuildDoubleField("Duration".Tr(TC.Property), members,
                ph => ph.Duration, sp => sp.Duration, DurationConfig));
            rightGroup.Children.Add(BuildDoubleField("Stretch Weight".Tr(TC.Property), members,
                ph => ph.StretchWeight, sp => sp.StretchWeight, WeightConfig));
            DockPanel.SetDock(rightGroup, Dock.Right);
            strip.Children.Add(rightGroup);
            strip.Children.Add(symbolField);   // 占满左侧

            var panel = new StackPanel() { Orientation = Orientation.Vertical };
            panel.Children.Add(strip);
            if (controller != null)
                panel.Children.Add(controller);
            panel.ContextMenu = BuildSlotContextMenu(members);   // 右键 拆分 / 删除（整个音素面板均可触发，同对齐位扇出）
            mPhonemeRowsPanel.Children.Add(panel);
            // 行单元之间的整宽分界线：由行容器统一拥有，控制器内部分隔已关。
            mPhonemeRowsPanel.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        }

        mPhonemePanel.IsVisible = mPhonemeRowsPanel.Children.Count > 0;
        mPhonemeSignature = PhonemeSignature();   // 同步基线：此后 OnSynthesizedPhonemesChanged 按签名分流（变=重建、不变=值刷新）
    }

    // 音素面板值级 flush：逐值框轻刷新。调度器不变量保证仅在本拍结构干净时执行，
    // 各 Refresh 按构建时结构快照（Pinned/Index）读数因此是安全的。
    void RefreshPhonemeValues()
    {
        foreach (var refresh in mPhonemeValueRefreshers)
            refresh();
    }

    // 编辑（松手提交）含合成音素的 slot：对涉及的每个 note 先钉死（LockPhonemes 幂等、保几何——"取不到就创建"=钉死），
    // 再把该成员 buffer 值写回该位音素属性，整体提交为一个撤销步。随后 Phonemes.MembershipModified 触发重建 → 该 slot 转真绑定。
    // 音素符号：透明背景的 EditableLabel，铺在顶部色条左侧、靠左显示（长符号有整行宽度容纳）。底色由色条提供。
    // 复用标准 TextInput 编辑（padding 等一致），圆角 2。双击原地改符号，提交即扇出。
    Control BuildSymbolCell(string displayed, IReadOnlyList<(PhonemeNoteInfo Note, int Index)> members)
    {
        var label = new EditableLabel()
        {
            Height = 28,
            MinWidth = 40,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Brushes.Transparent,   // 透明，露出色条底色
            Foreground = Brushes.White,
            FontSize = 12,
            CornerRadius = new(2),
            Padding = new(16, 0),               // 符号左侧留多一点边距
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Text = displayed,
        };
        label.EndInput.Subscribe(() =>
        {
            var text = label.Text;
            if (text != displayed)   // 无变化（含多值未改的 "(...)"）不提交
                CommitSymbol(members, text);
        });
        return label;
    }

    // 内建 double 字段（时长 / 权重）：[标题 | 可拖数值框] 同行。复用引擎属性那套——每成员一个 buffer（共享 throwaway doc）
    // 经 MultipleDataPropertyObject 扇出 + 三态合并；DraggableNumberBox 的拖动 merge 经此 buffer 走，settled 时对各成员
    // LockPhonemes（合成→钉死）+ 写回内建字段 + 一个撤销步。read 取当前值（钉死/合成）、write 落到 Duration/StretchWeight。
    Control BuildDoubleField(string tooltip, IReadOnlyList<(PhonemeNoteInfo Note, int Index)> members,
        Func<IPhoneme, IDataProperty<double>> field, Func<SynthesizedPhoneme, double> synthValue, DraggableNumberBoxConfig config)
    {
        var box = new DraggableNumberBox
        {
            Width = 64,
            Height = 28,
            Margin = new(2, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            BoxBackground = null,           // 透明底：静态只显数值、露出色条；双击才出标准深色输入框（去掉色缝）
            TextForeground = Brushes.White, // 色条上用纯白，不降明度
            MinValue = config.Min,
            MaxValue = config.Max,
            Response = config.Response,
            Step = config.Step,
            NumberFormat = config.Format,
            DefaultValue = config.DefaultValue,
        };
        box.SetupToolTip(tooltip);

        if (members.All(m => m.Note.Pinned))
        {
            // 全钉死：直接绑定真实属性。BindDataProperty 一手包办"订阅数据→刷新显示 + 编辑→merge/commit/撤销"；
            // 中间态双向同步后，拖动经数据层通知钢琴窗实时重绘。多成员扇出 + 三态经 MultipleDataProperty。
            var props = members.Select(m => field(m.Note.Note.Phonemes[m.Index])).ToList();
            IDataProperty<double> data = props.Count == 1
                ? props[0]
                : new MultipleDataProperty<double>(props, config.DefaultValue, v => PropertyValue.Create(v));
            box.BindDataProperty(data, mPhonemeSub);
            return box;
        }

        // 含合成音素：合成音素无可绑定的 IDataProperty（值在 SynthesizedPhonemes[] 里、无 .Modified/.Set），又不能一显示就锁定。
        // 故手写"首拖固定"——镜像波形拖拽 op：起手对各成员 LockPhonemes（合成→钉死）+ BeginMergeDirty（只延迟重合成、不抑制
        // 数据通知，钢琴窗据 Notes.Phonemes.Modified 实时重绘）；每帧先取 box.Value 再 DiscardTo(head) 后扇出写回；松手 Commit
        //（无改 Discard 连带回滚锁定）成一个撤销步。编辑期 mPhonemeScheduler.Suspended 抑制面板重建。锁定提交后转全钉死 → 重建走上面绑定路径。
        double ReadOf((PhonemeNoteInfo Note, int Index) m)
            => m.Note.Pinned ? field(m.Note.Note.Phonemes[m.Index]).Value : synthValue(m.Note.Note.SynthesizedPhonemes![m.Index]);
        void Refresh()
        {
            var vals = members.Select(ReadOf).Distinct().ToList();
            if (vals.Count == 1)
                box.Display(vals[0]);
            else
                box.DisplayMultiple();
        }
        Refresh();
        // 后续刷新只标脏不读数，读取集中回本 Refresh（值级 flush 经 mPhonemeValueRefreshers 调回）。
        // Refresh 按构建时结构快照（Pinned/Index）读数是安全的：值级 flush 仅在本拍结构干净时执行（调度器不变量），
        // 任何结构变化（如改歌词 Lyric.Set → Phonemes.Clear 解钉）必然已标结构脏、本拍走整面板重建而非值刷新。
        mPhonemeValueRefreshers.Add(Refresh);
        // 钉死成员属性变化（含撤销回退）→ 值级标脏。仅钉死成员有可订阅属性（混合 slot 里也可能有）。
        foreach (var m in members)
            if (m.Note.Pinned)
                field(m.Note.Note.Phonemes[m.Index]).Modified.Subscribe(mPhonemeScheduler.InvalidateValues, mPhonemeSub);
        // 合成（未钉死）成员的值取自 SynthesizedPhonemes 快照、无可订阅的 IDataProperty：重合成回填新值
        //（签名未变、不触发重建）时由面板级 OnSynthesizedPhonemesChanged 分流为值级标脏，此处无需再订阅。

        Head editHead = default;
        box.ValueWillChange.Subscribe(() =>
        {
            if (mPart == null)
                return;
            mPhonemeScheduler.Suspended = true;
            foreach (var (n, _) in members)
                n.Note.LockPhonemes();
            mPart.BeginMergeDirty();
            editHead = mPart.Head;
        }, mPhonemeSub);
        box.ValueChanged.Subscribe(() =>
        {
            if (mPart == null)
                return;
            // 必须先取 box.Value：DiscardTo 回退数据会经 field.Modified→Refresh 把显示也改回原值，后取就读到原值（交替闪烁）。
            var v = box.Value;
            mPart.DiscardTo(editHead);
            foreach (var (n, idx) in members)
                if (idx < n.Note.Phonemes.Count)
                    field(n.Note.Phonemes[idx]).Set(v);
        }, mPhonemeSub);
        box.ValueCommitted.Subscribe(() =>
        {
            if (mPart == null)
                return;
            var head = mPart.Head;
            mPart.EndMergeDirty();
            if (head == editHead)
                mPart.Discard();
            else
                mPart.Commit();
            // 复位抑制（编辑期被扣下的脏位自动补排），并显式标结构脏：提交后锁定成立，本 slot 转全钉死绑定路径。
            mPhonemeScheduler.Suspended = false;
            mPhonemeScheduler.InvalidateStructure();
        }, mPhonemeSub);

        return box;
    }

    // 音素 slot 右键菜单：拆分 / 删除。两者同对齐位扇出（每个有该位的 note 各自做，无该位忽略），各成员先 LockPhonemes
    // （合成→钉死），整体一个撤销步。
    Avalonia.Controls.ContextMenu BuildSlotContextMenu(IReadOnlyList<(PhonemeNoteInfo Note, int Index)> members)
    {
        var menu = new Avalonia.Controls.ContextMenu();

        // 拆分：把该位音素一分为二，两段完全复制原音素（符号/IsLead/权重/引擎属性全同），仅时长平分——前半 d/2、后半 d−d/2
        //（此式保证两 double 相加严格 == 原长）。
        menu.Items.Add(new Avalonia.Controls.MenuItem().SetName("Split".Tr(TC.Menu)).SetAction(() =>
        {
            if (mPart == null)
                return;
            mPart.BeginMergeDirty();
            foreach (var (n, idx) in members)
            {
                var note = n.Note;
                note.LockPhonemes();
                if (idx >= note.Phonemes.Count)
                    continue;
                var ph = note.Phonemes[idx];
                double d = ph.Duration.Value;
                double firstHalf = d / 2;
                var info = ph.GetInfo();         // 复制 Symbol/Duration/StretchWeight/IsLead/Properties
                info.Duration = d - firstHalf;   // 后半段
                ph.Duration.Set(firstHalf);      // 原音素留前半段
                note.Phonemes.Insert(idx + 1, Phoneme.Create(info));
            }
            mPart.EndMergeDirty();
            mPart.Commit();
        }));

        // 删除：删该位音素；删空则该 note 回到合成音素口径（空钉死列表 ≡ 合成）。
        menu.Items.Add(new Avalonia.Controls.MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(() =>
        {
            if (mPart == null)
                return;
            mPart.BeginMergeDirty();
            foreach (var (n, idx) in members)
            {
                var note = n.Note;
                note.LockPhonemes();
                if (idx < note.Phonemes.Count)
                    note.Phonemes.RemoveAt(idx);
            }
            mPart.EndMergeDirty();
            mPart.Commit();
        }));

        return menu;
    }

    // 编辑某 slot 符号：对各成员 note 先钉死（LockPhonemes 幂等、合成→钉死保几何），再写该位音素 Symbol，整体提交为一个撤销步。
    // 随后重合成回填经 SynthesizedPhonemesChanged + 签名变化触发重建。
    void CommitSymbol(IReadOnlyList<(PhonemeNoteInfo Note, int Index)> members, string symbol)
    {
        if (mPart == null)
            return;
        foreach (var (n, idx) in members)
        {
            var note = n.Note;
            note.LockPhonemes();
            if (idx < note.Phonemes.Count)
                note.Phonemes[idx].Symbol.Set(symbol);
        }
        mPart.Commit();
    }

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
    // 两面板的"标脏→合拍重建"调度器（构造函数初始化）；音素编辑拖动/键入期间经 Suspended 抑制重建。
    readonly ViewRefreshScheduler mNoteScheduler;
    readonly ViewRefreshScheduler mPhonemeScheduler;
    // 音素面板各值框的轻刷新入口：结构级 flush（重建）时重收，值级 flush 逐个调用。
    readonly List<Action> mPhonemeValueRefreshers = new();
    string mPhonemeSignature = string.Empty;

    // 时长：内部存秒，显示/键入按 ms（1ms/px 拖动），下界 0、无上界。
    static readonly DraggableNumberBoxConfig DurationConfig = DraggableNumberBoxConfig.Create(0)
        .WithMin(0)
        .WithResponse(DragResponse.Linear(0.001))
        .WithFormat(NumberFormat.Custom(v => $"{v * 1000:F0} ms", t => double.TryParse(t.Replace("ms", "").Trim(), out var ms) ? ms / 1000.0 : null));
    // 权重：下界 0、无上界，0.01/px 拖动，2 位小数。
    static readonly DraggableNumberBoxConfig WeightConfig = DraggableNumberBoxConfig.Create(1)
        .WithMin(0)
        .WithResponse(DragResponse.Linear(0.01))
        .WithFormat(NumberFormat.Decimals(2));
    readonly DisposableManager s = new();
    readonly DisposableManager mNoteSub = new();
    readonly DisposableManager mPhonemeSub = new();
}
