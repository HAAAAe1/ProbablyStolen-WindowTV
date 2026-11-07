using System;
using UnityEngine;

namespace WindowTV
{
    /// <summary>常驻更新器，由 ModEntry 在游戏加载后挂到场景里。</summary>
    public sealed class TvRuntime : MonoBehaviour
    {
        private float nextCourierCheck;

        private int tickErrors;
        private int updateCount;
        private float diagTimer;

        private void Update()
        {
            updateCount++;
            diagTimer -= Time.unscaledDeltaTime;
            if (diagTimer <= 0f)
            {
                diagTimer = 3f;
                // 打在外面而不是 Tick 里：就算 Tick 根本没被调、或者中途抛异常，这一行也照样出来
                ModEntry.Log.Log((object)("[WindowTV] 诊断 update=" + updateCount
                    + " tick=" + TvWindow.TickCount
                    + " err=" + tickErrors
                    + " " + TvWindow.Snapshot()));
            }

            try
            {
                TvWindow.Tick();
            }
            catch (Exception ex)
            {
                // 以前这里直接 enabled = false：只要抛过一次异常，电视就永久冻在当前画面，
                // 而且表面上看不出任何问题。改成记几次日志后继续跑，坏一次不至于全停。
                tickErrors++;
                if (tickErrors <= 5)
                {
                    ModEntry.Log.Log((object)("[WindowTV] Tick error #" + tickErrors + ": " + ex));
                }
            }

            // 每 10 秒确认一次「电视还没送到、且今天没人负责送」，
            // 覆盖读档时店门已经开着、错过当日客户生成钩子的情况。
            try
            {
                if (Time.unscaledTime >= nextCourierCheck)
                {
                    nextCourierCheck = Time.unscaledTime + 10f;
                    TvCourier.Guard();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
