using System;
using System.Reflection;
using HarmonyLib;

namespace WindowTV
{
    /// <summary>
    /// 锁住以后，让电视在游戏收纸 UI 面板的那一瞬间避一下。
    ///
    /// 游戏按 ESC 时会走 StoreUIManager.CloseTopmostEscapeTarget，它把 CanvasPaperUI
    /// 底下所有面板直接 SetActive(false)。电视窗口恰好挂在这一层，所以光把窗口自己的
    /// closeOnEscape 设成 false 没用 —— 窗口没被 Close，但会被一起收走。
    ///
    /// 这里只在这一个方法执行前后各做一件事：先把电视藏起来（同一个调用栈里就还原，
    /// 渲染在帧末才发生，肉眼看不到变化），让游戏去处理它本来要处理的目标、该弹菜单就弹。
    /// 别的方法、别的窗口、别的机器、游戏菜单一概不碰，也不改变任何返回值。
    /// </summary>
    internal static class TvEscapeDodge
    {
        private static Harmony harmony;
        private static bool dodged;

        /// <summary>
        /// 手动挂接而不是用特性：特性一旦对不上目标，加载器会把整个 mod 判为加载失败，
        /// 那连电视都没了。这里失败最多是"锁不住"，电视照用。
        /// Harmony 的 id 和加载器一致（"mod." + ID），卸载时会被一起清掉。
        /// </summary>
        internal static void Patch()
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(StoreUIManager), "CloseTopmostEscapeTarget");
                MethodInfo prefix = typeof(TvEscapeDodge).GetMethod(
                    nameof(Before), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo postfix = typeof(TvEscapeDodge).GetMethod(
                    nameof(After), BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || prefix == null || postfix == null)
                {
                    LogLine("[WindowTV] 找不到 CloseTopmostEscapeTarget，挂锁只能挡住窗口自己的 ESC");
                    return;
                }
                harmony = new Harmony("mod." + ModEntry.Id);
                harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                LogLine("[WindowTV] 挂锁已就绪");
            }
            catch (Exception ex)
            {
                LogLine("[WindowTV] 挂锁挂接失败：" + ex.Message);
            }
        }

        /// <summary>只在锁着、开着、看得见的时候才避让，其余情况什么都不做。</summary>
        private static void Before()
        {
            dodged = false;
            try
            {
                if (!TvWindow.Locked)
                {
                    return;
                }
                TvWindow.Dodge(true);
                dodged = true;
            }
            catch (Exception)
            {
                dodged = false;
            }
        }

        private static void After()
        {
            if (!dodged)
            {
                return;
            }
            dodged = false;
            try
            {
                TvWindow.Dodge(false);
            }
            catch (Exception)
            {
            }
        }

        private static void LogLine(string text)
        {
            try
            {
                if (ModEntry.Log != null)
                {
                    ModEntry.Log.Log((object)text);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
