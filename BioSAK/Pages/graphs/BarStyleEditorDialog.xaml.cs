using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BioSAK
{
    public partial class BarStyleEditorDialog : Window
    {
        private List<ChartDataSeries> dataSeries;
        private string chartType;
        public Dictionary<(int, int), BarStyle> UpdatedStyles { get; private set; }

        private Color currentFillColor = Colors.DodgerBlue;
        private Color currentBorderColor = Colors.Black;

        public BarStyleEditorDialog(List<ChartDataSeries> data, Dictionary<(int, int), BarStyle> styles, string type)
        {
            InitializeComponent();
            dataSeries = data;
            chartType = type;
            UpdatedStyles = new Dictionary<(int, int), BarStyle>(styles);

            InitializeControls();
            BorderThicknessSlider.ValueChanged += (s, e) => { BorderThicknessLabel.Text = BorderThicknessSlider.Value.ToString("F1"); UpdatePreview(); };
            PatternCombo.SelectionChanged += (s, e) => UpdatePreview();

            UpdatePreview();
        }

        private void InitializeControls()
        {
            // Populate series combo
            for (int i = 0; i < dataSeries.Count; i++)
                SeriesCombo.Items.Add($"{i}: {dataSeries[i].Name}");
            if (SeriesCombo.Items.Count > 0) SeriesCombo.SelectedIndex = 0;

            // Populate X index combo - use XLabels if available
            int maxX = dataSeries.Max(s => s.XValues.Count);
            for (int i = 0; i < maxX; i++)
            {
                string label;
                if (dataSeries.Count > 0 && i < dataSeries[0].XLabels.Count &&
                    !string.IsNullOrEmpty(dataSeries[0].XLabels[i]))
                {
                    label = $"{i}: {dataSeries[0].XLabels[i]}";
                }
                else if (dataSeries.Count > 0 && i < dataSeries[0].XValues.Count)
                {
                    label = $"{i}: X={dataSeries[0].XValues[i]:G4}";
                }
                else
                {
                    label = $"{i}";
                }
                XIndexCombo.Items.Add(label);
            }
            if (XIndexCombo.Items.Count > 0) XIndexCombo.SelectedIndex = 0;
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

        private void QuickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorStr)
            {
                try
                {
                    currentFillColor = (Color)ColorConverter.ConvertFromString(colorStr);
                    FillColorPreview.Background = new SolidColorBrush(currentFillColor);
                    UpdatePreview();
                }
                catch { }
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
            group.Children.Add(new GeometryDrawing(
                null, new Pen(new SolidColorBrush(color), 1),
                new LineGeometry(new Point(0, 0), new Point(8, 8))));
            group.Children.Add(new GeometryDrawing(
                null, new Pen(new SolidColorBrush(color), 1),
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
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(color), null,
                new EllipseGeometry(new Point(4, 4), 2, 2)));

            brush.Drawing = group;
            return brush;
        }

        private BarStyle CreateCurrentStyle()
        {
            return new BarStyle
            {
                FillColor = currentFillColor,
                BorderColor = currentBorderColor,
                BorderThickness = BorderThicknessSlider.Value,
                Pattern = GetSelectedPattern()
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplyStyle();
        }

        private void ApplyStyle()
        {
            var style = CreateCurrentStyle();

            if (ModeAll.IsChecked == true)
            {
                // Apply to all bars
                for (int s = 0; s < dataSeries.Count; s++)
                {
                    for (int x = 0; x < dataSeries[s].XValues.Count; x++)
                    {
                        UpdatedStyles[(s, x)] = CloneStyle(style);
                    }
                }
            }
            else if (ModeSeries.IsChecked == true)
            {
                // Apply to same series
                int selectedSeries = SeriesCombo.SelectedIndex;
                if (selectedSeries >= 0 && selectedSeries < dataSeries.Count)
                {
                    for (int x = 0; x < dataSeries[selectedSeries].XValues.Count; x++)
                    {
                        UpdatedStyles[(selectedSeries, x)] = CloneStyle(style);
                    }
                }
            }
            else if (ModeRow.IsChecked == true)
            {
                // Apply to same X (row)
                int selectedX = XIndexCombo.SelectedIndex;
                for (int s = 0; s < dataSeries.Count; s++)
                {
                    if (selectedX < dataSeries[s].XValues.Count)
                    {
                        UpdatedStyles[(s, selectedX)] = CloneStyle(style);
                    }
                }
            }
            else if (ModeIndividual.IsChecked == true)
            {
                // Apply to individual bar
                int selectedSeries = SeriesCombo.SelectedIndex;
                int selectedX = XIndexCombo.SelectedIndex;
                if (selectedSeries >= 0 && selectedX >= 0)
                {
                    UpdatedStyles[(selectedSeries, selectedX)] = CloneStyle(style);
                }
            }
        }

        private BarStyle CloneStyle(BarStyle style)
        {
            return new BarStyle
            {
                FillColor = style.FillColor,
                BorderColor = style.BorderColor,
                BorderThickness = style.BorderThickness,
                Pattern = style.Pattern
            };
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ApplyStyle();
            DialogResult = true;
            Close();
        }
    }
}