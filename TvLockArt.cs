using UnityEngine;

namespace WindowTV
{
    /// <summary>
    /// 面板上的挂锁小图标：锁上时是一把扣死的铜锁，解开时锁环抬起、颜色发暗。
    /// 和旋钮、拨杆同一套暖铜配色，逐像素画出来，不打包任何素材。
    /// 贴图按 96x96 生成、显示时缩到 34，边缘更顺。
    /// </summary>
    internal static class TvLockArt
    {
        internal const float DisplaySize = 32f;

        private const int S = 96;

        // 锁体
        private const int BodyLeft = 22;
        private const int BodyBottom = 12;
        private const int BodyW = 52;
        private const int BodyH = 36;
        private const int BodyRadius = 8;

        // 锁环
        private const float RingRadius = 20f;
        private const float RingHalfWidth = 4.4f;
        private const float RingCenterX = S / 2f;

        private static readonly Color BodyHigh = Rgb(226, 196, 132);
        private static readonly Color BodyLow = Rgb(142, 108, 54);
        private static readonly Color BodyEdge = Rgb(38, 25, 10);
        private static readonly Color RingHigh = Rgb(206, 178, 120);
        private static readonly Color RingLow = Rgb(120, 92, 46);
        private static readonly Color RingEdge = Rgb(30, 19, 7);

        // 解开状态：整体压暗，一眼能看出没锁
        private static readonly Color Dim = new Color(0.56f, 0.50f, 0.42f, 1f);

        private static Sprite lockedSprite;
        private static Sprite unlockedSprite;

        internal static Sprite Icon(bool locked)
        {
            if (locked)
            {
                if (lockedSprite == null)
                {
                    lockedSprite = Build(true);
                }
                return lockedSprite;
            }
            if (unlockedSprite == null)
            {
                unlockedSprite = Build(false);
            }
            return unlockedSprite;
        }

        private static Sprite Build(bool locked)
        {
            // 锁上时环心低、两条腿插进锁体；解开时环心抬高、腿停在锁体上方留一道缝
            float centerY = locked ? 50f : 64f;
            float legBottom = locked ? 28f : 56f;

            Color32[] pixels = new Color32[S * S];

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float ty = (y + 0.5f) / S;
                    Color c = Color.clear;

                    // 锁环：上半圈 + 两条竖腿，并起来就是个 U 形拱
                    float ring = RingCover(x, y, centerY, legBottom);
                    if (ring > 0f)
                    {
                        Color metal = Color.Lerp(RingLow, RingHigh, ty);
                        float edge = 1f - Mathf.Clamp01(ring);
                        metal = Color.Lerp(metal, RingEdge, edge * 0.85f);
                        metal = Color.Lerp(metal, Color.white, Highlight(x, y, 46f, 62f, 14f, 0.22f));
                        c = Over(c, Alpha(metal, ring));
                    }

                    // 锁体：竖向渐变 + 压深的外沿 + 上沿一道亮边
                    float body = Cover(x, y, BodyLeft, BodyBottom, BodyW, BodyH, BodyRadius);
                    if (body > 0f)
                    {
                        Color metal = Color.Lerp(BodyLow, BodyHigh, ty);
                        float edge = Mathf.Min(
                            Mathf.Min(x - BodyLeft, BodyLeft + BodyW - 1 - x),
                            Mathf.Min(y - BodyBottom, BodyBottom + BodyH - 1 - y)) / 2.2f;
                        if (edge < 1f)
                        {
                            metal = Color.Lerp(BodyEdge, metal, Mathf.Clamp01(edge));
                        }
                        if (ty > 0.40f && ty < 0.46f && x > BodyLeft + 4 && x < BodyLeft + BodyW - 5)
                        {
                            metal = Color.Lerp(metal, Color.white, 0.30f);
                        }
                        metal = Color.Lerp(metal, Color.white, Highlight(x, y, 36f, 40f, 18f, 0.16f));
                        c = Over(c, Alpha(metal, body));
                    }

                    if (!locked && c.a > 0f)
                    {
                        // 解开时褪成哑光，与扣死状态区分开
                        c = new Color(c.r * Dim.r, c.g * Dim.g, c.b * Dim.b, c.a);
                    }

                    pixels[y * S + x] = c;
                }
            }

            Texture2D texture = new Texture2D(S, S, TextureFormat.RGBA32, false);
            texture.name = locked ? "WindowTV.LockOn" : "WindowTV.LockOff";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, S, S),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }

        /// <summary>U 形锁环的覆盖率：中心以上的整圈 + 中心以下的两条竖腿。</summary>
        private static float RingCover(int x, int y, float centerY, float legBottom)
        {
            float px = x + 0.5f;
            float py = y + 0.5f;
            float dx = px - RingCenterX;
            float dy = py - centerY;

            float cover = 0f;
            if (py >= centerY)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                cover = Mathf.Clamp01(RingHalfWidth + 0.5f - Mathf.Abs(d - RingRadius));
            }
            if (py <= centerY && py >= legBottom)
            {
                float leg = Mathf.Clamp01(RingHalfWidth + 0.5f - Mathf.Abs(Mathf.Abs(dx) - RingRadius));
                if (leg > cover)
                {
                    cover = leg;
                }
            }
            return cover;
        }

        /// <summary>左上角一团柔光，让金属看起来是凸的。</summary>
        private static float Highlight(int x, int y, float cx, float cy, float radius, float strength)
        {
            float hx = (x - cx) / radius;
            float hy = (y - cy) / radius;
            float d = Mathf.Sqrt(hx * hx + hy * hy);
            return Mathf.Pow(Mathf.Max(0f, 1f - d), 2f) * strength;
        }

        private static float Cover(int x, int y, int left, int bottom, int w, int h, int radius)
        {
            float px = x + 0.5f - left;
            float py = y + 0.5f - bottom;
            if (px < 0f || py < 0f || px > w || py > h)
            {
                return 0f;
            }

            float dx = 0f;
            if (px < radius) { dx = radius - px; }
            else if (px > w - radius) { dx = px - (w - radius); }

            float dy = 0f;
            if (py < radius) { dy = radius - py; }
            else if (py > h - radius) { dy = py - (h - radius); }

            if (dx <= 0f && dy <= 0f)
            {
                return 1f;
            }
            return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }

        private static Color Alpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, c.a * a);
        }

        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0.0001f)
            {
                return Color.clear;
            }
            Color c = (over * over.a + under * under.a * (1f - over.a)) / a;
            c.a = a;
            return c;
        }

        internal static void Release()
        {
            DestroySprite(ref lockedSprite);
            DestroySprite(ref unlockedSprite);
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }
            Texture2D texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            sprite = null;
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
