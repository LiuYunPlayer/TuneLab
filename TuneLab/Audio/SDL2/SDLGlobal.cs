using System;
using SDL2;

namespace TuneLab.Audio.SDL2;

internal static class SDLGlobal
{
    // 全局配置信息
    public static readonly ushort PLAYBACK_FORMAT = SDL.AUDIO_F32SYS; // 32位浮点

    public static readonly ushort PLAYBACK_BUFFER_SAMPLES = 1024; // 默认缓冲区长度

    public static readonly uint PLAYBACK_POLL_INTERVAL = 5; // 轮循时间间隔(ms)

    // 用户事件
    public enum UserEvent
    {
        SDL_EVENT_BUFFER_END = (int)SDL.SDL_EventType.SDL_USEREVENT + 1,
        SDL_EVENT_MANUAL_STOP,
    }

    public delegate void ValueChangeEvent<T>(T newVal, T orgVal);

    public delegate void ValueEvent<T>(T val);

    public delegate void VoidEvent();

    public static void FloatsToBytes(float[] floats, byte[] bytes, int size)
    {
        for (int i = 0; i < size; i++)
        {
            if (i >= floats.Length || i * 4 + 3 >= bytes.Length)
            {
                break;
            }

            var b = BitConverter.GetBytes(floats[i]);
            bytes[i * 4] = b[0];
            bytes[i * 4 + 1] = b[1];
            bytes[i * 4 + 2] = b[2];
            bytes[i * 4 + 3] = b[3];
        }
    }
}