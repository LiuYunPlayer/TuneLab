namespace TuneLab.SDK;

// 插件实现的画布控件（CanvasConfig 的工厂创建，每次声明一个实例）。全部回调在主线程上。
// 所有成员都带空的默认体：只实现自己关心的（不实现 Render 只是画不出东西）；以后新增事件也是新增带默认体的方法。
// 坐标是画布内的逻辑像素（左上角为原点，与 DPI 无关）。按下后宿主自动捕获鼠标：拖出画布也继续收到移动与抬起。
// 宿主保证每个实例收到的事件成对：Enter / Leave 严格交替（换了实例、或画布出现在静止的指针下时，宿主主动发一次 Enter，
// 不等用户动鼠标——悬停之类的状态应在 Enter 里按其位置算出）；
// 每个 Up 都有本实例收到过的对应 Down；鼠标捕获意外丢失（如按着时切走窗口）时补发 Up（IsClick = false）。
// 一次操作要防被打断（实例被换掉），就在操作开始时写草稿（见 ICanvasHost.SetValue）；没写草稿的拖动可能中途换实例——
// 新实例收不到那次抬起，按"没在拖动"即可。
// 回调抛出的异常由宿主记录（标明来源插件），此后该实例不再被调用，直到下次重建。
//
// 加性约定（插件实现面）：将来在本面新增成员一律带默认接口方法体（DIM），使增补不破已装插件。
public interface ICanvasControl
{
    // 宿主需要重画时调用（尺寸变化、ICanvasHost.Invalidate 之后……）。只画、不改状态；尺寸读 ICanvasHost，值是你自己持有的
    //（声明时交给工厂的值，或你写的草稿）。
    // 宿主先画好底（与宿主输入框同款的深色圆角底），插件画在它上面。
    void Render(ICanvasDrawing drawing) { }

    // 鼠标：先问插件，没置 Handled 就冒泡给画布外层（参数面板里 = 宿主在那里的手势，如中键平移时间轴、Shift 拉范围选区）。
    // · 按下：要这次操作就置 e.Handled = true——之后的移动与抬起只给你（同现在的自动捕获）。不置 = 不要：宿主当场补发一次抬起
    //  （IsClick = false），这次拖动的移动与抬起都归外层，你收不到。**归属只在按下时定**，之后不能再交出或收回。
    //   要按"按下到抬起移动了多远"决定行为（点一下 vs 拖动）的，只能一律接住，宿主就拿不到这次按下；所以要和别的层叠着用的画布
    //   宜只在自己画了东西的地方接、空白处不接。
    // · 移动：没按着键时，用了（如悬停改了状态并要独占）就置 Handled，否则外层照常收到；正拖着自己接住的按下时一律不冒泡。
    // · 抬起：配对的按下是你接住的，抬起就是你的（不冒泡）。
    void OnMouseDown(CanvasMouseButtonEvent e) { }
    void OnMouseMove(CanvasMouseEvent e) { }
    void OnMouseUp(CanvasMouseButtonEvent e) { }
    // 指针进入画布。宿主保证其后紧跟一个同位置的 OnMouseMove（换了实例、画布出现在静止的指针下时宿主补发的 Enter 也一样），
    // 所以悬停之类按位置算的状态只在 OnMouseMove 里算即可；这里只放"进来了"本身要做的事。
    void OnMouseEnter(CanvasMouseEvent e) { }
    void OnMouseLeave(CanvasMouseEvent e) { }
    // 默认动作 = 滚动所在面板：用了这次滚轮就置 e.Handled = true（面板不滚）；没置则照常滚动面板——
    // 只在真正用到时置位（如指针在可调的元素上），别整块吞掉，否则用户滚面板时指针一扫过画布就卡住。
    void OnScroll(CanvasWheelEvent e) { }

    // 键盘：画布有焦点时收到（点一下画布即获得焦点）。同样严格成对：每个 KeyUp 都有本实例收到过的对应 KeyDown；
    // 按住时失去焦点（点别处、切走窗口）或画布离开界面，宿主补发 KeyUp。按住不放时系统重复发来的按下 IsRepeat = true；
    // 换了实例后，新实例收到的第一个按下 IsRepeat = false（对它来说键刚按下）。修饰键本身也有按下 / 抬起。
    // 默认动作 = 冒泡给外层（快捷键、面板自己的按键处理）：用了这个键就置 e.Handled = true，没用的键别置——
    // 否则画布有焦点时空格播放、撤销等快捷键都会失灵。
    void OnKeyDown(CanvasKeyEvent e) { }
    void OnKeyUp(CanvasKeyEvent e) { }
}
