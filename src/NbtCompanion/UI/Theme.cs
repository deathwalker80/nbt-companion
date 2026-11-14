using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NbtCompanion.UI
{
    /// <summary>Dark theme: same colours everywhere, dark scroll bars and title bar (Windows 10/11).</summary>
    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(20, 23, 28);
        public static readonly Color Panel = Color.FromArgb(28, 33, 40);
        public static readonly Color Field = Color.FromArgb(36, 42, 51);
        public static readonly Color Border = Color.FromArgb(55, 63, 75);
        public static readonly Color Text = Color.FromArgb(230, 232, 235);
        public static readonly Color Dim = Color.FromArgb(154, 164, 178);
        public static readonly Color Accent = Color.FromArgb(45, 127, 249);
        public static readonly Color AccentHover = Color.FromArgb(70, 145, 255);
        public static readonly Color AccentPressed = Color.FromArgb(30, 100, 210);
        public static readonly Color AccentDisabled = Color.FromArgb(40, 62, 98);
        public static readonly Color Hover = Color.FromArgb(33, 39, 47);
        public static readonly Color Thumb = Color.FromArgb(88, 99, 115);
        public static readonly Color Good = Color.FromArgb(63, 185, 80);
        public static readonly Color Bad = Color.FromArgb(248, 81, 73);
        public static readonly Color Warn = Color.FromArgb(210, 153, 34);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 14f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(Form f)
        {
            void apply()
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
                    }
                }
                catch (Exception)
                {
                    // older Windows
                }
            }
            if (f.IsHandleCreated)
            {
                apply();
            }
            else
            {
                f.HandleCreated += (s, e) => apply();
            }
        }

        public static Button Button(string text, bool primary = false)
        {
            return new RoundButton
            {
                Text = text,
                Primary = primary,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(120, 36),
                Padding = new Padding(14, 2, 14, 2),
                Margin = new Padding(0, 0, 10, 0),
                Font = Bold,
            };
        }

        public static TextBox TextBox(bool multiline = false, bool mono = false)
        {
            var t = new TextBox
            {
                BackColor = Field,
                ForeColor = Text,
                BorderStyle = BorderStyle.None,
                Font = mono ? Mono : Body,
                Multiline = multiline,
            };
            if (multiline)
            {
                // vertical only (the themed bar replaces it, see Frame); long lines wrap, nothing hidden on the right
                t.ScrollBars = ScrollBars.Vertical;
                t.WordWrap = true;
            }
            return t;
        }

        public static Label Label(string text, Font? font = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Body,
                ForeColor = color ?? Text,
                Margin = new Padding(0, 4, 12, 4),
            };
        }

        public static CheckBox Check(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                ForeColor = Text,
                Font = Body,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 4, 18, 4),
            };
        }

        public static RadioButton Radio(string text, bool value)
        {
            return new RadioButton
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                ForeColor = Text,
                Font = Body,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 4, 18, 4),
            };
        }

        public static ComboBox Combo()
        {
            return new ThemedCombo { Width = 220 };
        }

        /// <summary>ListView with dark header (owner drawn) and dark scroll bars.</summary>
        public static ListView List()
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BackColor = Field,
                ForeColor = Text,
                Font = Body,
                BorderStyle = BorderStyle.None,
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            lv.DrawColumnHeader += (s, e) =>
            {
                using var bg = new SolidBrush(Panel);
                e.Graphics.FillRectangle(bg, e.Bounds);
                using var line = new Pen(Border);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Bold, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            lv.DrawItem += (s, e) =>
            {
                // rows are drawn per subitem
            };
            lv.DrawSubItem += (s, e) =>
            {
                if (e.Item == null || e.SubItem == null)
                {
                    return;
                }
                bool sel = e.Item.Selected;
                using var bg = new SolidBrush(sel ? Accent : Field);
                e.Graphics.FillRectangle(bg, e.Bounds);
                Color fore = sel ? Color.White : (e.Item.ForeColor == Color.Empty ? Text : e.Item.ForeColor);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Body, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            // Widths start from the designed ones at every layout: the first column gives up room when the window is
            // narrow (no horizontal scroll bar) and gets it back when it widens; the last column fills the rest,
            // so the header has no unpainted part on the right.
            int[]? design = null;
            lv.Layout += (s, e) =>
            {
                if (lv.Columns.Count == 0)
                {
                    return;
                }
                if (design == null || design.Length != lv.Columns.Count)
                {
                    design = new int[lv.Columns.Count];
                    for (int i = 0; i < lv.Columns.Count; i++)
                    {
                        design[i] = lv.Columns[i].Width;
                    }
                }
                int first = design[0];
                int others = 0;
                for (int i = 1; i < lv.Columns.Count - 1; i++)
                {
                    others += design[i];
                }
                // Room computed as if the vertical scroll bar were always there (inside Frame the list is wider than what is
                // seen by exactly that bar, which stays hidden): when the bar appears no horizontal one follows.
                int fill = lv.Width - SystemInformation.VerticalScrollBarWidth - first - others;
                if (fill < 80 && lv.Columns.Count > 1)
                {
                    int newFirst = Math.Max(120, first - (80 - fill));
                    fill += first - newFirst;
                    first = newFirst;
                }
                if (lv.Columns.Count > 1 && lv.Columns[0].Width != first)
                {
                    lv.Columns[0].Width = first;
                }
                ColumnHeader last = lv.Columns[lv.Columns.Count - 1];
                int lastWidth = lv.Columns.Count > 1 ? Math.Max(40, fill) : Math.Max(40, lv.Width - SystemInformation.VerticalScrollBarWidth);
                if (last.Width != lastWidth)
                {
                    last.Width = lastWidth;
                }
            };
            return lv;
        }
    }
}
