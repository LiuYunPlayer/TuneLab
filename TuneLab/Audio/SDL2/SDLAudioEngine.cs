using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using SDL2;

namespace TuneLab.Audio.SDL2;

internal class SDLAudioEngine : IAudioEngine
{
    public event Action? PlayStateChanged;
    public event Action? ProgressChanged;

    public bool IsPlaying => _d.state == SDLPlaybackData.PlaybackState.Playing;

    public int SamplingRate => 44100;

    public double CurrentTime => (double)_lastPosition / SamplingRate;

    public void Init(IAudioProcessor processor)
    {
        var context = SynchronizationContext.Current;
        if (context == null)
        {
            throw new Exception("Can't get SynchronizationContext");
        }

        _audioProcessor = processor;

        // Begin initialize SDL
        {
            // 初始化SDL引擎，有备无患
            _ = SDLHost.InitHost();

            // 创建私有结构
            _d = new SDLPlaybackData();

            // 转发事件：设备更改
            _d.devChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    // ...
                }, null);
                // Console.WriteLine($"SDLPLayback: Audio device change to {newVal}.");
            };
            // 转发事件：播放状态更改
            _d.stateChanged = (newVal, oldVal) =>
            {
                context.Post(_ =>
                {
                    PlayStateChanged?.Invoke(); //
                }, null);
                // Console.WriteLine($"SDLPLayback: Play state change to {newVal}.");
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
                _d.setDriver("directsound");
            }

            // 创建 sample provider
            var sampleProvider = new SampleProvider(this);

            // 创建音频结构体
            _d.spec.freq = SamplingRate;
            _d.spec.format = SDLGlobal.PLAYBACK_FORMAT;
            _d.spec.channels = (byte)sampleProvider.WaveFormat.Channels;
            _d.spec.silence = 0;

            _d.sampleProvider = sampleProvider;
            _d.setDevId(0);

            // 打开默认音频设备延迟到播放操作时
        }
        // End initialize SDL
    }

    public void Destroy()
    {
        // 先关闭音频
        Pause();

        _d.sampleProvider = null;
        _d.setDevId(0);

        _d.setDriver("");
    }

    public void Play()
    {
        if (IsPlaying)
        {
            return;
        }

        // 如果没有打开音频设备那么打开第一个音频设备
        if (_d.curDevId == 0)
        {
            SwitchDevice(-1);
        }

        _d.start();
    }

    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        _d.stop();
    }

    public void Seek(double time)
    {
        _position = (int)(time * SamplingRate);
        _lastPosition = _position;
    }

    // 获取所有音频设备
    public List<string> GetDevices()
    {
        var res = new List<string>();
        int cnt = SDL.SDL_GetNumAudioDevices(0);
        for (int i = 0; i < cnt; i++)
        {
            res.Add(SDL.SDL_GetAudioDeviceName(i, 0));
        }

        return res;
    }

    // 切换音频设备
    public void SwitchDevice(int deviceNumber)
    {
        if (_d.state == SDLPlaybackData.PlaybackState.Playing)
        {
            Console.WriteLine("SDLPlayback: Don't change audio device when playing.");
            return;
        }

        // 打开音频设备
        uint id;
        string? deviceToOpen = deviceNumber < 0 ? null : SDL.SDL_GetAudioDeviceName(deviceNumber, 0);
        if ((id = SDL.SDL_OpenAudioDevice(
                deviceToOpen,
                0,
                ref _d.spec,
                out _,
                0)) == 0)
        {
            throw new IOException($"SDLPlayback: Failed to open audio device: {SDL.SDL_GetError()}.");
        }

        if (deviceToOpen == null)
        {
            Console.WriteLine($"SDLPlayback: Current Device");
        }
        else
        {
            Console.WriteLine($"SDLPlayback: {deviceToOpen}");
        }

        _d.setDevId(id);
    }

    // 获取所有音频驱动
    public List<string> GetDrivers()
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

    // 切换音频驱动
    public void SwitchDriver(string driver)
    {
        _d.setDriver(driver);
    }

    // 成员变量
    private IAudioProcessor? _audioProcessor;
    private int _position = 0;
    private int _lastPosition = 0;

    // SDL 相关
    private SDLPlaybackData _d;

    private class SampleProvider(SDLAudioEngine engine) : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(engine.SamplingRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int position = engine._position;
            int length = count / 2;

            engine._lastPosition = position;
            engine._position += length;

            for (int i = offset; i < offset + count; i++)
            {
                buffer[i] = 0;
            }

            engine._audioProcessor?.ProcessBlock(buffer, offset, position, length);
            return count;
        }
    }
}