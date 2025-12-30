using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace BioSAK
{
    public partial class BarChartWindow : Window
    {
        private List<ChartDataSeries> dataSeries;
        private string chartType; // "Column" or "MultiGroup"
        private string errorType;
        private string xAxisTitle;
        private string yAxisTitle = "Y";
        private string chartTitle = "";

        private double marginLeft = 70;
        private double marginRight = 30;
        private double marginTop = 50;
        private double marginBottom = 60;

        // Chart size control
        private bool useFixedSize = false;
        private double fixedChartWidth = 800;
        private double fixedChartHeight = 500;

        // Bar appearance settings: [seriesIndex, xIndex] -> BarStyle
        private Dictionary<(int, int), BarStyle> barStyles = new Dictionary<(int, int), BarStyle>();
        
        // Bar rectangles for click detection
        private List<BarInfo> barInfos = new List<BarInfo>();

        // Error bar and display settings
        private double errorBarThickness = 1.5;
        private bool showDataPoints = false;
        private double dataPointSize = 5;
        private ErrorBarDirection defaultErrorDirection = ErrorBarDirection.Both;
        
        // Grid line settings
        private bool showGridLines = false;
        private string gridLineStyle = "Solid";
        private Color gridLineColor = Color.FromRgb(224, 224, 224);
        
        // Scale settings
        private bool autoScale = true;
        private bool logScale = false;
        private double yMin = 0;
        private double yMax = 100;
        private double mainScaleInterval = 0; // 0 = auto
        private bool showSubScale = false;
        private int subScaleDivisions = 5;
        
        // Axis break settings
        private bool enableAxisBreak = false;
        private double axisBreakStart = 0;
        private double axisBreakEnd = 0;

        // Floating legend
        private Border? legendBorder = null;
        private bool isLegendDragging = false;
        private Point legendDragStart;
        private Point legendStartPosition;

        // Annotation system
        private List<FrameworkElement> annotations = new List<FrameworkElement>();
        private List<FrameworkElement> selectedAnnotations = new List<FrameworkElement>();
        private FrameworkElement? selectedAnnotation = null;
        private bool isDragging = false;
        private bool isSelecting = false;
        private Point dragStart;
        private Point elementStart;
        private Point selectionStart;

        private DateTime lastClickTime = DateTime.MinValue;
        private FrameworkElement? lastClickedElement = null;

        private readonly Color[] defaultColors = new Color[]
        {
            Color.FromRgb(66, 133, 244), Color.FromRgb(234, 67, 53),
            Color.FromRgb(52, 168, 83), Color.FromRgb(251, 188, 5),
            Color.FromRgb(154, 71, 182), Color.FromRgb(255, 112, 67),
            Color.FromRgb(0, 172, 193), Color.FromRgb(124, 77, 255),
        };

        public BarChartWindow(List<ChartDataSeries> data, string type, string error, string xTitle, 
            string errorDirection = "Both")
        {
            InitializeComponent();
            dataSeries = data;
            chartType = type;
            errorType = error;
            xAxisTitle = xTitle;
            chartTitle = type == "Column" ? "Column Chart" : "Multi Factors Chart";
            
            // Set default error direction
            defaultErrorDirection = errorDirection switch
            {
                "Up" => ErrorBarDirection.Up,
                "Down" => ErrorBarDirection.Down,
                _ => ErrorBarDirection.Both
            };

            InitializeBarStyles();
            this.Loaded += (s, e) => 
            { 
                // Set initial chart size from text boxes or fit to window
                if (double.TryParse(ChartWidthBox.Text, out double w) && 
                    double.TryParse(ChartHeightBox.Text, out double h))
                {
                    ChartCanvas.Width = w;
                    ChartCanvas.Height = h;
                }
                DrawChart(); 
                this.Focus(); 
            };
        }

        private void InitializeBarStyles()
        {
            for (int s = 0; s < dataSeries.Count; s++)
            {
                for (int x = 0; x < dataSeries[s].XValues.Count; x++)
                {
                    // For Column chart: color by series (s)
                    // For MultiGroup chart: color by X index (x)
                    int colorIndex = (chartType == "Column") ? s : x;
                    
                    barStyles[(s, x)] = new BarStyle
                    {
                        FillColor = defaultColors[colorIndex % defaultColors.Length],
                        BorderColor = Colors.Black,
                        BorderThickness = 1,
                        Pattern = FillPattern.Solid,
                        ErrorDirection = defaultErrorDirection
                    };
                }
            }
        }

        #region Keyboard and Mouse Events

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedAnnotations();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.C: CopyChartToClipboard(); e.Handled = true; break;
                    case Key.A: SelectAllAnnotations(); e.Handled = true; break;
                }
            }
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
            
            // Update size textboxes if not in fixed size mode
            if (!useFixedSize && ChartWidthBox != null && ChartHeightBox != null && 
                ChartCanvas.ActualWidth > 0 && ChartCanvas.ActualHeight > 0)
            {
                ChartWidthBox.Text = ((int)ChartCanvas.ActualWidth).ToString();
                ChartHeightBox.Text = ((int)ChartCanvas.ActualHeight).ToString();
            }
        }

        private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ChartCanvas);
            var now = DateTime.Now;

            // Check if clicked on annotation
            var hitElement = FindAnnotationAt(pos);
            if (hitElement != null)
            {
                bool isDoubleClick = (now - lastClickTime).TotalMilliseconds < 300 && lastClickedElement == hitElement;
                if (isDoubleClick)
                {
                    EditAnnotation(hitElement);
                }
                else
                {
                    ClearSelection();
                    SelectAnnotation(hitElement);
                    isDragging = true;
                    dragStart = pos;
                    elementStart = new Point(Canvas.GetLeft(hitElement), Canvas.GetTop(hitElement));
                    ChartCanvas.CaptureMouse();
                }
                lastClickTime = now;
                lastClickedElement = hitElement;
                return;
            }

            // Check if clicked on bar
            var clickedBar = barInfos.FirstOrDefault(b => b.Bounds.Contains(pos));
            if (clickedBar != null)
            {
                bool isDoubleClick = (now - lastClickTime).TotalMilliseconds < 300;
                if (isDoubleClick)
                {
                    EditSingleBar(clickedBar.SeriesIndex, clickedBar.XIndex);
                }
                lastClickTime = now;
                lastClickedElement = null;
                return;
            }

            // Start selection rectangle or open settings
            ClearSelection();
            bool isDoubleClickEmpty = (now - lastClickTime).TotalMilliseconds < 300 && lastClickedElement == null;
            if (isDoubleClickEmpty)
            {
                OpenSettingsDialog();
            }
            else
            {
                isSelecting = true;
                selectionStart = pos;
                SelectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionRect, pos.X);
                Canvas.SetTop(SelectionRect, pos.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
                ChartCanvas.CaptureMouse();
            }
            lastClickTime = now;
            lastClickedElement = null;
        }

        private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(ChartCanvas);

            if (isSelecting)
            {
                double x = Math.Min(pos.X, selectionStart.X);
                double y = Math.Min(pos.Y, selectionStart.Y);
                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = Math.Abs(pos.X - selectionStart.X);
                SelectionRect.Height = Math.Abs(pos.Y - selectionStart.Y);
                return;
            }

            if (isDragging && selectedAnnotation != null)
            {
                double dx = pos.X - dragStart.X;
                double dy = pos.Y - dragStart.Y;
                Canvas.SetLeft(selectedAnnotation, elementStart.X + dx);
                Canvas.SetTop(selectedAnnotation, elementStart.Y + dy);
            }
        }

        private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelecting)
            {
                double x1 = Canvas.GetLeft(SelectionRect);
                double y1 = Canvas.GetTop(SelectionRect);
                double x2 = x1 + SelectionRect.Width;
                double y2 = y1 + SelectionRect.Height;

                foreach (var ann in annotations)
                {
                    double ax = Canvas.GetLeft(ann);
                    double ay = Canvas.GetTop(ann);
                    if (ax >= x1 && ax <= x2 && ay >= y1 && ay <= y2)
                    {
                        selectedAnnotations.Add(ann);
                        HighlightAnnotation(ann, true);
                    }
                }
                SelectionRect.Visibility = Visibility.Collapsed;
                isSelecting = false;
            }

            isDragging = false;
            ChartCanvas.ReleaseMouseCapture();
        }

        #endregion

        #region Bar Editing

        private void EditBars_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new BarStyleEditorDialog(dataSeries, barStyles, chartType);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                barStyles = dialog.UpdatedStyles;
                DrawChart();
            }
        }

        private void EditSingleBar(int seriesIndex, int xIndex)
        {
            var key = (seriesIndex, xIndex);
            var currentStyle = barStyles.ContainsKey(key) ? barStyles[key] : new BarStyle();

            // Get X label
            string xLabel = (dataSeries[seriesIndex].XLabels.Count > xIndex && 
                            !string.IsNullOrEmpty(dataSeries[seriesIndex].XLabels[xIndex]))
                ? dataSeries[seriesIndex].XLabels[xIndex]
                : $"X={dataSeries[seriesIndex].XValues[xIndex]:G4}";

            var dialog = new SingleBarStyleDialog(currentStyle, dataSeries[seriesIndex].Name, xLabel);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                var newStyle = dialog.UpdatedStyle;
                
                // Link colors based on chart type:
                // Column: Same series index (s) -> update all X positions with same color
                // MultiGroup: Same X index -> update all series with same color
                
                if (chartType == "Column")
                {
                    // Update all bars with same series index (same Y column)
                    int numGroups = dataSeries.Max(s => s.XValues.Count);
                    for (int x = 0; x < numGroups; x++)
                    {
                        var updateKey = (seriesIndex, x);
                        if (!barStyles.ContainsKey(updateKey))
                            barStyles[updateKey] = new BarStyle();
                        barStyles[updateKey].FillColor = newStyle.FillColor;
                        barStyles[updateKey].BorderColor = newStyle.BorderColor;
                        barStyles[updateKey].BorderThickness = newStyle.BorderThickness;
                        barStyles[updateKey].Pattern = newStyle.Pattern;
                    }
                }
                else
                {
                    // MultiGroup: Update all bars with same X index (same position within groups)
                    for (int s = 0; s < dataSeries.Count; s++)
                    {
                        var updateKey = (s, xIndex);
                        if (!barStyles.ContainsKey(updateKey))
                            barStyles[updateKey] = new BarStyle();
                        barStyles[updateKey].FillColor = newStyle.FillColor;
                        barStyles[updateKey].BorderColor = newStyle.BorderColor;
                        barStyles[updateKey].BorderThickness = newStyle.BorderThickness;
                        barStyles[updateKey].Pattern = newStyle.Pattern;
                    }
                }
                
                DrawChart();
            }
        }

        #endregion

        #region Chart Drawing

        private void DrawChart()
        {
            var savedAnnotations = annotations.ToList();
            var positions = savedAnnotations.Select(a => new Point(Canvas.GetLeft(a), Canvas.GetTop(a))).ToList();

            // Save legend position if exists
            Point? savedLegendPos = null;
            if (legendBorder != null && ChartCanvas.Children.Contains(legendBorder))
            {
                savedLegendPos = new Point(Canvas.GetLeft(legendBorder), Canvas.GetTop(legendBorder));
            }

            ChartCanvas.Children.Clear();
            barInfos.Clear();

            if (dataSeries == null || dataSeries.Count == 0) return;

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;
            if (width < 100 || height < 100) return;

            double plotWidth = width - marginLeft - marginRight;
            double plotHeight = height - marginTop - marginBottom;

            // Calculate Y range
            double calculatedYMax = 0;
            double calculatedYMin = double.MaxValue;
            foreach (var s in dataSeries)
            {
                for (int i = 0; i < s.YValues.Count; i++)
                {
                    double val = s.YValues[i];
                    double err = GetErrorValue(s, i);
                    calculatedYMax = Math.Max(calculatedYMax, val + err);
                    calculatedYMin = Math.Min(calculatedYMin, val - err);
                }
            }
            
            if (calculatedYMin == double.MaxValue) calculatedYMin = 0;
            if (calculatedYMin < 0) calculatedYMin = 0; // Bar charts typically start at 0
            
            // Apply nice rounding to max value
            calculatedYMax = CalculateNiceYMax(calculatedYMax * 1.1);
            if (calculatedYMax == 0) calculatedYMax = 1;

            // Use manual scale if autoScale is disabled
            double useYMax = autoScale ? calculatedYMax : yMax;
            double useYMin = autoScale ? 0 : yMin;
            if (useYMax <= useYMin) useYMax = calculatedYMax;

            // Draw background and grid
            ChartCanvas.Children.Add(new Rectangle
            {
                Width = plotWidth,
                Height = plotHeight,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                StrokeThickness = 1
            });
            Canvas.SetLeft(ChartCanvas.Children[0], marginLeft);
            Canvas.SetTop(ChartCanvas.Children[0], marginTop);

            DrawGridLines(plotWidth, plotHeight, useYMax);
            DrawTickMarks(plotWidth, plotHeight, useYMax);

            if (chartType == "Column")
                DrawColumnBars(plotWidth, plotHeight, useYMax);
            else
                DrawMultiGroupBars(plotWidth, plotHeight, useYMax);

            DrawAxes(plotWidth, plotHeight, useYMax);
            DrawTitle();
            BuildLegend();

            // Restore legend position if it was saved
            if (savedLegendPos.HasValue && legendBorder != null)
            {
                Canvas.SetLeft(legendBorder, savedLegendPos.Value.X);
                Canvas.SetTop(legendBorder, savedLegendPos.Value.Y);
            }

            // Restore annotations
            for (int i = 0; i < savedAnnotations.Count; i++)
            {
                ChartCanvas.Children.Add(savedAnnotations[i]);
                Canvas.SetLeft(savedAnnotations[i], positions[i].X);
                Canvas.SetTop(savedAnnotations[i], positions[i].Y);
            }
            annotations = savedAnnotations;
        }

        /// <summary>
        /// Calculate a nice rounded Y axis maximum value (multiples of 5, 10, etc.)
        /// </summary>
        private double CalculateNiceYMax(double value)
        {
            if (value <= 0) return 1;
            
            // Find the order of magnitude
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double normalized = value / magnitude;
            
            // Round to nice numbers: 1, 2, 2.5, 5, 10
            double nice;
            if (normalized <= 1) nice = 1;
            else if (normalized <= 2) nice = 2;
            else if (normalized <= 2.5) nice = 2.5;
            else if (normalized <= 5) nice = 5;
            else nice = 10;
            
            return nice * magnitude;
        }

        /// <summary>
        /// Calculate nice tick interval based on range
        /// </summary>
        private double CalculateNiceInterval(double range, int targetTicks = 5)
        {
            if (range <= 0) return 1;
            
            double roughInterval = range / targetTicks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughInterval)));
            double normalized = roughInterval / magnitude;
            
            double nice;
            if (normalized <= 1) nice = 1;
            else if (normalized <= 2) nice = 2;
            else if (normalized <= 5) nice = 5;
            else nice = 10;
            
            return nice * magnitude;
        }

        /// <summary>
        /// Draw tick marks (main and sub scale)
        /// </summary>
        private void DrawTickMarks(double plotWidth, double plotHeight, double yMax)
        {
            double interval = mainScaleInterval > 0 ? mainScaleInterval : CalculateNiceInterval(yMax);
            double majorTickLength = 6;
            double minorTickLength = 3;
            
            // Draw major ticks
            for (double yVal = 0; yVal <= yMax; yVal += interval)
            {
                double y = marginTop + plotHeight - (yVal / yMax) * plotHeight;
                
                // Left tick
                ChartCanvas.Children.Add(new Line
                {
                    X1 = marginLeft - majorTickLength,
                    Y1 = y,
                    X2 = marginLeft,
                    Y2 = y,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                });
            }
            
            // Draw minor ticks if enabled
            if (showSubScale && subScaleDivisions > 1)
            {
                double minorInterval = interval / subScaleDivisions;
                for (double yVal = minorInterval; yVal < yMax; yVal += minorInterval)
                {
                    // Skip if this is a major tick position
                    if (Math.Abs(yVal % interval) < 0.0001) continue;
                    
                    double y = marginTop + plotHeight - (yVal / yMax) * plotHeight;
                    
                    ChartCanvas.Children.Add(new Line
                    {
                        X1 = marginLeft - minorTickLength,
                        Y1 = y,
                        X2 = marginLeft,
                        Y2 = y,
                        Stroke = Brushes.Black,
                        StrokeThickness = 0.5
                    });
                }
            }
        }

        private double GetErrorValue(ChartDataSeries s, int index)
        {
            if (errorType == "None") return 0;
            if (errorType == "SD" && index < s.YErrors.Count) return s.YErrors[index];
            if (errorType == "SEM" && index < s.SEMValues.Count) return s.SEMValues[index];
            if (errorType == "95CI" && index < s.SEMValues.Count && index < s.NValues.Count)
            {
                double sem = s.SEMValues[index];
                int n = s.NValues[index];
                double tCrit = GetTCritical(n - 1);
                return tCrit * sem;
            }
            return 0;
        }

        private double GetTCritical(int df)
        {
            if (df <= 0) return 1.96;
            if (df == 1) return 12.706; if (df == 2) return 4.303;
            if (df <= 5) return 2.571; if (df <= 10) return 2.228;
            if (df <= 20) return 2.086; if (df <= 30) return 2.042;
            return 1.96;
        }

        private void DrawColumnBars(double plotWidth, double plotHeight, double yMax)
        {
            // Column bars: each X value has one group, each series is a bar
            int numGroups = dataSeries.Max(s => s.XValues.Count);
            int numSeries = dataSeries.Count;

            double groupWidth = plotWidth / numGroups;
            double totalBarWidth = groupWidth * 0.8; // Use 80% of group width for bars
            double barWidth = totalBarWidth / numSeries;
            double gap = barWidth * 0.1;
            double groupPadding = groupWidth * 0.1; // 10% padding on each side

            for (int g = 0; g < numGroups; g++)
            {
                double groupStartX = marginLeft + g * groupWidth + groupPadding;
                double groupCenterX = marginLeft + g * groupWidth + groupWidth / 2;

                for (int s = 0; s < numSeries; s++)
                {
                    if (g >= dataSeries[s].YValues.Count) continue;

                    var key = (s, g);
                    var style = barStyles.ContainsKey(key) ? barStyles[key] : new BarStyle { FillColor = defaultColors[s % defaultColors.Length] };

                    double val = dataSeries[s].YValues[g];
                    double barHeight = (val / yMax) * plotHeight;
                    double barX = groupStartX + s * barWidth + gap / 2;
                    double barY = marginTop + plotHeight - barHeight;
                    double actualBarWidth = barWidth - gap;

                    var rect = CreateStyledBar(barX, barY, actualBarWidth, barHeight, style);
                    ChartCanvas.Children.Add(rect);

                    barInfos.Add(new BarInfo
                    {
                        SeriesIndex = s,
                        XIndex = g,
                        Bounds = new Rect(barX, barY, actualBarWidth, barHeight)
                    });

                    double barCenterX = barX + actualBarWidth / 2;

                    // Draw data points first (behind error bar)
                    DrawDataPoints(barCenterX, barY, plotHeight, yMax, dataSeries[s], g, actualBarWidth, style.FillColor);

                    // Error bar (use style direction if set, otherwise default)
                    double err = GetErrorValue(dataSeries[s], g);
                    if (err > 0)
                    {
                        double errPx = (err / yMax) * plotHeight;
                        DrawErrorBar(barCenterX, barY, errPx, Brushes.Black, actualBarWidth, style.ErrorDirection);
                    }
                }

                // X label - centered under the group of bars
                string labelText = "";
                if (dataSeries[0].XLabels.Count > g && !string.IsNullOrEmpty(dataSeries[0].XLabels[g]))
                    labelText = dataSeries[0].XLabels[g];
                else if (dataSeries[0].XValues.Count > g)
                    labelText = dataSeries[0].XValues[g].ToString("G4");

                if (!string.IsNullOrEmpty(labelText))
                {
                    var label = new TextBlock
                    {
                        Text = labelText,
                        FontSize = 10
                    };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(label, groupCenterX - label.DesiredSize.Width / 2);
                    Canvas.SetTop(label, marginTop + plotHeight + 5);
                    ChartCanvas.Children.Add(label);
                }
            }
        }

        private void DrawMultiGroupBars(double plotWidth, double plotHeight, double yMax)
        {
            // MultiGroup: each series is a group, X values are bars within group
            int numSeries = dataSeries.Count;
            double groupWidth = plotWidth / numSeries;

            for (int s = 0; s < numSeries; s++)
            {
                int numBars = dataSeries[s].XValues.Count;
                double totalBarWidth = groupWidth * 0.8;
                double barWidth = totalBarWidth / numBars;
                double gap = barWidth * 0.1;
                double groupPadding = groupWidth * 0.1;

                double groupStartX = marginLeft + s * groupWidth + groupPadding;
                double groupCenterX = marginLeft + s * groupWidth + groupWidth / 2;

                for (int x = 0; x < numBars; x++)
                {
                    var key = (s, x);
                    var style = barStyles.ContainsKey(key) ? barStyles[key] : 
                        new BarStyle { FillColor = defaultColors[x % defaultColors.Length] };

                    double val = dataSeries[s].YValues[x];
                    double barHeight = (val / yMax) * plotHeight;
                    double barX = groupStartX + x * barWidth + gap / 2;
                    double barY = marginTop + plotHeight - barHeight;
                    double actualBarWidth = barWidth - gap;

                    var rect = CreateStyledBar(barX, barY, actualBarWidth, barHeight, style);
                    ChartCanvas.Children.Add(rect);

                    barInfos.Add(new BarInfo
                    {
                        SeriesIndex = s,
                        XIndex = x,
                        Bounds = new Rect(barX, barY, actualBarWidth, barHeight)
                    });

                    double barCenterX = barX + actualBarWidth / 2;

                    // Draw data points first (behind error bar)
                    DrawDataPoints(barCenterX, barY, plotHeight, yMax, dataSeries[s], x, actualBarWidth, style.FillColor);

                    // Error bar (use style direction if set, otherwise default)
                    double err = GetErrorValue(dataSeries[s], x);
                    if (err > 0)
                    {
                        double errPx = (err / yMax) * plotHeight;
                        DrawErrorBar(barCenterX, barY, errPx, Brushes.Black, actualBarWidth, style.ErrorDirection);
                    }
                }

                // Series label - centered under the group
                var label = new TextBlock
                {
                    Text = dataSeries[s].Name,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, groupCenterX - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, marginTop + plotHeight + 5);
                ChartCanvas.Children.Add(label);
            }
        }

        private FrameworkElement CreateStyledBar(double x, double y, double width, double height, BarStyle style)
        {
            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = new SolidColorBrush(style.BorderColor),
                StrokeThickness = style.BorderThickness
            };

            rect.Fill = CreatePatternBrush(style);

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            return rect;
        }

        private Brush CreatePatternBrush(BarStyle style)
        {
            var baseColor = style.FillColor;

            switch (style.Pattern)
            {
                case FillPattern.Solid:
                    return new SolidColorBrush(baseColor);

                case FillPattern.HorizontalLines:
                    return CreateLineBrush(baseColor, 0);

                case FillPattern.VerticalLines:
                    return CreateLineBrush(baseColor, 90);

                case FillPattern.DiagonalUp:
                    return CreateLineBrush(baseColor, 45);

                case FillPattern.DiagonalDown:
                    return CreateLineBrush(baseColor, -45);

                case FillPattern.CrossHatch:
                    return CreateCrossHatchBrush(baseColor);

                case FillPattern.Dots:
                    return CreateDotsBrush(baseColor);

                default:
                    return new SolidColorBrush(baseColor);
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

        /// <summary>
        /// Convert Y data value to canvas Y coordinate, handling axis break if enabled
        /// </summary>
        private double ValueToCanvasY(double value, double plotHeight, double yMax)
        {
            if (!enableAxisBreak || axisBreakStart >= axisBreakEnd || axisBreakEnd >= yMax)
            {
                // No break - standard linear transformation
                return marginTop + plotHeight - (value / yMax) * plotHeight;
            }
            
            // With axis break
            double breakGapPixels = 15;
            double breakSize = axisBreakEnd - axisBreakStart;
            double effectiveMax = yMax - breakSize;
            double belowBreakHeight = (axisBreakStart / effectiveMax) * (plotHeight - breakGapPixels);
            double aboveBreakHeight = ((yMax - axisBreakEnd) / effectiveMax) * (plotHeight - breakGapPixels);
            double breakY = marginTop + plotHeight - belowBreakHeight;
            
            if (value <= axisBreakStart)
            {
                // Value is below the break
                return marginTop + plotHeight - (value / axisBreakStart) * belowBreakHeight;
            }
            else if (value >= axisBreakEnd)
            {
                // Value is above the break
                double aboveValue = value - axisBreakEnd;
                double aboveRange = yMax - axisBreakEnd;
                return breakY - breakGapPixels - (aboveValue / aboveRange) * aboveBreakHeight;
            }
            else
            {
                // Value is within the break - clamp to break start
                return breakY;
            }
        }

        private void DrawErrorBar(double cx, double top, double errPx, Brush brush, double barWidth, 
            ErrorBarDirection direction = ErrorBarDirection.Both)
        {
            double capWidth = barWidth / 3.0; // Cap width is 1/3 of bar width
            
            // Determine what to draw based on direction
            bool drawUp = direction == ErrorBarDirection.Both || direction == ErrorBarDirection.Up;
            bool drawDown = direction == ErrorBarDirection.Both || direction == ErrorBarDirection.Down;
            
            // Calculate Y positions
            double topY = drawUp ? top - errPx : top;
            double bottomY = drawDown ? top + errPx : top;
            
            // Vertical line
            ChartCanvas.Children.Add(new Line 
            { 
                X1 = cx, Y1 = topY, X2 = cx, Y2 = bottomY, 
                Stroke = brush, StrokeThickness = errorBarThickness 
            });
            
            // Top cap (if drawing up)
            if (drawUp)
            {
                ChartCanvas.Children.Add(new Line 
                { 
                    X1 = cx - capWidth / 2, Y1 = topY, X2 = cx + capWidth / 2, Y2 = topY, 
                    Stroke = brush, StrokeThickness = errorBarThickness 
                });
            }
            
            // Bottom cap (if drawing down)
            if (drawDown)
            {
                ChartCanvas.Children.Add(new Line 
                { 
                    X1 = cx - capWidth / 2, Y1 = bottomY, X2 = cx + capWidth / 2, Y2 = bottomY, 
                    Stroke = brush, StrokeThickness = errorBarThickness 
                });
            }
        }

        private void DrawDataPoints(double cx, double barY, double plotHeight, double yMax, 
            ChartDataSeries series, int xIndex, double barWidth, Color barColor)
        {
            if (!showDataPoints || xIndex >= series.RawReplicates.Count) return;
            
            var replicates = series.RawReplicates[xIndex];
            if (replicates == null || replicates.Count == 0) return;
            
            double pointSize = dataPointSize;
            var brush = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)); // Semi-transparent black
            
            foreach (var val in replicates)
            {
                double y = marginTop + plotHeight - (val / yMax) * plotHeight;
                var ellipse = new Ellipse
                {
                    Width = pointSize,
                    Height = pointSize,
                    Fill = brush,
                    Stroke = Brushes.White,
                    StrokeThickness = 0.5
                };
                Canvas.SetLeft(ellipse, cx - pointSize / 2);
                Canvas.SetTop(ellipse, y - pointSize / 2);
                ChartCanvas.Children.Add(ellipse);
            }
        }

        private void DrawGridLines(double plotWidth, double plotHeight, double yMax)
        {
            if (!showGridLines) return;
            
            var brush = new SolidColorBrush(gridLineColor);
            DoubleCollection dashArray = null;
            
            switch (gridLineStyle)
            {
                case "Dashed":
                    dashArray = new DoubleCollection { 6, 3 };
                    break;
                case "Dotted":
                    dashArray = new DoubleCollection { 2, 2 };
                    break;
            }
            
            // Use main scale interval or calculate nice interval
            double interval = mainScaleInterval > 0 ? mainScaleInterval : CalculateNiceInterval(yMax);
            
            for (double yVal = 0; yVal <= yMax; yVal += interval)
            {
                double y = marginTop + plotHeight - (yVal / yMax * plotHeight);
                var line = new Line 
                { 
                    X1 = marginLeft, 
                    Y1 = y, 
                    X2 = marginLeft + plotWidth, 
                    Y2 = y, 
                    Stroke = brush, 
                    StrokeThickness = 1 
                };
                if (dashArray != null)
                    line.StrokeDashArray = dashArray;
                ChartCanvas.Children.Add(line);
            }
        }

        private void DrawAxes(double plotWidth, double plotHeight, double yMax)
        {
            // Y axis labels - with axis break support
            if (enableAxisBreak && axisBreakStart < axisBreakEnd && axisBreakEnd < yMax)
            {
                DrawAxesWithBreak(plotWidth, plotHeight, yMax);
            }
            else
            {
                // Calculate interval
                double interval = mainScaleInterval > 0 ? mainScaleInterval : CalculateNiceInterval(yMax);
                
                // Draw Y axis labels at nice intervals
                for (double yVal = 0; yVal <= yMax; yVal += interval)
                {
                    double y = marginTop + plotHeight - (yVal / yMax) * plotHeight;
                    
                    // Format label based on value magnitude
                    string labelText;
                    if (logScale)
                        labelText = Math.Pow(10, yVal).ToString("G3");
                    else if (yVal == Math.Floor(yVal) && Math.Abs(yVal) < 10000)
                        labelText = yVal.ToString("F0");
                    else
                        labelText = yVal.ToString("G4");
                    
                    var lbl = new TextBlock { Text = labelText, FontSize = 10 };
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lbl, marginLeft - lbl.DesiredSize.Width - 8);
                    Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
                    ChartCanvas.Children.Add(lbl);
                }
            }

            // Clickable Y axis area (invisible rectangle for click detection)
            var yAxisClickArea = new Rectangle
            {
                Width = marginLeft,
                Height = plotHeight,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            yAxisClickArea.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                    OpenAxisSettings(true);
            };
            yAxisClickArea.ToolTip = "Double-click to edit Y axis settings";
            Canvas.SetLeft(yAxisClickArea, 0);
            Canvas.SetTop(yAxisClickArea, marginTop);
            ChartCanvas.Children.Add(yAxisClickArea);

            // X axis title (clickable)
            var xt = new TextBlock 
            { 
                Text = xAxisTitle, 
                FontSize = 12, 
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Double-click to edit X axis settings"
            };
            xt.MouseLeftButtonDown += (s, e) => 
            {
                if (e.ClickCount == 2)
                    OpenAxisSettings(false);
            };
            xt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(xt, marginLeft + (plotWidth - xt.DesiredSize.Width) / 2);
            Canvas.SetTop(xt, marginTop + plotHeight + 30);
            ChartCanvas.Children.Add(xt);

            // Y axis title (clickable)
            var yt = new TextBlock 
            { 
                Text = yAxisTitle, 
                FontSize = 12, 
                FontWeight = FontWeights.SemiBold, 
                RenderTransform = new RotateTransform(-90),
                Cursor = Cursors.Hand,
                ToolTip = "Double-click to edit Y axis settings"
            };
            yt.MouseLeftButtonDown += (s, e) => 
            {
                if (e.ClickCount == 2)
                    OpenAxisSettings(true);
            };
            yt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(yt, 10);
            Canvas.SetTop(yt, marginTop + (plotHeight + yt.DesiredSize.Width) / 2);
            ChartCanvas.Children.Add(yt);
        }

        private void DrawAxesWithBreak(double plotWidth, double plotHeight, double yMax)
        {
            // Calculate the effective range (excluding the break)
            double breakSize = axisBreakEnd - axisBreakStart;
            double effectiveMax = yMax - breakSize;
            double breakGapPixels = 15; // Gap for the break symbol
            double belowBreakHeight = (axisBreakStart / effectiveMax) * (plotHeight - breakGapPixels);
            double aboveBreakHeight = ((yMax - axisBreakEnd) / effectiveMax) * (plotHeight - breakGapPixels);
            
            double breakY = marginTop + plotHeight - belowBreakHeight - breakGapPixels;
            
            // Draw labels below break
            int numLabelsBelow = 3;
            for (int i = 0; i <= numLabelsBelow; i++)
            {
                double yVal = axisBreakStart * i / numLabelsBelow;
                double y = marginTop + plotHeight - (belowBreakHeight * i / numLabelsBelow);
                var lbl = new TextBlock { Text = yVal.ToString("F1"), FontSize = 10 };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, marginLeft - lbl.DesiredSize.Width - 5);
                Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
                ChartCanvas.Children.Add(lbl);
            }
            
            // Draw break symbol (zigzag)
            double zigzagWidth = 8;
            double zigzagY1 = breakY + breakGapPixels;
            double zigzagY2 = breakY;
            
            var breakPath = new System.Windows.Shapes.Path
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,
                Data = Geometry.Parse($"M {marginLeft - 5},{zigzagY1} " +
                    $"L {marginLeft - 5 + zigzagWidth / 2},{zigzagY1 - breakGapPixels / 3} " +
                    $"L {marginLeft - 5},{zigzagY1 - breakGapPixels * 2 / 3} " +
                    $"L {marginLeft - 5 + zigzagWidth / 2},{zigzagY2}")
            };
            ChartCanvas.Children.Add(breakPath);
            
            // Draw labels above break
            int numLabelsAbove = 2;
            for (int i = 0; i <= numLabelsAbove; i++)
            {
                double yVal = axisBreakEnd + (yMax - axisBreakEnd) * i / numLabelsAbove;
                double y = breakY - (aboveBreakHeight * i / numLabelsAbove);
                var lbl = new TextBlock { Text = yVal.ToString("F1"), FontSize = 10 };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, marginLeft - lbl.DesiredSize.Width - 5);
                Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
                ChartCanvas.Children.Add(lbl);
            }
        }

        private void OpenAxisSettings(bool isYAxis)
        {
            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;
            double plotHeight = height - marginTop - marginBottom;

            // Calculate current max
            double currentMax = 0;
            foreach (var s in dataSeries)
            {
                for (int i = 0; i < s.YValues.Count; i++)
                {
                    double err = GetErrorValue(s, i);
                    currentMax = Math.Max(currentMax, s.YValues[i] + err);
                }
            }
            currentMax = CalculateNiceYMax(currentMax * 1.1);

            var dialog = new AxisSettingsDialog(
                isYAxis, 
                isYAxis ? yAxisTitle : xAxisTitle,
                yMin, autoScale ? currentMax : yMax,
                showGridLines, gridLineStyle, gridLineColor, 
                mainScaleInterval, showSubScale, subScaleDivisions, logScale);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                if (isYAxis)
                {
                    yAxisTitle = dialog.AxisTitle;
                    autoScale = dialog.AutoScale;
                    logScale = dialog.LogScale;
                    
                    if (!dialog.AutoScale)
                    {
                        yMin = dialog.MinValue;
                        yMax = dialog.MaxValue;
                    }
                    
                    // Tick mark settings
                    mainScaleInterval = dialog.MainScaleInterval;
                    showSubScale = dialog.ShowSubScale;
                    subScaleDivisions = dialog.SubScaleDivisions;
                    
                    // Store axis break settings
                    enableAxisBreak = dialog.EnableBreak;
                    axisBreakStart = dialog.BreakStart;
                    axisBreakEnd = dialog.BreakEnd;
                }
                else
                {
                    xAxisTitle = dialog.AxisTitle;
                }
                
                showGridLines = dialog.ShowGridLines;
                gridLineStyle = dialog.GridLineStyle;
                gridLineColor = dialog.GridLineColor;
                
                DrawChart();
            }
        }

        private void DrawTitle()
        {
            double width = ChartCanvas.ActualWidth;
            string[] parts = chartTitle.Split('|');
            string main = parts[0].Trim();

            var t = new TextBlock { Text = main, FontSize = 16, FontWeight = FontWeights.Bold };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, (width - t.DesiredSize.Width) / 2);
            Canvas.SetTop(t, 8);
            ChartCanvas.Children.Add(t);
        }

        private void BuildLegend()
        {
            // Remove old legend if exists
            if (legendBorder != null && ChartCanvas.Children.Contains(legendBorder))
            {
                ChartCanvas.Children.Remove(legendBorder);
            }

            // Create legend container
            legendBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Cursor = Cursors.SizeAll
            };

            var legendStack = new StackPanel();

            if (chartType == "Column")
            {
                foreach (var s in dataSeries)
                {
                    int idx = dataSeries.IndexOf(s);
                    var style = barStyles.ContainsKey((idx, 0)) ? barStyles[(idx, 0)] : new BarStyle { FillColor = defaultColors[idx % defaultColors.Length] };
                    legendStack.Children.Add(CreateLegendItem(s.Name, style));
                }
            }
            else
            {
                if (dataSeries.Count > 0)
                {
                    for (int x = 0; x < dataSeries[0].XValues.Count; x++)
                    {
                        var style = barStyles.ContainsKey((0, x)) ? barStyles[(0, x)] : new BarStyle { FillColor = defaultColors[x % defaultColors.Length] };
                        string labelText = (dataSeries[0].XLabels.Count > x && !string.IsNullOrEmpty(dataSeries[0].XLabels[x]))
                            ? dataSeries[0].XLabels[x]
                            : $"X={dataSeries[0].XValues[x]:G4}";
                        legendStack.Children.Add(CreateLegendItem(labelText, style));
                    }
                }
            }

            legendBorder.Child = legendStack;

            // Add drag events
            legendBorder.MouseLeftButtonDown += Legend_MouseLeftButtonDown;
            legendBorder.MouseMove += Legend_MouseMove;
            legendBorder.MouseLeftButtonUp += Legend_MouseLeftButtonUp;

            ChartCanvas.Children.Add(legendBorder);

            // Position legend (default: top-left, move to top-right if overlapping data)
            PositionLegend();
        }

        private StackPanel CreateLegendItem(string text, BarStyle style)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 2, 3) };
            var rect = new Rectangle 
            { 
                Width = 14, Height = 14, 
                Fill = CreatePatternBrush(style), 
                Stroke = new SolidColorBrush(style.BorderColor), 
                StrokeThickness = 1, 
                Margin = new Thickness(0, 0, 6, 0) 
            };
            sp.Children.Add(rect);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            return sp;
        }

        private void PositionLegend()
        {
            if (legendBorder == null) return;

            legendBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double legendWidth = legendBorder.DesiredSize.Width;
            double legendHeight = legendBorder.DesiredSize.Height;

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;
            double plotWidth = width - marginLeft - marginRight;
            double plotHeight = height - marginTop - marginBottom;

            // Default position: top-left inside plot area
            double leftX = marginLeft + 10;
            double topY = marginTop + 10;

            // Check if legend overlaps with bars in top-left
            bool overlapsTopLeft = CheckLegendOverlap(leftX, topY, legendWidth, legendHeight);

            if (overlapsTopLeft)
            {
                // Try top-right
                double rightX = marginLeft + plotWidth - legendWidth - 10;
                bool overlapsTopRight = CheckLegendOverlap(rightX, topY, legendWidth, legendHeight);
                
                if (!overlapsTopRight)
                {
                    leftX = rightX;
                }
                // If both overlap, keep top-left (user can drag)
            }

            Canvas.SetLeft(legendBorder, leftX);
            Canvas.SetTop(legendBorder, topY);
        }

        private bool CheckLegendOverlap(double legendX, double legendY, double legendW, double legendH)
        {
            var legendRect = new Rect(legendX, legendY, legendW, legendH);

            foreach (var barInfo in barInfos)
            {
                if (legendRect.IntersectsWith(barInfo.Bounds))
                    return true;
            }
            return false;
        }

        private void Legend_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (legendBorder == null) return;
            
            isLegendDragging = true;
            legendDragStart = e.GetPosition(ChartCanvas);
            legendStartPosition = new Point(Canvas.GetLeft(legendBorder), Canvas.GetTop(legendBorder));
            legendBorder.CaptureMouse();
            e.Handled = true;
        }

        private void Legend_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isLegendDragging || legendBorder == null) return;

            Point currentPos = e.GetPosition(ChartCanvas);
            double deltaX = currentPos.X - legendDragStart.X;
            double deltaY = currentPos.Y - legendDragStart.Y;

            double newLeft = legendStartPosition.X + deltaX;
            double newTop = legendStartPosition.Y + deltaY;

            // Constrain to canvas bounds
            legendBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double legendW = legendBorder.DesiredSize.Width;
            double legendH = legendBorder.DesiredSize.Height;

            newLeft = Math.Max(0, Math.Min(newLeft, ChartCanvas.ActualWidth - legendW));
            newTop = Math.Max(0, Math.Min(newTop, ChartCanvas.ActualHeight - legendH));

            Canvas.SetLeft(legendBorder, newLeft);
            Canvas.SetTop(legendBorder, newTop);
        }

        private void Legend_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (legendBorder == null) return;
            
            isLegendDragging = false;
            legendBorder.ReleaseMouseCapture();
        }

        #endregion

        #region Settings Dialog

        private void OpenSettingsDialog()
        {
            var dialog = new BarChartSettingsWindow(dataSeries, chartTitle, xAxisTitle, yAxisTitle,
                errorBarThickness, showDataPoints, dataPointSize);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                chartTitle = dialog.ChartTitle;
                xAxisTitle = dialog.XAxisTitle;
                yAxisTitle = dialog.YAxisTitle;
                errorBarThickness = dialog.ErrorBarThickness;
                showDataPoints = dialog.ShowDataPoints;
                dataPointSize = dialog.DataPointSize;
                DrawChart();
            }
        }

        #endregion

        #region Annotation System (simplified - shared logic with ChartWindow)

        private void SelectAllAnnotations()
        {
            ClearSelection();
            foreach (var ann in annotations)
            {
                selectedAnnotations.Add(ann);
                HighlightAnnotation(ann, true);
            }
        }

        private void ClearSelection()
        {
            foreach (var ann in selectedAnnotations) HighlightAnnotation(ann, false);
            selectedAnnotations.Clear();
            if (selectedAnnotation != null) { HighlightAnnotation(selectedAnnotation, false); selectedAnnotation = null; }
        }

        private void SelectAnnotation(FrameworkElement? element)
        {
            selectedAnnotation = element;
            if (element != null) { selectedAnnotations.Add(element); HighlightAnnotation(element, true); }
        }

        private void HighlightAnnotation(FrameworkElement element, bool highlight)
        {
            if (element is Border border)
            {
                border.BorderBrush = highlight ? Brushes.DodgerBlue : (border.Tag as Brush ?? Brushes.Transparent);
                border.BorderThickness = highlight ? new Thickness(2) : new Thickness(border.Tag != null ? 1 : 0);
            }
        }

        private FrameworkElement? FindAnnotationAt(Point pos)
        {
            foreach (var ann in annotations)
            {
                double left = Canvas.GetLeft(ann);
                double top = Canvas.GetTop(ann);
                if (pos.X >= left - 5 && pos.X <= left + ann.ActualWidth + 5 && pos.Y >= top - 5 && pos.Y <= top + ann.ActualHeight + 5)
                    return ann;
            }
            return null;
        }

        private void DeleteSelectedAnnotations()
        {
            var toDelete = selectedAnnotations.Count > 0 ? selectedAnnotations.ToList() : (selectedAnnotation != null ? new List<FrameworkElement> { selectedAnnotation } : new List<FrameworkElement>());
            foreach (var ann in toDelete) { ChartCanvas.Children.Remove(ann); annotations.Remove(ann); }
            selectedAnnotations.Clear();
            selectedAnnotation = null;
        }

        private void EditAnnotation(FrameworkElement element)
        {
            // Simplified - just allow text editing
            if (element is Border border && border.Child is TextBlock tb)
            {
                var format = new TextFormatInfo
                {
                    FontFamily = tb.FontFamily,
                    FontSize = tb.FontSize,
                    TextColor = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.Black,
                    IsBold = tb.FontWeight == FontWeights.Bold,
                    IsItalic = tb.FontStyle == FontStyles.Italic,
                    ShowBorder = false
                };
                var dialog = new TextAnnotationDialog(tb.Text, format);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    tb.Text = dialog.AnnotationText;
                }
            }
        }

        private void AddTextBox_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextAnnotationDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                var tb = new TextBlock { Text = dialog.AnnotationText, FontSize = dialog.SelectedFontSize, Padding = new Thickness(5) };
                var border = new Border { Child = tb, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
                Canvas.SetLeft(border, 100);
                Canvas.SetTop(border, 100);
                ChartCanvas.Children.Add(border);
                annotations.Add(border);
            }
        }

        private void OpenSymbolPicker_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SymbolPickerDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedSymbol))
            {
                var tb = new TextBlock { Text = dialog.SelectedSymbol, FontSize = 14, Padding = new Thickness(5) };
                var border = new Border { Child = tb, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
                Canvas.SetLeft(border, 100);
                Canvas.SetTop(border, 100);
                ChartCanvas.Children.Add(border);
                annotations.Add(border);
            }
        }

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            // Simplified line adding
            if (sender is Button btn && btn.Tag is string lineType)
            {
                var line = new Line { X1 = 0, Y1 = 0, X2 = 80, Y2 = 0, Stroke = Brushes.Black, StrokeThickness = 2 };
                var canvas = new Canvas { Width = 85, Height = 10, Background = Brushes.Transparent };
                canvas.Children.Add(line);
                var border = new Border { Child = canvas, Cursor = Cursors.SizeAll };
                Canvas.SetLeft(border, 150);
                Canvas.SetTop(border, 150);
                ChartCanvas.Children.Add(border);
                annotations.Add(border);
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelectedAnnotations();

        #endregion

        #region Statistics

        private void OpenStatistics_Click(object sender, RoutedEventArgs e)
        {
            var statsWindow = new StatisticsWindow(dataSeries, chartType);
            statsWindow.Owner = this;
            statsWindow.Show();
        }

        #endregion

        #region Chart Size Control

        private void SizePreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Guard against event firing during initialization
            if (ChartWidthBox == null || ChartHeightBox == null || ChartCanvas == null) return;
            
            if (SizePresetCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "custom")
                {
                    // Show custom size controls
                    ChartWidthBox.Visibility = Visibility.Visible;
                    SizeXLabel.Visibility = Visibility.Visible;
                    ChartHeightBox.Visibility = Visibility.Visible;
                    ApplySizeBtn.Visibility = Visibility.Visible;
                    return;
                }
                else
                {
                    // Hide custom size controls
                    ChartWidthBox.Visibility = Visibility.Collapsed;
                    SizeXLabel.Visibility = Visibility.Collapsed;
                    ChartHeightBox.Visibility = Visibility.Collapsed;
                    ApplySizeBtn.Visibility = Visibility.Collapsed;
                }

                var parts = tag.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    if (w == 0 && h == 0)
                    {
                        // Auto fit
                        useFixedSize = false;
                        ChartCanvas.Width = double.NaN;
                        ChartCanvas.Height = double.NaN;
                        ChartCanvas.UpdateLayout();
                        DrawChart();
                    }
                    else
                    {
                        // Apply preset size
                        useFixedSize = true;
                        fixedChartWidth = w;
                        fixedChartHeight = h;
                        ChartCanvas.Width = w;
                        ChartCanvas.Height = h;
                        ChartWidthBox.Text = w.ToString();
                        ChartHeightBox.Text = h.ToString();
                        DrawChart();
                    }
                }
            }
        }

        private void ApplyChartSize_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ChartWidthBox.Text, out double width) || width < 200)
            {
                MessageBox.Show("Invalid width. Minimum is 200.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(ChartHeightBox.Text, out double height) || height < 150)
            {
                MessageBox.Show("Invalid height. Minimum is 150.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            useFixedSize = true;
            fixedChartWidth = width;
            fixedChartHeight = height;

            ChartCanvas.Width = width;
            ChartCanvas.Height = height;
            DrawChart();
        }

        private void FitToWindow_Click(object sender, RoutedEventArgs e)
        {
            useFixedSize = false;
            ChartCanvas.Width = double.NaN;
            ChartCanvas.Height = double.NaN;
            
            // Force layout update
            ChartCanvas.UpdateLayout();
            DrawChart();
            
            // Update textboxes to show current size
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ChartWidthBox.Text = ((int)ChartCanvas.ActualWidth).ToString();
                ChartHeightBox.Text = ((int)ChartCanvas.ActualHeight).ToString();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Calculate the bounding box of all visible elements on the canvas
        /// </summary>
        private Rect GetChartBoundingBox()
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (UIElement child in ChartCanvas.Children)
            {
                double left = Canvas.GetLeft(child);
                double top = Canvas.GetTop(child);
                
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                double width = 0, height = 0;

                if (child is FrameworkElement fe)
                {
                    fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    width = fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width;
                    height = fe.ActualHeight > 0 ? fe.ActualHeight : fe.DesiredSize.Height;
                }

                if (child is Line line)
                {
                    minX = Math.Min(minX, Math.Min(line.X1, line.X2));
                    minY = Math.Min(minY, Math.Min(line.Y1, line.Y2));
                    maxX = Math.Max(maxX, Math.Max(line.X1, line.X2));
                    maxY = Math.Max(maxY, Math.Max(line.Y1, line.Y2));
                }
                else
                {
                    minX = Math.Min(minX, left);
                    minY = Math.Min(minY, top);
                    maxX = Math.Max(maxX, left + width);
                    maxY = Math.Max(maxY, top + height);
                }
            }

            // Fallback to canvas size if no elements
            if (minX == double.MaxValue)
            {
                return new Rect(0, 0, ChartCanvas.ActualWidth, ChartCanvas.ActualHeight);
            }

            // Add small padding
            double padding = 5;
            return new Rect(
                Math.Max(0, minX - padding),
                Math.Max(0, minY - padding),
                Math.Min(ChartCanvas.ActualWidth, maxX + padding) - Math.Max(0, minX - padding),
                Math.Min(ChartCanvas.ActualHeight, maxY + padding) - Math.Max(0, minY - padding)
            );
        }

        #endregion

        #region Save/Copy

        private void SaveAsPng_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "barchart.png" };
            if (dlg.ShowDialog() == true)
            {
                // Render full canvas
                int width = (int)ChartCanvas.ActualWidth;
                int height = (int)ChartCanvas.ActualHeight;
                
                var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(ChartCanvas);
                
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var s = File.Create(dlg.FileName)) enc.Save(s);
                MessageBox.Show($"Saved! ({width}×{height})", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveAsSvg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "SVG File|*.svg", FileName = "barchart.svg" };
            if (dlg.ShowDialog() == true)
            {
                int width = (int)ChartCanvas.ActualWidth;
                int height = (int)ChartCanvas.ActualHeight;
                
                using (var w = new StreamWriter(dlg.FileName))
                {
                    w.WriteLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">");
                    w.WriteLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
                    
                    foreach (var c in ChartCanvas.Children)
                    {
                        if (c is Rectangle r && r.Tag?.ToString() != "Handle")
                        {
                            var fill = (r.Fill as SolidColorBrush)?.Color ?? Colors.Gray;
                            double rx = Canvas.GetLeft(r);
                            double ry = Canvas.GetTop(r);
                            if (double.IsNaN(rx)) rx = 0;
                            if (double.IsNaN(ry)) ry = 0;
                            w.WriteLine($"<rect x=\"{rx:F1}\" y=\"{ry:F1}\" width=\"{r.Width:F1}\" height=\"{r.Height:F1}\" fill=\"#{fill.R:X2}{fill.G:X2}{fill.B:X2}\"/>");
                        }
                        else if (c is Line l)
                        {
                            var col = (l.Stroke as SolidColorBrush)?.Color ?? Colors.Black;
                            w.WriteLine($"<line x1=\"{l.X1:F1}\" y1=\"{l.Y1:F1}\" x2=\"{l.X2:F1}\" y2=\"{l.Y2:F1}\" stroke=\"#{col.R:X2}{col.G:X2}{col.B:X2}\" stroke-width=\"{l.StrokeThickness}\"/>");
                        }
                        else if (c is TextBlock tb)
                        {
                            double tx = Canvas.GetLeft(tb);
                            double ty = Canvas.GetTop(tb);
                            if (double.IsNaN(tx)) tx = 0;
                            if (double.IsNaN(ty)) ty = 0;
                            var col = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
                            w.WriteLine($"<text x=\"{tx:F1}\" y=\"{ty + tb.FontSize:F1}\" font-size=\"{tb.FontSize}\" fill=\"#{col.R:X2}{col.G:X2}{col.B:X2}\">{System.Security.SecurityElement.Escape(tb.Text)}</text>");
                        }
                        else if (c is Ellipse el)
                        {
                            double ex = Canvas.GetLeft(el) + el.Width / 2;
                            double ey = Canvas.GetTop(el) + el.Height / 2;
                            var fill = (el.Fill as SolidColorBrush)?.Color ?? Colors.Gray;
                            w.WriteLine($"<ellipse cx=\"{ex:F1}\" cy=\"{ey:F1}\" rx=\"{el.Width / 2:F1}\" ry=\"{el.Height / 2:F1}\" fill=\"#{fill.R:X2}{fill.G:X2}{fill.B:X2}\"/>");
                        }
                    }
                    w.WriteLine("</svg>");
                }
                MessageBox.Show($"Saved! ({width}×{height})", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e) => CopyChartToClipboard();

        private void CopyChartToClipboard()
        {
            int width = (int)ChartCanvas.ActualWidth;
            int height = (int)ChartCanvas.ActualHeight;
            
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(ChartCanvas);
            Clipboard.SetImage(bmp);
            MessageBox.Show($"Chart copied! ({width}×{height})", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion
    }

    #region Helper Classes

    public class BarStyle
    {
        public Color FillColor { get; set; } = Colors.DodgerBlue;
        public Color BorderColor { get; set; } = Colors.Black;
        public double BorderThickness { get; set; } = 1;
        public FillPattern Pattern { get; set; } = FillPattern.Solid;
        public ErrorBarDirection ErrorDirection { get; set; } = ErrorBarDirection.Both;
    }

    public enum FillPattern
    {
        Solid,
        HorizontalLines,
        VerticalLines,
        DiagonalUp,
        DiagonalDown,
        CrossHatch,
        Dots
    }

    public enum ErrorBarDirection
    {
        Both,
        Up,
        Down
    }

    public class BarInfo
    {
        public int SeriesIndex { get; set; }
        public int XIndex { get; set; }
        public Rect Bounds { get; set; }
    }

    #endregion
}
