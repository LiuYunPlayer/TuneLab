using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using SDL2;

namespace TuneLab.Audio.SDL2;

internal class SDLPlaybackHandler : IAudioPlaybackHandler
{
    public event Action? PlayStateChanged;
    public event Action? ProgressChanged;
    public event Action? CurrentDeviceChanged;
    public event Action? DevicesChanged;

    public bool IsPlaying => _d.state == SDLPlaybackData.PlaybackState.Playing;

    // 当前音频驱动
    public string CurrentDriver
    {
        get => _d.driver;
        set { _d.SetDriver(value); }
    }

    // 当前音频设备
    public string CurrentDevice
    {
        get => _d.devName;
        set
        {
            // 打开音频设备
            if (value == null)
            {
                value = string.Empty;
            }

            _d.SetDevice(value);
        }
    }

    public int BufferSize { get; set; } = 1024; // TODO @SineStriker
    public int SampleRate { get; set; } = 44100; // TODO @SineStriker
    public int ChannelCount { get; set; } = 2; // TODO @SineStriker

    public void Init(IAudioSampleProvider provider)
    {
        var context = SynchronizationContext.Current;
        if (context == null)
        {
            throw new Exception("Failed to get SynchronizationContext");
        }

        _audioProvider = provider;

        // Begin initialize SDL
        {
            // 初始化SDL引擎，有备无患
            _ = SDLHost.InitHost();

            // 转发事件：设备更改
            _d.devChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    CurrentDeviceChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：播放状态更改
            _d.stateChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    PlayStateChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：设备列表更新
            _d.devicesUpdated = () =>
            {
                context.Post(_ =>
                {
                    DevicesChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：当前缓冲区被播放
            _d.samplesConsumed = val =>
            {
                context.Post(_ =>
                {
                    if (IsPlaying)
                    {
                        ProgressChanged?.Invoke();
                    }
                }, null);
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _d.SetDriver("directsound");
            }

            // 创建 sample provider
            var sampleProvider = new SampleProvider(this);

            // 创建音频结构体
            _d.spec.freq = SampleRate;
            _d.spec.format = SDLGlobal.PLAYBACK_FORMAT;
            _d.spec.channels = (byte)sampleProvider.WaveFormat.Channels;
            _d.spec.silence = 0;

            _d.sampleProvider = sampleProvider;
            _d.SetDevice(SDLGlobal.PLAYBACK_EMPTY_DEVICE);

            // 打开默认音频设备延迟到播放操作时
        }
        // End initialize SDL
    }

    public void Destroy()
    {
        // 先关闭音频
        Stop();

        _d.sampleProvider = null;
        _d.SetDevice(SDLGlobal.PLAYBACK_EMPTY_DEVICE);
        _d.SetDriver(string.Empty);
    }

    public void Start()
    {
        if (IsPlaying)
        {
            return;
        }

        // 如果没有打开音频设备那么打开默认音频设备
        if (_d.devId == 0)
        {
            CurrentDevice = string.Empty;
        }

        _d.Start();
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        _d.Stop();
    }

    // 获取所有音频设备
    public IReadOnlyList<string> GetAllDevices()
    {
        var res = new List<string>
        {
            string.Empty // 全自动音频设备
        };

        int cnt = SDL.SDL_GetNumAudioDevices(0);
        for (int i = 0; i < cnt; i++)
        {
            res.Add(SDL.SDL_GetAudioDeviceName(i, 0));
        }

        return res;
    }

    // 获取所有音频驱动
    public IReadOnlyList<string> GetAllDrivers()
    {
        var res = new List<string>();
        int cnt = SDL.SDL_GetNumAudioDrivers();
        for (int i = 0; i < cnt; i++)
        {
            var dev = SDL.SDL_GetAudioDriver(i);
            if (dev == "dummy" || dev == "disk")
            {
                continue;
            }

            res.Add(dev);
        }

        return res;
    }

    // 成员变量
    private IAudioSampleProvider? _audioProvider;

    // SDL 相关
    private SDLPlaybackData _d = new();

    private class SampleProvider(SDLPlaybackHandler engine) : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(engine.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int length = count / 2;

            for (int i = offset; i < offset + count; i++)
            {
                buffer[i] = 0;
            }

            engine._audioProvider?.Read(buffer, offset, length);
            return count;
        }
    }
}