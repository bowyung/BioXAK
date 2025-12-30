using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BioSAK
{
    public partial class LineSettingsDialog : Window
    {
        public double LineThickness { get; private set; } = 2;
        public Color LineColor { get; private set; } = Colors.Black;
        public string ArrowDirection { get; private set; } = "Right";

        private string lineType;
        private bool isInitialized = false;

        public LineSettingsDialog(LineShapeInfo info)
        {
            InitializeComponent();

            lineType = info.Type;
            LineThickness = info.Thickness;
            LineColor = (info.Stroke as SolidColorBrush)?.Color ?? Colors.Black;

            this.Loaded += (s, e) =>
            {
                ThicknessSlider.Value = LineThickness;
                ThicknessLabel.Text = LineThickness.ToString("F1");
                ColorPreview.Background = new SolidColorBrush(LineColor);

                // Show arrow direction options only for arrows
                if (lineType == "Arrow")
                {
                    ArrowDirectionGroup.Visibility = Visibility.Visible;

                    // Set current direction
                    switch (info.ArrowDirection)
                    {
                        case "Left":
                            ArrowLeftRadio.IsChecked = true;
                            break;
                        case "Both":
                            ArrowBothRadio.IsChecked = true;
                            break;
                        default:
                            ArrowRightRadio.IsChecked = true;
                            break;
                    }
                }

                isInitialized = true;
                UpdatePreview();
            };

            // Event handlers for preview update
            ArrowRightRadio.Checked += (s, e) => UpdatePreview();
            ArrowLeftRadio.Checked += (s, e) => UpdatePreview();
            ArrowBothRadio.Checked += (s, e) => UpdatePreview();
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isInitialized || ThicknessLabel == null) return;

            LineThickness = ThicknessSlider.Value;
            ThicknessLabel.Text = LineThickness.ToString("F1");
            UpdatePreview();
        }

        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new ColorPickerDialog(LineColor);
            colorDialog.Owner = this;
            if (colorDialog.ShowDialog() == true)
            {
                LineColor = colorDialog.SelectedColor;
                ColorPreview.Background = new SolidColorBrush(LineColor);
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (!isInitialized || PreviewCanvas == null) return;

            PreviewCanvas.Children.Clear();

            var brush = new SolidColorBrush(LineColor);
            double canvasWidth = PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : 280;
            double midY = 25;
            double padding = 20;

            switch (lineType)
            {
                case "Line":
                    var line = new Line
                    {
                        X1 = padding,
                        Y1 = midY,
                        X2 = canvasWidth - padding,
                        Y2 = midY,
                        Stroke = brush,
                        StrokeThickness = LineThickness
                    };
                    PreviewCanvas.Children.Add(line);
                    break;

                case "Arrow":
                    DrawArrowPreview(brush, canvasWidth, midY, padding);
                    break;

                case "UShape":
                    DrawUShapePreview(brush, canvasWidth, padding);
                    break;

                case "HShape":
                    DrawHShapePreview(brush, canvasWidth, midY, padding);
                    break;
            }
        }

        private void DrawArrowPreview(Brush brush, double canvasWidth, double midY, double padding)
        {
            string direction = "Right";
            if (ArrowLeftRadio.IsChecked == true) direction = "Left";
            else if (ArrowBothRadio.IsChecked == true) direction = "Both";

            double lineStart = padding;
            double lineEnd = canvasWidth - padding;
            double arrowSize = 8;

            if (direction == "Right" || direction == "Both")
            {
                // Right arrow
                var line = new Line
                {
                    X1 = lineStart + (direction == "Both" ? arrowSize : 0),
                    Y1 = midY,
                    X2 = lineEnd - arrowSize,
                    Y2 = midY,
                    Stroke = brush,
                    StrokeThickness = LineThickness
                };
                PreviewCanvas.Children.Add(line);

                var rightHead = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(lineEnd - arrowSize, midY - arrowSize),
                        new Point(lineEnd, midY),
                        new Point(lineEnd - arrowSize, midY + arrowSize)
                    },
                    Fill = brush
                };
                PreviewCanvas.Children.Add(rightHead);
            }

            if (direction == "Left" || direction == "Both")
            {
                if (direction == "Left")
                {
                    var line = new Line
                    {
                        X1 = lineStart + arrowSize,
                        Y1 = midY,
                        X2 = lineEnd,
                        Y2 = midY,
                        Stroke = brush,
                        StrokeThickness = LineThickness
                    };
                    PreviewCanvas.Children.Add(line);
                }

                var leftHead = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(lineStart + arrowSize, midY - arrowSize),
                        new Point(lineStart, midY),
                        new Point(lineStart + arrowSize, midY + arrowSize)
                    },
                    Fill = brush
                };
                PreviewCanvas.Children.Add(leftHead);
            }
        }

        private void DrawUShapePreview(Brush brush, double canvasWidth, double padding)
        {
            double height = 35;

            var leftLine = new Line
            {
                X1 = padding,
                Y1 = 5,
                X2 = padding,
                Y2 = height,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(leftLine);

            var topLine = new Line
            {
                X1 = padding,
                Y1 = 5,
                X2 = canvasWidth - padding,
                Y2 = 5,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(topLine);

            var rightLine = new Line
            {
                X1 = canvasWidth - padding,
                Y1 = 5,
                X2 = canvasWidth - padding,
                Y2 = height,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(rightLine);
        }

        private void DrawHShapePreview(Brush brush, double canvasWidth, double midY, double padding)
        {
            double halfHeight = 15;

            var leftLine = new Line
            {
                X1 = padding,
                Y1 = midY - halfHeight,
                X2 = padding,
                Y2 = midY + halfHeight,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(leftLine);

            var midLine = new Line
            {
                X1 = padding,
                Y1 = midY,
                X2 = canvasWidth - padding,
                Y2 = midY,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(midLine);

            var rightLine = new Line
            {
                X1 = canvasWidth - padding,
                Y1 = midY - halfHeight,
                X2 = canvasWidth - padding,
                Y2 = midY + halfHeight,
                Stroke = brush,
                StrokeThickness = LineThickness
            };
            PreviewCanvas.Children.Add(rightLine);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            LineThickness = ThicknessSlider.Value;

            if (ArrowLeftRadio.IsChecked == true)
                ArrowDirection = "Left";
            else if (ArrowBothRadio.IsChecked == true)
                ArrowDirection = "Both";
            else
                ArrowDirection = "Right";

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
