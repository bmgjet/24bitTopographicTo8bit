using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;


namespace _24bitTopographicTo8bit
{
    public partial class Form1 : Form
    {
        public Form1(){InitializeComponent();}
        public static bool Running = false;
        public static Bitmap MainImage;
        public static Bitmap Preview;
        static Color[] scale = new Color[]
         {
        ColorTranslator.FromHtml("#EAC7C8"), // Highest
        ColorTranslator.FromHtml("#EC867E"),
        ColorTranslator.FromHtml("#E6B879"),
        ColorTranslator.FromHtml("#EBDE7D"),
        ColorTranslator.FromHtml("#99EA7F"),
        ColorTranslator.FromHtml("#79EBA8"),
        ColorTranslator.FromHtml("#79E9E9"),
        ColorTranslator.FromHtml("#79CFEC")  // Lowest
         };

        public static Bitmap ResizeBitmap(Bitmap original, int width, int height)
        {
            Bitmap resized = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(resized))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            return resized;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg)|*.png;*.jpg",
                Title = "Select an Image File",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFile = openFileDialog.FileName;
                MainImage = new Bitmap(selectedFile);
                Preview = ResizeBitmap(MainImage, 256, 256);
                pictureBox1.BackgroundImage = ScaleImage(trackBar1.Value, Preview);
                button2.Enabled = true;
                button3.Enabled = true;
            }
        }

        public static Bitmap SmoothCoastline(Bitmap source, byte coastThreshold = 80, int blurRadius = 2)
        {
            int width = source.Width;
            int height = source.Height;
            int ratio = MainImage.Width / width;
            coastThreshold = Math.Max((byte)(coastThreshold / ratio), (byte)0);
            coastThreshold = Math.Min(coastThreshold, (byte)255);
            blurRadius = blurRadius / ratio;
            Bitmap result = new Bitmap(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color centerColor = source.GetPixel(x, y);
                    byte centerBrightness = centerColor.R;
                    if (centerBrightness <= coastThreshold)
                    {
                        int total = 0;
                        int count = 0;
                        for (int dy = -blurRadius; dy <= blurRadius; dy++)
                        {
                            for (int dx = -blurRadius; dx <= blurRadius; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                                {
                                    Color neighbor = source.GetPixel(nx, ny);
                                    total += neighbor.R;
                                    count++;
                                }
                            }
                        }
                        byte blurred = (byte)(total / count);
                        result.SetPixel(x, y, Color.FromArgb(blurred, blurred, blurred));
                    }
                    else
                    {
                        result.SetPixel(x, y, centerColor); // leave highlands untouched
                    }
                }
            }
            return result;
        }

        private Bitmap ScaleImage(int scale, Bitmap inputimg)
        {
            if (inputimg == null) return null;
            if (MainImage == null) return null;

            if (Running) { return inputimg; }
            Running = true;
            int width = inputimg.Width;
            int height = inputimg.Height;
            Bitmap output = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            try
            {
                ColorPalette palette = output.Palette;
                for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
                output.Palette = palette;
                BitmapData inData = inputimg.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, inputimg.PixelFormat);
                BitmapData outData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                unsafe
                {
                    byte* inScan0 = (byte*)inData.Scan0;
                    byte* outScan0 = (byte*)outData.Scan0;
                    int inBpp = Image.GetPixelFormatSize(inputimg.PixelFormat) / 8;
                    int minGray = int.MaxValue;
                    int maxGray = int.MinValue;
                    for (int y = 0; y < height; y++)
                    {
                        byte* inRow = inScan0 + (y * inData.Stride);
                        for (int x = 0; x < width; x++)
                        {
                            int i = x * inBpp;
                            Color inputColor = Color.FromArgb(inRow[i + 2], inRow[i + 1], inRow[i + 0]);
                            int gray = Math.Max(0, Math.Min(255, ElevationToGray(inputColor)));
                            if (gray < minGray) minGray = gray;
                            if (gray > maxGray) maxGray = gray;
                        }
                        int percent = (int)((y / (float)height) * 50);
                        this.Invoke(new Action(() => this.Text = $"Processing: {percent}%"));
                    }
                    for (int y = 0; y < height; y++)
                    {
                        byte* inRow = inScan0 + (y * inData.Stride);
                        byte* outRow = outScan0 + (y * outData.Stride);
                        for (int x = 0; x < width; x++)
                        {
                            int i = x * inBpp;
                            Color inputColor = Color.FromArgb(inRow[i + 2], inRow[i + 1], inRow[i + 0]);
                            int gray = Math.Max(0, Math.Min(255, ElevationToGray(inputColor)));

                            int stretchedGray = (int)((gray - minGray) / (double)(maxGray - minGray) * scale);
                            outRow[x] = (byte)Math.Max(0, Math.Min(255, stretchedGray));
                        }
                        int percent = 50 + (int)((y / (float)height) * 50);
                        this.Invoke(new Action(() => this.Text = $"Processing: {percent}%"));
                    }
                }

                inputimg.UnlockBits(inData);
                output.UnlockBits(outData);
                this.Invoke(new Action(() => this.Text = "24bit to 8bit"));
            }
            catch { }
            Running = false;
            return SmoothCoastline(output, (byte)trackBar2.Value,trackBar3.Value);
        }

        static int ElevationToGray(Color color)
        {
            RgbToLab(color, out double l0, out double a0, out double b0);

            int bestIndex = 0;
            double bestT = 0;
            double minDistance = double.MaxValue;

            for (int i = 0; i < scale.Length - 1; i++)
            {
                RgbToLab(scale[i], out double l1, out double a1, out double b1);
                RgbToLab(scale[i + 1], out double l2, out double a2, out double b2);

                // Vector math in Lab space
                double[] ab = { l2 - l1, a2 - a1, b2 - b1 };
                double[] ap = { l0 - l1, a0 - a1, b0 - b1 };

                double abDotAb = ab[0] * ab[0] + ab[1] * ab[1] + ab[2] * ab[2];
                double apDotAb = ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2];
                double t = abDotAb == 0 ? 0 : Math.Max(0, Math.Min(1, apDotAb / abDotAb));

                double[] closest = {
        l1 + ab[0] * t,
        a1 + ab[1] * t,
        b1 + ab[2] * t
    };

                // Calculate the distance from labInput to the closest point
                double distance = Math.Sqrt(
                    (l0 - closest[0]) * (l0 - closest[0]) +
                    (a0 - closest[1]) * (a0 - closest[1]) +
                    (b0 - closest[2]) * (b0 - closest[2])
                );

                // Update best match if this one is closer
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bestIndex = i;
                    bestT = t;
                }


                double dist = Math.Pow(l0 - closest[0], 2) +
                              Math.Pow(a0 - closest[1], 2) +
                              Math.Pow(b0 - closest[2], 2);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestIndex = i;
                    bestT = t;
                }
            }

            double scalePos = bestIndex + bestT;
            double gray = 255.0 * (1.0 - (scalePos / (scale.Length - 1)));
            return (int)Math.Round(gray);
        }
        static void RgbToLab(Color color, out double l, out double a, out double bLab)
        {
            // Normalize RGB to [0, 1]
            double r = color.R * 0.00392156862745; // 1 / 255
            double g = color.G * 0.00392156862745;
            double b = color.B * 0.00392156862745;

            // sRGB to Linear RGB (gamma correction)
            r = (r > 0.04045) ? Math.Pow((r + 0.055) * 0.9478672986, 2.4) : r * 0.0773993808;
            g = (g > 0.04045) ? Math.Pow((g + 0.055) * 0.9478672986, 2.4) : g * 0.0773993808;
            b = (b > 0.04045) ? Math.Pow((b + 0.055) * 0.9478672986, 2.4) : b * 0.0773993808;

            // Linear RGB to XYZ (D65)
            double x = r * 0.4124 + g * 0.3576 + b * 0.1805;
            double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
            double z = r * 0.0193 + g * 0.1192 + b * 0.9505;

            // Normalize to D65 white point
            x *= 1.052111060; // 1 / 0.95047
            z *= 0.918417016; // 1 / 1.08883

            // XYZ to Lab using f(t) function
            double fx = (x > 0.008856) ? Math.Pow(x, 1.0 / 3.0) : (7.787 * x + 0.1379310345);
            double fy = (y > 0.008856) ? Math.Pow(y, 1.0 / 3.0) : (7.787 * y + 0.1379310345);
            double fz = (z > 0.008856) ? Math.Pow(z, 1.0 / 3.0) : (7.787 * z + 0.1379310345);

            l = 116.0 * fy - 16.0;
            a = 500.0 * (fx - fy);
            bLab = 200.0 * (fy - fz);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "PNG Image|*.png";
                saveDialog.Title = "Save an Image File";
                saveDialog.DefaultExt = "png";
                saveDialog.AddExtension = true;
                saveDialog.FileName = "output.png";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    ScaleImage(trackBar1.Value, MainImage).Save(saveDialog.FileName, ImageFormat.Png);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            pictureBox1.BackgroundImage = ScaleImage(trackBar1.Value, Preview);
        }
    }
}