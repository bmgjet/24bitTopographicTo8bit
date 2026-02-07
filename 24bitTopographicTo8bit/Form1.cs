using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _24bitTopographicTo8bit
{
    public partial class Form1 : Form
    {
        public Form1() { InitializeComponent(); }

        private bool IsProcessing = false;
        private Bitmap MainImage;
        private Bitmap Preview;

        // The topographical color scale from High to Low
        private static readonly Color[] Scale = {
            ColorTranslator.FromHtml("#EAC7C8"),
            ColorTranslator.FromHtml("#EC867E"),
            ColorTranslator.FromHtml("#E6B879"),
            ColorTranslator.FromHtml("#EBDE7D"),
            ColorTranslator.FromHtml("#99EA7F"),
            ColorTranslator.FromHtml("#79EBA8"),
            ColorTranslator.FromHtml("#79E9E9"),
            ColorTranslator.FromHtml("#79CFEC")
        };

        private void button1_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    MainImage?.Dispose();
                    Preview?.Dispose();
                    MainImage = new Bitmap(ofd.FileName);
                    Preview = ResizeBitmap(MainImage, 512, 512);
                    UpdatePreview();
                    button2.Enabled = button3.Enabled = true;
                }
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            if (MainImage == null || IsProcessing) return;

            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "PNG|*.png", FileName = "heightmap.png" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    IsProcessing = true;
                    button2.Enabled = false; // Disable to prevent double-click

                    // Setup the progress reporter
                    var progress = new Progress<int>(percent => {
                        progressBar1.Value = percent;
                        this.Text = $"Processing: {percent}%";
                    });

                    int scaleVal = trackBar1.Value;
                    byte threshold = (byte)trackBar2.Value;
                    int radius = trackBar3.Value;

                    await Task.Run(() => {
                        using (Bitmap result = ProcessImage(MainImage, scaleVal, threshold, radius, progress))
                        {
                            result.Save(sfd.FileName, ImageFormat.Png);
                        }
                    });

                    this.Text = "24bit to 8bit - Complete";
                    progressBar1.Value = 0;
                    button2.Enabled = true;
                    IsProcessing = false;
                }
            }
        }

        private Bitmap ProcessImage(Bitmap input, int scaleContrast, byte coastThreshold, int blurRadius, IProgress<int> progress = null)
        {
            int width = input.Width;
            int height = input.Height;
            Bitmap output = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

            // Set Grayscale Palette
            ColorPalette pal = output.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            output.Palette = pal;

            BitmapData inData = input.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData outData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            unsafe
            {
                byte* pIn = (byte*)inData.Scan0;
                byte* pOut = (byte*)outData.Scan0;
                double contrastMultiplier = scaleContrast / 100.0;

                for (int y = 0; y < height; y++)
                {
                    // Update progress every 10 rows to reduce UI overhead
                    if (progress != null && y % 10 == 0)
                    {
                        progress.Report((int)((y / (float)height) * 100));
                    }

                    byte* rowIn = pIn + (y * inData.Stride);
                    byte* rowOut = pOut + (y * outData.Stride);

                    for (int x = 0; x < width; x++)
                    {
                        int bIdx = x * 3;
                        Color c = Color.FromArgb(rowIn[bIdx + 2], rowIn[bIdx + 1], rowIn[bIdx]);

                        int gray = ElevationToGray(c);
                        rowOut[x] = (byte)Math.Max(0, Math.Min(255, gray * contrastMultiplier));
                    }
                }
            }

            input.UnlockBits(inData);
            output.UnlockBits(outData);

            if (blurRadius > 0)
            {
                progress?.Report(95); // Finalizing stage
                return SmoothCoastline(output, coastThreshold, blurRadius);
            }

            return output;
        }

        private void button3_Click(object sender, EventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            var processed = ProcessImage(Preview, trackBar1.Value, (byte)trackBar2.Value, trackBar3.Value, null);
            pictureBox1.BackgroundImage?.Dispose();
            pictureBox1.BackgroundImage = processed;
        }

        private static int ElevationToGray(Color color)
        {
            RgbToLab(color, out double l0, out double a0, out double b0);
            double minDistance = double.MaxValue;
            double bestPos = 0;

            for (int i = 0; i < Scale.Length - 1; i++)
            {
                RgbToLab(Scale[i], out double l1, out double a1, out double b1);
                RgbToLab(Scale[i + 1], out double l2, out double a2, out double b2);

                // Project point onto line segment in Lab space
                double dL = l2 - l1; double dA = a2 - a1; double dB = b2 - b1;
                double t = ((l0 - l1) * dL + (a0 - a1) * dA + (b0 - b1) * dB) / (dL * dL + dA * dA + dB * dB);
                t = Math.Max(0, Math.Min(1, t));

                double dist = Math.Pow(l0 - (l1 + dL * t), 2) + Math.Pow(a0 - (a1 + dA * t), 2) + Math.Pow(b0 - (b1 + dB * t), 2);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestPos = i + t;
                }
            }

            return (int)(255 * (1.0 - (bestPos / (Scale.Length - 1))));
        }

        // Fast Pointer-based Blur for 8bpp
        public static Bitmap SmoothCoastline(Bitmap source, byte threshold, int radius)
        {
            int w = source.Width;
            int h = source.Height;
            Bitmap dest = new Bitmap(w, h, source.PixelFormat);
            dest.Palette = source.Palette;

            BitmapData srcData = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, source.PixelFormat);
            BitmapData destData = dest.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, source.PixelFormat);

            unsafe
            {
                byte* pSrc = (byte*)srcData.Scan0;
                byte* pDest = (byte*)destData.Scan0;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte center = pSrc[y * srcData.Stride + x];
                        if (center <= threshold)
                        {
                            int sum = 0, count = 0;
                            for (int ky = -radius; ky <= radius; ky++)
                            {
                                int ny = y + ky;
                                if (ny < 0 || ny >= h) continue;
                                for (int kx = -radius; kx <= radius; kx++)
                                {
                                    int nx = x + kx;
                                    if (nx >= 0 && nx < w)
                                    {
                                        sum += pSrc[ny * srcData.Stride + nx];
                                        count++;
                                    }
                                }
                            }
                            pDest[y * destData.Stride + x] = (byte)(sum / count);
                        }
                        else { pDest[y * destData.Stride + x] = center; }
                    }
                }
            }
            source.UnlockBits(srcData);
            dest.UnlockBits(destData);
            source.Dispose(); // Clean up intermediate
            return dest;
        }

        // Standard Resize
        public static Bitmap ResizeBitmap(Bitmap original, int width, int height)
        {
            Bitmap res = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(res))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, width, height);
            }
            return res;
        }

        // Optimized RGB to Lab
        static void RgbToLab(Color c, out double l, out double a, out double b)
        {
            double r = c.R / 255.0; double g = c.G / 255.0; double bl = c.B / 255.0;
            r = (r > 0.04045) ? Math.Pow((r + 0.055) / 1.055, 2.4) : r / 12.92;
            g = (g > 0.04045) ? Math.Pow((g + 0.055) / 1.055, 2.4) : g / 12.92;
            bl = (bl > 0.04045) ? Math.Pow((bl + 0.055) / 1.055, 2.4) : bl / 12.92;

            double x = (r * 0.4124 + g * 0.3576 + bl * 0.1805) / 0.95047;
            double y = (r * 0.2126 + g * 0.7152 + bl * 0.0722) / 1.00000;
            double z = (r * 0.0193 + g * 0.1192 + bl * 0.9505) / 1.08883;

            x = (x > 0.008856) ? Math.Pow(x, 1 / 3.0) : (7.787 * x) + (16 / 116.0);
            y = (y > 0.008856) ? Math.Pow(y, 1 / 3.0) : (7.787 * y) + (16 / 116.0);
            z = (z > 0.008856) ? Math.Pow(z, 1 / 3.0) : (7.787 * z) + (16 / 116.0);

            l = (116 * y) - 16;
            a = 500 * (x - y);
            b = 200 * (y - z);
        }
    }
}