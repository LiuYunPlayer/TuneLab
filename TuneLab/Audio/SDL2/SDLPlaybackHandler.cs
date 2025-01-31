using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SDL2;

namespace TuneLab.Audio.SDL2;

internal class SDLPlaybackHandler : IAudioPlaybackHandler
{
    public event Action? PlayStateChanged;
    public event Action? ProgressChanged;
    public event Action? CurrentDeviceChanged;
    public event Action? DevicesChanged;

    public bool IsPlaying => _d.state == SDLPlaybackData.PlaybackState.Playing;

    public static readonly string AutoDeviceName = "[AUTO]";

    // 当前音频驱动
    public string CurrentDriver
    {
        get => _d.driver;
        set
        {
            if (!_initialized)
            {
                _cachedArguments.CurrentDriver = value;
                return;
            }

            _d.SetDriver(value); // 请传入合法的音频驱动
        }
    }

    // 当前音频设备（需要经过名称转换）
    public string CurrentDevice
    {
        get
        {
            if (_d.devName == SDLGlobal.PLAYBACK_EMPTY_DEVICE)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(_d.devName))
            {
                return AutoDeviceName;
            }

            return _d.devName;
        }
        set
        {
            if (!_initialized)
            {
                _cachedArguments.CurrentDevice = value;
                return;
            }

            var isPlaying = IsPlaying;
            Stop();

            if (string.IsNullOrEmpty(value))
            {
                value = SDLGlobal.PLAYBACK_EMPTY_DEVICE;
            }
            else if (value == AutoDeviceName)
            {
                value = string.Empty;
            }

            _d.SetDevice(value);

            if (isPlaying)
            {
                Start();
            }
        }
    }

    public int BufferSize
    {
        get => _d.spec.samples;
        set
        {
            if (!_initialized)
            {
                _cachedArguments.BufferSize = value;
                return;
            }

            ResetAudioSpec((byte)value, null, null);
        }
    }

    public int SampleRate
    {
        get => _d.spec.freq;
        set
        {
            if (!_initialized)
            {
                _cachedArguments.SampleRate = value;
                return;
            }

            ResetAudioSpec(null, value, null);
        }
    }

    public int ChannelCount
    {
        get => _d.spec.channels;
        set
        {
            if (!_initialized)
            {
                _cachedArguments.ChannelCount = value;
                return;
            }

            ResetAudioSpec(null, null, (byte)value);
        }
    }

    public void Init(IAudioSampleProvider provider)
    {
        var context = SynchronizationContext.Current;
        if (context == null)
        {
            throw new Exception("SDL: failed to get SynchronizationContext");
        }

        _audioProvider = provider;

        // Begin initialize SDL
        {
            // 初始化SDL引擎，有备无患
            _ = SDLHost.InitHost();

            // 转发事件：设备更改
            _d.onDevChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    CurrentDeviceChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：播放状态更改
            _d.onStateChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    PlayStateChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：设备列表更新
            _d.onDevicesUpdated = () =>
            {
                context.Post(_ =>
                {
                    DevicesChanged?.Invoke(); //
                }, null);
            };
            // 转发事件：当前缓冲区被播放
            _d.onSamplesConsumed = val =>
            {
                context.Post(_ =>
                {
                    if (IsPlaying)
                    {
                        ProgressChanged?.Invoke();
                    }
                }, null);
            };
            // 填充缓冲区
            _d.onBufferEnd = (buffer, offset, count) =>
            {
                int length = count / 2;
                for (int i = offset; i < offset + count; i++)
                {
                    buffer[i] = 0;
                }

                _audioProvider.Read(buffer, offset, length);
                return count;
            };

            // 打开默认音频设备延迟到播放操作时
        }
        // End initialize SDL
        _initialized = true;

        if (_cachedArguments.CurrentDriver != null)
        {
            CurrentDriver = _cachedArguments.CurrentDriver;
        }
        else
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CurrentDriver = "directsound";
            }
        }

        if (_cachedArguments.CurrentDevice != null)
        {
            CurrentDevice = _cachedArguments.CurrentDevice;
        }

        if (_cachedArguments.BufferSize.HasValue)
        {
            BufferSize = _cachedArguments.BufferSize.Value;
        }

        if (_cachedArguments.SampleRate.HasValue)
        {
            SampleRate = _cachedArguments.SampleRate.Value;
        }

        if (_cachedArguments.ChannelCount.HasValue)
        {
            ChannelCount = _cachedArguments.ChannelCount.Value;
        }
    }

    public void Destroy()
    {
        // 先关闭音频
        Stop();

        _d.SetDevice(SDLGlobal.PLAYBACK_EMPTY_DEVICE);
        _d.SetDriver(string.Empty);

        _initialized = false;
    }

    private void ResetAudioSpec(ushort? samples, int? sampleRate, byte? channels)
    {
        var curDevice = _d.devName;
        var isPlaying = IsPlaying;

        Stop();
        if (curDevice != SDLGlobal.PLAYBACK_EMPTY_DEVICE)
        {
            _d.SetDevice(SDLGlobal.PLAYBACK_EMPTY_DEVICE);
        }

        if (samples.HasValue && samples.Value > 0)
            _d.spec.samples = samples.Value;
        if (sampleRate.HasValue && sampleRate.Value > 0)
            _d.spec.freq = sampleRate.Value;
        if (channels.HasValue && channels.Value > 0)
            _d.spec.channels = channels.Value;

        if (curDevice != SDLGlobal.PLAYBACK_EMPTY_DEVICE)
        {
            _d.SetDevice(curDevice);
        }

        if (isPlaying)
        {
            Start();
        }
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
            CurrentDevice = AutoDeviceName;
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
            AutoDeviceName
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
    private IAudioSampleProvider _audioProvider;

    struct CachedArguments
    {
        public string? CurrentDriver;
        public string? CurrentDevice;
        public int? BufferSize;
        public int? SampleRate;
        public int? ChannelCount;

        public CachedArguments()
        {
            CurrentDriver = null;
            CurrentDevice = null;
            BufferSize = null;
            SampleRate = null;
            ChannelCount = null;
        }
    }

    private bool _initialized = false;
    private CachedArguments _cachedArguments = new();

    // SDL 相关
    private SDLPlaybackData _d = new();
}