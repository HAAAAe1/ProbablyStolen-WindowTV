using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WindowTV
{
    /// <summary>
    /// 电视上的「设置」拨杆拨开后弹出的面板：四行，每行一句说明配一根拨杆开关。
    /// 和游戏 mod 设置页里那几项是同一份数据，改完即时生效。
    /// </summary>
    internal static class TvSettings
    {
        private const string WindowId = "window_tv.settings";

        /// <summary>一行开关：取值函数 + 那根拨杆的按钮元素，用来按当前值换贴图。</summary>
        private class Row
        {
            internal Func<bool> Get;
            internal CustomUIElement Button;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static CustomUIWindow window;

        internal static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsVisible; }
        }

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }
            Open();
        }

        internal static void Open()
        {
            if (CustomUIManager.Instance == null)
            {
                return;
            }
            if (window != null && window.IsAlive)
            {
                window.Show();
                window.BringToFront();
                return;
            }

            // 296 宽：标签靠左、拨杆靠右，中间留白，不要挤成一条
            CustomUIBuilder builder = CustomUIManager
                .Window(WindowId, "电视设置", "paper")
                .SetSize(272f, 262f)
                .SetDraggable(true)
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(Closed);

            rows.Clear();
            AddRow(builder, "扫描线与暗角", () => TvOptions.Crt, TvOptions.SetCrt, "opt_crt");
            AddRow(builder, "雪花噪点", () => TvOptions.Snow, TvOptions.SetSnow, "opt_snow");
            AddRow(builder, "开机亮线", () => TvOptions.Power, TvOptions.SetPower, "opt_power");
            AddRow(builder, "音效", () => TvOptions.Sound, TvOptions.SetSound, "opt_sound");

            builder.AddSpace(14f);
            builder.AddButton("关闭", Close, "opt_close");

            window = builder.Show();
            Refresh();
        }

        private static void AddRow(CustomUIBuilder builder, string label, Func<bool> get, Action<bool> set, string id)
        {
            builder.BeginRow(10f, TextAnchor.MiddleLeft);

            builder.AddLabel(label, id + "_label");
            CustomUIElement text = builder.Last;
            if (text != null && text.text != null)
            {
                text.text.enableAutoSizing = false;
                text.text.fontSize = 15f;
                text.text.alignment = TextAlignmentOptions.Left;
                text.text.color = new Color(0.88f, 0.84f, 0.76f, 1f);
            }

            // 把拨杆推到行尾，左边只放说明
            builder.AddFlexibleSpace();

            builder.AddButton(string.Empty, delegate { set(!get()); Refresh(); }, id);
            CustomUIElement button = builder.Last;
            if (button != null && button.root != null)
            {
                button.SetPreferredSize(TvPanelArt.DisplayW, TvPanelArt.DisplayH);
                LayoutElement layout = button.root.GetComponent<LayoutElement>();
                if (layout == null)
                {
                    layout = button.root.AddComponent<LayoutElement>();
                }
                layout.minWidth = TvPanelArt.DisplayW;
                layout.preferredWidth = TvPanelArt.DisplayW;
                layout.flexibleWidth = 0f;
                layout.minHeight = TvPanelArt.DisplayH;
                layout.preferredHeight = TvPanelArt.DisplayH;
                layout.flexibleHeight = 0f;
                TvWindow.SkinSwitch(button, get());
            }

            builder.End();

            Row row = new Row();
            row.Get = get;
            row.Button = button;
            rows.Add(row);

            builder.AddSpace(10f);
        }

        /// <summary>拨杆位置按当前配置重画；在游戏设置页里改了也会跟着变。</summary>
        internal static void Refresh()
        {
            if (window == null || !window.IsAlive)
            {
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null || row.Button == null || row.Button.image == null)
                {
                    continue;
                }
                Sprite sprite = TvPanelArt.SwitchSprite(row.Get());
                if (row.Button.image.sprite != sprite)
                {
                    row.Button.image.sprite = sprite;
                }
            }
        }

        internal static void Close()
        {
            if (window != null && window.IsAlive)
            {
                window.Close();
            }
            window = null;
            rows.Clear();
        }

        private static void Closed()
        {
            window = null;
            rows.Clear();
        }
    }
}
