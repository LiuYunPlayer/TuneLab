using System;
using System.Runtime.InteropServices;

namespace TuneLab.GUI;

// 跨平台"读取 / 设置鼠标屏幕坐标"的封装：供拖动时把光标锁回锚点（warp），实现无限拖——
// 每步读物理位移后把光标移回原处，光标原地不动、位移可无限累积，不受屏幕物理边缘约束。
//
// 各平台原生实现：
//   Windows —— user32 GetCursorPos/SetCursorPos（物理像素）。
//   macOS   —— CoreGraphics CGEventGetLocation/CGWarpMouseCursorPosition（全局显示坐标，单位为「点」= 逻辑单位）。
//   Linux/X11 —— Xlib XQueryPointer/XWarpPointer（物理像素）；纯 Wayland 出于安全禁止设光标坐标，
//                此处经 XOpenDisplay 探测，拿不到 X 连接则 IsSupported=false、优雅回退（拖动仍隐藏光标，
//                只是拖到物理屏幕边后不再累积）。
//
// CoordinatesAreLogical：macOS 的坐标已是逻辑单位（点），调用方无需再除渲染缩放；Windows/X11 为物理像素，
// 调用方需按 RenderScaling 折算到 DIP。
internal static class CursorWarp
{
    public static bool IsSupported => Backend.IsSupported;
    public static bool CoordinatesAreLogical => Backend.CoordinatesAreLogical;

    public static bool TryGetPosition(out double x, out double y) => Backend.TryGetPosition(out x, out y);
    public static void SetPosition(double x, double y) => Backend.SetPosition(x, y);

    // OS 层隐藏 / 恢复光标：拖动时指针被 warp 钉在锚点、正下方是内容子控件，其默认光标会盖过 Avalonia 的
    // Cursor=None，故须在系统层隐藏。计数式，务必成对调用（隐藏一次、恢复一次）。
    public static void SetCursorVisible(bool visible) => Backend.SetCursorVisible(visible);

    static readonly ICursorWarpBackend Backend = CreateBackend();

    static ICursorWarpBackend CreateBackend()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return new WindowsBackend();
            if (OperatingSystem.IsMacOS())
                return new MacBackend();
            if (OperatingSystem.IsLinux())
                return X11Backend.TryCreate() ?? (ICursorWarpBackend)new NullBackend();
        }
        catch
        {
            // 任一平台原生初始化异常时静默回退，绝不因光标 warp 崩溃拖动。
        }

        return new NullBackend();
    }

    interface ICursorWarpBackend
    {
        bool IsSupported { get; }
        bool CoordinatesAreLogical { get; }
        bool TryGetPosition(out double x, out double y);
        void SetPosition(double x, double y);
        void SetCursorVisible(bool visible);
    }

    sealed class NullBackend : ICursorWarpBackend
    {
        public bool IsSupported => false;
        public bool CoordinatesAreLogical => false;
        public bool TryGetPosition(out double x, out double y) { x = 0; y = 0; return false; }
        public void SetPosition(double x, double y) { }
        public void SetCursorVisible(bool visible) { }
    }

    // —— Windows ——
    sealed class WindowsBackend : ICursorWarpBackend
    {
        public bool IsSupported => true;
        public bool CoordinatesAreLogical => false;   // 物理像素

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0;
            y = 0;
            if (!GetCursorPos(out POINT p))
                return false;
            x = p.X;
            y = p.Y;
            return true;
        }

        public void SetPosition(double x, double y) => SetCursorPos((int)Math.Round(x), (int)Math.Round(y));

        public void SetCursorVisible(bool visible) => ShowCursor(visible);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static extern int ShowCursor(bool bShow);
    }

    // —— macOS ——
    sealed class MacBackend : ICursorWarpBackend
    {
        public bool IsSupported => true;
        public bool CoordinatesAreLogical => true;    // CoreGraphics 全局坐标以「点」计

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0;
            y = 0;
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero)
                return false;

            var p = CGEventGetLocation(evt);
            CFRelease(evt);
            x = p.X;
            y = p.Y;
            return true;
        }

        public void SetPosition(double x, double y)
        {
            CGWarpMouseCursorPosition(new CGPoint { X = x, Y = y });
            // warp 后系统会短暂抑制本地鼠标事件；重连光标与鼠标以尽快恢复连续位移。
            CGAssociateMouseAndMouseCursorPosition(true);
        }

        public void SetCursorVisible(bool visible)
        {
            if (visible)
                CGDisplayShowCursor(CGMainDisplayID());
            else
                CGDisplayHideCursor(CGMainDisplayID());
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CGPoint { public double X; public double Y; }   // CGFloat 在 64 位为 double

        const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(CoreGraphics)]
        static extern IntPtr CGEventCreate(IntPtr source);

        [DllImport(CoreGraphics)]
        static extern CGPoint CGEventGetLocation(IntPtr @event);

        [DllImport(CoreGraphics)]
        static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);

        [DllImport(CoreGraphics)]
        static extern int CGAssociateMouseAndMouseCursorPosition(bool connected);

        [DllImport(CoreGraphics)]
        static extern int CGDisplayHideCursor(uint display);

        [DllImport(CoreGraphics)]
        static extern int CGDisplayShowCursor(uint display);

        [DllImport(CoreGraphics)]
        static extern uint CGMainDisplayID();

        [DllImport(CoreFoundation)]
        static extern void CFRelease(IntPtr cf);
    }

    // —— Linux / X11 ——（纯 Wayland 无 Xlib 连接则 TryCreate 返回 null）
    sealed class X11Backend : ICursorWarpBackend
    {
        public bool IsSupported => true;
        public bool CoordinatesAreLogical => false;   // 物理像素

        public static X11Backend? TryCreate()
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return null;

            return new X11Backend(display);
        }

        X11Backend(IntPtr display)
        {
            mDisplay = display;
            mRoot = XDefaultRootWindow(display);
        }

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0;
            y = 0;
            if (!XQueryPointer(mDisplay, mRoot, out _, out _, out int rootX, out int rootY, out _, out _, out _))
                return false;
            x = rootX;
            y = rootY;
            return true;
        }

        public void SetPosition(double x, double y)
        {
            XWarpPointer(mDisplay, IntPtr.Zero, mRoot, 0, 0, 0, 0, (int)Math.Round(x), (int)Math.Round(y));
            XFlush(mDisplay);
        }

        public void SetCursorVisible(bool visible)
        {
            // 经 libXfixes 隐藏/恢复；缺库时静默忽略（仍 warp，只是光标不隐）。
            try
            {
                if (visible)
                    XFixesShowCursor(mDisplay, mRoot);
                else
                    XFixesHideCursor(mDisplay, mRoot);
                XFlush(mDisplay);
            }
            catch
            {
            }
        }

        readonly IntPtr mDisplay;
        readonly IntPtr mRoot;

        const string X11 = "libX11.so.6";
        const string Xfixes = "libXfixes.so.3";

        [DllImport(X11)]
        static extern IntPtr XOpenDisplay(IntPtr display);

        [DllImport(X11)]
        static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(X11)]
        static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr childReturn,
            out int rootX, out int rootY, out int winX, out int winY, out uint maskReturn);

        [DllImport(X11)]
        static extern int XWarpPointer(IntPtr display, IntPtr srcWindow, IntPtr destWindow,
            int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);

        [DllImport(X11)]
        static extern int XFlush(IntPtr display);

        [DllImport(Xfixes)]
        static extern void XFixesHideCursor(IntPtr display, IntPtr window);

        [DllImport(Xfixes)]
        static extern void XFixesShowCursor(IntPtr display, IntPtr window);
    }
}
