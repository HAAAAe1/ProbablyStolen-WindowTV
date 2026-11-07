using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WindowTV.Capture;

namespace WindowTV
{
    /// <summary>
    /// 电视窗口：一块原生 UI 面板 + 一张 RawImage。
    /// 画面由进程内的 WGC 捕获器直接提供，不再需要任何外部程序或共享内存。
    /// </summary>
    internal static class TvWindow
    {
        // 窗口尺寸按「视频区 + 右侧控件列」算：控件竖排在画面右边，
        // 不再吃掉画面上方的空间，所以同样的高度能放出更大的画面。
        private static readonly Vector2[] Sizes =
        {
            new Vector2(620f, 370f),
            new Vector2(860f, 490f),
            new Vector2(1120f, 630f),
            new Vector2(1460f, 820f)
        };

        private const float SideWidth = 108f;

        private static readonly string[] SizeNames = { "小", "中", "大", "特大" };

        // 尺寸旋钮的四个档位角度（负数在左，正数在右）
        private static readonly float[] SizeAngles = { -58f, -20f, 20f, 58f };

        // D3D 纹理第 0 行是画面顶部，Unity 纹理第 0 行是底部，
        // 直接把原始像素塞进 Texture2D 会上下颠倒，这里固定用 UV 翻转抵消，零拷贝开销。
        private static readonly Rect DisplayUv = new Rect(0f, 1f, 1f, -1f);

        private static int sizeIndex = 1;

        private static CustomUIWindow window;
        private static WindowCaptureHost host;
        private static string targetTitle = string.Empty;
        private static Texture2D texture;
        private static byte[] pixels;
        private static RawImage display;
        private static AspectRatioFitter fit;
        private static CustomUIElement statusLabel;
        private static CustomUIElement sizeCaption;
        private static CustomUIElement targetCaption;
        private static TvKnob.Handle sizeKnob;
        private static TvKnob.Handle targetKnob;
        private static CustomUIElement settingsSwitch;
        private static bool settingsOn;

        private static CustomUIElement lockButton;
        private static CustomUIElement lockCaption;

        /// <summary>锁住后按 ESC 不关电视；窗口关掉自动解除，下次打开是默认的没锁状态。</summary>
        private static bool escapeLocked;

        private static string lastStatus;
        private static bool away;
        private static bool asleep;

        private static TvScreenFx fx;

        // 掉线后自动重连：先等一小会儿再试，避免目标窗口正在重建时白折腾
        private static bool interrupted;
        private static float retryTimer;
        private static int retryCount;

        // 目标窗口自己声明了不许被捕获：这种情况下重连多少次都没用，得直接说清楚
        private static bool blockedByWindow;

        // 线程活着却不再出新帧时的兜底：WGC 有时会静默停摆（窗口被系统接管、驱动重置），
        // 光看 IsRunning 永远发现不了，画面就一直冻在最后一帧
        private static int lastSequence = -1;
        private static float stallTimer;

        private const float StallTimeout = 4f;

        /// <summary>诊断用：Tick 被调用了多少次，用来区分"没调用"和"调用了但没效果"。</summary>
        internal static int TickCount;

        /// <summary>把整条链路的状态压成一行，供外部日志使用。刻意吞掉异常，诊断本身不能把东西弄坏。</summary>
        internal static string Snapshot()
        {
            try
            {
                return "alive=" + (window != null && window.IsAlive)
                    + " fx=" + (fx != null)
                    + " host=" + (host != null)
                    + " run=" + (host != null && host.IsRunning)
                    + " size=" + (host != null ? host.Width + "x" + host.Height : "-")
                    + " seq=" + (host != null ? host.Sequence : -1)
                    + " arrived=" + (host != null ? host.ArrivedFrames : -1)
                    + " pub=" + (host != null ? host.PublishedFrames : -1)
                    + " tex=" + (texture != null)
                    + " dispOn=" + (display != null && display.enabled)
                    + " interrupted=" + interrupted
                    + " mode=" + (fx != null ? fx.Mode.ToString() : "-")
                    + " scale=" + (fx != null && fx.Root != null ? fx.Root.localScale.y.ToString("F2") : "-");
            }
            catch (Exception ex)
            {
                return "snapshot-error=" + ex.Message;
            }
        }

        private const int MaxRetries = 6;

        /// <summary>锁着、开着、看得见 —— 只有这种状态才需要替它挡一下 ESC。</summary>
        internal static bool Locked
        {
            get { return escapeLocked && window != null && window.IsAlive && window.IsVisible; }
        }

        /// <summary>
        /// ESC 收纸面板的那一刻把自己摘出去，同一个调用栈里再放回来。
        /// 画面在帧末才渲染，所以看不到闪烁；窗口对象、内容、拖拽位置都不受影响。
        /// </summary>
        internal static void Dodge(bool hide)
        {
            if (window == null || !window.IsAlive || window.Rect == null)
            {
                return;
            }
            window.Rect.gameObject.SetActive(!hide);
        }

        internal static bool IsOpen
        {
            get { return window != null && window.IsAlive; }
        }

        internal static int StoredSize
        {
            get { return sizeIndex; }
            set { sizeIndex = Math.Max(0, Math.Min(Sizes.Length - 1, value)); }
        }

        internal static string TargetTitle
        {
            get { return targetTitle; }
            set { targetTitle = value ?? string.Empty; }
        }

        internal static void Show()
        {
            if (CustomUIManager.Instance == null || away || asleep)
            {
                return;
            }
            if (window != null && window.IsAlive)
            {
                window.Show();
                window.BringToFront();
                return;
            }

            sizeIndex = Math.Max(0, Math.Min(Sizes.Length - 1, sizeIndex));

            CustomUIBuilder builder = CustomUIManager
                .Window("window_tv.tv", "深空电视", "paper")
                .SetSize(Sizes[sizeIndex].x, Sizes[sizeIndex].y)
                .SetDraggable(true)
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(Closed);

            // 左：视频区（占满剩余空间）；右：控件列（固定宽度竖排）
            builder.BeginRow(6f, TextAnchor.UpperLeft);
            RectTransform rowRoot = builder.CurrentContainer;
            LayoutElement rowLayout = rowRoot.gameObject.AddComponent<LayoutElement>();
            rowLayout.flexibleWidth = 1f;
            rowLayout.flexibleHeight = 1f;
            rowLayout.minHeight = 120f;

            GameObject area = new GameObject("WindowTV.VideoArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            area.transform.SetParent((Transform)rowRoot, false);
            LayoutElement layout = area.GetComponent<LayoutElement>();
            layout.minWidth = 80f;
            layout.minHeight = 80f;
            layout.flexibleHeight = 1f;
            layout.flexibleWidth = 1f;
            area.GetComponent<Image>().color = Color.black;

            GameObject video = new GameObject("Video", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            video.transform.SetParent(area.transform, false);

            fit = video.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;

            display = video.GetComponent<RawImage>();
            display.texture = null;
            display.color = Color.white;
            display.uvRect = DisplayUv;

            // 屏幕质感层（彩条 / 雪花 / 扫描线 / 暗角 / OSD）挂在视频区下，
            // 由 AspectRatioFitter 撑出多大就跟着多大，不会跑到黑边外面
            fx = TvScreenFx.Attach(video.GetComponent<RectTransform>());

            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            LayoutElement sideLayout = builder.CurrentContainer.gameObject.AddComponent<LayoutElement>();
            sideLayout.minWidth = SideWidth;
            sideLayout.preferredWidth = SideWidth;
            sideLayout.flexibleWidth = 0f;
            sideLayout.flexibleHeight = 1f;

            // 两个功能做成图标里那种旋钮：指针会转到对应档位，比两个方块按钮更贴题
            sizeKnob = TvKnob.Create(builder.CurrentContainer, "size", CycleSize);
            sizeKnob.Snap(SizeAngles[sizeIndex]);
            builder.AddLabel("尺寸 " + SizeNames[sizeIndex], "size_caption");
            builder.AddSpace(10f);

            targetKnob = TvKnob.Create(builder.CurrentContainer, "target", CycleTarget);
            targetKnob.Snap(-38f);
            builder.AddLabel("换窗口", "target_caption");
            builder.AddSpace(12f);

            builder.AddLabel("准备中…", "status");
            builder.AddSpace(6f);

            // 挂锁：扣上以后 ESC 不再关电视（想看得安稳就用它）
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            builder.AddButton(string.Empty, ToggleLock, "lock");
            lockButton = builder.Last;
            if (lockButton != null && lockButton.root != null)
            {
                lockButton.SetPreferredSize(TvLockArt.DisplaySize, TvLockArt.DisplaySize);
                LayoutElement lockLayout = lockButton.root.GetComponent<LayoutElement>();
                if (lockLayout == null)
                {
                    lockLayout = lockButton.root.AddComponent<LayoutElement>();
                }
                lockLayout.minWidth = TvLockArt.DisplaySize;
                lockLayout.preferredWidth = TvLockArt.DisplaySize;
                lockLayout.flexibleWidth = 0f;
                lockLayout.minHeight = TvLockArt.DisplaySize;
                lockLayout.preferredHeight = TvLockArt.DisplaySize;
                lockLayout.flexibleHeight = 0f;

                // 按钮默认可能是换图过渡，悬停会把锁图标顶掉，改成只变色
                if (lockButton.button != null)
                {
                    lockButton.button.transition = Selectable.Transition.ColorTint;
                }
            }
            builder.AddLabel("锁住", "lock_caption");
            lockCaption = builder.Last;
            StyleLockLabel(lockCaption);
            builder.End();

            builder.AddSpace(6f);

            // 老电视面板上那根拨杆开关：拨到右边就是打开设置，拨回来关掉
            builder.BeginRow(7f, TextAnchor.MiddleCenter);
            builder.AddButton(string.Empty, TvSettings.Toggle, "settings");
            settingsSwitch = builder.Last;
            if (settingsSwitch != null && settingsSwitch.root != null)
            {
                settingsSwitch.SetPreferredSize(TvPanelArt.DisplayW, TvPanelArt.DisplayH);
                LayoutElement switchLayout = settingsSwitch.root.GetComponent<LayoutElement>();
                if (switchLayout == null)
                {
                    switchLayout = settingsSwitch.root.AddComponent<LayoutElement>();
                }
                switchLayout.minWidth = TvPanelArt.DisplayW;
                switchLayout.preferredWidth = TvPanelArt.DisplayW;
                switchLayout.flexibleWidth = 0f;
                switchLayout.minHeight = TvPanelArt.DisplayH;
                switchLayout.preferredHeight = TvPanelArt.DisplayH;
                switchLayout.flexibleHeight = 0f;
                SkinSwitch(settingsSwitch, false);
            }
            builder.AddLabel("设置", "settings_label");
            builder.End();

            builder.End();
            builder.End();

            window = builder.Show();
            statusLabel = window.Get("status");
            sizeCaption = window.Get("size_caption");
            targetCaption = window.Get("target_caption");
            StyleCaption(sizeCaption);
            StyleCaption(targetCaption);
            StyleSwitchLabel(window.Get("settings_label"));
            settingsOn = false;
            escapeLocked = false;
            ApplyLockSkin();
            lastStatus = null;

            // OSD 借用游戏自带的 TMP 字体，不必往 mod 里塞字库
            if (fx != null && statusLabel != null && statusLabel.text != null)
            {
                fx.AttachFont(statusLabel.text.font);
            }
            if (fx != null)
            {
                fx.PowerOn();
            }
            TvAudio.PlayPower();

            RefreshTitle();
            RestartCapture();
        }

        /// <summary>由常驻 MonoBehaviour 每帧调用。</summary>
        internal static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            TickCount++;

            if (window == null || !window.IsAlive)
            {
                return;
            }

            if (fx != null)
            {
                fx.Tick(dt);
            }
            if (sizeKnob != null)
            {
                sizeKnob.Advance(dt);
            }
            if (targetKnob != null)
            {
                targetKnob.Advance(dt);
            }

            // 拨杆位置跟着设置面板的真实开合走：面板被 ESC 或叉关掉时也能自己弹回来
            bool open = TvSettings.IsOpen;
            if (settingsSwitch != null && settingsOn != open)
            {
                settingsOn = open;
                SkinSwitch(settingsSwitch, open);
            }

            UpdateSignal(dt);

            if (display == null || host == null)
            {
                UpdateScreenMode();
                return;
            }

            if (!interrupted && host.Status != lastStatus)
            {
                ApplyStatus(host.Status, false);
            }

            int w = host.Width;
            int h = host.Height;
            if (w <= 0 || h <= 0)
            {
                UpdateScreenMode();
                return;
            }

            if (texture == null || texture.width != w || texture.height != h)
            {
                RebuildTexture(w, h);
            }
            if (texture == null || pixels == null)
            {
                UpdateScreenMode();
                return;
            }

            if (host.TryCopyInto(pixels, out int _, out int _))
            {
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, false);
                // 真拿到画面了才把重连额度还回去，避免"连上又断"的循环一直重试
                retryCount = 0;
            }

            UpdateScreenMode();
        }

        /// <summary>
        /// 决定屏幕此刻该是彩条、画面还是雪花。
        /// 掉线判定只看「已经开始过捕获、但现在没在跑」，避免连第一帧之前误报中断。
        /// </summary>
        private static void UpdateScreenMode()
        {
            if (fx == null)
            {
                return;
            }

            if (interrupted)
            {
                fx.SetMode(TvSignal.Interrupted);
            }
            else if (texture == null)
            {
                fx.SetMode(TvSignal.Standby);
            }
            else
            {
                fx.SetMode(TvSignal.Live);
            }

            // 没画面时把 RawImage 关掉：空纹理的 RawImage 会画成一块白板
            if (display != null)
            {
                display.enabled = texture != null;
            }
        }

        private static void UpdateSignal(float dt)
        {
            // 窗口自己声明了不许抓，这里既不能进中断流程（提示会被"信号中断"盖掉），
            // 也不该重连 —— 每两秒拉起一个线程去抓一个永远不给帧的窗口纯属白烧。
            if (blockedByWindow)
            {
                retryTimer -= dt;
                if (retryTimer <= 0f)
                {
                    retryTimer = 3f;
                    // 万一那扇窗后来取消了标记（比如远程串流退出了），自己接上
                    IntPtr again = WindowFinder.FindByTitle(targetTitle);
                    if (again != IntPtr.Zero && !WindowFinder.IsCaptureBlocked(again))
                    {
                        blockedByWindow = false;
                        retryCount = 0;
                        RestartCapture();
                    }
                }
                return;
            }

            if (host != null && host.IsRunning)
            {
                // 线程还在跑，但四秒都没送出新帧，说明它已经卡死了。
                // 这种状态 IsRunning 一样是 true，不主动判定就会一直停在最后画面上。
                int seq = host.Sequence;
                if (seq != lastSequence)
                {
                    lastSequence = seq;
                    stallTimer = 0f;
                }
                else
                {
                    stallTimer += dt;
                    if (stallTimer >= StallTimeout && !string.IsNullOrEmpty(targetTitle))
                    {
                        stallTimer = 0f;
                        lastSequence = -1;
                        retryCount++;
                        if (retryCount <= MaxRetries)
                        {
                            ModEntry.Log.Log((object)("[WindowTV] 捕获线程存活但 4 秒无新帧，重建捕获"));
                            RestartCapture();
                            return;
                        }

                        // 重试额度用完：老老实实停在中断状态，别一直拉线程
                        interrupted = true;
                        retryTimer = 1.2f;
                        ApplyStatus("信号中断\n等待画面回来", true);
                        if (fx != null)
                        {
                            fx.SetMode(TvSignal.Interrupted);
                            fx.ShowOsd("信号中断", 2.6f);
                        }
                        ModEntry.Log.Log((object)("[WindowTV] 连续重建仍未拿到帧，停止重试"));
                        return;
                    }
                }

                if (interrupted)
                {
                    // 捕获线程自己活过来了（或尺寸重建完成），不用再等重连
                    interrupted = false;
                }
                return;
            }

            if (!interrupted)
            {
                // 还没选窗口时 host 也没启动，这不算掉线
                if (host == null || string.IsNullOrEmpty(targetTitle))
                {
                    return;
                }

                interrupted = true;
                retryTimer = 1.2f;
                ApplyStatus("信号中断\n等待画面回来", true);
                if (fx != null)
                {
                    fx.SetMode(TvSignal.Interrupted);
                    fx.ShowOsd("信号中断", 2.6f);
                }
                TvAudio.PlayClick();
                return;
            }

            retryTimer -= dt;
            if (retryTimer > 0f)
            {
                return;
            }
            retryTimer = 2.5f;

            // 抓不到就退回待机提示了（比如受保护窗口），再硬重连只会每两秒拉起一个线程
            if (retryCount >= MaxRetries)
            {
                return;
            }

            // 目标窗口被关掉又开回来（或换了同一个标题的窗口）时自己接上，不用手动拧旋钮
            IntPtr hwnd = WindowFinder.FindByTitle(targetTitle);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            retryCount++;
            interrupted = false;
            RestartCapture();
            if (fx != null)
            {
                fx.ShowOsd("信号已恢复", 1.8f);
            }
        }

        private static void RebuildTexture(int w, int h)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
            pixels = new byte[w * h * 4];
            texture = new Texture2D(w, h, TextureFormat.BGRA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (display != null)
            {
                display.texture = texture;
            }
            if (fit != null)
            {
                fit.aspectRatio = (float)w / h;
            }
        }

        /// <summary>旋钮下面的小字：字号收小、居中，免得在 108px 宽的列里被折行。</summary>
        private static void StyleCaption(CustomUIElement caption)
        {
            if (caption == null || caption.text == null)
            {
                return;
            }
            caption.text.enableAutoSizing = false;
            caption.text.fontSize = 14f;
            caption.text.alignment = TextAlignmentOptions.Center;
            caption.text.color = new Color(0.88f, 0.84f, 0.76f, 1f);
            // 20px 够放一行字（14 号加行距），把省下来的高度留给状态文字
            caption.SetPreferredSize(0f, 20f);
            LayoutElement layout = caption.root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = 20f;
                layout.flexibleHeight = 0f;
            }
        }

        /// <summary>挂锁开关：扣上以后 ESC 不收这台电视的窗口，再点一下放开。</summary>
        private static void ToggleLock()
        {
            escapeLocked = !escapeLocked;
            ApplyLockSkin();
            TvAudio.PlayClick();
            if (fx != null)
            {
                fx.ShowOsd(escapeLocked ? "已锁住" : "已解锁", 1.3f);
            }
        }

        private static void ApplyLockSkin()
        {
            if (lockButton != null && lockButton.image != null)
            {
                Sprite sprite = TvLockArt.Icon(escapeLocked);
                if (sprite != null)
                {
                    lockButton.image.sprite = sprite;
                    lockButton.image.type = Image.Type.Simple;
                    lockButton.image.color = Color.white;
                    lockButton.image.preserveAspect = true;
                }
            }
            if (lockCaption != null && lockCaption.IsAlive)
            {
                lockCaption.SetText(escapeLocked ? "已锁" : "锁住");
            }
            // 只改这一个窗口自己的开关：CustomUI 的 HandleEscape 会跳过 closeOnEscape=false 的窗口，
            // 键盘事件本身照旧往下走。游戏菜单、机器面板、其它 mod 窗口该怎么响应 ESC 就怎么响应，
            // 这里一概不碰。
            if (window != null && window.IsAlive)
            {
                window.closeOnEscape = !escapeLocked;
            }
        }

        /// <summary>锁旁边那两个字。</summary>
        private static void StyleLockLabel(CustomUIElement label)
        {
            if (label == null || label.text == null)
            {
                return;
            }
            label.text.enableAutoSizing = false;
            label.text.fontSize = 14f;
            label.text.alignment = TextAlignmentOptions.Left;
            label.text.color = new Color(0.88f, 0.84f, 0.76f, 1f);
            label.SetPreferredSize(40f, 20f);
            LayoutElement layout = label.root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = 40f;
                layout.preferredWidth = 40f;
                layout.flexibleWidth = 0f;
                layout.minHeight = 20f;
                layout.flexibleHeight = 0f;
            }
        }

        /// <summary>把默认的纸片按钮换成机械拨杆开关的贴图。</summary>
        internal static void SkinSwitch(CustomUIElement element, bool on)
        {
            if (element == null)
            {
                return;
            }
            if (element.text != null)
            {
                element.text.text = string.Empty;
            }
            if (element.image == null)
            {
                return;
            }
            Sprite sprite = TvPanelArt.SwitchSprite(on);
            if (sprite == null)
            {
                return;
            }
            element.image.sprite = sprite;
            element.image.type = Image.Type.Simple;
            element.image.color = Color.white;
        }

        /// <summary>拨杆旁边的「设置」二字。</summary>
        private static void StyleSwitchLabel(CustomUIElement label)
        {
            if (label == null || label.text == null)
            {
                return;
            }
            label.text.enableAutoSizing = false;
            label.text.fontSize = 14f;
            label.text.alignment = TextAlignmentOptions.Left;
            label.text.color = new Color(0.88f, 0.84f, 0.76f, 1f);
            label.SetPreferredSize(34f, 20f);
            LayoutElement layout = label.root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = 34f;
                layout.preferredWidth = 34f;
                layout.flexibleWidth = 0f;
                layout.minHeight = 20f;
                layout.flexibleHeight = 0f;
            }
        }

        private static string ShortTitle(string raw, int max)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }
            return raw.Length > max ? raw.Substring(0, max) + "…" : raw;
        }

        private static void RefreshTitle()
        {
            if (window == null || !window.IsAlive)
            {
                return;
            }
            // 窗口名不再占按钮宽度，改挂到标题栏上，方便确认正在投哪个窗口
            string name = ShortTitle(targetTitle, 18);
            window.SetTitle(string.IsNullOrEmpty(name) ? "深空电视" : "深空电视 · " + name);
        }

        private static void RestartCapture()
        {
            lastSequence = -1;
            stallTimer = 0f;
            if (host != null)
            {
                host.Dispose();
                host = null;
            }

            host = new WindowCaptureHost();
            IntPtr hwnd = WindowFinder.FindByTitle(targetTitle);
            if (hwnd == IntPtr.Zero)
            {
                ApplyStatus(string.IsNullOrEmpty(targetTitle)
                    ? "拧下面的旋钮挑窗口"
                    : "找不到该窗口，请重新选", true);
                return;
            }

            // 远程串流客户端（GameViewer 之类）和防录屏播放器会给自己打上
            // WDA_EXCLUDEFROMCAPTURE：会话能建、尺寸能读，但帧永远不会来。
            // 与其把它当成"信号中断"一直等，不如直接告诉玩家换个窗口。
            if (WindowFinder.IsCaptureBlocked(hwnd))
            {
                blockedByWindow = true;
                retryTimer = 1f;
                ModEntry.Log.Log((object)("[WindowTV] 目标窗口标记为 WDA_EXCLUDEFROMCAPTURE，不会送帧，已停止重连："
                    + targetTitle));
                ApplyStatus("这个窗口不让抓屏\n换一个窗口试试", true);
                if (fx != null)
                {
                    fx.SetMode(TvSignal.Interrupted);
                    fx.ShowOsd("此窗口禁止捕获", 3f);
                }
                return;
            }
            blockedByWindow = false;

            host.Start(hwnd);
            ApplyStatus(host.Status, true);
        }

        private static void CycleTarget()
        {
            List<WindowInfo> list = WindowFinder.List();
            if (list.Count == 0)
            {
                ApplyStatus("没有找到可投的窗口", true);
                return;
            }

            int current = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Title, targetTitle, StringComparison.Ordinal))
                {
                    current = i;
                    break;
                }
            }

            // 跳过被系统禁止抓屏的窗口（远程串流客户端、防录屏播放器）。
            // 这些窗口拧上去也只能看到雪花，等于给玩家挖了个死胡同。
            int index = -1;
            for (int step = 1; step <= list.Count; step++)
            {
                int candidate = (current + step + list.Count) % list.Count;
                if (!list[candidate].CaptureBlocked)
                {
                    index = candidate;
                    break;
                }
            }
            if (index < 0)
            {
                // 所有窗口都不让抓，那就照常往下走一格，至少标题会变、能看见提示
                index = (current + 1 + list.Count) % list.Count;
            }
            targetTitle = list[index].Title;

            if (targetKnob != null)
            {
                // 每换一次指针转一格，转到底也不会回弹，手感更接近真实旋钮
                targetKnob.TurnBy(46f);
            }
            RefreshTitle();
            interrupted = false;
            retryCount = 0;
            RestartCapture();

            // 换台时左上角闪一下频道号，配一记很轻的静电声
            if (fx != null)
            {
                fx.ShowOsd("CH " + (index + 1), 1.5f);
            }
            TvAudio.PlayClick();
        }

        private static void ApplyStatus(string text, bool force)
        {
            if (!force && text == lastStatus)
            {
                return;
            }
            lastStatus = text;
            if (statusLabel != null && statusLabel.IsAlive)
            {
                // 控件列只有一百来像素宽，把「投屏中 2560x1392」的空格换成硬换行，
                // 让状态和分辨率各占一行，而不是被自动换行拆得七零八落。
                statusLabel.SetText(text == null ? "" : text.Replace(" ", "\n"));
            }
        }

        private static void CycleSize()
        {
            TvAudio.PlayClick();
            sizeIndex = (sizeIndex + 1) % Sizes.Length;
            ModEntry.SaveSize(sizeIndex);
            if (window != null && window.IsAlive)
            {
                window.SetSize(Sizes[sizeIndex].x, Sizes[sizeIndex].y);
                if (sizeKnob != null)
                {
                    sizeKnob.TurnTo(SizeAngles[sizeIndex]);
                }
                if (sizeCaption != null && sizeCaption.IsAlive)
                {
                    sizeCaption.SetText("尺寸 " + SizeNames[sizeIndex]);
                }
                window.Center();
                LayoutRebuilder.ForceRebuildLayoutImmediate(window.Rect);
            }
        }

        /// <summary>设置开关变化时调用：已打开的电视立刻换效果，不用关掉重开。</summary>
        internal static void ApplyOptions()
        {
            if (fx != null)
            {
                fx.ApplyVisuals();
            }
        }

        private static void Closed()
        {
            TvSettings.Close();
            if (host != null)
            {
                try { host.Dispose(); } catch (Exception) { }
                host = null;
            }
            if (fx != null)
            {
                try { fx.Dispose(); } catch (Exception) { }
                fx = null;
            }
            interrupted = false;
            retryTimer = 0f;
            retryCount = 0;
            blockedByWindow = false;
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
            pixels = null;
            display = null;
            fit = null;
            statusLabel = null;
            sizeCaption = null;
            targetCaption = null;
            sizeKnob = null;
            targetKnob = null;
            settingsSwitch = null;
            settingsOn = false;
            lockButton = null;
            lockCaption = null;
            escapeLocked = false;
            lastStatus = null;
        }

        internal static void Close()
        {
            if (window != null && window.IsAlive)
            {
                window.Close();
            }
        }

        internal static void Leave()
        {
            away = true;
            Sync();
        }

        internal static void Return()
        {
            away = false;
            Sync();
        }

        internal static void Sleep()
        {
            asleep = true;
            Sync();
        }

        internal static void Wake()
        {
            asleep = false;
            Sync();
        }

        private static void Sync()
        {
            if (window == null || !window.IsAlive)
            {
                return;
            }
            if (away || asleep)
            {
                TvSettings.Close();
                window.Hide();
            }
            else
            {
                window.Show();
            }
        }

        internal static void Shutdown()
        {
            TvSettings.Close();
            Close();
            Closed();
            window = null;
            away = false;
            asleep = false;
            TvKnob.Release();
            TvPanelArt.Release();
            TvLockArt.Release();
            TvAudio.Release();
        }
    }
}
