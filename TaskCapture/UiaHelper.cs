using System.Drawing;
using System.Windows.Automation;

namespace TaskCapture
{
    public sealed class PanelCaptureResult
    {
        public List<Bitmap> Frames { get; } = new();
        public Rectangle ScreenRegion { get; init; }
    }
    public static class UiaHelper
    {
        public static AutomationElement FindScrollableForRegionAdvanced(AutomationElement root, Rectangle region)
        {
            var cond = new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true);
            var coll = root.FindAll(TreeScope.Subtree, cond);
            AutomationElement? best = null;
            double bestArea = 0;

            var reg = new System.Windows.Rect(region.Left, region.Top, region.Width, region.Height);

            for (int i = 0; i < coll.Count; i++)
            {
                var el = coll[i];
                var r = el.Current.BoundingRectangle; // System.Windows.Rect
                if (r.IsEmpty) continue;

                var inter = System.Windows.Rect.Intersect(r, reg);
                if (inter.IsEmpty) continue;
                double area = inter.Width * inter.Height;
                if (area > bestArea) { bestArea = area; best = el; }
            }
            if (best != null) return best;

            // fallback на старый способ
            return FindScrollableForRegion(root, new System.Drawing.Point(region.Left + region.Width / 2, region.Top + region.Height / 2));
        }
        public static AutomationElement FindScrollableForRegion(AutomationElement root, System.Drawing.Point screenPoint)
        {
            var pt = new System.Windows.Point(screenPoint.X, screenPoint.Y);
            var el = AutomationElement.FromPoint(pt);
            // поднимаемся по родителям, ищем ScrollPattern
            for (var cur = el; cur != null; cur = TreeWalker.ControlViewWalker.GetParent(cur))
            {
                if (SupportsScroll(cur)) return cur;
            }
            // fallback: первый со ScrollPattern в подпорядке
            var cond = new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true);
            return root.FindFirst(TreeScope.Subtree, cond) ?? throw new InvalidOperationException("Scrollable element not found.");
        }


        private static bool SupportsScroll(AutomationElement el)
        {
            object? p;
            return el.TryGetCurrentPattern(ScrollPattern.Pattern, out p);
        }

        public static async Task<PanelCaptureResult> ScrollAndCaptureAsync(
    AutomationElement scrollEl, Rectangle regionOnScreen,
    int overlapPx = 80, int settleMs = 120, bool stayInBrowser = true, Action<string>? log = null)
        {
            var res = new PanelCaptureResult { ScreenRegion = regionOnScreen };

            try { scrollEl.SetFocus(); } catch { }

            ScrollPattern? sp = null;
            if (scrollEl.TryGetCurrentPattern(ScrollPattern.Pattern, out var p)) sp = (ScrollPattern)p;

            if (sp != null) { TryScrollToTop(sp); await Task.Delay(settleMs); }

            double lastPct = -1; int stagnant = 0;
            res.Frames.Add(Utils.CaptureRegion(regionOnScreen));

            // точка скролла — немного ниже верхней границы панели
            int cx = regionOnScreen.Left + regionOnScreen.Width / 2;
            int cy = regionOnScreen.Top + Math.Max(40, regionOnScreen.Height / 10);

            int ticks = 0, maxTicks = 300;
            Bitmap? prev = null;

            while (true)
            {
                bool moved = false;
                double pctBefore = sp?.Current.VerticalScrollPercent ?? -1;

                if (sp?.Current.VerticallyScrollable == true)
                {
                    sp.Scroll(ScrollAmount.LargeIncrement, ScrollAmount.NoAmount);
                    await Task.Delay(settleMs);
                    double pctAfter = sp.Current.VerticalScrollPercent;
                    log?.Invoke($"UIA: pct {pctBefore:0.0} -> {pctAfter:0.0}");

                    if (pctAfter >= 99.9) { res.Frames.Add(Utils.CaptureRegion(regionOnScreen)); break; }
                    if (Math.Abs(pctAfter - pctBefore) >= 0.001) { stagnant = 0; moved = true; }
                    else stagnant++;
                }
                else stagnant++; // UIA не умеет — сразу к фоллбэку

                if (!moved && stagnant >= 2)
                {
                    // колесо без модификаторов, прямо в окно под точкой (cx,cy)
                    for (int i = 0; i < 6; i++)
                    {
                        Native.PostMouseWheelToPoint(cx, cy, -120);
                        await Task.Delay(18);
                    }
                    await Task.Delay(settleMs);
                    stagnant = 0;
                }

                var frame = Utils.CaptureRegion(regionOnScreen);
                res.Frames.Add(frame);

                if (++ticks >= maxTicks) break;

                if (prev != null && AreAlmostSame(prev, frame))
                    break;
                prev?.Dispose();
                prev = (Bitmap)frame.Clone();

                // стоп по «не меняется картинка» или лимиту итераций (подстраховка)
                if (res.Frames.Count > 300) break;
            }

            return res;
        }

        // простая проверка сходства кадров (быстрая): сравнить 100 сэмплов пикселей
        static bool AreAlmostSame(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return false;
            int same = 0, total = 120;
            var rnd = new Random(1);
            for (int i = 0; i < total; i++)
            {
                int x = rnd.Next(0, a.Width), y = rnd.Next(0, a.Height);
                if (a.GetPixel(x, y) == b.GetPixel(x, y)) same++;
            }
            return same > total * 0.98; // 98% одинаковых — считаем застой
        }

        private static void TryScrollToTop(ScrollPattern sp)
        {
            try
            {
                // попытка абсолютной установки (не все провайдеры поддерживают)
                if (sp.Current.VerticallyScrollable)
                    sp.SetScrollPercent(ScrollPattern.NoScroll, 0);
            }
            catch
            {
                try { for (int i = 0; i < 50; i++) sp.Scroll(ScrollAmount.LargeDecrement, ScrollAmount.NoAmount); }
                catch { }
            }
        }
    }
}
