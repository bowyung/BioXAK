using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BioSAK
{
    public partial class ColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; }

        // 7 rows × 6 columns: each row goes from white to target color
        private static readonly Color[] TargetColors =
        {
            Color.FromRgb(0,   0,   0),     // Grayscale: white → black
            Color.FromRgb(255, 0,   0),     // Red
            Color.FromRgb(255, 165, 0),     // Orange
            Color.FromRgb(255, 255, 0),     // Yellow
            Color.FromRgb(0,   128, 0),     // Green
            Color.FromRgb(0,   0,   139),   // Dark Blue / Navy
            Color.FromRgb(128, 0,   128),   // Purple
        };

        private const int Columns = 6;
        private const double CellWidth = 55;
        private const double CellHeight = 42;

        public ColorPickerDialog(Color initialColor)
        {
            InitializeComponent();

            SelectedColor = initialColor;
            RedBox.Text = initialColor.R.ToString();
            GreenBox.Text = initialColor.G.ToString();
            BlueBox.Text = initialColor.B.ToString();
            PreviewBox.Background = new SolidColorBrush(initialColor);

            BuildPalette();
        }

        private void BuildPalette()
        {
            ColorGrid.Children.Clear();

            foreach (var target in TargetColors)
            {
                for (int col = 0; col < Columns; col++)
                {
                    // Interpolate from white (col=0) to target (col=Columns-1)
                    double t = (double)col / (Columns - 1);
                    Color color = Lerp(Colors.White, target, t);

                    var rect = new Rectangle
                    {
                        Width = CellWidth,
                        Height = CellHeight,
                        Fill = new SolidColorBrush(color),
                        Cursor = Cursors.Hand,
                        Tag = color,
                        // No margin — continuous blocks
                        Margin = new Thickness(0)
                    };

                    rect.MouseLeftButtonDown += ColorRect_Click;

                    // Highlight on hover
                    rect.MouseEnter += (s, e) =>
                    {
                        if (s is Rectangle r) r.StrokeThickness = 2;
                    };
                    rect.MouseLeave += (s, e) =>
                    {
                        if (s is Rectangle r) r.StrokeThickness = 0;
                    };
                    rect.Stroke = Brushes.White;
                    rect.StrokeThickness = 0;

                    ColorGrid.Children.Add(rect);
                }
            }
        }

        /// <summary>
        /// Linear interpolation between two colors.
        /// t=0 returns 'from', t=1 returns 'to'.
        /// </summary>
        private static Color Lerp(Color from, Color to, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t)
            );
        }

        private void ColorRect_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle rect && rect.Tag is Color color)
            {
                SelectedColor = color;
                RedBox.Text = color.R.ToString();
                GreenBox.Text = color.G.ToString();
                BlueBox.Text = color.B.ToString();
                PreviewBox.Background = new SolidColorBrush(color);
            }
        }

        private void RGB_Changed(object sender, TextChangedEventArgs e)
        {
            if (RedBox == null || GreenBox == null || BlueBox == null || PreviewBox == null) return;

            if (byte.TryParse(RedBox.Text, out byte r) &&
                byte.TryParse(GreenBox.Text, out byte g) &&
                byte.TryParse(BlueBox.Text, out byte b))
            {
                SelectedColor = Color.FromRgb(r, g, b);
                PreviewBox.Background = new SolidColorBrush(SelectedColor);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}