using System;
using UnityEngine;

namespace WindowTV
{
    /// <summary>
    /// 电视自己的小音效：开机低鸣、换台静电。
    /// 波形用代码合成，不打包音频文件，也就不需要 files 能力。
    /// 音量刻意压得很低（整体约 0.1 的峰值），只是给操作一点反馈，不抢游戏声音。
    /// </summary>
    internal static class TvAudio
    {
        private const int Rate = 44100;

        internal static bool Enabled = true;

        private static GameObject holder;
        private static AudioSource source;
        private static AudioClip clickClip;
        private static AudioClip powerClip;

        internal static void PlayClick()
        {
            if (!Ensure())
            {
                return;
            }
            source.PlayOneShot(clickClip, 0.65f);
        }

        internal static void PlayPower()
        {
            if (!Ensure())
            {
                return;
            }
            source.PlayOneShot(powerClip, 0.7f);
        }

        private static bool Ensure()
        {
            if (!Enabled)
            {
                return false;
            }
            if (source != null)
            {
                return true;
            }
            try
            {
                holder = new GameObject("WindowTV.Audio");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                source = holder.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 0.34f;
                source.ignoreListenerPause = true;

                clickClip = BuildClick();
                powerClip = BuildPower();
                return source != null && clickClip != null && powerClip != null;
            }
            catch (Exception ex)
            {
                try { ModEntry.Log.Log((object)("[WindowTV] 音效初始化失败：" + ex.Message)); } catch (Exception) { }
                return false;
            }
        }

        internal static void Release()
        {
            if (clickClip != null) { UnityEngine.Object.Destroy(clickClip); clickClip = null; }
            if (powerClip != null) { UnityEngine.Object.Destroy(powerClip); powerClip = null; }
            if (holder != null) { UnityEngine.Object.Destroy(holder); holder = null; }
            source = null;
        }

        /// <summary>换台：一记很短的静电"沙"。带一阶低通，把高频毛刺磨掉才不会刺耳。</summary>
        private static AudioClip BuildClick()
        {
            float seconds = 0.085f;
            int count = Mathf.RoundToInt(Rate * seconds);
            float[] data = new float[count];
            System.Random random = new System.Random(7919);
            float low = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                low += 0.42f * (noise - low);
                float attack = Mathf.Clamp01(t / 0.004f);
                float decay = Mathf.Exp(-t * 58f);
                data[i] = low * 0.42f * attack * decay;
            }

            return MakeClip("WindowTV.Click", data);
        }

        /// <summary>开机：低频缓缓抬起再落下的"嗡"，配一点极轻的嘶声。</summary>
        private static AudioClip BuildPower()
        {
            float seconds = 0.34f;
            int count = Mathf.RoundToInt(Rate * seconds);
            float[] data = new float[count];
            System.Random random = new System.Random(104729);
            float phase = 0f;
            float low = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float k = t / seconds;

                // 频率从 54Hz 升到 96Hz，像显像管通电时那一下
                float freq = Mathf.Lerp(54f, 96f, Mathf.Sqrt(k));
                phase += freq / Rate;
                float tone = Mathf.Sin(phase * Mathf.PI * 2f);

                float noise = (float)random.NextDouble() * 2f - 1f;
                low += 0.3f * (noise - low);

                float envelope = Mathf.Clamp01(t / 0.035f) * Mathf.Exp(-t * 5.2f);
                data[i] = (tone * 0.5f + low * 0.18f) * envelope * 0.5f;
            }

            return MakeClip("WindowTV.Power", data);
        }

        private static AudioClip MakeClip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
