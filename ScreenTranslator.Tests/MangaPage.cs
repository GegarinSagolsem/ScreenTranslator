namespace ScreenTranslator.Tests;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

/// <summary>A synthetic manga page: panels, drawings, screentone, speech bubbles and a narration box.</summary>
internal static class MangaPage
{
    public static readonly (string Text, Rectangle Area)[] Expected =
    [
        ("どこへ行くの？島の奥だよ。", new Rectangle(560, 90, 230, 260)),
        ("お腹が空いたなあ…", new Rectangle(90, 700, 200, 250)),
        ("その夜、嵐が来た。", new Rectangle(640, 690, 150, 230)),
    ];

    public static Bitmap Draw(out List<(string Text, Rectangle Bubble)> bubbles)
    {
        var page = new Bitmap(900, 1200, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(page);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var border = new Pen(Color.Black, 5);
        var rnd = new Random(7);

        // Panels
        var panels = new[] { new Rectangle(30, 30, 840, 560), new Rectangle(30, 620, 400, 550), new Rectangle(460, 620, 410, 550) };
        foreach (var p in panels) g.DrawRectangle(border, p);

        // Drawings: a character silhouette, hair strokes, speed lines, hatching, screentone dots
        using (var dark = new SolidBrush(Color.FromArgb(40, 40, 40))) g.FillEllipse(dark, 120, 160, 260, 380);
        using (var pen = new Pen(Color.Black, 3))
        {
            for (int i = 0; i < 25; i++)
                g.DrawBezier(pen, 100 + rnd.Next(300), 100 + rnd.Next(200), rnd.Next(30, 860), rnd.Next(40, 580), rnd.Next(30, 860), rnd.Next(40, 580), 100 + rnd.Next(400), 400 + rnd.Next(150));
            for (int i = 0; i < 40; i++) g.DrawLine(pen, 470, 640 + i * 13, 620, 660 + i * 13);   // hatching in panel 3
        }
        using (var dot = new SolidBrush(Color.FromArgb(90, 90, 90)))
            for (int y = 640; y < 1150; y += 9) for (int x = 300; x < 420; x += 9) g.FillEllipse(dot, x, y, 4, 4);  // screentone

        bubbles = [];
        DrawBubble(g, new Rectangle(540, 70, 270, 300), ["どこへ行くの？", "島の奥だよ。"], ellipse: true);
        bubbles.Add((Expected[0].Text, new Rectangle(540, 70, 270, 300)));
        DrawBubble(g, new Rectangle(70, 680, 240, 290), ["お腹が", "空いたなあ…"], ellipse: true);
        bubbles.Add((Expected[1].Text, new Rectangle(70, 680, 240, 290)));
        DrawBubble(g, new Rectangle(630, 680, 170, 250), ["その夜、", "嵐が来た。"], ellipse: false); // narration box
        bubbles.Add((Expected[2].Text, new Rectangle(630, 680, 170, 250)));

        // A big sound effect drawn over the art (not in a bubble)
        using var sfx = new Font("Yu Gothic UI", 64, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("ドン", sfx, Brushes.Black, 380, 420);
        return page;
    }

    /// <summary>Columns right to left: columns[0] is the right-most column.</summary>
    static void DrawBubble(Graphics g, Rectangle r, string[] columns, bool ellipse)
    {
        using var fill = new SolidBrush(Color.White);
        using var outline = new Pen(Color.Black, 3);
        if (ellipse) { g.FillEllipse(fill, r); g.DrawEllipse(outline, r); }
        else { g.FillRectangle(fill, r); g.DrawRectangle(outline, r); }

        using var f = new Font("Yu Gothic UI", 26, GraphicsUnit.Pixel);
        int pitchX = 44, pitchY = 30;
        int totalWidth = columns.Length * pitchX;
        int startX = r.Left + r.Width / 2 + totalWidth / 2 - pitchX;
        int maxLen = columns.Max(c => c.Length);
        int startY = r.Top + (r.Height - maxLen * pitchY) / 2;
        for (int c = 0; c < columns.Length; c++)
            for (int i = 0; i < columns[c].Length; i++)
            {
                var ch = columns[c][i].ToString();
                var w = g.MeasureString(ch, f).Width;
                g.DrawString(ch, f, Brushes.Black, startX - c * pitchX + (28 - w) / 2, startY + i * pitchY);
            }
    }
}
