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
        public static readonly Color Good = Color.FromArgb(63, 185, 80);
        public static readonly Color Bad = Color.FromArgb(248, 81, 73);
        public static readonly Color Warn = Color.FromArgb(210, 153, 34);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 14f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark scroll bars on a control with scroll bars (TextBox, RichTextBox, ListView).</summary>
        public static void DarkScrollBars(Control c)
        {
            void apply()
            {
                try
                {
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
                catch (Exception)
                {
                    // older Windows: standard scroll bars
                }
            }
            if (c.IsHandleCreated)
            {
                apply();
            }
            else
            {
                c.HandleCreated += (s, e) => apply();
            }
        }

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
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(120, 34),
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(0, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Field,
                ForeColor = Color.White,
                Font = Bold,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Border;
            b.FlatAppearance.MouseDownBackColor = primary ? Accent : Panel;
            return b;
        }

        public static TextBox TextBox(bool multiline = false, bool mono = false)
        {
            var t = new TextBox
            {
                BackColor = Field,
                ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = mono ? Mono : Body,
                Multiline = multiline,
            };
            if (multiline)
            {
                t.ScrollBars = ScrollBars.Vertical;
                t.WordWrap = !mono;
                DarkScrollBars(t);
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
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Field,
                ForeColor = Text,
                Font = Body,
                Width = 220,
            };
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
                BorderStyle = BorderStyle.FixedSingle,
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
            // the last column fills the width, so the header has no unpainted part on the right
            lv.Layout += (s, e) =>
            {
                if (lv.Columns.Count == 0)
                {
                    return;
                }
                int others = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++)
                {
                    others += lv.Columns[i].Width;
                }
                int fill = lv.ClientSize.Width - others;
                ColumnHeader last = lv.Columns[lv.Columns.Count - 1];
                if (fill > 60 && last.Width != fill)
                {
                    last.Width = fill;
                }
            };
            DarkScrollBars(lv);
            return lv;
        }
    }
}
