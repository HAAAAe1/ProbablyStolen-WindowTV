using System;
using System.Collections.Generic;
using UnityEngine;
using WindowTV.Capture;

namespace WindowTV
{
    public sealed class ModEntry : IMod
    {
        public const string Id = "window_tv";

        public static ModLog Log;

        private static ConfigEntry<int> TelevisionSize;

        private GameObject runner;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            try
            {
                ModConfig config = ModHelper.GetConfig(Id);
                TelevisionSize = config.Define(
                    "television_size",
                    1,
                    "深空电视",
                    (Func<string>)(() => "窗口尺寸（小 / 中 / 大 / 特大）"),
                    (int?)0,
                    (int?)3,
                    (Func<string>)null,
                    false);
                TvWindow.StoredSize = TelevisionSize.Value;

                // 四个显示/声音开关交给 TvOptions 管，电视上的「设置」按钮和这里共用同一份配置
                TvOptions.Load(config);
            }
            catch (Exception ex)
            {
                Log.Log((object)("[WindowTV] 读取配置失败：" + ex.Message));
            }
            Log.Log((object)"[WindowTV] 已加载");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += TvItem.Register;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            ModHook.OnWakingUpLate += OnNewDay;
            ModHook.OnGoingSleepEarly += OnSleep;
            ModHook.OnLeavingStoreEarly += TvWindow.Leave;
            ModHook.OnReturningStoreLate += TvWindow.Return;

            // 深空电视由送货员送到店里，而不是直接刷在柜台上
            ModHook.OnGenerateCustomerNormal += TvCourier.TryQueue;
            ModHook.OnShutterOpenedLate += OnShutterOpened;

            ModHelper.AddDebugFunction(Id, "Spawn", "深空电视: 拿一台", TvItem.GiveItem, false);
            ModHelper.AddDebugFunction(Id, "Remove", "深空电视: 清掉所有", TvItem.RemoveAll, false);
            ModHelper.AddDebugFunction(Id, "SelfTest", "深空电视: 捕获自检", SelfTest, false);

            // 锁住时，ESC 收纸面板的那一瞬间让电视避一下；别的界面一律不碰
            TvEscapeDodge.Patch();
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= TvItem.Register;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHook.OnWakingUpLate -= OnNewDay;
            ModHook.OnGoingSleepEarly -= OnSleep;
            ModHook.OnLeavingStoreEarly -= TvWindow.Leave;
            ModHook.OnReturningStoreLate -= TvWindow.Return;
            ModHook.OnGenerateCustomerNormal -= TvCourier.TryQueue;
            ModHook.OnShutterOpenedLate -= OnShutterOpened;

            ModHelper.RemoveDebugFunctions(Id);

            TvWindow.Shutdown();
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner);
                runner = null;
            }
        }

        private void OnGameLoaded()
        {
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner);
            }
            runner = new GameObject("WindowTV.Runtime");
            runner.AddComponent<TvRuntime>();
            UnityEngine.Object.DontDestroyOnLoad(runner);
            Log.Log((object)"[WindowTV] 运行时已挂载");
        }

        private void OnNewDay()
        {
            TvWindow.Wake();
        }

        /// <summary>
        /// 兜底：读档进入时当日客户队列可能已经生成完毕，
        /// 这里在开店后再补一次，保证当天就能见到送货员，不用等到第二天。
        /// </summary>
        private void OnShutterOpened()
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                if (store != null)
                {
                    TvCourier.TryQueue(store.storeClientManager);
                }
            }
            catch (Exception ex)
            {
                Log.Log((object)("[WindowTV] 开店补送货员失败：" + ex.Message));
            }
        }

        private void OnSleep()
        {
            TvWindow.Sleep();
        }

        /// <summary>只枚举窗口、不启动捕获，用来确认 COM 与窗口枚举在 Mono 下是否正常。</summary>
        private static void SelfTest()
        {
            try
            {
                List<WindowInfo> list = WindowFinder.List();
                Log.Log((object)("[WindowTV] 自检：可投窗口 " + list.Count + " 个"));
                int shown = 0;
                for (int i = 0; i < list.Count && shown < 6; i++)
                {
                    Log.Log((object)("[WindowTV]   [" + list[i].ProcessName + "] " + list[i].Title));
                    shown++;
                }
            }
            catch (Exception ex)
            {
                Log.Log((object)("[WindowTV] 自检失败：" + ex));
            }
        }

        internal static void SaveSize(int index)
        {
            try
            {
                if (TelevisionSize != null)
                {
                    TelevisionSize.Set(index);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
