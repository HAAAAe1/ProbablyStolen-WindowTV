using System;

namespace WindowTV
{
    /// <summary>
    /// 电视的四个显示/声音开关。设置页（游戏内的 mod 设置）和电视上的「设置」按钮
    /// 都读写这里，改完立刻作用到已经打开的那台电视上。
    /// </summary>
    internal static class TvOptions
    {
        private const string Section = "深空电视";

        private static ConfigEntry<bool> crt;
        private static ConfigEntry<bool> snow;
        private static ConfigEntry<bool> power;
        private static ConfigEntry<bool> sound;

        internal static bool Crt { get { return crt == null || crt.Value; } }
        internal static bool Snow { get { return snow == null || snow.Value; } }
        internal static bool Power { get { return power == null || power.Value; } }
        internal static bool Sound { get { return sound == null || sound.Value; } }

        internal static void Load(ModConfig config)
        {
            if (config == null)
            {
                return;
            }

            crt = config.Define(
                "television_crt",
                true,
                Section,
                (Func<string>)(() => "扫描线与暗角"),
                (Func<string>)(() => "老显像管的质感；关掉画面更干净平整"),
                false);

            snow = config.Define(
                "television_snow",
                true,
                Section,
                (Func<string>)(() => "雪花噪点"),
                (Func<string>)(() => "没信号和掉线时屏幕上的雪花；关掉就是纯黑"),
                false);

            power = config.Define(
                "television_power",
                true,
                Section,
                (Func<string>)(() => "开机亮线动画"),
                (Func<string>)(() => "打开电视时画面从一条亮线拉开的那一下"),
                false);

            sound = config.Define(
                "television_sound",
                true,
                Section,
                (Func<string>)(() => "开机与换台音效"),
                (Func<string>)(() => "音量很小，只是给操作一点反馈"),
                false);

            // 玩家如果在游戏的 mod 设置页里改，也要立刻同步到电视上
            crt.OnChanged += delegate(bool value) { Apply(); };
            snow.OnChanged += delegate(bool value) { Apply(); };
            power.OnChanged += delegate(bool value) { Apply(); };
            sound.OnChanged += delegate(bool value) { Apply(); };

            Apply();
        }

        internal static void SetCrt(bool value) { Write(crt, value); }
        internal static void SetSnow(bool value) { Write(snow, value); }
        internal static void SetPower(bool value) { Write(power, value); }
        internal static void SetSound(bool value) { Write(sound, value); }

        private static void Write(ConfigEntry<bool> entry, bool value)
        {
            if (entry == null)
            {
                return;
            }
            try
            {
                entry.Set(value);
                if (entry.Config != null)
                {
                    entry.Config.Save();
                }
            }
            catch (Exception ex)
            {
                try { ModEntry.Log.Log((object)("[WindowTV] 保存设置失败：" + ex.Message)); } catch (Exception) { }
            }
            Apply();
        }

        /// <summary>把开关推到实际的渲染/音频对象上。</summary>
        internal static void Apply()
        {
            TvScreenFx.CrtEnabled = Crt;
            TvScreenFx.SnowEnabled = Snow;
            TvScreenFx.PowerEnabled = Power;
            TvAudio.Enabled = Sound;

            TvWindow.ApplyOptions();
        }
    }
}
