using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.GUI.Input;
using TuneLab.SDK;
using TuneLab.Utils;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.GUI.Components;

// 插件画布（CanvasConfig）的宿主侧：一块区域 + 插件控件实例的生命周期 + 草稿 / 提交 + 绘图 / 事件翻译。
//
// 生命周期：实例寿命 = 一次声明。值由插件在声明时取、让工厂捕获（宿主不读回给控件），所以工厂只对它那次声明时的值有效：
// · SetFactory（每次声明更新都会调）：换上新工厂、作废当前实例并重画，真正调工厂推迟到下一次需要它时（渲染 / 鼠标事件）——
//   收起的字段不建实例，同一轮里的多次作废也只建一次。这是建实例的唯一来源。
// · 本字段的值被外部改了：实例与工厂一并作废，但不重画——屏幕上留着最后画出的一帧，撑到面板按新声明送来新工厂（面板刷新
//   推迟到下一拍，所在窗口编辑中则推迟到编辑结束）。旧工厂捕获的是旧值，绝不拿它重建。
// 草稿：插件的 SetValue 只写进本实例的草稿（不进工程——工程里未提交的写入是整个文档共享的，会被别处任何一次提交顺手带走、
// 也会让撤销失效）；CommitValue 才把草稿一次写入工程并提交成一个撤销步。编辑状态由插件宣告（BeginEdit / EndEdit，宿主不猜）。
// 插件宣告了编辑、或有草稿、或有弹出 / 弹窗开着 = 面板仍在使用：登记为所在窗口的"编辑中"（见 EditGate），期间面板不按新声明刷新、
// 本实例不会被换掉。
// 宿主从不替插件提交、也不替它结束编辑：实例作废时（外部改了本字段的值、面板被拆、画布离开界面）草稿与编辑状态一律作废。
// 外部改值的判定：本字段的值变了、且不是本画布自己提交的（提交时记下写后的值，通知来时与它比对）。
// 插件回调一律包在 try 里：抛出即记录（经 ErrorAttributed 标明来源插件）并让该实例失效，直到下次重建。
// 事件的 Handled：只在宿主有默认动作的事件上读（滚轮 = 滚动面板），其余事件忽略。事件按实例成对转发，见下方事件转发。
internal sealed class PluginCanvas : Component
{
    // 换绑数据字段：当前实例作废（工厂保留——与 SetFactory 的先后不限）。
    public void Bind(IDataProperty<PropertyValue> field)
    {
        Drop();
        s.DisposeAll();
        mField = field;
        mSeen = CurrentValue;   // 与 SetFactory 先后不限：两处都记下声明时的值
        field.Modified.AsEverytime().Subscribe(_ => OnFieldModified(), s);
    }

    public void Unbind()
    {
        Drop();
        s.DisposeAll();
        mField = null;
        mFactory = null;
    }

    // 换上新声明的工厂：当前实例作废并重画。记下声明时的值：此后值再变，这个工厂就过时了（见 OnFieldModified）。
    public void SetFactory(Func<ICanvasHost, ICanvasControl> factory)
    {
        mFactory = factory;
        mSeen = CurrentValue;
        Drop();
        InvalidateVisual();
    }

    PropertyValue CurrentValue => mField is IRawValueProperty raw ? raw.RawValue : mField?.Value ?? PropertyValue.Null;

    // 没有异常堆栈可归因时（如插件误用 API），按工厂所在程序集的加载上下文名标明来源插件——与 Log.ErrorAttributed
    // 按堆栈找到的是同一个名字（插件包目录名）。宿主自己的程序集返回空串。
    string PluginPrefix() => SourceName() is { } name ? "[" + name + "] " : "";

    // 来源插件名（插件包目录名，插件改不了）；宿主自己的程序集为 null。
    string? SourceName()
    {
        var context = mFactory == null ? null : AssemblyLoadContext.GetLoadContext(mFactory.Method.Module.Assembly);
        return context == null || context == AssemblyLoadContext.Default || string.IsNullOrEmpty(context.Name) ? null : context.Name;
    }

    // 宿主启动时注入（同 ButtonActions.Invoke）：按来源插件名（宿主为 "host"）与父窗口造一个弹窗服务。
    // 默认（测试 / 无宿主环境）给无窗口口径。
    public static Func<string, Window?, IDialogService> CreateDialogService { get; set; } = (_, _) => ButtonActions.NoWindowDialogs;

    // 弹出菜单的弹层：挂在画布自己的子树上（Popup 要在视觉树里才弹得出来），锚定画布、按画布坐标定位。用到时才建。
    DropDownMenu Menu()
    {
        if (mMenu == null)
        {
            mMenu = new DropDownMenu(this);
            AttachPopup(mMenu);
        }
        return mMenu;
    }

    // 弹层挂进 / 摘出画布的子树（Popup 要在视觉树里才弹得出来）。
    public void AttachPopup(Control popup)
    {
        VisualChildren.Add(popup);
        LogicalChildren.Add(popup);
    }

    public void DetachPopup(Control popup)
    {
        VisualChildren.Remove(popup);
        LogicalChildren.Remove(popup);
    }

    DropDownMenu? mMenu;

    void OnFieldModified()
    {
        if (mOwnWrite > 0)
            return;
        // 通知面是整个数据对象（任一字段变都会来），只有本字段的值真的变了才算外部改值。
        if (CurrentValue.Equals(mSeen))
            return;
        Kill();
    }

    // 值变了：实例（连同草稿、编辑状态、弹出）与工厂一并作废，不再收事件、写回不生效。不请求重画——留着最后一帧，
    // 等新工厂（SetFactory）到了再建；这期间万一被迫重画（如尺寸变了），只画底色。
    void Kill()
    {
        mFactory = null;
        if (mSession == null)
            return;
        var session = mSession;
        mSession = null;
        session.Close();
    }

    // 作废当前实例（连同草稿，下次需要时重建）并请求重画。
    void Drop()
    {
        if (mSession == null)
            return;
        var session = mSession;
        mSession = null;
        session.Close();
        InvalidateVisual();
    }

    Session? Ensure()
    {
        if (mSession != null)
            return mSession.IsFaulted ? null : mSession;
        if (mField == null || mFactory == null)
            return null;

        var session = new Session(this, mField);
        mSession = session;
        try
        {
            session.Control = mFactory(session) ?? throw new InvalidOperationException("CanvasConfig factory returned null.");
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Canvas control factory threw", ex);
            session.Fault();
        }
        if (session.IsFaulted)
            return null;

        // 新实例建好时指针可能已停在画布上（换了实例 / 面板重建后画布出现在静止的指针下）：主动给它补 Enter，
        // 不等用户动鼠标（悬停高亮等）。不靠 Avalonia：控件没变时它不再发指针事件，新控件的命中检测要等后续画面更新、
        // 时有时无——按窗口最后的指针位置自己判（PointerTracker）。
        // 先当场补：在 Render 里建的实例，第一帧就带着悬停状态画出来（推迟的话第一帧是"指针不在"的样子，下一拍才亮——
        // 每次提交后换实例都会闪一下）。Render 时布局已定，判得出；判不出（如刚挂上、尺寸还是 0）再推迟一拍补。
        if (!TryEnter(session))
            Dispatcher.UIThread.Post(() => TryEnter(session));
        return session;
    }

    bool TryEnter(Session session)
    {
        if (mSession != session || session.Inside)
            return true;
        if (!PointerTracker.TryGetOver(this, out var position, out var modifiers))
            return false;
        Enter(session, ToPoint(position), ToModifiers(modifiers.ToModifierKeys()));
        return true;
    }

    public override void Render(DrawingContext context)
    {
        // 底 = 宿主输入框同款（Style.BACK、圆角 4，同数值框 / 路径框）：一眼看出这是一块可编辑的区域；
        // 也让画布整块可命中（插件没画东西的地方同样收得到鼠标）。插件画在它上面。
        context.FillRectangle(Style.BACK.ToBrush(), new Rect(Bounds.Size), 4);
        mRendering = true;
        try
        {
            if (Ensure() is not { } session)
                return;
            var drawing = new Drawing(context, Scaling);
            session.Call(control => control.Render(drawing));
            drawing.Expire();
        }
        finally
        {
            mRendering = false;
        }
    }

    bool mRendering;   // 正在画：这期间插件请求重画（如建实例时补发的 Enter 里）不必再排一帧

    // —— 事件转发：宿主保证每个实例收到的事件成对（契约见 ICanvasControl）——
    // · Enter / Leave 严格交替：换了实例时指针可能已在画布内，转发下一个事件前先给新实例补一次 Enter；
    // · Up 只转发给收到过对应 Down 的实例（换实例后不会收到孤立的抬起）；鼠标捕获意外丢失时补发 Up（IsClick = false）。

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        var button = ToButton(e.MouseButtonType);
        mPressed |= Bit(button);
        if (Prepare() is not { } session)
            return;
        session.Pressed |= Bit(button);
        session.Call(control => control.OnMouseDown(new CanvasMouseButtonEvent(ToPoint(e.Position), ToModifiers(e.KeyModifiers), button, e.IsDoubleClick, false)));
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        var button = ToButton(e.MouseButtonType);
        if ((mPressed & Bit(button)) != 0)   // 在别处按下、到画布上才松开的抬起不算
            Release(button, ToPoint(e.Position), ToModifiers(e.KeyModifiers), e.IsClick);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
        => Prepare()?.Call(control => control.OnMouseMove(new CanvasMouseEvent(ToPoint(e.Position), ToModifiers(e.KeyModifiers))));

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        if (Ensure() is { Inside: false } session)
            Enter(session, ToPoint(e.Position), ToModifiers(e.KeyModifiers));
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        if (mSession is { Inside: true } session)   // 不为发一个 Leave 新建实例
            Leave(session, ToPoint(e.Position), ToModifiers(e.KeyModifiers));
    }

    // 插件置了 Handled 才消费这次滚轮；没置（含没实现 OnScroll、实例失效）则照常冒泡去滚面板。
    protected override void OnScroll(WheelEventArgs e)
    {
        var args = new CanvasWheelEvent(ToPoint(e.Position), ToModifiers(e.KeyModifiers), ToPoint(e.Delta));
        Prepare()?.Call(control => control.OnScroll(args));
        e.Handled = args.Handled;
    }

    // —— 键盘：画布有焦点时才来（Component 可聚焦，点一下即获得焦点）。同样按实例成对转发：
    // · 重复与否按实例算：本实例收到过这个键的按下 = 重复；换了实例后新实例收到的第一个按下记作刚按下（不是重复），
    //   于是按住方向键微调、每格都提交（每次提交都换实例）也能一直连下去；
    // · 抬起只转发给收到过对应按下的实例；失去焦点 / 离开界面时补发抬起（之后的抬起到不了这里）。
    // 多个物理键映到同一个 CanvasKey（左右 Shift、macOS 的 Cmd 与 Control）：第一个按下才算按下、最后一个抬起才算抬起。
    // 没收录的键不经画布、照常冒泡。插件置 Handled 才吞掉这个键（快捷键、外层面板就收不到）。

    protected override void OnKeyDownEvent(KeyEventArgs e) => KeyDown(e);
    protected override void OnKeyPressedEvent(KeyEventArgs e) => KeyDown(e);   // Component 把同一键的后续按下（系统重复）分到这里

    void KeyDown(KeyEventArgs e)
    {
        if (ToKey(e.Key) is not { } key)
            return;
        mKeys[e.Key] = key;
        if (Ensure() is not { } session)
            return;
        var args = new CanvasKeyEvent(key, ToModifiers(e.ModifierKeys()), isRepeat: !session.Keys.Add(key));
        session.Call(control => control.OnKeyDown(args));
        e.Handled = args.Handled;
    }

    protected override void OnKeyUpEvent(KeyEventArgs e)
    {
        if (!mKeys.Remove(e.Key, out var key) || mKeys.ContainsValue(key))
            return;   // 在别处按下的键 / 同一 CanvasKey 还有别的物理键按着
        if (ReleaseKey(key, ToModifiers(e.ModifierKeys())) is { } args)
            e.Handled = args.Handled;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        ReleaseKeys();
    }

    CanvasKeyEvent? ReleaseKey(CanvasKey key, CanvasModifierKeys modifiers)
    {
        if (mSession is not { } session || !session.Keys.Remove(key))
            return null;
        var args = new CanvasKeyEvent(key, modifiers, isRepeat: false);
        session.Call(control => control.OnKeyUp(args));
        return args;
    }

    void ReleaseKeys()
    {
        var keys = new HashSet<CanvasKey>(mKeys.Values);
        mKeys.Clear();
        foreach (var key in keys)
            ReleaseKey(key, ToModifiers(Modifiers));
    }

    // 捕获丢失（按着鼠标时切走窗口等）：抬起再也不会来，补发。推迟一拍再判：正常松开时 Avalonia 也会释放捕获，
    // 让真正的抬起（带 IsClick）先到、清掉按键位，这里就什么都不做。
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Dispatcher.UIThread.Post(ReleaseAll);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PointerTracker.Watch(this);   // 补发 Enter 要知道指针停在哪（见 Ensure）
        mTopLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (mTopLevel != null)
            mTopLevel.ScalingChanged += OnScalingChanged;
    }

    // 设备像素比变了（窗口拖到另一块屏幕）：插件按它生成的位图、宿主栅格化的 SVG 都要按新比例重画。
    void OnScalingChanged(object? sender, System.EventArgs e) => InvalidateVisual();

    double Scaling => mTopLevel?.RenderScaling ?? 1;

    // 离开界面（面板被拆、页签切走、窗口关闭）：补齐成对事件，再连同草稿作废实例——看不见的草稿不能一直挂着、
    // 让所在窗口的面板停止刷新。
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (mTopLevel != null)
            mTopLevel.ScalingChanged -= OnScalingChanged;
        mTopLevel = null;
        ReleaseAll();
        ReleaseKeys();
        if (mSession is { Inside: true } session)
            Leave(session, ToPoint(MousePosition), ToModifiers(Modifiers));
        Drop();
    }

    // 取当前实例并补齐 Enter（换了实例而指针仍在画布内时）。
    Session? Prepare()
    {
        var session = Ensure();
        if (session is { Inside: false } && IsPointerOver)
            Enter(session, ToPoint(MousePosition), ToModifiers(Modifiers));
        return session is { IsFaulted: false } ? session : null;
    }

    // Enter 之后紧跟一个同位置的 Move（真实进入与宿主补发的一样）：按位置算的状态（悬停等）插件只在 OnMouseMove 里算即可。
    // 真实进入时 Avalonia 随后还会再发一个同位置的移动，插件可能连收两个，位移为零、无害。
    static void Enter(Session session, Point position, CanvasModifierKeys modifiers)
    {
        session.Inside = true;
        session.Call(control => control.OnMouseEnter(new CanvasMouseEvent(position, modifiers)));
        session.Call(control => control.OnMouseMove(new CanvasMouseEvent(position, modifiers)));
    }

    static void Leave(Session session, Point position, CanvasModifierKeys modifiers)
    {
        session.Inside = false;
        session.Call(control => control.OnMouseLeave(new CanvasMouseEvent(position, modifiers)));
    }

    void Release(CanvasMouseButton button, Point position, CanvasModifierKeys modifiers, bool isClick)
    {
        mPressed &= ~Bit(button);
        if (mSession is { } session && (session.Pressed & Bit(button)) != 0)
        {
            session.Pressed &= ~Bit(button);
            session.Call(control => control.OnMouseUp(new CanvasMouseButtonEvent(position, modifiers, button, false, isClick)));
        }
    }

    void ReleaseAll()
    {
        foreach (var button in Enum.GetValues<CanvasMouseButton>())
        {
            if ((mPressed & Bit(button)) != 0)
                Release(button, ToPoint(MousePosition), ToModifiers(Modifiers), false);
        }
    }

    static int Bit(CanvasMouseButton button) => 1 << (int)button;

    static Point ToPoint(Avalonia.Point point) => new(point.X, point.Y);

    static CanvasModifierKeys ToModifiers(ModifierKeys keys)
    {
        var result = CanvasModifierKeys.None;
        if ((keys & ModifierKeys.Ctrl) != 0) result |= CanvasModifierKeys.Ctrl;
        if ((keys & ModifierKeys.Alt) != 0) result |= CanvasModifierKeys.Alt;
        if ((keys & ModifierKeys.Shift) != 0) result |= CanvasModifierKeys.Shift;
        return result;
    }

    static CanvasKey? ToKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => CanvasKey.A + (key - Key.A),
        >= Key.D0 and <= Key.D9 => CanvasKey.Digit0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => CanvasKey.Digit0 + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F12 => CanvasKey.F1 + (key - Key.F1),
        Key.Back => CanvasKey.Backspace,
        Key.Tab => CanvasKey.Tab,
        Key.Enter => CanvasKey.Enter,
        Key.Escape => CanvasKey.Escape,
        Key.Space => CanvasKey.Space,
        Key.PageUp => CanvasKey.PageUp,
        Key.PageDown => CanvasKey.PageDown,
        Key.End => CanvasKey.End,
        Key.Home => CanvasKey.Home,
        Key.Left => CanvasKey.ArrowLeft,
        Key.Up => CanvasKey.ArrowUp,
        Key.Right => CanvasKey.ArrowRight,
        Key.Down => CanvasKey.ArrowDown,
        Key.Delete => CanvasKey.Delete,
        Key.LeftShift or Key.RightShift => CanvasKey.Shift,
        Key.LeftCtrl or Key.RightCtrl => CanvasKey.Ctrl,
        Key.LeftAlt or Key.RightAlt => CanvasKey.Alt,
        Key.LWin or Key.RWin when OperatingSystem.IsMacOS() => CanvasKey.Ctrl,   // Cmd，同修饰键的归一
        _ => null,
    };

    static CanvasMouseButton ToButton(MouseButtonType type) => type switch
    {
        MouseButtonType.PrimaryButton => CanvasMouseButton.Primary,
        MouseButtonType.MiddleButton => CanvasMouseButton.Middle,
        MouseButtonType.SecondaryButton => CanvasMouseButton.Secondary,
        _ => CanvasMouseButton.Other,
    };

    // 一个插件控件实例的宿主面（即交给它的 ICanvasHost）。Close 之后一切写回不生效。
    sealed class Session(PluginCanvas view, IDataProperty<PropertyValue> field) : ICanvasHost
    {
        public ICanvasControl Control { get; set; } = null!;
        public bool IsFaulted { get; private set; }
        // 本实例收到的事件状态（成对转发用，见事件转发）：是否收到过 Enter 而未 Leave、哪些键收到过 Down 而未 Up。
        public bool Inside { get; set; }
        public int Pressed { get; set; }
        public HashSet<CanvasKey> Keys { get; } = [];   // 收到过按下而未抬起的键
        bool IsAlive => !mClosed && !IsFaulted;

        public double Width => view.Bounds.Width;
        public double Height => view.Bounds.Height;
        public double DevicePixelRatio => view.Scaling;
        public void Invalidate()
        {
            VerifyThread();
            if (IsAlive && !view.mRendering)
                view.InvalidateVisual();
        }

        // 编辑状态：插件声明（是否正在编辑只有插件知道，宿主不猜）。计数、可嵌套；与草稿同时起作用，见 UpdateLock。
        public void BeginEdit()
        {
            VerifyThread();
            if (!IsAlive)
                return;
            mEditCount++;
            UpdateLock();
        }

        public void EndEdit()
        {
            VerifyThread();
            if (!IsAlive)
                return;   // 实例已作废：编辑状态随之清零，配对的 EndEdit 不算错
            if (mEditCount == 0)
            {
                Log.Error(view.PluginPrefix() + "Canvas EndEdit was called without a matching BeginEdit; ignored.");
                return;
            }
            mEditCount--;
            UpdateLock();
        }

        public bool SetValue(PropertyValue value)
        {
            VerifyThread();
            if (!IsAlive)
                return false;
            // Multiple 不是值（多选读出时"各对象不一致"的标记），草稿也不收：收了就会被提交、或被当成"有值"画出来。
            try
            {
                PropertySlot.EnsureStorable(value);
            }
            catch (ArgumentException)
            {
                Log.Error(view.PluginPrefix() + "Canvas SetValue was given Multiple (the multi-selection's differing values) and nothing was written. Write a real value; to hold the canvas while editing without changing the value, use BeginEdit.");
                return false;
            }
            mDraft = value;
            UpdateLock();
            view.InvalidateVisual();
            return true;
        }

        public void CommitValue()
        {
            VerifyThread();
            if (!IsAlive || mDraft is not { } value)
                return;
            ClearDraft();
            // 兜底：SetValue 已拒收 Multiple，正常走不到这里。仍留一道：存储层会拒它，而 CommitValue 常在 await 之后调
            //（滚轮推迟提交），抛出去没有人接——不写，草稿已丢，记一条归因到插件的错误。
            try
            {
                PropertySlot.EnsureStorable(value);
            }
            catch (ArgumentException)
            {
                Log.Error(view.PluginPrefix() + "Canvas CommitValue was given Multiple (the multi-selection's differing values) and nothing was written. Commit a real value, or call DiscardValue when nothing changed.");
                view.InvalidateVisual();
                return;
            }
            view.mOwnWrite++;
            try
            {
                field.Set(value);   // 与草稿据以的值相同则不产生改动，Commit 也就什么都不做
                field.Commit();
                view.mSeen = view.CurrentValue;
            }
            finally
            {
                view.mOwnWrite--;
            }
            view.InvalidateVisual();
        }

        public void DiscardValue()
        {
            VerifyThread();
            if (!IsAlive || mDraft == null)
                return;
            ClearDraft();
            view.InvalidateVisual();
        }

        public void Call(Action<ICanvasControl> action)
        {
            if (!IsAlive)
                return;
            try
            {
                action(Control);
            }
            catch (Exception ex)
            {
                Log.ErrorAttributed("Canvas control threw", ex);
                Fault();
            }
        }

        public void Fault()
        {
            IsFaulted = true;
            ClosePopup();
            ClearDraft();
            view.InvalidateVisual();
        }

        public void Close()
        {
            mClosed = true;
            ClosePopup();
            ClearDraft();
        }

        // —— 弹出：同一时刻至多一个（mPopup），关闭 = 结果为 null。结果经 RunContinuationsAsynchronously 交回插件：
        // 插件的续体总在弹出状态清掉之后才跑，那时再 SetValue / CommitValue 不会撞上"弹出仍开着"。

        public Task<string?> PopupTextBox(string text, double x, double y, double width, double height, double fontSize,
            CanvasAlignment alignment = CanvasAlignment.Center, bool multiline = false)
        {
            VerifyThread();
            ArgumentNullException.ThrowIfNull(text);
            if (!IsAlive)
                return Task.FromResult<string?>(null);
            ClosePopup();

            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = new PopupToken();
            var popup = new CanvasTextPopup(view, text, x, y, width, height, fontSize, alignment, multiline, value =>
            {
                if (mPopup == token)
                {
                    mPopup = null;
                    UpdateLock();
                }
                result.TrySetResult(value);
            });
            token.Close = popup.Cancel;
            mPopup = token;
            UpdateLock();
            popup.Open();
            return result.Task;
        }

        public Task<PropertyValue?> PopupMenu(double x, double y, double width, double height, IReadOnlyList<TuneLab.SDK.ComboBoxItem> items)
        {
            VerifyThread();
            ArgumentNullException.ThrowIfNull(items);
            if (!IsAlive)
                return Task.FromResult<PropertyValue?>(null);
            ClosePopup();

            var result = new TaskCompletionSource<PropertyValue?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var menu = view.Menu();
            var token = new PopupToken();
            void Finish(PropertyValue? value)
            {
                if (result.Task.IsCompleted)
                    return;
                menu.ItemPicked -= OnPicked;
                menu.Closed -= OnClosed;
                if (mPopup == token)
                {
                    mPopup = null;
                    UpdateLock();
                }
                result.TrySetResult(value);
            }
            void OnPicked(DropDownItem item) => Finish(item.Tag is PropertyValue value ? value : (PropertyValue?)null);
            // 选中时菜单先关（Closed）再报选中（ItemPicked）：关闭的结果推迟一拍，让选中先到。
            void OnClosed() => Dispatcher.UIThread.Post(() => Finish(null));
            menu.ItemPicked += OnPicked;
            menu.Closed += OnClosed;
            token.Close = () =>
            {
                Finish(null);
                menu.Close();
            };
            mPopup = token;
            UpdateLock();
            menu.Open(ComboBoxController.BuildItems(items), 0, AnchorRect(x, y, width, height));
            return result.Task;
        }

        // 菜单的锚点矩形：收进画布范围、宽高至少 1。Avalonia 定位时把锚点矩形与画布范围求交，零尺寸（"在这一点弹出"）
        // 或落在画布外的矩形交出来是空的，菜单就跑到画布左上角。
        Rect AnchorRect(double x, double y, double width, double height)
        {
            double maxX = Math.Max(0, view.Bounds.Width - 1), maxY = Math.Max(0, view.Bounds.Height - 1);
            double left = double.IsFinite(x) ? Math.Clamp(x, 0, maxX) : 0;
            double top = double.IsFinite(y) ? Math.Clamp(y, 0, maxY) : 0;
            double right = double.IsFinite(x) && double.IsFinite(width) ? Math.Clamp(x + width, left + 1, maxX + 1) : left + 1;
            double bottom = double.IsFinite(y) && double.IsFinite(height) ? Math.Clamp(y + height, top + 1, maxY + 1) : top + 1;
            return new Rect(left, top, right - left, bottom - top);
        }

        void ClosePopup() => mPopup?.Close();

        sealed class PopupToken
        {
            public Action Close = () => { };
        }

        // —— 弹窗服务：画布有效期内都能用；每次弹窗打开期间算"在用"（mDialogCount）。真正的弹窗由宿主注入的实现画。

        public IDialogService DialogService => mDialogService ??= new CanvasDialogService(this);

        async Task<T> RunDialog<T>(Func<IDialogService, Task<T>> call)
        {
            if (!IsAlive)
                throw new InvalidOperationException("ICanvasHost.DialogService is no longer valid: the canvas control instance was replaced or closed.");
            var dialogs = mDialogs ??= CreateDialogService(view.SourceName() ?? "host", TopLevel.GetTopLevel(view) as Window);
            mDialogCount++;
            UpdateLock();
            try
            {
                return await call(dialogs);
            }
            finally
            {
                mDialogCount--;
                UpdateLock();
            }
        }

        // IDialogService 任意线程可调：切到 UI 线程再进 RunDialog（计数与 EditGate 只在 UI 线程上动）。
        sealed class CanvasDialogService(Session session) : IDialogService
        {
            public Task ShowMessage(string title, string message) => Run(async d => { await d.ShowMessage(title, message); return true; });
            public Task<bool> Confirm(string title, string message) => Run(d => d.Confirm(title, message));
            public Task RunWithProgress(string title, Func<IProgressReporter, CancellationToken, Task> work)
                => Run(async d => { await d.RunWithProgress(title, work); return true; });
            public Task<PropertyObject?> PromptInputs(string title, ObjectConfig config) => Run(d => d.PromptInputs(title, config));
            public Task<PropertyObject?> PromptInputs(string title, Func<PropertyObject, ObjectConfig> config) => Run(d => d.PromptInputs(title, config));

            Task<T> Run<T>(Func<IDialogService, Task<T>> call)
                => Dispatcher.UIThread.CheckAccess() ? session.RunDialog(call) : Dispatcher.UIThread.InvokeAsync(() => session.RunDialog(call));
        }

        void ClearDraft()
        {
            mDraft = null;
            UpdateLock();
        }

        // 面板是否在用 = 插件声明了编辑（BeginEdit 未配平）、有草稿、有弹出或弹窗开着，几者同时起作用、互不替代；
        // 实例作废后一律不算。在用即在所在窗口的 EditGate 上登记一次（所在窗口的属性面板推迟刷新、本实例不被换掉），
        // 不在用即撤销登记。
        void UpdateLock()
        {
            if (!IsAlive)
                mEditCount = 0;
            if (IsAlive && (mEditCount > 0 || mDraft != null || mPopup != null || mDialogCount > 0))
            {
                mEditing ??= EditGate.Of(view)?.BeginEdit();
            }
            else
            {
                mEditing?.Dispose();
                mEditing = null;
            }
        }

        static void VerifyThread()
        {
            if (!Dispatcher.UIThread.CheckAccess())
                throw new InvalidOperationException("ICanvasHost can only be used on the UI thread.");
        }

        bool mClosed;
        PropertyValue? mDraft;
        int mEditCount;          // 插件 BeginEdit 未配平的次数
        PopupToken? mPopup;      // 开着的弹出（文本框 / 菜单），至多一个
        int mDialogCount;        // 开着的弹窗数（弹窗可嵌套：进度窗里再弹确认）
        IDialogService? mDialogService;   // 交给插件的那一个（包一层：有效期检查 + 在用计数）
        IDialogService? mDialogs;         // 宿主注入的真正实现，用到时才建
        IDisposable? mEditing;   // 面板在用期间在所在窗口 EditGate 上的登记（见 UpdateLock）
    }

    // ICanvasDrawing 的 Avalonia 实现：只在一次 Render 期间有效（之后调用抛 InvalidOperationException）。
    // 绘制状态（Push*）记在本次 Render 自己的栈上：撤销某一层连同其内层一起撤销，Render 结束时（Expire）撤销剩下的——
    // 插件漏了 Dispose 也不会把状态带进宿主后续的绘制。
    sealed class Drawing(DrawingContext context, double scaling) : ICanvasDrawing
    {
        public void Expire()
        {
            PopTo(0);
            mExpired = true;
        }

        public void DrawImage(CanvasImage image, double x, double y, double width, double height)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(image);
            if (width > 0 && height > 0 && image.Content is CanvasImageFactory.Content content)
                content.Draw(context, new Rect(x, y, width, height), scaling);
        }

        public IDisposable PushOpacity(double opacity)
        {
            Verify();
            return Push(context.PushOpacity(double.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1)));
        }

        public IDisposable PushClip(double x, double y, double width, double height, double cornerRadius = 0)
        {
            Verify();
            var rect = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
            return Push(context.PushClip(new RoundedRect(rect, Radius(cornerRadius))));
        }

        public IDisposable PushTransform(System.Numerics.Matrix3x2 transform)
        {
            Verify();
            var m = transform;
            return Push(context.PushTransform(new Matrix(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32)));
        }

        IDisposable Push(DrawingContext.PushedState state)
        {
            mStack.Add(state);
            return new Pushed(this, mStack.Count);
        }

        // 撤销到只剩 depth 层（后进先出）。
        void PopTo(int depth)
        {
            while (mStack.Count > depth)
            {
                mStack[^1].Dispose();
                mStack.RemoveAt(mStack.Count - 1);
            }
        }

        // 第 depth 层的撤销句柄：撤销它 = 撤销到它之下。早已撤销（被外层带走、Render 已结束）则什么都不做。
        sealed class Pushed(Drawing owner, int depth) : IDisposable
        {
            public void Dispose()
            {
                if (mDisposed)
                    return;
                mDisposed = true;
                if (!owner.mExpired && owner.mStack.Count >= depth)
                    owner.PopTo(depth - 1);
            }

            bool mDisposed;
        }

        readonly List<DrawingContext.PushedState> mStack = [];

        public void DrawLine(Point from, Point to, System.Drawing.Color color, double width = 1)
        {
            Verify();
            if (width > 0)
                context.DrawLine(StrokePen(color, width), from.ToAvalonia(), to.ToAvalonia());
        }

        public void DrawPolyline(IReadOnlyList<Point> points, System.Drawing.Color color, double width = 1)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count >= 2 && width > 0)
                context.DrawGeometry(null, StrokePen(color, width), Polygon(points, closed: false));
        }

        public void DrawPolygon(IReadOnlyList<Point> points, System.Drawing.Color color, double width = 1)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count >= 2 && width > 0)
                context.DrawGeometry(null, StrokePen(color, width), Polygon(points, closed: true));
        }

        public void FillPolygon(IReadOnlyList<Point> points, System.Drawing.Color color)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count >= 3)
                context.DrawGeometry(Brush(color), null, Polygon(points, closed: true));
        }

        public void DrawRectangle(double x, double y, double width, double height, System.Drawing.Color color, double lineWidth = 1, double cornerRadius = 0)
        {
            Verify();
            if (width >= 0 && height >= 0 && lineWidth > 0)
                context.DrawRectangle(null, new Pen(Brush(color), lineWidth), new Rect(x, y, width, height), Radius(cornerRadius), Radius(cornerRadius));
        }

        public void FillRectangle(double x, double y, double width, double height, System.Drawing.Color color, double cornerRadius = 0)
        {
            Verify();
            if (width > 0 && height > 0)
                context.DrawRectangle(Brush(color), null, new Rect(x, y, width, height), Radius(cornerRadius), Radius(cornerRadius));
        }

        public void DrawEllipse(Point center, double radiusX, double radiusY, System.Drawing.Color color, double width = 1)
        {
            Verify();
            if (radiusX >= 0 && radiusY >= 0 && width > 0)
                context.DrawEllipse(null, new Pen(Brush(color), width), center.ToAvalonia(), radiusX, radiusY);
        }

        public void FillEllipse(Point center, double radiusX, double radiusY, System.Drawing.Color color)
        {
            Verify();
            if (radiusX > 0 && radiusY > 0)
                context.DrawEllipse(Brush(color), null, center.ToAvalonia(), radiusX, radiusY);
        }

        public void DrawText(string text, double x, double y, double width, double height, System.Drawing.Color color, double fontSize, CanvasAlignment alignment = CanvasAlignment.Center)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(text);
            if (!(fontSize > 0))
                return;
            // CanvasAlignment 的取值与宿主的九宫格编码一致（见其注释）；未知值按居中。同一个对齐既定矩形内的锚点、又定文字自身的
            // 对齐点，于是零尺寸矩形 = 以该点为锚。
            int align = Enum.IsDefined(alignment) ? (int)alignment : (int)CanvasAlignment.Center;
            context.DrawString(text, new Rect(x, y, Math.Max(0, width), Math.Max(0, height)), Brush(color), fontSize, align, align, alignLines: true);
        }

        public (double Width, double Height) MeasureText(string text, double fontSize)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(text);
            if (!(fontSize > 0) || text.Length == 0)
                return (0, 0);
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                TuneLab.Utils.Extensions.DrawStringTypeface, fontSize, null);
            return (formatted.Width, formatted.Height);
        }

        void Verify()
        {
            if (mExpired)
                throw new InvalidOperationException("ICanvasDrawing is only valid during ICanvasControl.Render.");
        }

        static double Radius(double cornerRadius) => cornerRadius > 0 ? cornerRadius : 0;

        static IBrush Brush(System.Drawing.Color color) => new SolidColorBrush(Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B));

        // 折线 / 多边形的转角与端点取圆头（同宿主自己画曲线的 DrawCurve），粗线时不出尖角。
        static Pen StrokePen(System.Drawing.Color color, double width) => new(Brush(color), width, null, PenLineCap.Round, PenLineJoin.Round);

        static StreamGeometry Polygon(IReadOnlyList<Point> points, bool closed)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0].ToAvalonia(), closed);
                for (int i = 1; i < points.Count; i++)
                    g.LineTo(points[i].ToAvalonia());
                g.EndFigure(closed);
            }
            return geometry;
        }

        bool mExpired;
    }

    readonly DisposableManager s = new();
    IDataProperty<PropertyValue>? mField;
    Func<ICanvasHost, ICanvasControl>? mFactory;
    Session? mSession;
    PropertyValue mSeen;
    int mOwnWrite;
    int mPressed;              // 当前按着的键（按 CanvasMouseButton 取位），与实例无关
    readonly Dictionary<Key, CanvasKey> mKeys = [];
    Avalonia.Controls.TopLevel? mTopLevel;   // 画布有焦点时按下、还没抬起的物理键，与实例无关
}
