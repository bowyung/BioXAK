using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BioSAK
{
    public partial class SingleBarStyleDialog : Window
    {
        public BarStyle UpdatedStyle { get; private set; } = new BarStyle();
        private Color currentFillColor;
        private Color currentBorderColor;

        public SingleBarStyleDialog(BarStyle currentStyle, string seriesName, string xLabel)
        {
            InitializeComponent();

            TitleText.Text = $"Edit Bar: {seriesName} at {xLabel}";

            currentFillColor = currentStyle.FillColor;
            currentBorderColor = currentStyle.BorderColor;

            FillColorPreview.Background = new SolidColorBrush(currentFillColor);
            BorderColorPreview.Background = new SolidColorBrush(currentBorderColor);
            BorderThicknessSlider.Value = currentStyle.BorderThickness;
            BorderThicknessLabel.Text = currentStyle.BorderThickness.ToString("F1");

            // Set pattern combo
            foreach (ComboBoxItem item in PatternCombo.Items)
            {
                if (item.Tag?.ToString() == currentStyle.Pattern.ToString())
                {
                    PatternCombo.SelectedItem = item;
                    break;
                }
            }
            if (PatternCombo.SelectedItem == null && PatternCombo.Items.Count > 0)
                PatternCombo.SelectedIndex = 0;

            // Set error direction combo
            foreach (ComboBoxItem item in ErrorDirectionCombo.Items)
            {
                if (item.Tag?.ToString() == currentStyle.ErrorDirection.ToString())
                {
                    ErrorDirectionCombo.SelectedItem = item;
                    break;
                }
            }
            if (ErrorDirectionCombo.SelectedItem == null && ErrorDirectionCombo.Items.Count > 0)
                ErrorDirectionCombo.SelectedIndex = 0;

            BorderThicknessSlider.ValueChanged += (s, e) => { BorderThicknessLabel.Text = BorderThicknessSlider.Value.ToString("F1"); UpdatePreview(); };
            PatternCombo.SelectionChanged += (s, e) => UpdatePreview();

            UpdatePreview();
        }

        private void ChooseFillColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorPickerDialog(currentFillColor);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                currentFillColor = dialog.SelectedColor;
                FillColorPreview.Background = new SolidColorBrush(currentFillColor);
                UpdatePreview();
            }
        }

        private void ChooseBorderColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorPickerDialog(currentBorderColor);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                currentBorderColor = dialog.SelectedColor;
                BorderColorPreview.Background = new SolidColorBrush(currentBorderColor);
                UpdatePreview();
            }
        }

        private FillPattern GetSelectedPattern()
        {
            if (PatternCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return Enum.TryParse<FillPattern>(tag, out var pattern) ? pattern : FillPattern.Solid;
            }
            return FillPattern.Solid;
        }

        private ErrorBarDirection GetSelectedErrorDirection()
        {
            if (ErrorDirectionCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return Enum.TryParse<ErrorBarDirection>(tag, out var dir) ? dir : ErrorBarDirection.Both;
            }
            return ErrorBarDirection.Both;
        }

        private void UpdatePreview()
        {
            var style = new BarStyle
            {
                FillColor = currentFillColor,
                BorderColor = currentBorderColor,
                BorderThickness = BorderThicknessSlider.Value,
                Pattern = GetSelectedPattern()
            };

            PreviewBorder.Background = CreatePatternBrush(style);
            PreviewBorder.BorderBrush = new SolidColorBrush(style.BorderColor);
            PreviewBorder.BorderThickness = new Thickness(style.BorderThickness);
        }

        private Brush CreatePatternBrush(BarStyle style)
        {
            var color = style.FillColor;

            switch (style.Pattern)
            {
                case FillPattern.Solid:
                    return new SolidColorBrush(color);
                case FillPattern.HorizontalLines:
                    return CreateLineBrush(color, 0);
                case FillPattern.VerticalLines:
                    return CreateLineBrush(color, 90);
                case FillPattern.DiagonalUp:
                    return CreateLineBrush(color, 45);
                case FillPattern.DiagonalDown:
                    return CreateLineBrush(color, -45);
                case FillPattern.CrossHatch:
                    return CreateCrossHatchBrush(color);
                case FillPattern.Dots:
                    return CreateDotsBrush(color);
                default:
                    return new SolidColorBrush(color);
            }
        }

        private Brush CreateLineBrush(Color color, double angle)
        {
            var brush = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute
            };

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
                null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(
                null, new Pen(new SolidColorBrush(color), 2),
                new LineGeometry(new Point(0, 4), new Point(8, 4))));

            brush.Drawing = group;
            brush.Transform = new RotateTransform(angle, 4, 4);
            return brush;
        }

        private Brush CreateCrossHatchBrush(Color color)
        {
            var brush = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute
            };

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
                null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(color), 1),
                new LineGeometry(new Point(0, 0), new Point(8, 8))));
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(color), 1),
                new LineGeometry(new Point(8, 0), new Point(0, 8))));

            brush.Drawing = group;
            return brush;
        }

        private Brush CreateDotsBrush(Color color)
        {
            var brush = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute
            };

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
                null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null,
                new EllipseGeometry(new Point(4, 4), 2, 2)));

            brush.Drawing = group;
            return brush;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            UpdatedStyle = new BarStyle
            {
                FillColor = currentFillColor,
                BorderColor = currentBorderColor,
                BorderThickness = BorderThicknessSlider.Value,
                Pattern = GetSelectedPattern(),
                ErrorDirection = GetSelectedErrorDirection()
            };

            DialogResult = true;
            Close();
        }
    }
}
