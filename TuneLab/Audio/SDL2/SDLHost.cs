using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SDL2;

namespace TuneLab.Audio.SDL2;

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
    public int[] GetVersion()
    {
        SDL.SDL_GetVersion(out var ver);
        return new int[] { ver.major, ver.minor, ver.patch };
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