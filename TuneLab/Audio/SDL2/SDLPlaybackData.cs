using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SDL2;
using NAudio.Wave;

namespace TuneLab.Audio.SDL2;

internal class SDLPlaybackData
{
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused,
    }

    // 事件委托
    public SDLGlobal.ValueChangeEvent<string>? devChanged;

    public SDLGlobal.ValueChangeEvent<PlaybackState>? stateChanged;

    public SDLGlobal.ValueEvent<int>? samplesConsumed;

    public SDLGlobal.VoidEvent? devicesUpdated;

    // 播放信息
    public string driver = string.Empty;

    public PlaybackState state = PlaybackState.Stopped;

    public ISampleProvider? sampleProvider = null;

    // 控制参数
    public uint devId = 0; // 当前播放设备
    public string devName = string.Empty; // 当前播放设备名

    public SDL.SDL_AudioSpec spec;

    public Thread producer;

    public Mutex mutex;

    // 控制块
    public struct CallbackBlock
    {
        public IntPtr audio_chunk;
        public int audio_len;
        public int audio_pos;
    }

    public CallbackBlock scb;

    public float[] pcm_buffer;

    // 构造函数
    public SDLPlaybackData()
    {
        spec.samples = SDLGlobal.PLAYBACK_BUFFER_SAMPLES; //缓冲区字节数/单个采样字节数/声道数
        spec.userdata = IntPtr.Zero; // 不使用
        spec.callback = workCallback;

        pcm_buffer = null;

        mutex = new Mutex();
    }

    public void Start()
    {
        // 初始化控制块
        scb.audio_chunk = IntPtr.Zero;
        scb.audio_len = 0;
        scb.audio_pos = 0;

        // 启动生产者线程
        producer = new Thread(poll);
        producer.Start();
    }

    public void Stop()
    {
        // 结束生产者线程
        notifyStop();

        // 等待结束
        producer.Join();
        producer = null;
    }

    public void SetDriver(string drv)
    {
        if (!string.IsNullOrEmpty(driver))
        {
            if (devId > 0)
            {
                // 停止播放
                if (state == PlaybackState.Playing)
                {
                    Stop();
                }

                // 关闭音频设备
                SDL.SDL_CloseAudioDevice(devId);

                devId = 0;
                devName = string.Empty;
            }

            // 关闭上一个驱动
            SDL.SDL_AudioQuit();
        }

        if (!string.IsNullOrEmpty(drv))
        {
            // 打开当前驱动
            SDL.SDL_AudioInit(drv);

            // 刷新音频设备
            _ = SDL.SDL_GetNumAudioDevices(0);
        }

        driver = drv;
    }

    public void SetDevice(string dev)
    {
        var orgId = devId;
        var orgName = devName;

        if (orgId > 0)
        {
            // 停止播放
            if (state == PlaybackState.Playing)
            {
                Stop();
            }

            // 关闭上一个设备
            SDL.SDL_CloseAudioDevice(orgId);
        }

        if (dev != SDLGlobal.PLAYBACK_EMPTY_DEVICE)
        {
            // 打开当前设备
            uint id;
            if ((id = SDL.SDL_OpenAudioDevice(
                    string.IsNullOrEmpty(dev) ? null : dev,
                    0,
                    ref spec,
                    out _,
                    0)) == 0)
            {
                throw new IOException($"SDL: Failed to open audio device \"{dev}\": {SDL.SDL_GetError()}");
            }

            devId = id;
            devName = dev;

            // 初始化临时浮点数组
            var cnt = spec.samples * spec.channels;
            if (cnt == 0)
            {
                pcm_buffer = null;
            }
            else if (pcm_buffer == null || cnt != pcm_buffer.Length)
            {
                pcm_buffer = new float[cnt];
            }
        }
        else
        {
            devId = 0;
            devName = string.Empty;
        }

        // 通知音频设备已更改
        if (orgName != devName)
        {
            devChanged?.Invoke(devName, orgName);
        }
    }

    public void SetState(PlaybackState newState)
    {
        var orgState = state;
        state = newState;

        // 通知播放状态已更改
        if (state != orgState)
        {
            stateChanged?.Invoke(state, orgState);
        }
    }

    // 消费者
    private void workCallback(IntPtr udata, IntPtr stream, int len)
    {
        // 上锁
        mutex.WaitOne();

        // 缓冲区置为静音
        SDLReimpl.SDL_memset(stream, 0, (IntPtr)len);

        if (scb.audio_len > 0 && scb.audio_chunk != IntPtr.Zero)
        {
            len = Math.Min(len, scb.audio_len);

            // 将缓冲区中的声音写入流
            SDL.SDL_MixAudioFormat(
                stream,
                IntPtr.Add(scb.audio_chunk, scb.audio_pos),
                spec.format,
                (uint)len,
                SDL.SDL_MIX_MAXVOLUME
            );

            scb.audio_pos += len;
            scb.audio_len -= len;

            samplesConsumed?.Invoke(len / sizeof(float));

            // 判断是否完毕
            if (scb.audio_len == 0)
            {
                notifyGetAudioFrame();
            }
        }

        // 放锁
        mutex.ReleaseMutex();
    }

    // 通知缓冲区已空
    private void notifyGetAudioFrame()
    {
        var e = new SDL.SDL_Event();
        e.type = (SDL.SDL_EventType)SDLGlobal.UserEvent.SDL_EVENT_BUFFER_END;
        SDL.SDL_PushEvent(ref e);
    }

    // 通知暂停
    private void notifyStop()
    {
        var e = new SDL.SDL_Event();
        e.type = (SDL.SDL_EventType)SDLGlobal.UserEvent.SDL_EVENT_MANUAL_STOP;
        SDL.SDL_PushEvent(ref e);
    }

    // 生产者
    private void poll()
    {
        // 固定缓冲区
        var gch = GCHandle.Alloc(pcm_buffer, GCHandleType.Pinned);

        // 设置暂停标识位
        SDL.SDL_PauseAudioDevice(devId, 0);
        SetState(PlaybackState.Playing);

        // 第一次事件
        notifyGetAudioFrame();

        // 外层循环
        bool devicesChanged = false;
        while (true)
        {
            bool over = false;

            // 不停地获取事件
            while (SDL.SDL_PollEvent(out var e) > 0)
            {
                switch ((int)e.type)
                {
                    // 缓存用完
                    case (int)SDLGlobal.UserEvent.SDL_EVENT_BUFFER_END:
                    {
                        // 上锁
                        mutex.WaitOne();

                        // 从文件中读取数据，剩下的就交给音频设备去完成了
                        // 它播放完一段数据后会执行回调函数，获取等多的数据
                        int samples = sampleProvider.Read(pcm_buffer, 0, spec.samples * spec.channels);
                        if (samples <= 0)
                        {
                            // 播放完毕
                            over = true;
                        }
                        else
                        {
                            // 重置缓冲区
                            scb.audio_chunk = Marshal.UnsafeAddrOfPinnedArrayElement(pcm_buffer, 0);
                            scb.audio_len = samples * sizeof(float); // 长度为读出数据长度，在read_audio_data中做减法
                            scb.audio_pos = 0; // 设置当前位置为缓冲区头部
                        }

                        // 放锁
                        mutex.ReleaseMutex();
                        break;
                    }

                    // 停止命令
                    case (int)SDLGlobal.UserEvent.SDL_EVENT_MANUAL_STOP:
                    {
                        over = true;
                        break;
                    }

                    // 音频设备插入
                    case (int)SDL.SDL_EventType.SDL_AUDIODEVICEADDED:
                    {
                        devicesChanged = true;
                        break;
                    }

                    // 音频设备移除
                    case (int)SDL.SDL_EventType.SDL_AUDIODEVICEREMOVED:
                    {
                        var dev = e.adevice.which;
                        if (dev == devId)
                        {
                            SetDevice(SDLGlobal.PLAYBACK_EMPTY_DEVICE);
                            over = true;
                        }

                        devicesChanged = true;
                        break;
                    }
                }
            }

            if (over)
            {
                break;
            }

            SDL.SDL_Delay(SDLGlobal.PLAYBACK_POLL_INTERVAL);
        }

        // 设置暂停标识位
        SDL.SDL_PauseAudioDevice(devId, 1);
        SetState(PlaybackState.Stopped);

        // 设备变动后重新检测音频设备
        if (devicesChanged)
        {
            _ = SDL.SDL_GetNumAudioDevices(0);
            devicesUpdated?.Invoke();
        }

        // 解除固定缓冲区
        gch.Free();
    }
}