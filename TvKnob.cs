using System;
using UnityEngine;
using UnityEngine.UI;

namespace WindowTV
{
    /// <summary>
    /// 复古旋钮控件：贴图在运行时用代码逐像素画出来（不打包任何图片资源），
    /// 配色和物品图标上的旋钮一致，指针会随操作真的转过去。
    /// </summary>
    internal static class TvKnob
    {
        internal const float DialSize = 72f;

        private const int TexSize = 128;
        private const float Outer = 62.5f;
        private const float BodyRadius = 59.5f;
        private const float CapRadius = 19f;
        private const float CapRingRadius = 23f;

        // 和 WindowTV-Art/draw_tv_icon.py 用同一套颜色
        private static readonly Color Ink = Rgb(37, 25, 15);
        private static readonly Color ShellTop = Rgb(203, 186, 152);
        private static readonly Color ShellBottom = Rgb(150, 126, 84);
        private static readonly Color Shade = Rgb(64, 45, 28);
        private static readonly Color GrooveColor = Rgb(46, 33, 20);
        private static readonly Color NeedleColor = Rgb(240, 228, 200);

        private static Sprite bodySprite;
        private static Sprite needleSprite;

        /// <summary>一个旋钮的可动部分：指针角度平滑逼近目标角度。</summary>
        internal sealed class Handle
        {
            private readonly RectTransform needle;
            private float angle;
            private float target;

            internal Handle(RectTransform needle, float degrees)
            {
                this.needle = needle;
                angle = degrees;
                target = degrees;
                Apply();
            }

            internal void Snap(float degrees)
            {
                angle = degrees;
                target = degrees;
                Apply();
            }

            internal void TurnTo(float degrees)
            {
                target = degrees;
            }

            internal void TurnBy(float degrees)
            {
                target += degrees;
            }

            internal void Advance(float deltaTime)
            {
                if (needle == null || Mathf.Approximately(angle, target))
                {
                    return;
                }
                float k = 1f - Mathf.Exp(-16f * Mathf.Max(0.0001f, deltaTime));
                angle = Mathf.Lerp(angle, target, k);
                if (Mathf.Abs(target - angle) < 0.05f)
                {
                    angle = target;
                }
                Apply();
            }

            private void Apply()
            {
                if (needle != null)
                {
                    // 角度取负：让正数表示顺时针，符合拧旋钮的直觉
                    needle.localRotation = Quaternion.Euler(0f, 0f, -angle);
                }
            }
        }

        internal static Handle Create(RectTransform parent, string name, Action onClick)
        {
            EnsureSprites();

            GameObject root = new GameObject(
                "WindowTV." + name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.minHeight = DialSize;
            layout.preferredHeight = DialSize;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;

            // 透明的整块热区：圆盘外面一点点也能点中，比只能点圆内好按
            Image hit = root.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Image dial = CreateLayer(rect, "Dial", bodySprite);
            RectTransform needle = CreateLayer(rect, "Needle", needleSprite).rectTransform;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = dial;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.97f, 0.95f, 0.90f, 1f);
            colors.pressedColor = new Color(0.78f, 0.75f, 0.69f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(new UnityEngine.Events.UnityAction(delegate
            {
                if (onClick != null)
                {
                    onClick();
                }
            }));

            return new Handle(needle, 0f);
        }

        private static Image CreateLayer(RectTransform parent, string name, Sprite sprite)
        {
            GameObject go = new GameObject("WindowTV.Knob" + name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(DialSize, DialSize);

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        // ---------------- 贴图生成 ----------------

        private static void EnsureSprites()
        {
            if (bodySprite != null && needleSprite != null)
            {
                return;
            }
            bodySprite = BuildSprite(BuildBodyPixels(), "WindowTV.KnobBody");
            needleSprite = BuildSprite(BuildNeedlePixels(), "WindowTV.KnobNeedle");
        }

        internal static void Release()
        {
            DestroySprite(ref bodySprite);
            DestroySprite(ref needleSprite);
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

        private static Sprite BuildSprite(Color32[] pixels, string name)
        {
            Texture2D texture = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, TexSize, TexSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        private static Color32[] BuildBodyPixels()
        {
            Color32[] pixels = new Color32[TexSize * TexSize];
            float center = TexSize * 0.5f;

            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float cover = Cover(Outer, r);
                    if (cover <= 0f)
                    {
                        pixels[y * TexSize + x] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }

                    Color col = new Color(0f, 0f, 0f, 0f);
                    col = Mix(col, Ink, cover);
                    col = Mix(col, BodyColor(dx, dy, r), Cover(BodyRadius, r));

                    // 中心小盘：比盘面略暗，像旋钮中间的轴帽
                    Color cap = BodyColor(dx * 0.5f, dy * 0.5f, r);
                    cap = new Color(cap.r * 0.9f, cap.g * 0.9f, cap.b * 0.9f, 1f);
                    col = Mix(col, cap, Cover(CapRadius, r));

                    // 轴帽外面一圈深色沟
                    float ring = Mathf.Clamp01(Cover(CapRingRadius, r) - Cover(CapRadius + 0.5f, r));
                    col = Mix(col, Shade, ring);

                    // 左上高光，只落在盘外环，别糊住中间
                    float hx = (dx + 17f) / 23f;
                    float hy = (dy - 19f) / 15f;
                    float hd = Mathf.Sqrt(hx * hx + hy * hy);
                    float sheen = Mathf.Clamp01(1f - hd);
                    sheen = sheen * sheen * 0.42f;
                    if (sheen > 0f)
                    {
                        float band = Mathf.Clamp01(Cover(BodyRadius - 2f, r) - Cover(CapRingRadius + 4f, r));
                        col = Mix(col, Color.white, sheen * band);
                    }

                    // 外圈滚花：24 道径向凹槽。没有它，圆盘 + 指针容易被看成钟面
                    float slot = Mathf.Atan2(dy, dx) / (2f * Mathf.PI / 24f);
                    float arc = Mathf.Abs(slot - Mathf.Round(slot)) * (2f * Mathf.PI / 24f) * Mathf.Max(r, 0.001f);
                    float groove = Cover(1.1f, arc) * Mathf.Clamp01((r - 47f) / 3f);
                    col = Mix(col, GrooveColor, groove * 0.34f);

                    pixels[y * TexSize + x] = col;
                }
            }

            return pixels;
        }

        private static Color32[] BuildNeedlePixels()
        {
            Color32[] pixels = new Color32[TexSize * TexSize];
            float center = TexSize * 0.5f;

            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float d = SegmentDistance(dx, dy, 0f, 25f, 0f, 46f);

                    Color col = new Color(0f, 0f, 0f, 0f);
                    col = Mix(col, Ink, Cover(6f, d) * 0.9f);
                    col = Mix(col, NeedleColor, Cover(4.4f, d));

                    // 中心一点点深色轴，让指针看起来是插在轴上的
                    float axle = Cover(3.2f, Mathf.Sqrt(dx * dx + dy * dy));
                    col = Mix(col, Shade, axle * 0.55f);

                    pixels[y * TexSize + x] = col;
                }
            }

            return pixels;
        }

        private static Color BodyColor(float dx, float dy, float r)
        {
            float v = Mathf.InverseLerp(-BodyRadius, BodyRadius, dy);
            Color col = Mix(ShellBottom, ShellTop, v);

            float slant = Mathf.Clamp01(0.5f + (dy * 0.55f - dx * 0.35f) / (BodyRadius * 2f));
            float gain = Mathf.Lerp(0.94f, 1.07f, slant);
            col = new Color(col.r * gain, col.g * gain, col.b * gain, 1f);

            // 边缘倒角压暗，做出一点立体感
            float bevel = Mathf.InverseLerp(BodyRadius - 6f, BodyRadius, r);
            if (bevel > 0f)
            {
                float k = Mathf.Lerp(1f, 0.55f, bevel);
                col = new Color(col.r * k, col.g * k, col.b * k, 1f);
            }
            return col;
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax;
            float vy = by - ay;
            float wx = px - ax;
            float wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / Mathf.Max(0.0001f, vx * vx + vy * vy));
            float cx = ax + vx * t;
            float cy = ay + vy * t;
            float dx = px - cx;
            float dy = py - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float Cover(float edge, float distance)
        {
            return Mathf.Clamp01(edge - distance + 0.5f);
        }

        private static Color Mix(Color from, Color to, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(
                from.r + (to.r - from.r) * t,
                from.g + (to.g - from.g) * t,
                from.b + (to.b - from.b) * t,
                from.a + (to.a - from.a) * t);
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
