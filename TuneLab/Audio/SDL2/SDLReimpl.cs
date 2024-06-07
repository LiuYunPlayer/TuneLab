using System;
using System.Runtime.InteropServices;
using System.Text;
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