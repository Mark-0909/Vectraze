using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;

namespace Vectraze
{
    /// <summary>
    /// Interaction logic for Pixelated.xaml
    /// </summary>
    public partial class Pixelated : UserControl
    {
        public Pixelated(BitmapImage bitmapImage)
        {
            InitializeComponent();
            PixelCanvas.Loaded += (s, e) =>
            {
                RenderPixelatedImage(bitmapImage);
            };
        }

        private void RenderPixelatedImage(BitmapImage image)
        {
            int targetSize = 32; // The size of the longer edge in the pixel grid
            int pixelWidth, pixelHeight;

            // Maintain aspect ratio based on original image
            double aspectRatio = image.PixelWidth / (double)image.PixelHeight;
            if (aspectRatio >= 1.0)
            {
                // Landscape or square
                pixelWidth = targetSize;
                pixelHeight = (int)(targetSize / aspectRatio);
            }
            else
            {
                // Portrait
                pixelHeight = targetSize;
                pixelWidth = (int)(targetSize * aspectRatio);
            }

            // Resize image to lower resolution (pixelated size)
            TransformedBitmap resized = new TransformedBitmap(image, new ScaleTransform(
                pixelWidth / (double)image.PixelWidth,
                pixelHeight / (double)image.PixelHeight));

            // Copy pixel data
            int stride = pixelWidth * 4;
            byte[] pixels = new byte[stride * pixelHeight];
            resized.CopyPixels(pixels, stride, 0);

            // Get canvas size
            double canvasWidth = PixelCanvas.ActualWidth;
            double canvasHeight = PixelCanvas.ActualHeight;

            if (canvasWidth == 0 || canvasHeight == 0)
            {
                canvasWidth = 512;
                canvasHeight = 512;
            }

            // Calculate proportional cell size
            double cellSizeX = canvasWidth / pixelWidth;
            double cellSizeY = canvasHeight / pixelHeight;
            double cellSize = Math.Min(cellSizeX, cellSizeY); // uniform cells

            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;

            double offsetX = (canvasWidth - totalImageWidth) / 2;
            double offsetY = (canvasHeight - totalImageHeight) / 2;

            PixelCanvas.Children.Clear();

            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < pixelWidth; x++)
                {
                    int index = (y * stride) + (x * 4);
                    byte b = pixels[index];
                    byte g = pixels[index + 1];
                    byte r = pixels[index + 2];
                    byte a = pixels[index + 3];

                    Rectangle rect = new Rectangle
                    {
                        Width = cellSize,
                        Height = cellSize,
                        Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                        Stroke = Brushes.Gray,
                        StrokeThickness = 0.25
                    };

                    Canvas.SetLeft(rect, offsetX + x * cellSize);
                    Canvas.SetTop(rect, offsetY + y * cellSize);
                    PixelCanvas.Children.Add(rect);
                }
            }
        }



    }
}