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

        public static bool HasChanged(byte[] previous, byte[] current, double threshold = 0.01)
        {
            if (previous.Length != current.Length) return true;

            // Fast vectorised path for the common case: nothing on screen moved
            if (previous.AsSpan().SequenceEqual(current)) return false;

            int allowed = (int)(previous.Length * threshold);
            int diffCount = 0;
            for (int i = 0; i < previous.Length; i++)
            {
                if (previous[i] != current[i] && ++diffCount > allowed)
                    return true;
            }

            return false;
        }
    }
}
