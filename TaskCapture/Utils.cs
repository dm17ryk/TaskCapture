using System.Drawing;

namespace TaskCapture
{
    public static class Utils
    {
        public static (Rectangle left, Rectangle right) SplitRect(Rectangle r, double leftPct, double rightPct)
        {
            var total = leftPct + rightPct;
            var lw = (int)Math.Round(r.Width * (leftPct / total));
            var left = new Rectangle(r.Left, r.Top, lw, r.Height);
            var right = new Rectangle(r.Left + lw, r.Top, r.Width - lw, r.Height);
            left.Inflate(-8, -8); right.Inflate(-8, -8); // не задевать сплиттер
            return (left, right);
        }
        public static System.Drawing.Point CenterOf(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);


        public static System.Drawing.Bitmap CaptureRegion(Rectangle r)
        {
            var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size);
            return bmp;
        }
    }
}
