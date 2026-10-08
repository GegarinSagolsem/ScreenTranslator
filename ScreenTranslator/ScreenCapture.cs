using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenTranslator
{
    public static class ScreenCapture
    {
        public static Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            }

            return bitmap;
        }

        /// <summary>Raw BGRA pixels, top-down, stride = width * 4.</summary>
        public static byte[] BitmapToBytes(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[bmpData.Stride * bitmap.Height];
                Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        // A pixel only counts as changed when a colour channel moves by more than this,
        // so video noise and dithering don't look like new text
        private const int ChannelTolerance = 24;

        /// <summary>
        /// True when at least <paramref name="minChangedPixels"/> pixels really changed. Counting pixels rather
        /// than a share of the region means a single new character still registers in a large text box.
        /// </summary>
        public static bool HasChanged(byte[] previous, byte[] current, int minChangedPixels = 12)
        {
            if (previous.Length != current.Length) return true;

            // Fast vectorised path for the common case: nothing on screen moved
            if (previous.AsSpan().SequenceEqual(current)) return false;

            int changed = 0;
            for (int i = 0; i + 2 < previous.Length; i += 4) // BGRA; alpha is always opaque
            {
                if (Math.Abs(previous[i] - current[i]) > ChannelTolerance ||
                    Math.Abs(previous[i + 1] - current[i + 1]) > ChannelTolerance ||
                    Math.Abs(previous[i + 2] - current[i + 2]) > ChannelTolerance)
                {
                    if (++changed >= minChangedPixels)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Decides which captured frames are worth reading. Text that types out letter by letter changes
    /// on every tick, so a changed frame is only read once it stops changing. A frame that repeats
    /// one from two ticks ago also counts as settled: that's a blinking "▼ next" arrow, not typing,
    /// which never goes back to an earlier frame. Scenes that never stop animating are still read
    /// every <see cref="MaxSettleWait"/>.
    /// </summary>
    public sealed class FrameGate
    {
        public static readonly TimeSpan MaxSettleWait = TimeSpan.FromSeconds(3);

        private byte[]? _processed;       // last frame that was read
        private byte[]? _pending;         // newest changed frame waiting to settle
        private byte[]? _pendingPrevious; // the one before it, to spot blinking
        private DateTime _changeStartedAt;

        /// <summary>Forget everything, so the next frame is read straight away.</summary>
        public void Reset()
        {
            _processed = null;
            _pending = null;
            _pendingPrevious = null;
        }

        public bool ShouldProcess(byte[] frame, DateTime now)
        {
            if (_processed == null)
                return Accept(frame); // new region or settings: read immediately

            if (!ScreenCapture.HasChanged(_processed, frame))
            {
                _pending = _pendingPrevious = null; // back to what is already translated
                return false;
            }

            if (_pending == null)
            {
                _pending = frame;
                _changeStartedAt = now;
                return false;
            }

            bool settled = !ScreenCapture.HasChanged(_pending, frame) ||
                           (_pendingPrevious != null && !ScreenCapture.HasChanged(_pendingPrevious, frame));
            if (settled || now - _changeStartedAt >= MaxSettleWait)
                return Accept(frame);

            _pendingPrevious = _pending;
            _pending = frame;
            return false;
        }

        private bool Accept(byte[] frame)
        {
            _processed = frame;
            _pending = _pendingPrevious = null;
            return true;
        }
    }
}
