using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenTranslator
{
    public class ScreenCapture
    {
        public Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            }

            return bitmap;
        }

        public static byte[] BitmapToBytes(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            int byteCount = bmpData.Stride * bitmap.Height;
            byte[] bytes = new byte[byteCount];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, bytes, 0, byteCount);

            bitmap.UnlockBits(bmpData);
            return bytes;
        }

        public static bool HasChanged(byte[] previous, byte[] current, double threshold = 0.01)
        {
            if (previous.Length != current.Length) return true;

            int diffCount = 0;
            for (int i = 0; i < previous.Length; i++)
            {
                if (previous[i] != current[i])
                    diffCount++;
            }

            double diffRatio = (double)diffCount / previous.Length;
            return diffRatio > threshold;
        }
    }
}