using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GtaAtlas.Render
{
    /// <summary>
    /// Transparent background from two renders of the same frame, over black and over white: where the two differ the
    /// background shows through. Recovers soft edges (MSAA) and glass, which a colour key can't.
    /// </summary>
    public static class ThumbnailMatte
    {
        /// <summary>Combines RGBA pixels rendered over black and over white into straight-alpha BGRA (GDI+ ARGB layout).</summary>
        public static byte[] Combine(byte[] onBlack, byte[] onWhite)
        {
            if (onBlack.Length != onWhite.Length || onBlack.Length % 4 != 0)
                throw new ArgumentException("Both renders must have the same RGBA size.");
            var bgra = new byte[onBlack.Length];
            for (var i = 0; i < onBlack.Length; i += 4)
            {
                // Over black: c = a·C. Over white: c = a·C + (1 - a). So 1 - a = white - black.
                var see = (onWhite[i] - onBlack[i] + onWhite[i + 1] - onBlack[i + 1] + onWhite[i + 2] - onBlack[i + 2]) / 3;
                var a = 255 - Math.Clamp(see, 0, 255);
                if (a == 0)
                    continue;
                bgra[i] = Unpremultiply(onBlack[i + 2], a);
                bgra[i + 1] = Unpremultiply(onBlack[i + 1], a);
                bgra[i + 2] = Unpremultiply(onBlack[i], a);
                bgra[i + 3] = (byte)a;
            }
            return bgra;
        }

        private static byte Unpremultiply(byte c, int a) => (byte)Math.Min(255, (c * 255 + a / 2) / a);

        /// <summary>True when (almost) nothing is opaque: the model didn't draw.</summary>
        public static bool IsEmpty(byte[] bgra, int minPixels = 16)
        {
            var count = 0;
            for (var i = 3; i < bgra.Length; i += 4)
            {
                if (bgra[i] > 32 && ++count >= minPixels)
                    return false;
            }
            return true;
        }

        public static Bitmap ToBitmap(byte[] bgra, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(bgra, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
    }
}
