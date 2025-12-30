using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BioSAK
{
    public partial class ColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; }

        private readonly Color[] paletteColors = new Color[]
        {
            // Row 1 - Reds
            Color.FromRgb(244, 67, 54),
            Color.FromRgb(233, 30, 99),
            Color.FromRgb(156, 39, 176),
            Color.FromRgb(103, 58, 183),
            Color.FromRgb(63, 81, 181),
            Color.FromRgb(33, 150, 243),
            Color.FromRgb(3, 169, 244),
            Color.FromRgb(0, 188, 212),

            // Row 2 - Greens/Yellows
            Color.FromRgb(0, 150, 136),
            Color.FromRgb(76, 175, 80),
            Color.FromRgb(139, 195, 74),
            Color.FromRgb(205, 220, 57),
            Color.FromRgb(255, 235, 59),
            Color.FromRgb(255, 193, 7),
            Color.FromRgb(255, 152, 0),
            Color.FromRgb(255, 87, 34),

            // Row 3 - Browns/Grays
            Color.FromRgb(121, 85, 72),
            Color.FromRgb(158, 158, 158),
            Color.FromRgb(96, 125, 139),
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(66, 66, 66),
            Color.FromRgb(117, 117, 117),
            Color.FromRgb(189, 189, 189),
            Color.FromRgb(255, 255, 255),

            // Row 4 - Dark variants
            Color.FromRgb(183, 28, 28),
            Color.FromRgb(136, 14, 79),
            Color.FromRgb(74, 20, 140),
            Color.FromRgb(49, 27, 146),
            Color.FromRgb(26, 35, 126),
            Color.FromRgb(13, 71, 161),
            Color.FromRgb(1, 87, 155),
            Color.FromRgb(0, 96, 100),

            // Row 5 - Light variants
            Color.FromRgb(255, 205, 210),
            Color.FromRgb(248, 187, 208),
            Color.FromRgb(225, 190, 231),
            Color.FromRgb(209, 196, 233),
            Color.FromRgb(197, 202, 233),
            Color.FromRgb(187, 222, 251),
            Color.FromRgb(179, 229, 252),
            Color.FromRgb(178, 235, 242),
        };

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
            foreach (var color in paletteColors)
            {
                var rect = new Rectangle
                {
                    Width = 32,
                    Height = 32,
                    Fill = new SolidColorBrush(color),
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand,
                    Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    StrokeThickness = 1,
                    RadiusX = 3,
                    RadiusY = 3,
                    Tag = color
                };

                rect.MouseLeftButtonDown += ColorRect_Click;
                ColorPalette.Children.Add(rect);
            }
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
