using System.Drawing;
using System.Drawing.Drawing2D;

namespace ScreenTranslator
{
    /// <summary>
    /// Windows OCR reads Chinese only in horizontal lines, so vertical text (columns read top to bottom,
    /// right to left) comes out as gibberish. This finds each column, cuts it into character cells and
    /// lays the cells out side by side as one horizontal line per column, which OCR reads correctly.
    /// </summary>
    internal static class VerticalText
    {
        private const int InkContrast = 64;       // luminance difference from the background that counts as ink
        private const double FuriganaWidth = 0.6;  // columns narrower than this share of the typical one are ruby text
        private const double ValleyDepth = 0.25;   // ink this far below both sides' peaks separates two bands

        /// <param name="Sources">The column(s) in the captured bitmap that make up this line, in reading order.</param>
        /// <param name="CellCounts">Characters (cells) per source column, to split the recognised text back up.</param>
        /// <param name="RowCenterY">Vertical centre of the re-laid-out line in the rearranged image.</param>
        public sealed record ColumnRow(IReadOnlyList<Rectangle> Sources, IReadOnlyList<int> CellCounts, int RowCenterY, int RowHeight);

        public sealed class Rearranged(Bitmap image, IReadOnlyList<ColumnRow> rows) : IDisposable
        {
            public Bitmap Image { get; } = image;
            public IReadOnlyList<ColumnRow> Rows { get; } = rows;
            public void Dispose() => Image.Dispose();
        }

        /// <returns>The columns laid out as horizontal lines, right-most column first; null if no columns were found.</returns>
        public static Rearranged? Rearrange(Bitmap source)
        {
            int width = source.Width, height = source.Height;
            var pixels = ScreenCapture.BitmapToBytes(source);
            var (ink, background) = InkMask(pixels, width, height);

            var columns = FindColumns(ink, width, height);
            if (columns.Count == 0) return null;

            var cellsPerColumn = columns.Select(c => FindCells(ink, width, c)).ToList();
            int charSize = columns.Max(c => c.Width);
            int gap = Math.Max(2, charSize / 5);
            int rowHeight = (int)(charSize * 1.4);
            int margin = charSize;

            // Windows OCR ignores a line of only one to three characters (the end of a paragraph, like
            // "る。"), so a short column continues the line of the column before it when they're neighbours
            var lines = new List<List<int>>();
            for (int i = 0; i < columns.Count; i++)
            {
                bool isShort = cellsPerColumn[i].Count <= 3;
                bool nextToPrevious = i > 0 && columns[i - 1].Left - columns[i].Right < charSize * 2;
                if (isShort && nextToPrevious) lines[^1].Add(i);
                else lines.Add([i]);
            }

            int imageWidth = margin * 2 + lines.Max(line => line.Sum(i => (cellsPerColumn[i].Count + 1) * (charSize + gap)));
            int imageHeight = margin * 2 + lines.Count * rowHeight * 2;

            var image = new Bitmap(imageWidth, imageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rows = new List<ColumnRow>();
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                for (int row = 0; row < lines.Count; row++)
                {
                    int rowTop = margin + row * rowHeight * 2; // a blank row between lines keeps them apart
                    int x = margin;

                    foreach (int i in lines[row])
                    {
                        foreach (var cell in cellsPerColumn[i])
                        {
                            if (IsTurned(ink, width, cell))
                            {
                                // Vertical text turns ー, … and 「」 by 90°; OCR reads them laid back down
                                using var turned = source.Clone(cell, source.PixelFormat);
                                turned.RotateFlip(RotateFlipType.Rotate270FlipNone);
                                g.DrawImage(turned, new Rectangle(x + (charSize - turned.Width) / 2,
                                    rowTop + (rowHeight - turned.Height) / 2, turned.Width, turned.Height));
                            }
                            else
                            {
                                int y = rowTop + (rowHeight - cell.Height) / 2;
                                int cellX = x + (charSize - cell.Width) / 2;
                                g.DrawImage(source, new Rectangle(cellX, y, cell.Width, cell.Height), cell, GraphicsUnit.Pixel);
                            }
                            x += charSize + gap;
                        }
                    }

                    rows.Add(new ColumnRow(lines[row].Select(i => columns[i]).ToList(),
                        lines[row].Select(i => cellsPerColumn[i].Count).ToList(), rowTop + rowHeight / 2, rowHeight));
                }
            }

            return new Rearranged(image, rows);
        }

        /// <summary>
        /// A long-vowel bar (｜ for ー), a vertical ellipsis (︙) or a turned bracket (﹁): ink much narrower
        /// than the column, at least half a character tall, and not an exclamation mark (a bar over a dot).
        /// </summary>
        private static bool IsTurned(bool[] ink, int width, Rectangle cell)
        {
            int left = cell.Right, right = cell.Left - 1;
            var inkPerY = new int[cell.Height];
            for (int y = 0; y < cell.Height; y++)
                for (int x = cell.Left; x < cell.Right; x++)
                {
                    if (!ink[(cell.Top + y) * width + x]) continue;
                    inkPerY[y]++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }

            if (right < left || right - left + 1 > cell.Width * 0.4 || cell.Height < cell.Width * 0.5) return false;
            var strokes = Runs(inkPerY, 1);
            bool exclamation = strokes.Count == 2 && strokes[0].Length >= cell.Width * 0.4 && strokes[1].Length <= cell.Width * 0.25;
            return !exclamation;
        }

        /// <summary>Marks pixels that differ clearly from the most common (background) brightness.</summary>
        private static (bool[] Ink, Color Background) InkMask(byte[] bgra, int width, int height)
        {
            var luminance = new byte[width * height];
            var histogram = new int[256];
            for (int i = 0; i < luminance.Length; i++)
            {
                int p = i * 4;
                luminance[i] = (byte)((bgra[p + 2] * 299 + bgra[p + 1] * 587 + bgra[p] * 114) / 1000);
                histogram[luminance[i]]++;
            }

            int backgroundLevel = Array.IndexOf(histogram, histogram.Max());
            var ink = new bool[luminance.Length];
            long r = 0, g = 0, b = 0, count = 0;
            for (int i = 0; i < luminance.Length; i++)
            {
                int diff = Math.Abs(luminance[i] - backgroundLevel);
                ink[i] = diff > InkContrast;
                if (diff <= 8)
                {
                    int p = i * 4;
                    b += bgra[p]; g += bgra[p + 1]; r += bgra[p + 2]; count++;
                }
            }

            var background = count == 0 ? Color.White : Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
            return (ink, background);
        }

        /// <summary>
        /// Columns are vertical bands of ink. Long horizontal lines (speech-bubble outlines, underlines)
        /// would join every column into one band, and vertical text never has a horizontal stroke wider
        /// than a character or two, so those lines are erased first. Ruby (furigana) runs in a thin band
        /// beside its column and often touches it or the next one, as in tightly set manga, so bands are
        /// also split where their ink drops into a clear valley, and the thin bands are left out.
        /// </summary>
        private static List<Rectangle> FindColumns(bool[] ink, int width, int height)
        {
            EraseLongHorizontalLines(ink, width, height, Math.Max(60, width / 3));

            const int minInk = 2;
            var inkPerX = new int[width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (ink[y * width + x]) inkPerX[x]++;

            var pieces = new List<(int Start, int Length)>();
            foreach (var (start, length) in Runs(inkPerX, minInk))
                SplitAtValleys(inkPerX, start, start + length, pieces);
            if (pieces.Count == 0) return [];

            // Join pieces of one character split by a gap (川, い): both narrow, close together, and
            // together no wider than a column. A column and its ruby are never both narrow.
            int typicalWidth = TypicalWidth(inkPerX, pieces);
            var joined = new List<(int Start, int Length)> { pieces[0] };
            foreach (var piece in pieces.Skip(1))
            {
                var last = joined[^1];
                bool narrow = last.Length < typicalWidth * 0.8 && piece.Length < typicalWidth * 0.8;
                bool close = piece.Start - (last.Start + last.Length) <= typicalWidth / 4;
                if (narrow && close && piece.Start + piece.Length - last.Start <= typicalWidth * 1.25)
                    joined[^1] = (last.Start, piece.Start + piece.Length - last.Start);
                else
                    joined.Add(piece);
            }

            int columnWidth = TypicalWidth(inkPerX, joined);
            var columns = new List<Rectangle>();
            foreach (var (start, length) in joined)
            {
                if (length < 4 || length < columnWidth * FuriganaWidth) continue; // noise or ruby text

                int top = -1, bottom = -1;
                for (int y = 0; y < height; y++)
                {
                    for (int x = start; x < start + length; x++)
                    {
                        if (!ink[y * width + x]) continue;
                        if (top < 0) top = y;
                        bottom = y;
                        break;
                    }
                }

                if (top >= 0 && bottom - top + 1 >= length * 0.8) // a column is at least one character tall
                    columns.Add(new Rectangle(start, top, length, bottom - top + 1));
            }

            return columns.OrderByDescending(c => c.Right).ToList(); // read right to left
        }

        /// <summary>
        /// Small text can leave no blank row between two characters. Ink too tall to be one character is
        /// cut at its thinnest row about a character down, until what's left fits.
        /// </summary>
        private static void CutTouchingCharacters(int[] inkPerY, int start, int end, int charSize, List<(int Start, int Length)> pieces)
        {
            while (end - start > charSize * 1.8)
            {
                int cut = start + (int)(charSize * 0.7);
                for (int y = cut + 1; y <= start + charSize * 1.3; y++)
                    if (inkPerY[y] < inkPerY[cut]) cut = y;
                pieces.Add((start, cut - start));
                start = cut;
            }
            pieces.Add((start, end - start));
        }

        /// <summary>
        /// Splits the band [start, end) where its ink drops well below the peaks on both sides, deepest
        /// valley first, and trims each piece's faint edges (a stray mark of the neighbouring band).
        /// </summary>
        private static void SplitAtValleys(int[] inkPerX, int start, int end, List<(int Start, int Length)> pieces)
        {
            var peakLeft = new int[end - start];
            var peakRight = new int[end - start];
            for (int x = start, max = 0; x < end; x++) peakLeft[x - start] = max = Math.Max(max, inkPerX[x]);
            for (int x = end - 1, max = 0; x >= start; x--) peakRight[x - start] = max = Math.Max(max, inkPerX[x]);

            int valley = -1;
            for (int x = start + 1; x < end - 1; x++)
            {
                bool clear = inkPerX[x] <= ValleyDepth * Math.Min(peakLeft[x - start - 1], peakRight[x - start + 1]);
                if (clear && (valley < 0 || inkPerX[x] < inkPerX[valley])) valley = x;
            }

            if (valley >= 0)
            {
                SplitAtValleys(inkPerX, start, valley, pieces);
                SplitAtValleys(inkPerX, valley + 1, end, pieces);
                return;
            }

            int peak = peakLeft[^1];
            while (start < end && inkPerX[start] < peak * 0.1) start++;
            while (end > start && inkPerX[end - 1] < peak * 0.1) end--;
            if (end > start) pieces.Add((start, end - start));
        }

        /// <summary>The usual column width, weighted by ink so that thin ruby bands and specks don't drag it down.</summary>
        private static int TypicalWidth(int[] inkPerX, List<(int Start, int Length)> bands)
        {
            var byWidth = bands
                .Select(b => (b.Length, Ink: Enumerable.Range(b.Start, b.Length).Sum(x => inkPerX[x])))
                .OrderBy(b => b.Length)
                .ToList();
            long total = byWidth.Sum(b => (long)b.Ink), seen = 0;
            foreach (var (length, inkCount) in byWidth)
            {
                seen += inkCount;
                if (seen * 2 >= total) return length;
            }
            return byWidth[^1].Length;
        }

        private static void EraseLongHorizontalLines(bool[] ink, int width, int height, int maxLength)
        {
            for (int y = 0; y < height; y++)
            {
                int start = -1;
                for (int x = 0; x <= width; x++)
                {
                    bool on = x < width && ink[y * width + x];
                    if (on && start < 0) start = x;
                    else if (!on && start >= 0)
                    {
                        if (x - start > maxLength)
                            Array.Fill(ink, false, y * width + start, x - start);
                        start = -1;
                    }
                }
            }
        }

        /// <summary>Splits a column into roughly square character cells, keeping multi-part glyphs (二, 三) whole.</summary>
        private static List<Rectangle> FindCells(bool[] ink, int width, Rectangle column)
        {
            var inkPerY = new int[column.Height];
            for (int y = 0; y < column.Height; y++)
                for (int x = column.Left; x < column.Right; x++)
                    if (ink[(column.Top + y) * width + x]) inkPerY[y]++;

            var pieces = new List<(int Start, int Length)>();
            foreach (var (start, length) in Runs(inkPerY, 1))
                CutTouchingCharacters(inkPerY, start, start + length, column.Width, pieces);

            var cells = new List<Rectangle>();
            int maxCell = (int)(column.Width * 1.15);
            (int Start, int End)? cell = null;
            foreach (var (start, length) in pieces)
            {
                int end = start + length;
                if (cell is { } c && end - c.Start <= maxCell)
                {
                    cell = (c.Start, end);
                    continue;
                }
                if (cell is { } done)
                    cells.Add(new Rectangle(column.Left, column.Top + done.Start, column.Width, done.End - done.Start));
                cell = (start, end);
            }
            if (cell is { } final)
                cells.Add(new Rectangle(column.Left, column.Top + final.Start, column.Width, final.End - final.Start));

            return cells;
        }

        private static List<(int Start, int Length)> Runs(int[] values, int threshold)
        {
            var runs = new List<(int, int)>();
            int start = -1;
            for (int i = 0; i <= values.Length; i++)
            {
                bool on = i < values.Length && values[i] >= threshold;
                if (on && start < 0) start = i;
                else if (!on && start >= 0)
                {
                    runs.Add((start, i - start));
                    start = -1;
                }
            }
            return runs;
        }
    }
}
