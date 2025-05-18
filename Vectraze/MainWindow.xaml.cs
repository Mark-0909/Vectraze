using Microsoft.Win32;
using System.Text;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void AddImageBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Open Image File";
            openFileDialog.Filter = "Image Files|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                // Store image
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                // Image preview
                ImagePreview.Source = bitmap;
            }
        }

        private void DropBorder_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropBorder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files.Length > 0)
                {
                    string filePath = files[0];

                    string extension = System.IO.Path.GetExtension(filePath).ToLower();
                    string[] allowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

                    if (allowedExtensions.Contains(extension))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(filePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        ImagePreview.Source = bitmap;
                    }
                    else
                    {
                        MessageBox.Show("Unsupported file format. Please drop an image.");
                    }
                }
            }
        }

        private void RasterizedBtn_Click(object sender, RoutedEventArgs e)
        {
            Pixelated PixelUsercontrol = new Pixelated((BitmapImage)ImagePreview.Source);
            this.Content = PixelUsercontrol;
        }
    }
}