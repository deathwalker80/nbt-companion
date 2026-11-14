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

    /// <summary>Thin border around a field (text box, list): grey, blue while the field has the focus.</summary>
    public sealed class Frame : Panel
    {
        private readonly Control _inner;

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
            }
            Padding = single ? new Padding(8, 6, 8, 4) : new Padding(1);
            inner.Dock = DockStyle.Fill;
            Controls.Add(inner);
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_inner.BackColor);
            using var pen = new Pen(_inner.ContainsFocus ? Theme.Accent : Theme.Border);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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
