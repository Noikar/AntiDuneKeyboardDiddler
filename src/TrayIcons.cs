using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// Draws the tray icon at run time rather than shipping an .ico, which keeps the tool a
    /// single self-contained executable. The status dot makes the state readable at a glance:
    /// grey while idle, green while guarding the running game.
    /// </summary>
    internal static class TrayIcons
    {
        public static Icon Idle()
        {
            return Build(Color.FromArgb(150, 150, 150));
        }

        public static Icon Armed()
        {
            return Build(Color.FromArgb(60, 190, 90));
        }

        private static Icon Build(Color accent)
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    var body = new Rectangle(1, 8, 29, 19);

                    using (var fill = new SolidBrush(Color.FromArgb(240, 240, 240)))
                    using (var edge = new Pen(Color.FromArgb(45, 45, 45), 2f))
                    {
                        graphics.FillRectangle(fill, body);
                        graphics.DrawRectangle(edge, body);
                    }

                    using (var keys = new SolidBrush(Color.FromArgb(45, 45, 45)))
                    {
                        for (int row = 0; row < 2; row++)
                        {
                            for (int column = 0; column < 5; column++)
                            {
                                graphics.FillRectangle(keys, 4 + (column * 5), 11 + (row * 5), 3, 3);
                            }
                        }

                        graphics.FillRectangle(keys, 9, 21, 13, 3);
                    }

                    using (var dot = new SolidBrush(accent))
                    using (var ring = new Pen(Color.FromArgb(45, 45, 45), 2f))
                    {
                        var indicator = new Rectangle(19, 0, 12, 12);

                        graphics.FillEllipse(dot, indicator);
                        graphics.DrawEllipse(ring, indicator);
                    }
                }

                // GetHicon hands back an unmanaged icon that has to be destroyed; cloning gives
                // a managed copy that owns its own handle and survives the destroy below.
                IntPtr handle = bitmap.GetHicon();

                try
                {
                    using (Icon unmanaged = Icon.FromHandle(handle))
                    {
                        return (Icon)unmanaged.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
    }
}
