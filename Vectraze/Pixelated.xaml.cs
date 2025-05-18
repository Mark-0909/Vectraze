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
            RenderPixelatedImage(bitmapImage);
        }

        private void RenderPixelatedImage(BitmapImage image)
        {
            int pixelWidth = 32; // or user-selected size
            int pixelHeight = 32;

            // Resize the image down to small resolution
            TransformedBitmap resized = new TransformedBitmap(image, new ScaleTransform(
                pixelWidth / (double)image.PixelWidth,
                pixelHeight / (double)image.PixelHeight));

            // Copy pixel data
            int stride = pixelWidth * 4;
            byte[] pixels = new byte[stride * pixelHeight];
            resized.CopyPixels(pixels, stride, 0);

            int cellSize = 16; // Size of each drawn "pixel"
            PixelCanvas.Width = pixelWidth * cellSize;
            PixelCanvas.Height = pixelHeight * cellSize;

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

                    Canvas.SetLeft(rect, x * cellSize);
                    Canvas.SetTop(rect, y * cellSize);

                    PixelCanvas.Children.Add(rect);
                }
            }
        }
    }
}