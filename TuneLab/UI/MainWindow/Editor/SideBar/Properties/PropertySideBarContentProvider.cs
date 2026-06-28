using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using DynamicData;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Configs;
using static TuneLab.GUI.Dialog;

namespace TuneLab.UI;

internal class PropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Properties.GetImage(Style.LIGHT_WHITE), Name = "Properties".Tr(TC.Property), Items = [mPresetPanel, mPartPanel, mEffectsPanel, mAutomationPanel, mNotePanel, mPhonemePanel] };

    public PropertySideBarContentProvider()
    {
        var presetName = new Label() { Content = "Preset".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPresetPanel.Title = presetName;
        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mPresetMoreButton = new TuneLab.GUI.Components.Button() { Width = 28, Height = 28 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } })
            .AddContent(new() { Item = new TextItem() { Text = "\u22EF", FontSize = 16 }, ColorSet = new() { Color = Colors.White } });
        mPresetMoreButton.Clicked += OnPresetMoreButtonClicked;

        // 与 Script 侧栏脚本下拉同构：点开自定义 Flyout，列 None + 各 preset（点击应用、行右侧 ✕ 删除）。钮文字显示当前选中 preset。
        mPresetButton = new TuneLab.GUI.Components.Button() { Height = 28, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
        mPresetLabel = new ButtonContent() { Item = new TextItem() { Text = NonePresetOption, FontSize = 12 }, ColorSet = new() { Color = Style.LIGHT_WHITE } };
        mPresetButton.AddContent(mPresetLabel);
        mPresetButton.Clicked += OnPresetButtonClicked;

        mPresetFlyout = new Flyout() { Placement = PlacementMode.BottomEdgeAlignedLeft };
        mPresetFlyout.FlyoutPresenterClasses.Add("agent-menu");
        mPresetFlyout.Closed += (_, _) =>
        {
            mPresetFlyoutJustClosed = true;   // light-dismiss 再次点钮时先关闭，置标志让随后 Click 不重开 → toggle
            Dispatcher.UIThread.Post(() => mPresetFlyoutJustClosed = false, DispatcherPriority.Input);
        };

        var presetRow = new DockPanel() { LastChildFill = true };
        DockPanel.SetDock(mPresetMoreButton, Dock.Right);
        presetRow.Children.Add(mPresetMoreButton);
        mPresetButton.Margin = new(0, 0, 8, 0);
        presetRow.Children.Add(mPresetButton);
        mPresetContent.Children.Add(presetRow);

        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush(), Margin = new(-12, 0) });
        mPresetContentContainer.Child = mPresetContent;
        mPresetPanel.Content = mPresetContentContainer;

        var partName = new Label() { Content = "Part".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPartPanel.Title = partName;
        mPartContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPartContent.Children.Add(mPartFixedController);
        mPartContent.Children.Add(mPartPropertiesController);
        mPartPanel.Content = mPartContent;

        var effectsName = new Label() { Content = "Effects".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mEffectsPanel.Title = effectsName;
        mEffectsContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mEffectsContent.Children.Add(mEffectsController);
        mEffectsPanel.Content = mEffectsContent;

        var automationName = new Label() { Content = "Automation".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mAutomationPanel.Title = automationName;
        mAutomationContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mAutomationContent.Children.Add(mAutomationController);
        mAutomationPanel.Content = mAutomationContent;

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

        LoadPresets();
    }

    void OnPresetButtonClicked()
    {
        if (mPresetFlyoutJustClosed) return;   // 再次点击恰逢 light-dismiss 刚关 → 不重开（toggle）
        PopulatePresetMenu();
        mPresetFlyout.ShowAt(mPresetButton);
    }

    // 每次打开重建：顶部「None」（恢复默认）+ 分隔线 + 各 preset（点击应用并记为选中、右侧 ✕ 删除）。
    // 删除走二次确认（preset 比会话贵重，保留谨慎，不跟 script 的即时删一刀切）。
    void PopulatePresetMenu()
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical, MinWidth = 220 };
        stack.Children.Add(FlyoutMenuRow.Build(NonePresetOption.Tr(TC.Property), null, () => ApplySelection(null), null, mPresetFlyout));

        if (mPresets.Count > 0)
            stack.Children.Add(new Border() { Height = 1, Margin = new(8, 4), Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush() });
        foreach (var preset in mPresets)
        {
            var name = preset.Name;
            stack.Children.Add(FlyoutMenuRow.Build(name, name,
                () => ApplySelection(name),
                () => { mPresetFlyout.Hide(); _ = DeletePreset(name); }, mPresetFlyout));
        }

        mPresetFlyout.Content = stack;
    }

    // 点行：记为选中并应用到当前 part（None = 恢复默认）。没有 part 时只更新选中、不应用。
    void ApplySelection(string? presetName)
    {
        SetSelectedPreset(presetName);
        OnApplyPresetClicked();
    }

    void OnPresetMoreButtonClicked()
    {
        var menu = new ContextMenu();
        var hasSelection = SelectedPresetName() != null;
        var hasPart = mPart != null;

        {
            var menuItem = new MenuItem().SetName("Save As".Tr(TC.Menu)).SetAction(async () => await OnSaveAsPresetClicked());
            menuItem.IsEnabled = hasPart;
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetName("Save".Tr(TC.Property)).SetAction(async () => await OnSavePresetClicked());
            menuItem.IsEnabled = hasPart && hasSelection;
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetName("Rename".Tr(TC.Menu)).SetAction(async () => await OnRenamePresetClicked());
            menuItem.IsEnabled = hasSelection;
            menu.Items.Add(menuItem);
        }

        mPresetMoreButton.OpenContextMenu(menu);
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
            // part 属性 commit（结果态）→ 重算 part 面板（自身联动）并沿链重算 note 面板（note config 依赖 part 值）。
            mPart.Properties.Modified.Subscribe(OnPartPropertiesModified, s);

            Setup(mPart);
        }
    }

    void Setup(IMidiPart part)
    {
        mAutomationController.Part = part;
        mPartFixedController.Part = part;
        RefreshPartController();
        mEffectsController.SetPart(part);
        RefreshNoteController();
        RefreshPhonemeController();
    }

    void Terminate()
    {
        mAutomationController.Part = null;
        mPartFixedController.Part = null;
        mPartPropertiesController.ResetConfig();
        mNotePropertiesController.ResetConfig();
        mEffectsController.SetPart(null);
        mNoteSub.DisposeAll();
        mNoteData = null;
        mPhonemeSub.DisposeAll();
        mPhonemeRowsPanel.Children.Clear();
        mPhonemePanel.IsVisible = false;
    }

    // part 值 commit：part 面板按当前值重算（数据对象不变，走 Reconcile），并沿链触发 note 面板重算。
    void OnPartPropertiesModified()
    {
        if (mPart == null)
            return;

        ReconcilePartController();
        ReconcileNoteController();
        RefreshPhonemeController();   // 音素面板：part 值变可能改 schema/对齐，直接重建（编辑为 settled 触发，无活拖拽）
    }

    // ---- 条件属性面板：config = f(context)，按当前值重算并 keyed-diff 到控件树 ----
    // part config 仅依赖 part 自身值；note config 依赖 part 值 + 当前选中 note 的三态合并值。

    void RefreshPartController()
    {
        if (mPart == null)
            return;

        mPartPropertiesController.SetConfig(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()), mPart.Properties);
    }

    // 重算 defer 到下一 UI 调度：属性 commit 可能发生在控件自身事件回调链中（如 ComboBox 的 SelectionChanged），
    // 同步重算会重入修改控件集合——Avalonia 的 ComboBox 在其 SelectionChanged 处理中 Clear/重填 Items 会抛 IndexOutOfRange。
    // 推迟到当前事件链完全返回后再 reconcile；pending 标志合并一拍内的多次触发。
    void ReconcilePartController()
    {
        if (mPartReconcilePending)
            return;
        mPartReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mPartReconcilePending = false;
            if (mPart == null)
                return;
            mPartPropertiesController.Reconcile(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()));
        });
    }

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

    // part 面板单 part → 单元素列表（保留列表形以备多 part）；note 面板 → 单个所属 part + 各选中 note 列表
    //（宿主不替插件合并，插件按需 .Merge()）。
    // 声明面活视图壳（引擎无关、调用级）：part 面板单 part → 单元素列表；note 面板 → 单个所属 part + 各选中 note 列表
    //（宿主不替插件合并，插件按需遍历 .Merge()）。复用数据层 PartContext/PartNote（TuneLab.Data）。
    PartPropertyContext BuildPartContext()
        => PartPropertyContext.Single(mPart!);

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

    async Task OnSaveAsPresetClicked()
    {
        if (mPart == null)
            return;

        var presetName = await RequestPresetNameAsync();
        if (presetName == null)
            return;

        var existingPreset = mPresets.FirstOrDefault(preset => preset.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (existingPreset != null)
        {
            var shouldOverwrite = await ConfirmOverwriteAsync(existingPreset.Name);
            if (!shouldOverwrite)
                return;

            presetName = existingPreset.Name;
        }

        SavePreset(presetName);
    }

    async Task OnRenamePresetClicked()
    {
        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
            return;

        var presetName = await RequestPresetNameAsync(selectedPresetName);
        if (presetName == null || presetName.Equals(selectedPresetName, StringComparison.OrdinalIgnoreCase))
            return;

        var existingPreset = mPresets.FirstOrDefault(preset => preset.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (existingPreset != null)
        {
            var shouldOverwrite = await ConfirmOverwriteAsync(existingPreset.Name);
            if (!shouldOverwrite)
                return;

            presetName = existingPreset.Name;
        }

        RenamePreset(selectedPresetName, presetName);
    }

    async Task OnSavePresetClicked()
    {
        if (mPart == null)
            return;

        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
            return;

        if (!await ConfirmOverwriteAsync(selectedPresetName))
            return;

        SavePreset(selectedPresetName);
    }

    void OnApplyPresetClicked()
    {
        if (mPart == null)
            return;

        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
        {
            ApplyDefaultPreset();
            return;
        }

        var preset = mPresets.FirstOrDefault(item => item.Name.Equals(selectedPresetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return;

        ApplyPreset(preset);
    }

    void ApplyDefaultPreset()
    {
        if (mPart == null)
            return;

        ResetPartPropertiesToDefaults(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()), mPart.Properties);
        ResetAutomationDefaults();
        mPart.Commit();
    }

    void ApplyPreset(PartPreset preset)
    {
        if (mPart == null)
            return;

        mPart.SoundSource.SetInfo(new SoundSourceInfo() { Type = preset.Voice.Type, ID = preset.Voice.ID });
        ResetPartPropertiesToDefaults(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()), mPart.Properties);
        ApplyPresetProperties(preset.Properties, mPart.Properties);
        ApplyAutomationDefaults(preset);
        mPart.Commit();
    }

    // 沿 ObjectConfig 结构与数据节点并行导航：嵌套 config 走 node.Object(key) 下降，叶子 config 写 node.SetValue。
    static void ResetPartPropertiesToDefaults(ObjectConfig config, IDataPropertyObject node)
    {
        foreach (var kvp in config.Properties)
        {
            if (kvp.Value is ObjectConfig objectConfig)
            {
                ResetPartPropertiesToDefaults(objectConfig, node.Object(kvp.Key.Id));
            }
            else if (kvp.Value is ArrayConfig or ListConfig or ExtensibleObjectConfig)
            {
                // 数组/列表/变长键控容器：写入默认值（递归各元素/键 config 默认值拼成 PropertyArray/PropertyObject）。
                // 显式重置即物化该值（变长键控 = 当前声明键的默认对象，替换整个容器值）。
                node.SetValue(kvp.Key.Id, kvp.Value.GetDefaultValue());
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                node.SetValue(kvp.Key.Id, valueConfig.DefaultValue);
            }
        }
    }

    static void ApplyPresetProperties(PropertyObject properties, IDataPropertyObject node)
    {
        foreach (var property in properties.Map)
        {
            if (property.Value.ToObject(out var propertyObject))
            {
                ApplyPresetProperties(propertyObject, node.Object(property.Key));
            }
            else
            {
                node.SetValue(property.Key, property.Value);
            }
        }
    }

    void ResetAutomationDefaults()
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            if (mPart.Automations.TryGetValue(kvp.Key.Id, out var automation))
                automation.DefaultValue.Set(kvp.Value.DefaultValue);
        }
    }

    void ApplyAutomationDefaults(PartPreset preset)
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            double value = preset.Automations.GetValueOrDefault(kvp.Key.Id, kvp.Value.DefaultValue);
            if (mPart.Automations.TryGetValue(kvp.Key.Id, out var automation))
            {
                automation.DefaultValue.Set(value);
            }
            else if (value != kvp.Value.DefaultValue)
            {
                mPart.AddAutomation(kvp.Key.Id)?.DefaultValue.Set(value);
            }
        }
    }

    // 删除指定 preset（行内 ✕ 触发，先二次确认）；若删的是当前选中项，选中回落 None。
    async Task DeletePreset(string presetName)
    {
        if (!await ConfirmDeleteAsync(presetName))
            return;

        try
        {
            PresetConfigManager.DeletePreset(presetName);
            if (presetName.Equals(mSelectedPresetName, StringComparison.OrdinalIgnoreCase))
                SetSelectedPreset(null);
            LoadPresets();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to delete preset: \n" + ex.Message);
        }
    }

    async Task<bool> ConfirmOverwriteAsync(string presetName)
    {
        return await ConfirmAsync(string.Format("Overwrite preset \"{0}\"?".Tr(TC.Property), presetName), "Save".Tr(TC.Property));
    }

    async Task<bool> ConfirmDeleteAsync(string presetName)
    {
        return await ConfirmAsync(string.Format("Delete preset \"{0}\"?".Tr(TC.Property), presetName), "Delete".Tr(TC.Property));
    }

    async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var dialog = new Dialog();
        dialog.SetTitle("Tips".Tr(TC.Dialog));
        dialog.SetMessage(message);

        bool confirmed = false;
        dialog.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
        var confirmButton = dialog.AddButton(confirmText, ButtonType.Primary);
        confirmButton.Pressed += () => confirmed = true;
        dialog.Topmost = true;
        await dialog.ShowDialog(mPresetPanel.Window());
        return confirmed;
    }

    async Task<string?> RequestPresetNameAsync(string initialName = "")
    {
        var dialog = new NameInputDialog("Preset Name".Tr(TC.Property), initialName);
        var presetName = await dialog.ShowDialog<string?>(mPresetPanel.Window());
        presetName = presetName?.Trim();
        if (string.IsNullOrWhiteSpace(presetName))
            return null;

        if (presetName.Equals(NonePresetOption, StringComparison.OrdinalIgnoreCase))
        {
            await mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "\"None\" is reserved.");
            return null;
        }

        return presetName;
    }

    void SavePreset(string presetName)
    {
        try
        {
            PresetConfigManager.SavePreset(BuildPreset(presetName));
            LoadPresets(presetName);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to save preset: \n" + ex.Message);
        }
    }

    void RenamePreset(string oldPresetName, string newPresetName)
    {
        try
        {
            PresetConfigManager.RenamePreset(oldPresetName, newPresetName);
            LoadPresets(newPresetName);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to rename preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to rename preset: \n" + ex.Message);
        }
    }

    PartPreset BuildPreset(string presetName)
    {
        if (mPart == null)
            throw new InvalidOperationException("Part is null.");

        var preset = new PartPreset()
        {
            Name = presetName,
            Voice = mPart.SoundSource.GetInfo(),
            Properties = mPart.Properties.GetInfo(),
        };

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            var key = kvp.Key.Id;
            if (mPart.Automations.TryGetValue(key, out var automation))
                preset.Automations[key] = automation.DefaultValue.Value;
            else
                preset.Automations[key] = kvp.Value.DefaultValue;
        }

        return preset;
    }

    // 重载 preset 列表（下拉每次打开时按 mPresets 重建）。selectedPresetName 给定则设为选中；否则保留当前选中，
    // 若当前选中已不在列表中（被删/改名）则回落 None。
    void LoadPresets(string? selectedPresetName = null)
    {
        mPresets = PresetConfigManager.LoadPresets();
        var keep = selectedPresetName ?? mSelectedPresetName;
        SetSelectedPreset(keep != null && mPresets.Any(p => p.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) ? keep : null);
    }

    // 选中态：撑住 ⋯ 菜单 Save/Rename 的作用目标 + 下拉钮文字（null = None）。Flyout 是瞬态菜单，故选中态须自己记。
    void SetSelectedPreset(string? presetName)
    {
        mSelectedPresetName = string.IsNullOrWhiteSpace(presetName) ? null : presetName;
        mPresetLabel.Item = new TextItem() { Text = (mSelectedPresetName ?? NonePresetOption.Tr(TC.Property)) + "  ▾", FontSize = 12 };
    }

    string? SelectedPresetName() => mSelectedPresetName;

    readonly Border mPresetContentContainer = new() { Background = Style.INTERFACE.ToBrush(), Padding = new(12, 0, 12, 12) };
    readonly StackPanel mPresetContent = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mEffectsContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPhonemeContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPresetPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mEffectsPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mNotePanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPhonemePanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNoteContentPanel = new();

    readonly TuneLab.GUI.Components.Button mPresetButton;
    readonly ButtonContent mPresetLabel;
    readonly Flyout mPresetFlyout;
    bool mPresetFlyoutJustClosed;
    string? mSelectedPresetName;
    readonly TuneLab.GUI.Components.Button mPresetMoreButton;
    readonly AutomationDefaultsController mAutomationController = new();
    readonly EffectsController mEffectsController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly PropertyObjectController mPartPropertiesController = new();
    readonly PropertyObjectController mNotePropertiesController = new();
    // 音素属性逐 slot 呈现：按 IsLead 分界、核=0 的有符号对齐索引把各 note 音素合并成 slot，每 slot 一行（符号标签 + 控制器）。
    readonly StackPanel mPhonemeRowsPanel = new() { Orientation = Orientation.Vertical };

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    const string NonePresetOption = "None";
    IMidiPart? mPart = null;
    List<PartPreset> mPresets = [];
    MultipleDataPropertyObject? mNoteData = null;
    bool mPartReconcilePending = false;
    bool mNoteReconcilePending = false;
    bool mNoteRefreshPending = false;
    bool mPhonemeRefreshPending = false;
    readonly DisposableManager s = new();
    readonly DisposableManager mNoteSub = new();
    readonly DisposableManager mPhonemeSub = new();
}
