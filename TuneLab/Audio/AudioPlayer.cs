using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;

namespace TuneLab.Audio;

internal class AudioPlayer
{
    public void Play(IAudioData audio)
    {
        lock (mLockObject)
        {
            mAudioClips.Add(new(audio));
        }
    }

    public void AddData(int count, float[] buffer, int offset)
    {
        HashSet<AudioClip> playedAudioClips = [];
        lock (mLockObject)
        {
            foreach (var audio in mAudioClips)
            {
                audio.AddData(count, buffer, offset);
                if (audio.Played)
                    playedAudioClips.Add(audio);
            }
            mAudioClips.Remove(playedAudioClips);
        }
    }

    class AudioClip(IAudioData audioData)
    {
        public bool Played => position >= audioData.Count;
        public void AddData(int count, float[] buffer, int offset)
        {
            int endPosition = Math.Min(audioData.Count, position + count);
            for (int i = position; i < endPosition; i++)
            {
                buffer[offset + (i - position) * 2] += audioData.GetLeft(i);
                buffer[offset + (i - position) * 2 + 1] += audioData.GetRight(i);
            }
            position += count;
        }

        int position = 0;
    }

    HashSet<AudioClip> mAudioClips = [];

    object mLockObject = new();
}
