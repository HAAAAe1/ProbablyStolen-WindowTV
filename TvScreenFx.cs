using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WindowTV
{
    /// <summary>屏幕上当前该显示什么。</summary>
    internal enum TvSignal
    {
        Standby,     // 还没连上：彩条测试卡
        Live,        // 有画面
        Interrupted  // 掉线：雪花 + 提示
    }

    /// <summary>
    /// 老电视的"屏幕质感"层：彩条待机画面、淡雪花、极淡扫描线与四角暗角，
    /// 外加开机亮线动画和 OSD 字幕。
    ///
    /// 所有贴图都在运行时逐像素生成，不打包任何图片资源，权限声明不变。
    /// 效果刻意做得很轻：扫描线只有 7% 黑、暗角最高 32%，正常看画面时几乎察觉不到。
    /// </summary>
    internal sealed class TvScreenFx
    {
        // 扫描线：每 4 个 UI 像素一行暗线，透明度极低
        private const int ScanPeriod = 4;
        private const float ScanAlpha = 0.07f;

        // 暗角最深处的不透明度，再高就会明显压住画面边角
        private const float VignetteAlpha = 0.26f;

        private const float PowerDuration = 0.42f;

        /// <summary>设置里可关掉扫描线和暗角。</summary>
        internal static bool CrtEnabled = true;

        /// <summary>设置里可关掉雪花。</summary>
        internal static bool SnowEnabled = true;

        /// <summary>设置里可关掉开机亮线动画。</summary>
        internal static bool PowerEnabled = true;

        private RectTransform root;
        private RawImage bars;
        private RawImage snow;
        private RawImage scan;
        private RawImage vignette;
        private TextMeshProUGUI osd;

        private Texture2D barsTex;
        private Texture2D snowTex;
        private Texture2D scanTex;
        private Texture2D vigTex;

        private TvSignal mode = TvSignal.Standby;

        private float barsAlpha = 1f;
        private float snowAlpha = 0.20f;
        private float power = -1f;
        private float osdHold;
        private float osdAlpha;
        private float snowTimer;
        private int scanHeight;

        internal TvSignal Mode { get { return mode; } }

        internal RectTransform Root { get { return root; } }

        internal static TvScreenFx Attach(RectTransform parent)
        {
            if (parent == null)
            {
                return null;
            }

            TvScreenFx fx = new TvScreenFx();
            fx.root = parent;

            fx.barsTex = BuildBars();
            fx.bars = fx.CreateLayer("Bars", fx.barsTex, new Color(1f, 1f, 1f, 1f));

            fx.snowTex = BuildSnow();
            fx.snow = fx.CreateLayer("Snow", fx.snowTex, new Color(1f, 1f, 1f, 0f));

            fx.vigTex = BuildVignette();
            fx.vignette = fx.CreateLayer("Vignette", fx.vigTex, new Color(1f, 1f, 1f, 1f));

            // 扫描线尺寸跟着画面高度走，等第一次 Tick 拿到真实高度再生成
            fx.scan = fx.CreateLayer("Scan", null, new Color(0f, 0f, 0f, ScanAlpha));

            fx.ApplyVisuals();
            return fx;
        }

        /// <summary>把设置开关作用到已经建好的图层上，切换时不用重开电视。</summary>
        internal void ApplyVisuals()
        {
            if (vignette != null)
            {
                vignette.gameObject.SetActive(CrtEnabled);
            }
            if (scan != null)
            {
                scan.gameObject.SetActive(CrtEnabled);
            }
            if (!CrtEnabled && scanTex != null)
            {
                UnityEngine.Object.Destroy(scanTex);
                scanTex = null;
                scanHeight = 0;
            }
            if (!PowerEnabled && root != null)
            {
                power = -1f;
                root.localScale = Vector3.one;
            }
            if (snow != null && !SnowEnabled)
            {
                snowAlpha = 0f;
                Color color = snow.color;
                color.a = 0f;
                snow.color = color;
            }
        }

        /// <summary>OSD 用游戏自带的 TMP 字体，不额外打包字库。</summary>
        internal void AttachFont(TMP_FontAsset font)
        {
            if (font == null || osd != null || root == null)
            {
                return;
            }

            GameObject go = new GameObject("WindowTV.Osd", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(20f, -16f);
            rect.sizeDelta = new Vector2(460f, 56f);

            osd = go.GetComponent<TextMeshProUGUI>();
            osd.font = font;
            osd.fontSize = 32f;
            osd.color = new Color(0.96f, 0.93f, 0.86f, 0f);
            osd.alignment = TextAlignmentOptions.TopLeft;
            osd.enableWordWrapping = false;
            osd.raycastTarget = false;
            osd.text = string.Empty;
        }

        internal void SetMode(TvSignal value)
        {
            mode = value;
        }

        /// <summary>在画面左上角闪一行字，像老电视的频道号。</summary>
        internal void ShowOsd(string text, float seconds)
        {
            if (osd == null)
            {
                return;
            }
            osd.text = text ?? string.Empty;
            osdHold = Mathf.Max(0.2f, seconds);
        }

        internal void PowerOn()
        {
            if (root == null)
            {
                return;
            }
            if (!PowerEnabled)
            {
                power = -1f;
                root.localScale = Vector3.one;
                return;
            }
            power = 0f;
            barsAlpha = 1f;
            snowAlpha = mode == TvSignal.Live ? 0f : 0.20f;
            // 从一条亮线开始，这是老显像管开机最标志性的一下
            root.localScale = new Vector3(1f, 0.012f, 1f);
        }

        internal void Tick(float dt)
        {
            if (root == null)
            {
                return;
            }

            AnimatePower(dt);
            AnimateAlpha(ref barsAlpha, mode == TvSignal.Standby ? 1f : 0f, dt, 9f);

            // 彩条的透明度必须每帧写回图层颜色。
            // 之前只把值算进 barsAlpha 就再也没用过，图层一直停在创建时的 alpha=1，
            // 结果整块彩条永远压在画面上面 —— 捕获成不成功看到的都是彩条。
            if (bars != null)
            {
                Color barsColor = bars.color;
                barsColor.a = barsAlpha;
                bars.color = barsColor;
            }

            float snowTarget = mode == TvSignal.Live || !SnowEnabled
                ? 0f
                : (mode == TvSignal.Interrupted ? 0.32f : 0.20f);
            AnimateAlpha(ref snowAlpha, snowTarget, dt, 5f);

            AnimateSnow(dt);
            AnimateOsd(dt);
            RebuildScanIfNeeded();
        }

        private void AnimatePower(float dt)
        {
            if (power < 0f)
            {
                return;
            }

            power += dt;
            float sy;
            if (power < PowerDuration)
            {
                float k = power / PowerDuration;
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                sy = Mathf.Lerp(0.012f, 1.05f, eased);
            }
            else
            {
                float t = Mathf.Clamp01((power - PowerDuration) / 0.12f);
                sy = Mathf.Lerp(1.05f, 1f, t);
                if (t >= 1f)
                {
                    power = -1f;
                    sy = 1f;
                }
            }
            root.localScale = new Vector3(1f, sy, 1f);
        }

        private static void AnimateAlpha(ref float current, float target, float dt, float speed)
        {
            float k = 1f - Mathf.Exp(-speed * Mathf.Max(0.0001f, dt));
            current = Mathf.Lerp(current, target, k);
            if (Mathf.Abs(current - target) < 0.004f)
            {
                current = target;
            }
        }

        private void AnimateSnow(float dt)
        {
            if (snow == null)
            {
                return;
            }

            Color color = snow.color;
            color.a = snowAlpha;
            snow.color = color;

            snowTimer += dt;
            if (snowTimer < 0.055f)
            {
                return;
            }
            snowTimer = 0f;

            // 每隔一小会儿挪一下采样起点，看起来就像雪花在跳，但幅度很小不刺眼
            snow.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 1f, 1f);
        }

        private void AnimateOsd(float dt)
        {
            if (osd == null)
            {
                return;
            }

            if (osdHold > 0f)
            {
                osdHold -= dt;
            }

            float target = osdHold > 0f ? 1f : 0f;
            float speed = target > osdAlpha ? 13f : 2.6f;
            osdAlpha = Mathf.MoveTowards(osdAlpha, target, speed * dt);

            Color color = osd.color;
            color.a = osdAlpha;
            osd.color = color;
        }

        private void RebuildScanIfNeeded()
        {
            if (scan == null || !CrtEnabled)
            {
                return;
            }

            int height = Mathf.RoundToInt(root.rect.height);
            if (height < 16)
            {
                return;
            }

            int rows = Mathf.Min(height, 1024);
            if (rows == scanHeight && scanTex != null)
            {
                return;
            }
            scanHeight = rows;

            if (scanTex != null)
            {
                UnityEngine.Object.Destroy(scanTex);
            }
            scanTex = BuildScan(rows, height);
            scan.texture = scanTex;
        }

        private RawImage CreateLayer(string name, Texture2D texture, Color color)
        {
            GameObject go = new GameObject("WindowTV.Fx" + name, typeof(RectTransform), typeof(RawImage));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            RawImage image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            image.uvRect = new Rect(0f, 0f, 1f, 1f);
            return image;
        }

        internal void Dispose()
        {
            if (scanTex != null) { UnityEngine.Object.Destroy(scanTex); scanTex = null; }
            if (barsTex != null) { UnityEngine.Object.Destroy(barsTex); barsTex = null; }
            if (snowTex != null) { UnityEngine.Object.Destroy(snowTex); snowTex = null; }
            if (vigTex != null) { UnityEngine.Object.Destroy(vigTex); vigTex = null; }

            bars = null;
            snow = null;
            scan = null;
            vignette = null;
            osd = null;
            root = null;
        }

        // ---------------- 贴图生成 ----------------

        private static readonly Color32[] BarColors =
        {
            new Color32(110, 110, 105, 255), // 白（压暗，像老显像管用久了那样）
            new Color32(110, 104, 55, 255),  // 黄
            new Color32(55, 107, 110, 255),  // 青
            new Color32(52, 104, 60, 255),   // 绿
            new Color32(103, 57, 103, 255),  // 品红
            new Color32(110, 52, 52, 255),   // 红
            new Color32(51, 60, 109, 255)    // 蓝
        };

        /// <summary>彩条测试卡：七色竖条，条与条之间一道细分割线。</summary>
        private static Texture2D BuildBars()
        {
            const int w = 112;
            const int h = 63;
            Color32[] pixels = new Color32[w * h];
            Color32 gap = new Color32(26, 20, 14, 255);
            int barWidth = Mathf.Max(1, w / BarColors.Length);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int bar = x * BarColors.Length / w;
                    Color32 c = BarColors[bar];
                    if ((x % barWidth) == 0)
                    {
                        c = gap;
                    }
                    pixels[y * w + x] = c;
                }
            }
            return BuildTexture(pixels, w, h, "WindowTV.Bars", TextureWrapMode.Clamp);
        }

        /// <summary>雪花底噪：灰度偏高、对比很低，压上透明度后才不会刺眼。</summary>
        private static Texture2D BuildSnow()
        {
            // 这张贴图是拉伸铺满整屏的（uvRect 是 0,0,1,1），128 在 1080 宽画面上要放大 8 倍，
            // 双线性插值后颗粒糊成一片色块；256 才看得出是细雪
            const int size = 256;
            Color32[] pixels = new Color32[size * size];
            System.Random random = new System.Random(20261107);
            for (int i = 0; i < pixels.Length; i++)
            {
                int v = 118 + random.Next(0, 90);
                pixels[i] = new Color32((byte)v, (byte)v, (byte)v, 255);
            }
            return BuildTexture(pixels, size, size, "WindowTV.Snow", TextureWrapMode.Repeat);
        }

        /// <summary>扫描线：按画面像素高度生成，暗线占四分之一，权重渐变避免出现硬条纹。</summary>
        private static Texture2D BuildScan(int rows, int screenHeight)
        {
            const int w = 4;
            Color32[] pixels = new Color32[w * rows];
            float[] weights = { 0.05f, 0.35f, 1f, 0.35f };

            // 画面被缩放采样时（高度超过 1024 才会发生），按比例把周期凑到整数行，
            // 否则暗线会时有时无，看着像闪烁。
            int cycle = Mathf.Max(1, Mathf.RoundToInt((float)ScanPeriod * rows / Mathf.Max(1, screenHeight)));

            for (int y = 0; y < rows; y++)
            {
                int within = y % cycle;
                int k = Mathf.Min(ScanPeriod - 1, within * ScanPeriod / cycle);
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(weights[k] * 255f), 0, 255);
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            return BuildTexture(pixels, w, rows, "WindowTV.Scan", TextureWrapMode.Clamp);
        }

        /// <summary>四角暗角：中心完全透明，越靠边越暗，形成显像管内凹的感觉。</summary>
        private static Texture2D BuildVignette()
        {
            const int w = 128;
            const int h = 72;
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                    float d = Mathf.Max(nx, ny);
                    float a = Mathf.Clamp01((d - 0.52f) / 0.48f);
                    a = a * a * VignetteAlpha;
                    pixels[y * w + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return BuildTexture(pixels, w, h, "WindowTV.Vignette", TextureWrapMode.Clamp);
        }

        private static Texture2D BuildTexture(Color32[] pixels, int w, int h, string name, TextureWrapMode wrap)
        {
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = wrap;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
