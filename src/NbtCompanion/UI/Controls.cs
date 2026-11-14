using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NbtCompanion.UI
{
    internal static class Shapes
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Rounded button: primary (filled blue) or secondary (outlined); hover, pressed and disabled states.</summary>
    public sealed class RoundButton : Button
    {
        private bool _hover;
        private bool _down;

        public bool Primary { get; set; }

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            Color border;
            Color text;
            if (!Enabled)
            {
                fill = Primary ? Theme.AccentDisabled : Theme.Field;
                border = Primary ? Theme.AccentDisabled : Theme.Border;
                text = Theme.Dim;
            }
            else if (Primary)
            {
                fill = _down ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                text = Color.White;
            }
            else
            {
                fill = _down ? Theme.Back : _hover ? Theme.Border : Theme.Field;
                border = _hover ? Theme.Dim : Theme.Border;
                text = Theme.Text;
            }
            using (GraphicsPath p = Shapes.Round(r, 7))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
            {
                using GraphicsPath p = Shapes.Round(new Rectangle(2, 2, Width - 5, Height - 5), 6);
                using var pen = new Pen(Color.FromArgb(150, Color.White)) { DashStyle = DashStyle.Dot };
                g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Entry of the page list: number in a circle, text, blue bar on the left when selected.</summary>
    public sealed class NavItem : Control
    {
        private bool _hover;
        private bool _selected;

        public string Number { get; set; } = "";

        public bool Selected
        {
            get { return _selected; }
            set
            {
                _selected = value;
                Invalidate();
            }
        }

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Height = 44;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_selected || _hover)
            {
                using GraphicsPath p = Shapes.Round(r, 8);
                using var b = new SolidBrush(_selected ? Theme.Field : Theme.Hover);
                g.FillPath(b, p);
            }
            if (_selected)
            {
                using GraphicsPath bar = Shapes.Round(new Rectangle(0, 9, 4, Height - 18), 2);
                using var b = new SolidBrush(Theme.Accent);
                g.FillPath(b, bar);
            }
            var circle = new Rectangle(14, (Height - 26) / 2, 26, 26);
            using (var b = new SolidBrush(_selected ? Theme.Accent : Theme.Field))
            using (var pen = new Pen(_selected ? Theme.Accent : Theme.Border))
            {
                g.FillEllipse(b, circle);
                g.DrawEllipse(pen, circle);
            }
            TextRenderer.DrawText(g, Number, Theme.Bold, circle, _selected ? Color.White : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Text, Theme.Bold, new Rectangle(50, 0, Width - 54, Height), _selected ? Color.White : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>State pill: dot in ForeColor (green / amber / grey / red), text in the normal colour.</summary>
    public sealed class Chip : Control
    {
        public Chip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Font = Theme.Small;
            Height = 26;
            Margin = new Padding(0, 0, 8, 0);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Width = TextRenderer.MeasureText(Text, Font).Width + 34;
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            Invalidate();
            base.OnForeColorChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Shapes.Round(r, Height / 2))
            using (var b = new SolidBrush(Theme.Panel))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            using (var dot = new SolidBrush(ForeColor))
            {
                g.FillEllipse(dot, 11, Height / 2 - 4, 8, 8);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(25, 0, Width - 30, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Rounded panel that holds the content of a page or of the log.</summary>
    public sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Padding = new Padding(16, 14, 16, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath p = Shapes.Round(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using var b = new SolidBrush(Theme.Panel);
            using var pen = new Pen(Theme.Border);
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
    }

    /// <summary>
    /// Thin border around a field: grey, blue while the field has the focus. Multi-line text boxes, the log and the
    /// lists get the themed scroll bar: the native one is pushed outside a clipping panel (so it is never seen) and
    /// a drawn bar on the right follows and drives the field.
    /// </summary>
    public sealed class Frame : Panel
    {
        private readonly Control _inner;
        private readonly Panel? _clip;
        private readonly ThemedVBar? _bar;

        public Frame(Control inner)
        {
            _inner = inner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = inner.BackColor;
            Margin = inner.Margin;
            bool single = inner is TextBox tb && !tb.Multiline;
            if (single)
            {
                // size taken before docking: docked inside an empty panel the field would shrink
                Size = new Size(inner.Width + 16, ((TextBox)inner).PreferredHeight + 10);
                Padding = new Padding(8, 6, 8, 4);
                inner.Dock = DockStyle.Fill;
                Controls.Add(inner);
            }
            else
            {
                Padding = new Padding(1);
                _bar = new ThemedVBar(inner) { Dock = DockStyle.Right };
                _clip = new Panel { Dock = DockStyle.Fill, BackColor = inner.BackColor };
                inner.Dock = DockStyle.None;
                inner.Location = new Point(0, 0);
                _clip.Controls.Add(inner);
                _clip.Resize += (s, e) => LayoutInner();
                Controls.Add(_clip);
                Controls.Add(_bar);
            }
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
        }

        private void LayoutInner()
        {
            if (_clip == null)
            {
                return;
            }
            // wider than the visible area by the width of the native scroll bar, which stays hidden on the right
            _inner.Bounds = new Rectangle(0, 0, _clip.ClientSize.Width + SystemInformation.VerticalScrollBarWidth, _clip.ClientSize.Height);
            _bar?.Sync();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_inner.BackColor);
            using var pen = new Pen(_inner.ContainsFocus ? Theme.Accent : Theme.Border);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Vertical scroll bar in the theme colours. Reads the position of the field it serves (GetScrollInfo) and moves it:
    /// EM_SETSCROLLPOS for RichTextBox (pixels), EM_LINESCROLL for TextBox (lines), TopIndex for ListBox (items),
    /// TopItem for ListView (rows).
    /// </summary>
    public sealed class ThemedVBar : Control
    {
        private const int SbVert = 1;
        private const int SifAll = 0x17;
        private const int WmMouseWheel = 0x020A;
        private const int EmLineScroll = 0x00B6;
        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmGetScrollPos = 0x0400 + 221;
        private const int EmSetScrollPos = 0x0400 + 222;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public int Size;
            public int Mask;
            public int Min;
            public int Max;
            public int Page;
            public int Pos;
            public int TrackPos;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref NativePoint lParam);

        private readonly Control _target;
        private readonly Timer _timer = new Timer { Interval = 120 };
        private int _min;
        private int _max;
        private int _page = 1;
        private int _pos;
        private bool _hover;
        private bool _drag;
        private int _dragOffset;

        public ThemedVBar(Control target)
        {
            _target = target;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Width = 12;
            BackColor = target.BackColor;
            // the field scrolls by itself too (wheel, keys, new log lines): the bar follows
            _timer.Tick += (s, e) => Sync();
            _timer.Start();
            target.Resize += (s, e) => Sync();
            target.TextChanged += (s, e) => Sync();
        }

        private bool Scrollable
        {
            get { return _max - _min + 1 > _page && _page > 0; }
        }

        public void Sync()
        {
            if (!_target.IsHandleCreated || _drag)
            {
                return;
            }
            var si = new ScrollInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<ScrollInfo>(), Mask = SifAll };
            bool ok;
            try
            {
                ok = GetScrollInfo(_target.Handle, SbVert, ref si);
            }
            catch (Exception)
            {
                ok = false;
            }
            int min = ok ? si.Min : 0;
            int max = ok ? si.Max : 0;
            int page = ok ? Math.Max(si.Page, 1) : 1;
            int pos = ok ? si.Pos : 0;
            if (min != _min || max != _max || page != _page || pos != _pos)
            {
                _min = min;
                _max = max;
                _page = page;
                _pos = pos;
                Invalidate();
            }
        }

        private Rectangle Thumb()
        {
            int track = Height - 4;
            int range = _max - _min + 1;
            int h = Math.Max(28, (int)((long)track * _page / Math.Max(range, 1)));
            h = Math.Min(h, track);
            int span = Math.Max(range - _page, 1);
            int y = 2 + (int)((long)(track - h) * (_pos - _min) / span);
            return new Rectangle(2, y, Width - 4, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_target.BackColor);
            if (!Scrollable)
            {
                return;
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath track = Shapes.Round(new Rectangle(3, 2, Width - 6, Height - 4), (Width - 6) / 2))
            using (var b = new SolidBrush(Theme.Panel))
            {
                g.FillPath(b, track);
            }
            Rectangle t = Thumb();
            using System.Drawing.Drawing2D.GraphicsPath p = Shapes.Round(t, t.Width / 2);
            using var tb = new SolidBrush(_drag ? Theme.Accent : _hover ? Theme.Dim : Theme.Thumb);
            g.FillPath(tb, p);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Scrollable || e.Button != MouseButtons.Left)
            {
                return;
            }
            Rectangle t = Thumb();
            if (t.Contains(e.Location))
            {
                _drag = true;
                _dragOffset = e.Y - t.Y;
                Capture = true;
            }
            else
            {
                // click on the track: one page towards the click
                MoveTo(e.Y < t.Y ? _pos - _page : _pos + _page);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_drag)
            {
                return;
            }
            Rectangle t = Thumb();
            int track = Height - 4 - t.Height;
            int y = Math.Max(0, Math.Min(track, e.Y - _dragOffset - 2));
            int span = Math.Max(_max - _min + 1 - _page, 0);
            int target = _min + (track > 0 ? (int)((long)span * y / track) : 0);
            MoveTo(target);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag)
            {
                _drag = false;
                Capture = false;
                Sync();
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // the wheel over the bar scrolls the field as if the pointer were on it
            int wparam = (e.Delta & 0xFFFF) << 16;
            int lparam = (Cursor.Position.Y << 16) | (Cursor.Position.X & 0xFFFF);
            SendMessage(_target.Handle, WmMouseWheel, (IntPtr)wparam, (IntPtr)lparam);
            Sync();
            base.OnMouseWheel(e);
        }

        private void MoveTo(int target)
        {
            int span = Math.Max(_max - _min + 1 - _page, 0);
            target = Math.Max(_min, Math.Min(_min + span, target));
            if (_target is RichTextBox)
            {
                var pt = new NativePoint();
                SendMessage(_target.Handle, EmGetScrollPos, IntPtr.Zero, ref pt);
                pt.Y = target;
                SendMessage(_target.Handle, EmSetScrollPos, IntPtr.Zero, ref pt);
            }
            else if (_target is ListBox lb)
            {
                lb.TopIndex = Math.Max(0, Math.Min(lb.Items.Count - 1, target));
            }
            else if (_target is ListView lv)
            {
                // report view: vertical scroll units are rows
                if (lv.Items.Count > 0)
                {
                    lv.TopItem = lv.Items[Math.Max(0, Math.Min(lv.Items.Count - 1, target))];
                }
            }
            else
            {
                // multi-line TextBox: scroll units are lines; relative scroll from the first visible line
                int first = (int)SendMessage(_target.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
                SendMessage(_target.Handle, EmLineScroll, IntPtr.Zero, (IntPtr)(target - first));
            }
            _pos = target;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The complete log on screen: one line per entry, coloured (errors red, results green), long lines wrapped, so
    /// nothing is hidden on the right. A standard list box underneath, so the themed bar of Frame can drive it.
    /// Ctrl+A selects everything, Ctrl+C copies the selected lines.
    /// </summary>
    public sealed class LogView : ListBox
    {
        private readonly System.Collections.Generic.List<(string text, Color color)> _lines = new System.Collections.Generic.List<(string text, Color color)>();
        private int _wrapWidth;

        public LogView()
        {
            DrawMode = DrawMode.OwnerDrawVariable;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Mono;
            SelectionMode = SelectionMode.MultiExtended;
            IntegralHeight = false;
            ScrollAlwaysVisible = true;
            HorizontalScrollbar = false;
        }

        public System.Collections.Generic.IReadOnlyList<(string text, Color color)> Lines
        {
            get { return _lines; }
        }

        public void AddLine(string text, Color color)
        {
            bool atEnd = TopIndex + VisibleCount() >= Items.Count - 1;
            _lines.Add((text, color));
            Items.Add(text);
            if (atEnd)
            {
                TopIndex = Math.Max(0, Items.Count - 1);
            }
        }

        public void SetLines(System.Collections.Generic.IEnumerable<(string text, Color color)> lines)
        {
            BeginUpdate();
            _lines.Clear();
            Items.Clear();
            foreach ((string text, Color color) l in lines)
            {
                _lines.Add(l);
                Items.Add(l.text);
            }
            EndUpdate();
            TopIndex = Math.Max(0, Items.Count - 1);
        }

        public void ClearLines()
        {
            _lines.Clear();
            Items.Clear();
        }

        private int VisibleCount()
        {
            int h = Math.Max(1, ItemHeight);
            return ClientSize.Height / h;
        }

        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            base.OnMeasureItem(e);
            if (e.Index < 0 || e.Index >= _lines.Count)
            {
                return;
            }
            int w = Math.Max(50, ClientSize.Width - 8);
            Size sz = TextRenderer.MeasureText(_lines[e.Index].text, Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ItemHeight = Math.Min(255, Math.Max(Font.Height + 2, sz.Height + 2));
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lines.Count)
            {
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentDisabled : Theme.Back))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            var r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 1, e.Bounds.Width - 8, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, _lines[e.Index].text, Font, r, _lines[e.Index].color, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // wrapped heights depend on the width: measured again when the width changes
            if (ClientSize.Width != _wrapWidth && IsHandleCreated)
            {
                _wrapWidth = ClientSize.Width;
                var copy = new System.Collections.Generic.List<(string text, Color color)>(_lines);
                SetLines(copy);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                BeginUpdate();
                for (int i = 0; i < Items.Count; i++)
                {
                    SetSelected(i, true);
                }
                EndUpdate();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                var sb = new System.Text.StringBuilder();
                foreach (int i in SelectedIndices)
                {
                    sb.AppendLine(_lines[i].text);
                }
                if (sb.Length > 0)
                {
                    Clipboard.SetText(sb.ToString());
                }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>Drop-down list in the theme: box, arrow and the opened list drawn in the app colours.</summary>
    public sealed class ThemedCombo : ComboBox
    {
        private bool _hover;

        public ThemedCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ItemHeight = Font.Height + 8;
        }

        /// <summary>The closed box is drawn over the native one after WM_PAINT; the opened list stays native and owner-drawn.</summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && IsHandleCreated)
            {
                using Graphics g = Graphics.FromHwnd(Handle);
                PaintBox(g);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            Invalidate();
            base.OnSelectedIndexChanged(e);
        }

        private void PaintBox(Graphics g)
        {
            g.Clear(Parent?.BackColor ?? Theme.Panel);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Theme.Field))
            {
                g.FillRectangle(b, r);
            }
            using (var pen = new Pen(Focused || DroppedDown ? Theme.Accent : _hover ? Theme.Dim : Theme.Border))
            {
                g.DrawRectangle(pen, r);
            }
            TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font, new Rectangle(8, 0, Width - 34, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            // arrow
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width - 16;
            int cy = Height / 2;
            using (var b = new SolidBrush(Theme.Dim))
            {
                g.FillPolygon(b, new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 4) });
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Field))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                sel ? Color.White : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Animated bar under the header while an operation runs.</summary>
    public sealed class BusyBar : Control
    {
        private readonly Timer _timer = new Timer { Interval = 30 };
        private int _pos;

        public BusyBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 3;
            _timer.Tick += (s, e) =>
            {
                _pos = (_pos + 8) % Math.Max(1, Width + 240);
                Invalidate();
            };
        }

        public bool Running
        {
            get { return _timer.Enabled; }
            set
            {
                _timer.Enabled = value;
                _pos = 0;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Panel);
            if (!_timer.Enabled)
            {
                return;
            }
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(b, _pos - 240, 0, 240, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
