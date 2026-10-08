namespace ScreenTranslator.Tests;

[TestClass]
public sealed class FrameGateTests
{
    private const int Width = 300, Height = 60;
    private static readonly DateTime Start = new(2026, 1, 1);

    /// <summary>A white frame with <paramref name="chars"/> black "characters", an optional blinking arrow and sprite.</summary>
    private static byte[] Frame(int chars, bool arrow = false, int spriteX = -1)
    {
        var pixels = Enumerable.Repeat((byte)255, Width * Height * 4).ToArray();
        for (int c = 0; c < chars; c++) Fill(pixels, 5 + c * 20, 10, 14, 14);
        if (arrow) Fill(pixels, 270, 40, 10, 10);
        if (spriteX >= 0) Fill(pixels, spriteX, 30, 12, 12);
        return pixels;
    }

    private static void Fill(byte[] pixels, int x, int y, int w, int h)
    {
        for (int row = y; row < y + h; row++)
            for (int col = x; col < x + w; col++)
            {
                int i = (row * Width + col) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            }
    }

    /// <summary>Feeds frames 500 ms apart and returns the indexes of the frames that were read.</summary>
    private static List<int> Reads(params byte[][] frames)
    {
        var gate = new FrameGate();
        return frames.Select((f, i) => (Read: gate.ShouldProcess(f, Start.AddMilliseconds(500 * i)), i))
                     .Where(r => r.Read).Select(r => r.i).ToList();
    }

    [TestMethod]
    public void OneNewCharacterIsAChange() => Assert.IsTrue(ScreenCapture.HasChanged(Frame(3), Frame(4)));

    [TestMethod]
    public void VideoNoiseIsNotAChange()
    {
        var noisy = Frame(3);
        var random = new Random(1);
        for (int i = 0; i < noisy.Length; i += 4)
            noisy[i] = (byte)Math.Clamp(noisy[i] + random.Next(-12, 13), 0, 255);

        Assert.IsFalse(ScreenCapture.HasChanged(Frame(3), noisy));
    }

    [TestMethod]
    public void TypewriterTextIsReadOnlyOnceItStops()
    {
        var reads = Reads(Frame(0), Frame(1), Frame(2), Frame(3), Frame(4), Frame(4), Frame(4));

        CollectionAssert.AreEqual(new[] { 0, 5 }, reads); // the empty start, then the finished line
    }

    [TestMethod]
    public void BlinkingArrowAfterNewTextCountsAsSettled()
    {
        var reads = Reads(Frame(2), Frame(5, arrow: true), Frame(5), Frame(5, arrow: true));

        CollectionAssert.AreEqual(new[] { 0, 3 }, reads);
    }

    [TestMethod]
    public void BlinkingArrowOnTextAlreadyReadIsIgnored()
    {
        var reads = Reads(Frame(5, arrow: true), Frame(5), Frame(5, arrow: true), Frame(5), Frame(5, arrow: true));

        CollectionAssert.AreEqual(new[] { 0 }, reads);
    }

    [TestMethod]
    public void NeverSettlingSceneIsStillReadEveryFewSeconds()
    {
        var frames = Enumerable.Range(0, 16).Select(i => Frame(3, spriteX: 60 + i * 14)).ToArray();

        var reads = Reads(frames);

        Assert.HasCount(3, reads);
        Assert.IsTrue(reads.Zip(reads.Skip(1)).All(p => (p.Second - p.First) * 0.5 <= FrameGate.MaxSettleWait.TotalSeconds + 0.5));
    }

    [TestMethod]
    public void ResetReadsTheNextFrameImmediately()
    {
        var gate = new FrameGate();
        gate.ShouldProcess(Frame(1), Start);
        gate.Reset();

        Assert.IsTrue(gate.ShouldProcess(Frame(1), Start.AddSeconds(1)));
    }
}
