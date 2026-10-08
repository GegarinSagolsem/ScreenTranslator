using System.Drawing;

namespace ScreenTranslator
{
    /// <summary>
    /// Finds speech bubbles and narration boxes on a manga page: enclosed, roundish white areas of a
    /// sensible size. Reading each one separately keeps drawings, screentone and panel borders out of
    /// the text, and gives one translation per bubble.
    /// </summary>
    internal static class SpeechBubbles
    {
        private const int WorkingSize = 1000;     // analyse a downscaled copy; plenty for finding shapes
        private const double MinArea = 0.001;     // share of the page: a "!?" bubble on a two-page spread
        private const double MaxArea = 0.25;
        private const double MinFill = 0.45;      // white pixels / bounding box: bubbles are solid, backgrounds aren't
        private const double MaxAspect = 5;
        private const double MinInk = 0.015;      // a bubble holds text: some ink inside, but not mostly ink
        private const double MaxInk = 0.45;

        /// <param name="Bounds">The bubble's interior in the source image.</param>
        /// <param name="Image">The interior with everything outside the bubble's shape painted white.</param>
        /// <param name="Ink">What's written inside the bubble (text, ruby, marks) in the source image; empty if nothing.</param>
        public sealed class Bubble(Rectangle bounds, Bitmap image, Rectangle ink) : IDisposable
        {
            public Rectangle Bounds { get; } = bounds;
            public Bitmap Image { get; } = image;
            public Rectangle Ink { get; } = ink;
            public void Dispose() => Image.Dispose();
        }

        public static List<Bubble> Find(Bitmap page)
        {
            double scale = Math.Min(1.0, (double)WorkingSize / Math.Max(page.Width, page.Height));
            int w = Math.Max(1, (int)(page.Width * scale)), h = Math.Max(1, (int)(page.Height * scale));
            using var small = scale < 1.0 ? new Bitmap(page, w, h) : null;
            var white = WhiteMask(ScreenCapture.BitmapToBytes(small ?? page), w, h);

            var (labels, regions) = Label(white, w, h);
            var candidates = regions
                .Where(r => !r.TouchesEdge)
                .Where(r => r.Box.Width * r.Box.Height is var box && box >= MinArea * w * h && box <= MaxArea * w * h)
                .Where(r => (double)r.Pixels / (r.Box.Width * r.Box.Height) >= MinFill)
                .Where(r => Math.Max(r.Box.Width, r.Box.Height) <= MaxAspect * Math.Min(r.Box.Width, r.Box.Height))
                .ToList();

            // A panel's white background has the bubbles inside it as holes: keep the bubbles, not the panel
            candidates = candidates.Where(outer => !candidates.Any(inner => inner != outer && outer.Box.Contains(inner.Box))).ToList();

            var bubbles = new List<Bubble>();
            foreach (var region in candidates)
            {
                var inside = Inside(labels, w, region);
                if (InkShare(white, w, region.Box, inside) is < MinInk or > MaxInk) continue; // e.g. an empty gap in the art

                var bounds = new Rectangle((int)(region.Box.X / scale), (int)(region.Box.Y / scale),
                    Math.Min(page.Width, (int)Math.Ceiling(region.Box.Width / scale)), Math.Min(page.Height, (int)Math.Ceiling(region.Box.Height / scale)));
                bounds.Intersect(new Rectangle(0, 0, page.Width, page.Height));
                if (bounds.Width < 8 || bounds.Height < 8) continue;

                var (image, ink) = MaskedCrop(page, bounds, inside, region.Box, scale);
                bubbles.Add(new Bubble(bounds, image, ink));
            }
            return bubbles;
        }

        private static bool[] WhiteMask(byte[] bgra, int w, int h)
        {
            var lum = new byte[w * h];
            var histogram = new int[256];
            for (int i = 0; i < lum.Length; i++)
            {
                int p = i * 4;
                lum[i] = (byte)((bgra[p + 2] * 299 + bgra[p + 1] * 587 + bgra[p] * 114) / 1000);
                histogram[lum[i]]++;
            }

            // "Paper white": a little below the brightest 5 % of the page
            int count = 0, bright = 255;
            for (; bright > 0 && count < lum.Length / 20; bright--) count += histogram[bright];
            int threshold = Math.Max(170, bright - 35);

            var white = new bool[lum.Length];
            for (int i = 0; i < lum.Length; i++) white[i] = lum[i] >= threshold;
            return white;
        }

        private sealed record Region(int Id, Rectangle Box, int Pixels, bool TouchesEdge);

        /// <summary>Connected white areas (4-neighbour flood fill).</summary>
        private static (int[] Labels, List<Region> Regions) Label(bool[] white, int w, int h)
        {
            var labels = new int[w * h];
            var regions = new List<Region>();
            var stack = new Stack<int>();
            int next = 0;

            for (int start = 0; start < labels.Length; start++)
            {
                if (!white[start] || labels[start] != 0) continue;

                int id = ++next, pixels = 0, minX = w, minY = h, maxX = 0, maxY = 0;
                bool edge = false;
                labels[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    pixels++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge = true;

                    if (x > 0) Visit(i - 1);
                    if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w);
                    if (y < h - 1) Visit(i + w);
                }
                regions.Add(new Region(id, new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), pixels, edge));

                void Visit(int n)
                {
                    if (white[n] && labels[n] == 0)
                    {
                        labels[n] = id;
                        stack.Push(n);
                    }
                }
            }
            return (labels, regions);
        }

        /// <summary>
        /// The bubble's shape within its box: its white pixels plus the text they enclose. Anything that
        /// can reach the edge of the box without crossing the bubble (outline, art around a round bubble)
        /// is outside.
        /// </summary>
        private static bool[] Inside(int[] labels, int w, Region region)
        {
            var box = region.Box;
            var outside = new bool[box.Width * box.Height];
            var stack = new Stack<(int X, int Y)>();

            void Push(int x, int y)
            {
                int local = (y - box.Y) * box.Width + (x - box.X);
                if (outside[local] || labels[y * w + x] == region.Id) return;
                outside[local] = true;
                stack.Push((x, y));
            }

            for (int x = box.Left; x < box.Right; x++) { Push(x, box.Top); Push(x, box.Bottom - 1); }
            for (int y = box.Top; y < box.Bottom; y++) { Push(box.Left, y); Push(box.Right - 1, y); }
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (x > box.Left) Push(x - 1, y);
                if (x < box.Right - 1) Push(x + 1, y);
                if (y > box.Top) Push(x, y - 1);
                if (y < box.Bottom - 1) Push(x, y + 1);
            }

            // The shape was found on a smaller copy, so at full size its edge can hold a sliver of the
            // outline; one pixel in from the edge is still well clear of the text
            var inside = new bool[outside.Length];
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    int i = y * box.Width + x;
                    inside[i] = !outside[i] && x > 0 && y > 0 && x < box.Width - 1 && y < box.Height - 1
                        && !outside[i - 1] && !outside[i + 1] && !outside[i - box.Width] && !outside[i + box.Width];
                }
            return inside;
        }

        /// <summary>Share of the bubble's shape that isn't white, i.e. its text.</summary>
        private static double InkShare(bool[] white, int w, Rectangle box, bool[] inside)
        {
            int area = 0, ink = 0;
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    if (!inside[y * box.Width + x]) continue;
                    area++;
                    if (!white[(box.Y + y) * w + box.X + x]) ink++;
                }
            return area == 0 ? 0 : (double)ink / area;
        }

        /// <returns>The crop, and the box around the dark pixels left in it, in page coordinates.</returns>
        private static (Bitmap Image, Rectangle Ink) MaskedCrop(Bitmap page, Rectangle bounds, bool[] inside, Rectangle box, double scale)
        {
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            var crop = page.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = crop.LockBits(new Rectangle(0, 0, crop.Width, crop.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[data.Stride * crop.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int y = 0; y < crop.Height; y++)
                {
                    int sy = Math.Clamp((int)((bounds.Y + y) * scale) - box.Y, 0, box.Height - 1);
                    for (int x = 0; x < crop.Width; x++)
                    {
                        int sx = Math.Clamp((int)((bounds.X + x) * scale) - box.X, 0, box.Width - 1);
                        int p = y * data.Stride + x * 4;
                        if (inside[sy * box.Width + sx])
                        {
                            if (pixels[p + 2] * 299 + pixels[p + 1] * 587 + pixels[p] * 114 < 160_000)
                            {
                                left = Math.Min(left, x); right = Math.Max(right, x);
                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                            }
                            continue;
                        }
                        pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 255;
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                crop.UnlockBits(data);
            }
            var ink = right < 0 ? Rectangle.Empty
                : new Rectangle(bounds.X + left, bounds.Y + top, right - left + 1, bottom - top + 1);
            return (crop, ink);
        }
    }
}
