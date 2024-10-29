using System;
using System.Runtime.InteropServices;
using SDL2;

namespace TuneLab.Audio.SDL2;

internal static class SDLReimpl
{
    #region SDL2Reimpl# Variables

    private const string nativeLibName = "SDL2";

    #endregion

    #region SDL_stdinc.h

    /**
     * STD functions
     */
    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_memset(IntPtr dst, int c, IntPtr len);

    #endregion

    #region SDL_mutex.h

    /**
     * Mutex functions
    */
    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateMutex();

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_LockMutex(IntPtr mutex);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_TryLockMutex(IntPtr mutex);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_UnlockMutex(IntPtr mutex);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyMutex(IntPtr mutex);

    /**
     * Semaphore functions
     */
    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateSemaphore(UInt32 initial_value);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroySemaphore(IntPtr sem);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SemWait(IntPtr sem);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SemTryWait(IntPtr sem);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SemWaitTimeout(IntPtr sem, UInt32 ms);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SemPost(IntPtr sem);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SemValue(IntPtr sem);

    /**
     * Condition variable functions
     */
    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateCond();

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyCond(IntPtr cond);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_CondSignal(IntPtr cond);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_CondBroadcast(IntPtr cond);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_CondWait(IntPtr cond, IntPtr mutex);

    [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_CondWaitTimeout(IntPtr cond, IntPtr mutex, UInt32 ms);

    #endregion
}

internal static class SDLGlobal
{
    // 全局配置信息
    public static readonly ushort PLAYBACK_FORMAT = SDL.AUDIO_F32SYS; // 32位浮点

    private static readonly ushort PLAYBACK_BUFFER_SAMPLES_SDL = 4096; // 默认缓冲区长度_SDL
    private static readonly ushort PLAYBACK_BUFFER_SAMPLES_DX = 1024; // 默认缓冲区长度_DIRECTSOUND
    public static ushort PLAYBACK_BUFFER_SAMPLES {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return PLAYBACK_BUFFER_SAMPLES_DX;
            }
            return PLAYBACK_BUFFER_SAMPLES_SDL;
        }
    }

    public static readonly uint PLAYBACK_POLL_INTERVAL = 5; // 轮循时间间隔(ms)

    public static readonly string PLAYBACK_EMPTY_DEVICE = "%EMPTY_DEVICE%";

    // 用户事件
    public enum UserEvent
    {
        SDL_EVENT_BUFFER_END = (int)SDL.SDL_EventType.SDL_USEREVENT + 1,
        SDL_EVENT_MANUAL_STOP,
    }

    public delegate void ValueChangeEvent<T>(T newVal, T orgVal);

    public delegate void ValueEvent<T>(T val);

    public delegate void VoidEvent();
}

internal class SDLHost
{
    private static SDLHost self = null;

    // 初始化
    public static SDLHost InitHost()
    {
        return Instance;
    }

    // 反初始化
    public static void QuitHost()
    {
        if (self == null)
        {
            return;
        }

        self = null;
        GC.Collect(); // 强制删除
    }

    // 构造函数
    public SDLHost()
    {
        // 初始化单例
        if (self != null)
        {
            throw new Exception("SDLHost: Duplicated SDL Host.");
        }

        self = this;

        // 设置输出级别
        SDL.SDL_LogSetPriority(
            (int)SDL.SDL_LogCategory.SDL_LOG_CATEGORY_APPLICATION,
            SDL.SDL_LogPriority.SDL_LOG_PRIORITY_INFO
        );

        // 初始化
        if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) < 0)
        {
            throw new Exception($"SDLHost: Failed to initialize SDL: {SDL.SDL_GetError()}.");
        }
    }

    // 析构函数
    ~SDLHost()
    {
        SDL.SDL_Quit();
    }

    public static SDLHost Instance => self == null ? self = new SDLHost() : self;

    // 获取版本号
    public Version GetVersion()
    {
        SDL.SDL_GetVersion(out var ver);
        return new Version(ver.major, ver.minor, ver.patch);
    }

    // 获取输出设备数
    public int NumOutputDevices()
    {
        return SDL.SDL_GetNumAudioDevices(0);
    }

    // 获取输入设备数
    public int NumInputDevices()
    {
        return SDL.SDL_GetNumAudioDevices(1);
    }

    // 获取输出设备
    public int NumAudioDrivers()
    {
        return SDL.SDL_GetNumAudioDrivers();
    }
}
