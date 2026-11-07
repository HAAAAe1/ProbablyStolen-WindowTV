using UnityEngine;

namespace WindowTV
{
    /// <summary>
    /// 电视面板上的机械拨杆开关：深色凹槽里一根金属拨杆，左右两个位置。
    /// 和两个旋钮同一套配色，逐像素画出来，不打包任何素材。
    /// 贴图按 2 倍尺寸生成、显示时缩到 48x26，边缘更顺。
    /// </summary>
    internal static class TvPanelArt
    {
        internal const float DisplayW = 64f;
        internal const float DisplayH = 32f;

        private const int W = 128;
        private const int H = 64;

        private const int PlateRadius = 14;
        private const int SlotInset = 7;
        private const int SlotRadius = 10;
        private const int LeverW = 36;
        private const int LeverH = 48;
        private const int LeverRadius = 7;
        private const int LeverPad = 6;

        private static readonly Color PlateTop = Rgb(94, 71, 48);
        private static readonly Color PlateBot = Rgb(44, 30, 17);
        private static readonly Color PlateEdge = Rgb(16, 10, 5);
        private static readonly Color SlotTop = Rgb(11, 7, 3);
        private static readonly Color SlotBot = Rgb(32, 21, 12);
        private static readonly Color LeverTop = Rgb(218, 200, 168);
        private static readonly Color LeverBot = Rgb(134, 110, 68);
        private static readonly Color LeverEdge = Rgb(26, 16, 6);
        private static readonly Color GrooveColor = Rgb(74, 56, 34);
        private static readonly Color RimLight = Rgb(126, 100, 66);

        private static Sprite offSprite;
        private static Sprite onSprite;

        internal static Sprite SwitchSprite(bool on)
        {
            if (on)
            {
                if (onSprite == null)
                {
                    onSprite = Build(true);
                }
                return onSprite;
            }
            if (offSprite == null)
            {
                offSprite = Build(false);
            }
            return offSprite;
        }

        private static Sprite Build(bool on)
        {
            Color32[] pixels = new Color32[W * H];
            int leverX = on ? (W - SlotInset - LeverPad - LeverW) : (SlotInset + LeverPad);
            int leverY = (H - LeverH) / 2;

            for (int y = 0; y < H; y++)
            {
                // Texture2D 的第 0 行是底部，换成"1 = 顶部"方便写光从上面来
                float ty = (y + 0.5f) / H;

                for (int x = 0; x < W; x++)
                {
                    Color c = Color.clear;

                    // 底座：竖向渐变 + 压深的边 + 上沿一道细亮边
                    float plate = Cover(x, y, 0, 0, W, H, PlateRadius);
                    if (plate > 0f)
                    {
                        Color body = Color.Lerp(PlateBot, PlateTop, ty);
                        float edge = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y)) / 2.5f;
                        if (edge < 1f)
                        {
                            body = Color.Lerp(PlateEdge, body, edge);
                        }
                        if (ty > 0.86f && ty < 0.93f && x > 9 && x < W - 9)
                        {
                            body = Color.Lerp(body, RimLight, 0.6f);
                        }
                        c = Alpha(body, plate);
                    }

                    // 凹槽：上沿更暗、下沿略亮，才像凹进去
                    float slot = Cover(x, y, SlotInset, SlotInset, W - SlotInset * 2, H - SlotInset * 2, SlotRadius);
                    if (slot > 0f)
                    {
                        Color inner = Color.Lerp(SlotBot, SlotTop, ty);
                        c = Over(c, Alpha(inner, slot));
                    }

                    // 拨杆：金属棒，顶部高光、底部反光、中间几道竖向滚花
                    float lever = Cover(x, y, leverX, leverY, LeverW, LeverH, LeverRadius);
                    if (lever > 0f)
                    {
                        float lx = (x - leverX + 0.5f) / LeverW;
                        float ly = (y - leverY + 0.5f) / LeverH;

                        Color metal = Color.Lerp(LeverBot, LeverTop, ly);
                        float le = Mathf.Min(
                            Mathf.Min(x - leverX, leverX + LeverW - 1 - x),
                            Mathf.Min(y - leverY, leverY + LeverH - 1 - y)) / 2.0f;
                        if (le < 1f)
                        {
                            metal = Color.Lerp(LeverEdge, metal, le);
                        }

                        // 左上椭圆高光，比一条横带自然
                        float hx = (lx * LeverW - 11f) / 11f;
                        float hy = (ly * LeverH - 34f) / 10f;
                        float sheen = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Sqrt(hx * hx + hy * hy)), 2f) * 0.34f;
                        metal = Color.Lerp(metal, Color.white, sheen);

                        // 右侧压暗一点，做出圆柱感
                        if (lx > 0.82f)
                        {
                            metal = Color.Lerp(metal, GrooveColor, 0.28f);
                        }
                        if (ly < 0.10f)
                        {
                            metal = Color.Lerp(metal, LeverTop, 0.30f);
                        }

                        // 竖向滚花：细、浅，缩到显示尺寸后是"有点纹理"，不是格栅
                        float gx = 7f + Mathf.Round((lx * LeverW - 7f) / 9f) * 9f;
                        if (le >= 1f && Mathf.Abs(lx * LeverW - gx) < 0.9f && ly > 0.16f && ly < 0.84f)
                        {
                            metal = Color.Lerp(metal, GrooveColor, 0.30f);
                        }

                        c = Over(c, Alpha(metal, lever));
                    }

                    pixels[y * W + x] = c;
                }
            }

            Texture2D texture = new Texture2D(W, H, TextureFormat.RGBA32, false);
            texture.name = on ? "WindowTV.SwitchOn" : "WindowTV.SwitchOff";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, W, H),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }

        /// <summary>圆角矩形覆盖率，边缘一像素做抗锯齿，缩到 48x26 才不会锯齿。</summary>
        private static float Cover(int x, int y, int left, int top, int w, int h, int radius)
        {
            float px = x + 0.5f - left;
            float py = y + 0.5f - top;
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
            DestroySprite(ref offSprite);
            DestroySprite(ref onSprite);
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
