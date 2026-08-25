using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Piko.Desktop.Services;

internal static class PikoTrayIconFactory
{
    private const int CanvasSize = 64;

    public static Icon Create()
    {
        using var bitmap = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var shadow = new SolidBrush(Color.FromArgb(72, 10, 31, 32));
            graphics.FillEllipse(shadow, 4, 5, 56, 56);

            using var background = new LinearGradientBrush(
                new Rectangle(4, 3, 56, 56),
                Color.FromArgb(255, 49, 105, 101),
                Color.FromArgb(255, 27, 69, 70),
                LinearGradientMode.ForwardDiagonal);
            graphics.FillEllipse(background, 4, 3, 56, 56);

            using var rim = new Pen(Color.FromArgb(150, 159, 215, 197), 2);
            graphics.DrawEllipse(rim, 5, 4, 54, 54);

            using var font = new Font("Segoe UI Black", 39, FontStyle.Bold, GraphicsUnit.Pixel);
            using var letter = new SolidBrush(Color.FromArgb(255, 255, 244, 222));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            };
            graphics.DrawString("P", font, letter, new RectangleF(2, -1, 60, 62), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}

