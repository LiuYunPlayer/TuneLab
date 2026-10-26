using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.GUI.Controllers;

// 配置驱动的属性面板：按 ObjectConfig 渲染控件，逐字段绑定到 IDataPropertyObject 的可绑定字段，
// 复用单属性的撤销/刷新/提交机制。控件类型经注册表分发，加新控件只需注册一项；嵌套对象经 Object(key) 逐层导航。
//
// 支持 keyed-diff reconcile（条件属性面板）：config 可随数据值变化，调用方在值 commit 后重算整棵 config 再调
// Reconcile，本控件按 key diff——同 key 同类型复用控件仅更参数（不闪、不丢焦点）、key 消失则 dispose、新 key 建、
// 同 key 换类型则换控件。纯参数变化不重排布局；仅结构（增删/换类型/顺序）变化时重排。订阅/重算时机由调用方管理。
// 各值控件的编辑过程（开始改值 → 提交）登记到所在窗口的 EditGate；窗口编辑中 Reconcile 推迟到编辑结束（见 Reconcile）。
internal class PropertyObjectController : StackPanel
{
    // 是否在每个属性后渲染尾随分隔线（默认 false）。字段之间靠间距分开、不画线：线只留给分组边界
    //（侧栏分组标题自带顶线，见 SideBarSection），线才是层级信号——每个字段后都一条线时，分组边界淹没在字段边界里。
    // 需要逐字段分界的宿主可置 true。设置须在 SetConfig 前（决定 creator 的 LayoutViews）。
    public bool ShowSeparators { get; set; } = false;

    // 字段折叠的记忆作用域（宿主按「面板 + 插件身份」给，如 "part:Voice:SomeEngine"）：非 null 时字段可折叠、
    // 折叠状态按「作用域/字段 id」记进 FieldFoldStore；嵌套对象的子控制器沿用并追加自身键。null = 字段不可折叠
    //（如列表元素里的字段：元素没有跨会话稳定的身份，按路径记不准）；嵌套对象仍可临时折叠、只是不记。
    // 设置须在 SetConfig 前（决定 creator 的折叠形态）。
    public string? CollapseScope { get; set; }

    string? FoldKey(PropertyKey key) => CollapseScope == null ? null : CollapseScope + "/" + key.Id;

    // 恢复默认值 = 把该字段从数据里移除：本仓「默认 = 字段不存在」（声明面只承载用户改过的稀疏值），故对所有类型都成立——
    // 标量回到声明默认、列表回到插件给的初始内容（seed）、嵌套对象整块恢复。多选外观扇出到每个成员；一次提交 = 一个撤销单元。
    void ResetField(PropertyKey key)
    {
        DataObject.RemoveValue(key.Id);
        DataObject.Commit();
    }

    // 收起时的值摘要：多选不一致时统一写 "(Multiple)"（同下拉的多值显示）。
    static string? MultipleOr(PropertyValue value, Func<PropertyValue, string?> format)
        => value.IsMultiple() ? "(Multiple)" : format(value);

    // 标题行（30，文字居中、下方留白约 7）到其下控件的间隔：7 + 5 = 12，即「标题文字 → 自己的控件」12。
    // 控件下边距仍是 12，故「控件 → 下一个标题文字」= 12 + 7 = 19：标签离自己的控件更近。
    const double FieldGap = 5;

    static string ItemsSummary(int count) => string.Format("{0} items".Tr("PropertyObjectController"), count);

    // 收起时的摘要：首个非空行（开头的空行不算，否则摘要为空、看着像没填）；其后还有内容就补 "…"，
    // 让人知道不止这一行。\r\n 与 \r 都按换行切（粘贴来的文本常带 \r，留着会混进宽度与截断）。
    // 全是空白则不显示摘要（同空字符串）。
    internal static string? FirstLineSummary(string text)
    {
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        int first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0)
            return null;
        bool more = lines.Skip(first + 1).Any(line => !string.IsNullOrWhiteSpace(line));
        return more ? lines[first] + " …" : lines[first];
    }

    // 宿主注入的逐属性右键菜单项（返回 null/空 = 不加）：菜单项在每次右键时现取，随宿主状态（如钉选态）即时变化，
    // 排在通用项「恢复默认值」之前。嵌套 ObjectConfig 的子控制器不继承本钩子（作用面 = 本层直属属性）。
    public Func<PropertyKey, IControllerConfig, IReadOnlyList<MenuItem>?>? ItemContextMenuProvider { get; set; }

    // 按钮（ButtonConfig）的作用域与完成通知，见 ButtonRoot。面板持有方在根控制器上设 / 订阅；嵌套控制器经
    // AttachButtonRoot 共享根的那一份（路径 = 根到本层的 key / 元素 token 串）。
    public string ButtonScope { get => mButtonRoot.Scope; set => mButtonRoot.Scope = value; }
    public event Action? ButtonActionCompleted { add => mButtonRoot.ActionCompleted += value; remove => mButtonRoot.ActionCompleted -= value; }

    internal void AttachButtonRoot(ButtonRoot root, string path)
    {
        mButtonRoot = root;
        mButtonPath = path;
    }

    // 嵌套控制器归池前调用：断开与外层树的共享，免得池里取出来当根用时还连着别人的作用域。
    internal void DetachButtonRoot() => AttachButtonRoot(new ButtonRoot(), string.Empty);

    public PropertyObjectController()
    {
        Background = Style.INTERFACE.ToBrush();
    }

    // 绑定到（新的）数据对象并对齐到 config。数据对象切换时必须重建全部控件——复用的 creator 仍绑在旧对象的字段上，
    // 故先 ResetConfig 清空再全新建。静态用法（effect/嵌套）= 仅调一次；动态用法（条件面板）在数据对象不变时改调 Reconcile。
    public void SetConfig(ObjectConfig config, IDataPropertyObject dataObject)
    {
        ResetConfig();
        mDataObject = dataObject;
        ReconcileNow(config);
    }

    // 数据对象不变，把控件树 keyed-diff 到新 config。
    // 所在窗口有编辑进行中（见 EditGate）时只记下最新的 config、编辑结束后再对齐：新声明可能去掉 / 换掉正被拖的字段，
    // 或增减它上方的字段把它挤得跳位置。期间到来的多次只取最后一次；编辑自己的提交还会引起一次新的声明，
    // 结束后对齐的总是最新的那份。
    public void Reconcile(ObjectConfig config)
    {
        if (mDataObject == null)
            return;

        if (EditGate.Of(this) is { IsEditing: true } gate)
        {
            bool scheduled = mPendingConfig != null;
            mPendingConfig = config;
            if (!scheduled)
                gate.AfterEditing(ApplyPendingConfig);
            return;
        }

        ReconcileNow(config);
    }

    void ApplyPendingConfig()
    {
        if (mPendingConfig is not { } config)
            return;   // 期间已被更新的 Reconcile / SetConfig / ResetConfig 取代
        mPendingConfig = null;
        Reconcile(config);
    }

    void ReconcileNow(ObjectConfig config)
    {
        mPendingConfig = null;
        if (mDataObject == null)
            return;

        var nextOrder = new List<string>();
        var nextByKey = new Dictionary<string, Creator>();
        bool structureChanged = false;

        foreach (var kvp in config.Properties)
        {
            var key = kvp.Key;      // PropertyKey：Id 用于数据寻址与 diff、DisplayText 用于标签
            var id = key.Id;
            var cfg = kvp.Value;
            if (nextByKey.ContainsKey(id))
                continue; // ObjectConfig 是 map，key 本应唯一；防御重复

            if (mCreatorsByKey.TryGetValue(id, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(cfg);
                mCreatorsByKey.Remove(id);
                nextByKey.Add(id, existing);
            }
            else
            {
                if (!mFactories.TryGetValue(cfg.GetType(), out var factory))
                {
                    Log.Error($"No controller found for config type: {cfg.GetType()}");
                    continue;
                }
                nextByKey.Add(id, factory(this, key, cfg));
                structureChanged = true; // 新建（含同 key 换类型）
            }
            nextOrder.Add(id);
        }

        if (mCreatorsByKey.Count > 0)
            structureChanged = true; // 有 key 被删
        if (!mOrder.SequenceEqual(nextOrder))
            structureChanged = true; // 顺序变

        if (structureChanged)
        {
            // 先把被删 creator 的视图移出可视树，再 dispose（归还控件 / 分隔符到池）。
            foreach (var kvp in mCreatorsByKey)
            {
                foreach (var view in kvp.Value.LayoutViews)
                    Children.Remove(view);
                kvp.Value.Dispose();
            }
            mCreatorsByKey = nextByKey;
            mOrder = nextOrder;
            AlignChildren();
        }
        else
        {
            // 纯参数更新：控件原位不动（复用项已 Update），仅刷新内部簿记引用。
            mCreatorsByKey = nextByKey;
            mOrder = nextOrder;
        }

        // 末项标记：单行的末项（收起的字段 / 行内字段 / 按钮行）补底边距，见 Creator.SetTrailing。
        for (int i = 0; i < mOrder.Count; i++)
            mCreatorsByKey[mOrder[i]].SetTrailing(i == mOrder.Count - 1);
    }

    // 当前没有任何字段（未 SetConfig、已 ResetConfig，或 config 本身为空）。
    public bool IsEmpty => mOrder.Count == 0;

    public void ResetConfig()
    {
        Children.Clear();
        foreach (var kvp in mCreatorsByKey)
            kvp.Value.Dispose();
        mCreatorsByKey = new();
        mOrder = new();
        mDataObject = null;
        mPendingConfig = null;
    }

    // 增量对齐 Children 到目标顺序（mOrder 各 creator 的 LayoutViews 拼接）：已在正确位置的复用控件**不动**
    //（留在可视树、保持焦点 / 编辑态），仅移动错位的、插入新建的；尾部多余兜底移除。
    void AlignChildren()
    {
        int index = 0;
        foreach (var key in mOrder)
        {
            foreach (var view in mCreatorsByKey[key].LayoutViews)
            {
                int current = Children.IndexOf(view);
                if (current == index)
                {
                    index++;
                    continue;
                }
                if (current >= 0)
                    Children.RemoveAt(current);
                Children.Insert(index, view);
                index++;
            }
        }
        while (Children.Count > index)
            Children.RemoveAt(Children.Count - 1);
    }

    IDataPropertyObject DataObject => mDataObject ?? throw new InvalidOperationException("PropertyObjectController has no data object.");

    // creator 不自行加入 Parent.Children；创建控件、绑定数据、暴露 Views，布局由 AlignChildren 增量管理。
    // 每个 creator 自带一个尾随分隔符（生命周期内实例稳定），使增量对齐能把复用控件留在可视树原位、不失焦
    //（否则 reconcile 重排时正在编辑的 TextBox 被移出可视树即丢焦点，无法连续输入）。
    abstract class Creator : IDisposable
    {
        protected Creator(PropertyObjectController parent)
        {
            Parent = parent;
            // 【池化控件取出来不是干净的】Border 池同时供两种形状：本条横向分界线（只定高、横向拉满）与
            // ObjectCreator 的竖向缩进线（只定宽、纵向拉满）。谁少写一个属性，就会继承上一个用户留下的值——
            // 竖线归池后当分界线取出，会带着 Width=1，而"显式 Width + Stretch"在 Avalonia 里表现为居中，
            // 于是整条线缩成面板中央一颗点（实测症状）。故两边都把自己依赖的属性写全，NaN = 交回自动。
            mSeparator = ObjectPoolManager.Get<Border>();
            mSeparator.Width = double.NaN;
            mSeparator.Height = 1;
            mSeparator.Margin = default;
            mSeparator.Background = Style.BACK.ToBrush();
        }

        protected PropertyObjectController Parent { get; }
        protected readonly DisposableManager s = new();
        public abstract Type ConfigType { get; }
        public abstract IEnumerable<Control> Views { get; }
        // 可折叠的字段返回其折叠件（按钮没有：它是操作、不是值，收起后没有可显示的东西）。
        public virtual FieldFold? Fold => null;

        // 是否所在列表的最后一项：单行的末项后面紧跟分组线 / 对象结尾，要补底边距（同 FieldFold.IsTrailing）。
        public virtual void SetTrailing(bool trailing)
        {
            if (Fold is { } fold)
                fold.IsTrailing = trailing;
        }
        // 布局序列 = 内容控件 + 尾随分隔符。
        public IEnumerable<Control> LayoutViews
        {
            get
            {
                foreach (var view in Views)
                    yield return view;
                if (Parent.ShowSeparators)
                    yield return mSeparator;
            }
        }
        public abstract void Update(IControllerConfig config);
        public virtual void Dispose()
        {
            s.DisposeAll();
            foreach (var detach in mContextMenuDetachers)
                detach();
            mContextMenuDetachers.Clear();
            ObjectPoolManager.Return(mSeparator);
        }

        // 字段的右键菜单，挂在该字段的各块控件上（标题行 + 主体）：宿主注入的项（如钉选）在前，
        // 通用项「恢复默认值」在后——所有字段都有，是恢复默认值的统一入口（拖拽数值框的双击已用于键入，滑条的双击只管滑条本身）。
        // 菜单每次打开现建（项文案 / 勾选态随宿主状态变）。控件自带右键菜单的（如文本框的复制粘贴）自己先处理、不冒泡到这里。
        // 控件多为池化复用，handler 记入 detacher、Dispose 时摘除，避免归池后残留订阅。
        protected void AttachContextMenu(PropertyKey key, Func<IControllerConfig> currentConfig, params Control[] controls)
        {
            foreach (var control in controls)
            {
                var target = control;
                void OnContextRequested(object? sender, ContextRequestedEventArgs e)
                {
                    var menu = new ContextMenu();
                    var items = Parent.ItemContextMenuProvider?.Invoke(key, currentConfig());
                    if (items != null && items.Count > 0)
                    {
                        foreach (var item in items)
                            menu.Items.Add(item);
                        menu.Items.Add(new Separator());
                    }
                    menu.Items.Add(new MenuItem().SetName("Reset to Default".Tr("PropertyObjectController")).SetAction(() => Parent.ResetField(key)));
                    menu.Open(target);
                    e.Handled = true;
                }
                target.ContextRequested += OnContextRequested;
                mContextMenuDetachers.Add(() => target.ContextRequested -= OnContextRequested);
            }
        }

        readonly List<Action> mContextMenuDetachers = new();
        readonly Border mSeparator;
    }

    class ObjectCreator : Creator
    {
        public ObjectCreator(PropertyObjectController parent, PropertyKey key, ObjectConfig config) : base(parent)
        {
            // 对象标题比字段标签亮一档（值的亮度）：分组（白 14px）> 对象（亮 12px）> 字段（暗 12px）。
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);   // 与其余字段同高：收起后各行等高
            mTitle.Foreground = Style.LIGHT_WHITE.ToBrush();

            // 引导线：从标题三角的正下方垂下、贯穿全部子字段——「三角 + 竖线」连成一体，一眼看出这条线归哪个对象、到哪结束
            //（字段之间不再画分隔线，对象的边界全靠它）。同上：Height 也要显式交回自动，否则接手的是一条从分界线归池、带着 Height=1 的 Border。
            mBorder = ObjectPoolManager.Get<Border>();
            mBorder.Margin = new(FieldFold.GuideX, 4, 0, 0);
            mBorder.Width = 1;
            mBorder.Height = double.NaN;
            mBorder.Background = Style.LINE.ToBrush();

            mController = ObjectPoolManager.Get<PropertyObjectController>();
            mController.AttachButtonRoot(parent.mButtonRoot, parent.mButtonPath + "/" + key.Id);
            mController.CollapseScope = parent.FoldKey(key);
            mController.SetConfig(config, parent.DataObject.Object(key.Id));

            // 子字段的标签左缘 = 引导线右侧再缩进 24（与顶层字段同一套边距），故整体缩进 = 引导线位置 + 1。
            // 底边距 12：对象块结尾与下一个同级字段隔开。DockPanel 出自对象池、与单行字段共用，那几处取出时会把边距写回。
            mDockPanel = ObjectPoolManager.Get<DockPanel>();
            mDockPanel.Margin = new(0, 0, 0, 12);
            mDockPanel.AddDock(mBorder, Dock.Left);
            mDockPanel.AddDock(mController);

            // 对象总能折叠（哪怕没有记忆作用域——那时只是不记）；收起时不显示摘要。
            mFold = new FieldFold(mTitle, [mDockPanel], parent.FoldKey(key), foldable: true);
            AttachContextMenu(key, () => config, mFold.Header);
        }

        public override Type ConfigType => typeof(ObjectConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mDockPanel];
        public override void Update(IControllerConfig config) => mController.Reconcile((ObjectConfig)config);

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mDockPanel.Children.Clear();
            ObjectPoolManager.Return(mDockPanel);
            ObjectPoolManager.Return(mBorder);
            mController.ResetConfig();
            mController.DetachButtonRoot();
            mController.CollapseScope = null;
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly Border mBorder;
        readonly DockPanel mDockPanel;
        readonly FieldFold mFold;
        readonly PropertyObjectController mController;
    }

    class SliderCreator : Creator
    {
        public SliderCreator(PropertyObjectController parent, PropertyKey key, SliderConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);

            mController = ObjectPoolManager.Get<SliderController>();
            mController.Margin = new(24, FieldGap, 24, 12);
            Apply(config);

            // 标题 + 滑条包进同一容器，右键菜单挂容器上整行可触（含控件外边距的空白区——
            // 透明底使空白区可命中；不包则空白区落在外层 StackPanel 上、不归本属性）。
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.GetValue(key.Id, PropertyValue.Create(mConfig.DefaultValue)),
                    v => v.ToDouble(out var d) ? mConfig.Format.Format(d) : null),
                watch: parent.DataObject);
            mPanel = ObjectPoolManager.Get<StackPanel>();
            mPanel.Background = Avalonia.Media.Brushes.Transparent;
            mPanel.Children.Add(mFold.Header);
            mPanel.Children.Add(mController);
            AttachContextMenu(key, () => mConfig, mPanel);

            // 先绑定（初次刷新即把真实值写入），Relayout 才加入可视树——否则池复用的控件会以残留旧值/旧量程
            // 先布局渲染一帧，thumb 随后才跳到正确位置（初次选中音符时可见的瞬间挪动）。
            mController.BindDataProperty(parent.DataObject.DoubleField(key.Id, config.DefaultValue), s);
            EditGate.Track<double>(mController, s);
        }

        void Apply(SliderConfig config)
        {
            mConfig = config;
            mController.SetScale(config.Scale);
            mController.SetDefaultValue(config.DefaultValue);
            mController.NumberFormat = config.Format;
            mController.ShowRandomButton = config.Randomizable;
            mController.SetBoundLabels(config.MinLabel, config.MaxLabel);
        }

        public override Type ConfigType => typeof(SliderConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mPanel];
        public override void Update(IControllerConfig config) { Apply((SliderConfig)config); mFold.RefreshSummary(); }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mPanel.Children.Clear();
            ObjectPoolManager.Return(mPanel);
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly StackPanel mPanel;
        readonly FieldFold mFold;
        readonly SliderController mController;
        SliderConfig mConfig = null!;   // Apply 于构造期必先行
    }

    // 可拖拽数值框：number 的无界/单界/双界通用控件（slider 是双有界特化）。控件自身实现 IDataValueController<double>，
    // 故直接用之、不另套 controller。无界数值无可视轨道、本就占地小，故采 CheckBox 式同行布局（标题在左、数值框靠右），
    // 不像 slider 占整行。
    class DraggableNumberBoxCreator : Creator
    {
        public DraggableNumberBoxCreator(PropertyObjectController parent, PropertyKey key, DraggableNumberBoxConfig config) : base(parent)
        {
            mController = new DraggableNumberBox() { Width = 80 };

            // 量程内随机取值的按钮（默认隐藏，由 config.Randomizable 开启，见 Apply）：同 slider 放在最右侧。
            // 翻译上下文写成控件名（而非本嵌套 creator 的类名），使按钮日后换宿主也不改词条。
            mRandomButton = new Button() { Width = 24, Height = 24, Margin = new(4, 0, 0, 0), IsVisible = false };
            mRandomButton
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = Style.LIGHT_WHITE.Opacity(0.1), PressedColor = Style.LIGHT_WHITE.Opacity(0.16) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.Dice, Scale = 0.7 }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
            mRandomButton.Clicked += mController.Randomize;
            mRandomButton.SetupToolTip("Randomize".Tr("DraggableNumberBox"));

            // 数值框与随机按钮同列靠右：按钮隐藏时该列宽度即数值框宽度。无界数值无可视轨道、本就占地小，故是单行字段：
            // 整列放在标题行右侧（行内控件，见 FieldFold.AddInline），收起时同一位置显示当前值、不可改。
            mValueColumn = new StackPanel() { Orientation = Orientation.Horizontal };
            mValueColumn.Children.Add(mController);
            mValueColumn.Children.Add(mRandomButton);

            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);

            Apply(config);
            mController.BindDataProperty(parent.DataObject.DoubleField(key.Id, config.DefaultValue), s);
            EditGate.Track(mController, s);
            mFold = new FieldFold(mTitle, [mValueColumn], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.GetValue(key.Id, PropertyValue.Create(mConfig.DefaultValue)),
                    v => v.ToDouble(out var d) ? mConfig.Format.Format(d) : null),
                watch: parent.DataObject);
            mFold.AddInline(mValueColumn);
            AttachContextMenu(key, () => mConfig, mFold.Header);
        }

        void Apply(DraggableNumberBoxConfig config)
        {
            mConfig = config;
            mController.MinValue = config.Min;
            mController.MaxValue = config.Max;
            mController.Response = config.Response;
            mController.Step = config.Step;
            mController.NumberFormat = config.Format;
            mController.DefaultValue = config.DefaultValue;
            // 随机入口只在双有界时给：无界侧上没有均匀分布可取（见 DraggableNumberBox.Randomize）。
            mRandomButton.IsVisible = config.Randomizable && config.Min.HasValue && config.Max.HasValue;
        }

        public override Type ConfigType => typeof(DraggableNumberBoxConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header];
        public override void Update(IControllerConfig config) { Apply((DraggableNumberBoxConfig)config); mFold.RefreshSummary(); }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly StackPanel mValueColumn;
        readonly DraggableNumberBox mController;
        readonly Button mRandomButton;
        DraggableNumberBoxConfig mConfig = null!;   // Apply 于构造期必先行
    }

    // 文本字段：单行框，或（IsMultiline）多行框。两者是【同一个 config 类型】的两种形态，故 reconcile 会
    // 复用同一个 creator——但用的是两个不同控件。控件放进一个容器、Views 恒为该容器，切形态时只换容器里的
    // 孩子：Views 不变 ⇒ 不必让 reconcile 知道这件事，也不触发整面板重排。
    class TextCreator : Creator
    {
        public TextCreator(PropertyObjectController parent, PropertyKey key, TextBoxConfig config) : base(parent)
        {
            mKey = key;
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);
            mHost = ObjectPoolManager.Get<Panel>();
            mHost.Margin = new(24, FieldGap, 24, 12);
            Build(config);
            // 摘要：多行文本取首个非空行；掩码字段不泄露内容。
            mFold = new FieldFold(mTitle, [mHost], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.GetValue(key.Id, PropertyValue.Create(config.DefaultValue)),
                    v => !v.ToString(out var text) ? null : mIsPassword ? (text.Length == 0 ? null : "••••••") : FirstLineSummary(text)),
                watch: parent.DataObject);
            AttachContextMenu(key, () => config, mFold.Header, mHost);
        }

        public override Type ConfigType => typeof(TextBoxConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mHost];

        public override void Update(IControllerConfig config)
        {
            var c = (TextBoxConfig)config;
            if (IsMultiline(c) != mIsMultiline)
            {
                Teardown();
                Build(c);
                return;
            }

            if (mMultiline != null)
                mMultiline.SetMaxVisibleLines(c.MaxVisibleLines);
            else if (mSingleLine != null)
                mSingleLine.IsPassword = c.IsPassword;
            mIsPassword = c.IsPassword;
            mFold.RefreshSummary();
        }

        // 掩码只有单行有（多行框不支持逐字符掩码），两者同时声明时以掩码为准——静默降级插件作者看不见，故告警一次。
        static bool IsMultiline(TextBoxConfig config)
        {
            if (!config.IsMultiline || !config.IsPassword)
                return config.IsMultiline;
            if (sWarnedPasswordMultiline)
                return false;
            sWarnedPasswordMultiline = true;
            Log.Warning("TextBoxConfig declares both IsPassword and IsMultiline; masking has no multi-line form, so the field stays single-line.");
            return false;
        }

        void Build(TextBoxConfig config)
        {
            mIsPassword = config.IsPassword;
            mIsMultiline = IsMultiline(config);
            var field = Parent.DataObject.StringField(mKey.Id, config.DefaultValue);
            if (mIsMultiline)
            {
                // 不池化：AvaloniaEdit 控件自带文档与渲染层、状态面比单行大得多，复用要清的东西比省下的多。
                mMultiline = new MultilineTextInput() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, DiscardOnEscape = true };
                mMultiline.SetMaxVisibleLines(config.MaxVisibleLines);
                mMultiline.BindDataProperty(field, mFieldScope);
                EditGate.Track(mMultiline, mFieldScope);
                mHost.Children.Add(mMultiline);
            }
            else
            {
                mSingleLine = ObjectPoolManager.Get<SingleLineTextController>();
                mSingleLine.IsPassword = config.IsPassword;
                mSingleLine.BindDataProperty(field, mFieldScope);
                EditGate.Track(mSingleLine, mFieldScope);
                mHost.Children.Add(mSingleLine);
            }
        }

        // 拆掉当前形态的控件：先断绑定（否则归池后仍在响应数据变化），再移出可视树。
        void Teardown()
        {
            mFieldScope.DisposeAll();
            mHost.Children.Clear();
            if (mSingleLine != null)
            {
                ObjectPoolManager.Return(mSingleLine);
                mSingleLine = null;
            }
            mMultiline = null;
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            Teardown();
            ObjectPoolManager.Return(mHost);
            ObjectPoolManager.Return(mTitle);
        }

        readonly PropertyKey mKey;
        readonly Label mTitle;
        readonly Panel mHost;
        readonly FieldFold mFold;
        bool mIsPassword;
        readonly DisposableManager mFieldScope = new();   // 只管当前控件的绑定，换形态时单独回收
        bool mIsMultiline;
        SingleLineTextController? mSingleLine;
        MultilineTextInput? mMultiline;

        static bool sWarnedPasswordMultiline;
    }

    // 路径选择：文本框 + 浏览按钮。值仍是普通 string 字段，与 SingleLineTextCreator 同一绑定路径，
    // 只多一个把选中路径写回文本框的按钮（PathPicker 内部经 Text setter 触发 will-change/changed/committed，故照常入撤销栈）。
    class PathPickerCreator : Creator
    {
        public PathPickerCreator(PropertyObjectController parent, PropertyKey key, PathPickerConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);

            mController = ObjectPoolManager.Get<PathPicker>();
            mController.Margin = new(24, FieldGap, 24, 12);
            mController.Apply(config);

            mController.BindDataProperty(parent.DataObject.StringField(key.Id, config.DefaultValue), s);
            EditGate.Track(mController, s);
            // 摘要：文件 / 文件夹名（整条路径在窄侧栏里放不下，名字才是辨认它的部分）。
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.GetValue(key.Id, PropertyValue.Create(config.DefaultValue)),
                    v => v.ToString(out var path) && path.Length > 0 ? System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) : null),
                watch: parent.DataObject);
            AttachContextMenu(key, () => config, mFold.Header, mController);
        }

        public override Type ConfigType => typeof(PathPickerConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];
        public override void Update(IControllerConfig config) => mController.Apply((PathPickerConfig)config);

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly PathPicker mController;
    }

    class ComboBoxCreator : Creator
    {
        public ComboBoxCreator(PropertyObjectController parent, PropertyKey key, ComboBoxConfig config) : base(parent)
        {
            mKey = key.Id;
            mConfig = config;
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);

            mController = ObjectPoolManager.Get<ComboBoxController>();
            mController.Margin = new(24, FieldGap, 24, 12);
            mController.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            BindWith(config);
            // 摘要：选中项的显示文本；值不在选项里时显示原值字面量（同下拉本体的呈现）。
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.RawValueField(mKey, mConfig.DefaultOption.Value).Value,
                    v => mConfig.Items.Leaves().Where(item => item.Value.Equals(v)).Select(item => item.ShowText()).FirstOrDefault() ?? v.ToString()),
                watch: parent.DataObject);
            AttachContextMenu(key, () => mConfig, mFold.Header, mController);
        }

        // SetConfig（自洽显示 default）后紧接 bind（bind 内 Refresh 把当前数据值按三态显示出来）——复刻"SetConfig→bind"
        // 成对范式；reconcile 改选项时也走它，否则显示会停在 SetConfig 给的 default 而非数据值。
        void BindWith(ComboBoxConfig config)
        {
            mController.SetConfig(config);
            // 绑裸 PropertyValue 字段：option 值可为任意类型，存进数据的就是该值本身（非显示文本）。按原值读、不按默认值的
            // 类型过滤——另一类型的合法选项、不在候选里要琥珀显示的旧值都得原样读出（见 RawValueField）。
            mController.BindDataProperty(Parent.DataObject.RawValueField(mKey, config.DefaultOption.Value), s);
            EditGate.Track(mController, s);
        }

        public override Type ConfigType => typeof(ComboBoxConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];

        // 选项未变则跳过：SetConfig 会 Clear + 重填 Items 打断当前选中，无谓重建。选项变则解旧绑定 → BindWith
        //（SetConfig + 重新 bind），让控件显示回当前数据值。
        public override void Update(IControllerConfig config)
        {
            var combo = (ComboBoxConfig)config;
            if (combo.Items.SequenceEqual(mConfig.Items))
                return;
            mConfig = combo;
            s.DisposeAll();
            BindWith(combo);
            mFold.RefreshSummary();
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly string mKey;
        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly ComboBoxController mController;
        ComboBoxConfig mConfig;
    }

    // 数组字段：字段本身带 key 标题，内容是独立的 Array/ListController 元素面板（元素行无标题，见 ArrayController.cs）。
    class ArrayCreator : Creator
    {
        public ArrayCreator(PropertyObjectController parent, PropertyKey key, ArrayConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30, FieldGap);
            mController = new ArrayController { ButtonRoot = parent.mButtonRoot, ButtonPath = parent.mButtonPath + "/" + key.Id };
            mController.Bind(parent.DataObject.Array(key.Id));
            mController.Apply(config);
            mCount = config.Elements.Count;
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key), summary: () => ItemsSummary(mCount));
            AttachContextMenu(key, () => config, mFold.Header);
        }

        public override Type ConfigType => typeof(ArrayConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];
        public override void Update(IControllerConfig config)
        {
            var array = (ArrayConfig)config;
            mController.Apply(array);
            mCount = array.Elements.Count;
            mFold.RefreshSummary();
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mController.Unbind();
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly ArrayController mController;
        int mCount;
    }

    class ListCreator : Creator
    {
        public ListCreator(PropertyObjectController parent, PropertyKey key, ListConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30, FieldGap);
            mController = new ListController { ButtonRoot = parent.mButtonRoot, ButtonPath = parent.mButtonPath + "/" + key.Id };
            mController.Bind(parent.DataObject.Array(key.Id));
            mController.Apply(config);
            mCount = config.Elements.Count;   // 显示的行数（含尚未物化的 seed 行）
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key), summary: () => ItemsSummary(mCount));
            AttachContextMenu(key, () => config, mFold.Header);
        }

        public override Type ConfigType => typeof(ListConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];
        public override void Update(IControllerConfig config)
        {
            var list = (ListConfig)config;
            mController.Apply(list);
            mCount = list.Elements.Count;
            mFold.RefreshSummary();
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mController.Unbind();
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly ListController mController;
        int mCount;
    }

    // tag 组字段：字段带 key 标题，内容是 chip 横排的 TagController（见 TagController.cs）。按整段值读写，
    // 故绑的是父对象 + 键，而不是 Array(key) 的逐元素外观。
    class TagCreator : Creator
    {
        public TagCreator(PropertyObjectController parent, PropertyKey key, TagConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30, FieldGap);
            mController = new TagController();
            mController.Bind(parent.DataObject, key.Id);
            mController.Apply(config);
            mConfig = config;
            // 摘要：已选标签的显示文本（多选取并集），逗号相连。
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => TagModel.Summary(parent.DataObject, key.Id, mConfig), watch: parent.DataObject);
            AttachContextMenu(key, () => mConfig, mFold.Header);
        }

        public override Type ConfigType => typeof(TagConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];
        public override void Update(IControllerConfig config)
        {
            mConfig = (TagConfig)config;
            mController.Apply(mConfig);
            mFold.RefreshSummary();
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mController.Unbind();
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly TagController mController;
        TagConfig mConfig;
    }

    // 变长键控容器字段：字段带 key 标题，内容是 ExtensibleObjectController（命名条目 + 增删，见 ExtensibleObjectController.cs）。
    // 绑到 Object(key.Id) 的嵌套对象——条目是该对象内的键。
    class ExtensibleObjectCreator : Creator
    {
        public ExtensibleObjectCreator(PropertyObjectController parent, PropertyKey key, ExtensibleObjectConfig config) : base(parent)
        {
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30, FieldGap);
            mController = new ExtensibleObjectController { ButtonRoot = parent.mButtonRoot, ButtonPath = parent.mButtonPath + "/" + key.Id };
            mController.Bind(parent.DataObject.Object(key.Id));
            mController.Apply(config);
            mConfig = config;
            // 摘要：已加入条目的名字（按插件给的顺序），逗号相连。
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => mConfig.Properties.Count == 0 ? null : string.Join(", ", mConfig.Properties.Select(kvp => kvp.Key.DisplayText ?? kvp.Key.Id)));
            AttachContextMenu(key, () => mConfig, mFold.Header);
        }

        public override Type ConfigType => typeof(ExtensibleObjectConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header, mController];
        public override void Update(IControllerConfig config)
        {
            mConfig = (ExtensibleObjectConfig)config;
            mController.Apply(mConfig);
            mFold.RefreshSummary();
        }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            mController.Unbind();
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly ExtensibleObjectController mController;
        ExtensibleObjectConfig mConfig;
    }

    class CheckBoxCreator : Creator
    {
        // 单行字段：勾选框就放在标题行右侧（行内控件，见 FieldFold.AddInline）；收起时同一位置显示「开 / 关」、不可改。
        public CheckBoxCreator(PropertyObjectController parent, PropertyKey key, CheckBoxConfig config) : base(parent)
        {
            mController = ObjectPoolManager.Get<Components.CheckBox>();
            mTitle = CreateTitle(key.DisplayText ?? key.Id, 30);

            mController.BindDataProperty(parent.DataObject.BooleanField(key.Id, config.DefaultValue), s);
            EditGate.Track(mController, s);
            mFold = new FieldFold(mTitle, [mController], parent.FoldKey(key),
                summary: () => MultipleOr(parent.DataObject.GetValue(key.Id, PropertyValue.Create(config.DefaultValue)),
                    v => v.ToBoolean(out var on) ? (on ? "On" : "Off").Tr("PropertyObjectController") : null),
                watch: parent.DataObject);
            mFold.AddInline(mController);
            AttachContextMenu(key, () => config, mFold.Header);
        }

        public override Type ConfigType => typeof(CheckBoxConfig);
        public override FieldFold? Fold => mFold;
        public override IEnumerable<Control> Views => [mFold.Header];
        public override void Update(IControllerConfig config) { }

        public override void Dispose()
        {
            base.Dispose();
            mFold.Dispose();
            ObjectPoolManager.Return(mTitle);
            ObjectPoolManager.Return(mController);
        }

        readonly Label mTitle;
        readonly FieldFold mFold;
        readonly Components.CheckBox mController;
    }

    // 按钮（ButtonConfig）：单行（标题在左、按钮靠右；或无标题整行按钮，见构造）。不绑任何数据字段——按钮没有值。
    // 点击交 ButtonActions 统一调度：执行期间不可重入（键 = 树作用域 + 本按钮路径，面板重建后仍认得出），
    // 结束后经 ButtonRoot 通知面板持有方刷新。Reconcile 时换用新 config：每次声明重算都会给出新闭包，点击永远用最新的。
    // 执行中的指示是叠在按钮上的边框光带（RunningBorder）：不往按钮里加内容物，文字与布局在执行前后完全不变。
    class ButtonCreator : Creator
    {
        // 两种形态，都是一行标准行高（30，按钮高 24、居中），都不折叠——按钮是操作、不是值，收起后无值可显示，
        // 压成一行反而像个空字段：
        //   · 有标题（显示名非空）：标题在左、按钮靠右，宽度随文字；
        //   · 无标题（显示名显式给空字符串，如 ("run", "")）：按钮铺满整行——按钮文字本身就是说明，再配标题反而重复。
        //     显示名缺省（null）照旧回退到 id 当标题，故只有作者明确给空串才是整行形态。
        public ButtonCreator(PropertyObjectController parent, PropertyKey key, ButtonConfig config) : base(parent)
        {
            mConfig = config;
            mRoot = parent.mButtonRoot;
            mPath = parent.mButtonPath + "/" + key.Id;
            mFullWidth = key.DisplayText is { Length: 0 };

            // 行容器不取对象池：要给它定高（30），池化的 DockPanel 还被对象字段借去当内容区，定高会漏给下一任主人。
            mRow = new DockPanel() { Height = RowHeight, LastChildFill = true };

            mText = new TextItem() { Text = config.Text };
            mButton = new Button() { Height = ButtonHeight };
            mButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
            mButton.AddContent(new() { Item = mText, ColorSet = new() { Color = Style.LIGHT_WHITE } });
            mButton.Clicked += OnClicked;
            mButtonHost = new Panel() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            mButtonHost.Children.Add(mButton);
            mButtonHost.Children.Add(mRunning);   // 叠在按钮上方、不参与命中

            if (mFullWidth)
            {
                mButtonHost.Margin = new(24, 0);
                mRow.Children.Add(mButtonHost);
            }
            else
            {
                mButtonHost.Margin = new(8, 0, 24, 0);
                DockPanel.SetDock(mButtonHost, Dock.Right);
                mRow.Children.Add(mButtonHost);
                mTitle = CreateTitle(key.DisplayText ?? key.Id, RowHeight);
                mRow.Children.Add(mTitle);
            }

            ButtonActions.RunningChanged += RefreshState;
            RefreshState();
        }

        const double RowHeight = 30;
        const double ButtonHeight = 24;   // 与拖拽数值框同高，两者并排时对齐

        public override void SetTrailing(bool trailing) => mRow.Margin = trailing ? new(0, 0, 0, 7) : default;   // 同 FieldFold 末项底边距

        string RunningKey => mRoot.Scope + mPath;

        // 按钮所在的窗口就是本次点击弹窗的父窗口（宿主确知点击来源，不靠"最前面的窗口"去猜）。
        void OnClicked() => ButtonActions.Click(RunningKey, mConfig, mRoot, TopLevel.GetTopLevel(mButton) as Window);

        void RefreshState()
        {
            bool running = ButtonActions.IsRunning(RunningKey);
            mButton.IsHitTestVisible = !running;
            mRunning.IsRunning = running;
            mText.Text = mConfig.Text;
            mButton.Width = mFullWidth ? double.NaN : Math.Max(72, MeasureText(mConfig.Text) + 32);   // 整行形态随行宽
            mButton.InvalidateVisual();
        }

        static double MeasureText(string text)
            => new Avalonia.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, Avalonia.Media.FlowDirection.LeftToRight,
                TuneLab.Utils.Extensions.DrawStringTypeface, 12, null).Width;

        public override Type ConfigType => typeof(ButtonConfig);
        public override IEnumerable<Control> Views => [mRow];

        public override void Update(IControllerConfig config)
        {
            mConfig = (ButtonConfig)config;
            RefreshState();
        }

        public override void Dispose()
        {
            base.Dispose();
            ButtonActions.RunningChanged -= RefreshState;
            mButton.Clicked -= OnClicked;
            mRunning.IsRunning = false;
            mButtonHost.Children.Clear();
            mRow.Children.Clear();
            if (mTitle != null)
                ObjectPoolManager.Return(mTitle);
        }

        readonly ButtonRoot mRoot;
        readonly string mPath;
        readonly bool mFullWidth;
        readonly Label? mTitle;
        readonly DockPanel mRow;
        readonly Button mButton;
        readonly Panel mButtonHost;
        readonly RunningBorder mRunning = new();
        readonly TextItem mText;
        ButtonConfig mConfig;
    }

    // bottomGap：标题下方留白。容器字段（数组/列表/键控容器）的内容紧接标题、自身不带上边距，需要它隔开；
    // 其余字段的控件自带上边距，取 0。标题出自对象池，故每次都显式重设，不继承上一任主人的值。
    static Label CreateTitle(string title, double height, double bottomGap = 0)
    {
        var label = ObjectPoolManager.Get<Label>();
        label.Height = height;
        label.Margin = new(0, 0, 0, bottomGap);
        label.FontSize = 12;
        label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;   // 标题行文字一律居中（行高 30 = 上 7 + 字 16 + 下 7）
        label.Foreground = Style.TEXT_LABEL.ToBrush();   // 标签暗一档、值亮（见 Style.TEXT_LABEL）
        label.Content = title;
        label.Padding = new(24, 0);
        return label;
    }

    static readonly Map<Type, Func<PropertyObjectController, PropertyKey, IControllerConfig, Creator>> mFactories = new()
    {
        { typeof(ObjectConfig), (parent, key, config) => new ObjectCreator(parent, key, (ObjectConfig)config) },
        { typeof(SliderConfig), (parent, key, config) => new SliderCreator(parent, key, (SliderConfig)config) },
        { typeof(DraggableNumberBoxConfig), (parent, key, config) => new DraggableNumberBoxCreator(parent, key, (DraggableNumberBoxConfig)config) },
        { typeof(TextBoxConfig), (parent, key, config) => new TextCreator(parent, key, (TextBoxConfig)config) },
        { typeof(PathPickerConfig), (parent, key, config) => new PathPickerCreator(parent, key, (PathPickerConfig)config) },
        { typeof(ComboBoxConfig), (parent, key, config) => new ComboBoxCreator(parent, key, (ComboBoxConfig)config) },
        { typeof(CheckBoxConfig), (parent, key, config) => new CheckBoxCreator(parent, key, (CheckBoxConfig)config) },
        { typeof(ArrayConfig), (parent, key, config) => new ArrayCreator(parent, key, (ArrayConfig)config) },
        { typeof(ListConfig), (parent, key, config) => new ListCreator(parent, key, (ListConfig)config) },
        { typeof(TagConfig), (parent, key, config) => new TagCreator(parent, key, (TagConfig)config) },
        { typeof(ExtensibleObjectConfig), (parent, key, config) => new ExtensibleObjectCreator(parent, key, (ExtensibleObjectConfig)config) },
        { typeof(ButtonConfig), (parent, key, config) => new ButtonCreator(parent, key, (ButtonConfig)config) },
    };

    ButtonRoot mButtonRoot = new();
    ObjectConfig? mPendingConfig;   // 窗口编辑中推迟的最新 config（见 Reconcile）
    string mButtonPath = string.Empty;
    IDataPropertyObject? mDataObject;
    Dictionary<string, Creator> mCreatorsByKey = new();
    List<string> mOrder = new();
}
