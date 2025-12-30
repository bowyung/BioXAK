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
    public partial class ChartWindow : Window
    {
        private List<ChartDataSeries> dataSeries;
        private string chartType;
        private string errorType;
        private string xAxisTitle;
        private string yAxisTitle = "Y";
        private string chartTitle = "";

        private double marginLeft = 70;
        private double marginRight = 20;
        private double marginTop = 50;
        private double marginBottom = 50;

        private DateTime lastClickTime = DateTime.MinValue;
        private FrameworkElement? lastClickedElement = null;

        // Annotation tracking
        private List<FrameworkElement> annotations = new List<FrameworkElement>();
        private List<FrameworkElement> selectedAnnotations = new List<FrameworkElement>();
        private FrameworkElement? selectedAnnotation = null;
        private bool isDragging = false;
        private bool isResizing = false;
        private bool isSelecting = false;
        private Point dragStart;
        private Point elementStart;
        private Point selectionStart;
        private string currentResizeHandle = "";
        private Canvas? currentLineCanvas = null;

        // Clipboard for annotations
        private List<AnnotationClipboardItem> clipboardItems = new List<AnnotationClipboardItem>();

        // Chart data points for alignment
        private List<Point> chartDataPoints = new List<Point>();

        private readonly Color[] defaultColors = new Color[]
        {
            Color.FromRgb(66, 133, 244), Color.FromRgb(234, 67, 53),
            Color.FromRgb(52, 168, 83), Color.FromRgb(251, 188, 5),
            Color.FromRgb(154, 71, 182), Color.FromRgb(255, 112, 67),
            Color.FromRgb(0, 172, 193), Color.FromRgb(124, 77, 255),
        };

        public ChartWindow(List<ChartDataSeries> data, string type, string error, string xTitle)
        {
            InitializeComponent();

            dataSeries = data;
            chartType = type;
            errorType = error;
            xAxisTitle = xTitle;
            chartTitle = $"{type} Chart";

            for (int i = 0; i < dataSeries.Count; i++)
            {
                dataSeries[i].LineColor = defaultColors[i % defaultColors.Length];
            }

            this.Loaded += (s, e) => { DrawChart(); this.Focus(); };
        }

        #region Keyboard Shortcuts

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
                    case Key.C:
                        if (selectedAnnotations.Count > 0 || selectedAnnotation != null)
                            CopySelectedAnnotations();
                        else
                            CopyChartToClipboard();
                        e.Handled = true;
                        break;
                    case Key.V:
                        PasteAnnotations();
                        e.Handled = true;
                        break;
                    case Key.A:
                        SelectAllAnnotations();
                        e.Handled = true;
                        break;
                }
            }
        }

        private void SelectAllAnnotations()
        {
            ClearSelection();
            foreach (var ann in annotations)
            {
                selectedAnnotations.Add(ann);
                HighlightAnnotation(ann, true);
            }
        }

        private void CopySelectedAnnotations()
        {
            clipboardItems.Clear();
            var toCopy = selectedAnnotations.Count > 0 ? selectedAnnotations :
                         (selectedAnnotation != null ? new List<FrameworkElement> { selectedAnnotation } : new List<FrameworkElement>());

            foreach (var ann in toCopy)
            {
                var item = new AnnotationClipboardItem
                {
                    Left = Canvas.GetLeft(ann),
                    Top = Canvas.GetTop(ann)
                };

                if (ann is Border border)
                {
                    if (border.Child is TextBlock tb)
                    {
                        item.Type = "Text";
                        item.Text = tb.Text;
                        item.FormatInfo = border.DataContext as TextFormatInfo;
                    }
                    else if (border.Child is Canvas lineCanvas && lineCanvas.Tag is LineShapeInfo lineInfo)
                    {
                        item.Type = "Line";
                        item.LineInfo = CloneLineInfo(lineInfo);
                    }
                }
                clipboardItems.Add(item);
            }
        }

        private void PasteAnnotations()
        {
            if (clipboardItems.Count == 0) return;

            ClearSelection();
            double offset = 20;

            foreach (var item in clipboardItems)
            {
                if (item.Type == "Text" && item.FormatInfo != null)
                {
                    CreateTextAnnotation(item.Text ?? "", item.FormatInfo.FontSize,
                        item.FormatInfo.FontFamily, item.FormatInfo.IsBold,
                        item.FormatInfo.IsItalic, item.FormatInfo.ShowBorder,
                        item.FormatInfo.TextColor, item.Left + offset, item.Top + offset);
                }
                else if (item.Type == "Line" && item.LineInfo != null)
                {
                    CreateLineFromInfo(item.LineInfo, item.Left + offset, item.Top + offset);
                }
            }
        }

        private void DeleteSelectedAnnotations()
        {
            var toDelete = selectedAnnotations.Count > 0 ? selectedAnnotations.ToList() :
                          (selectedAnnotation != null ? new List<FrameworkElement> { selectedAnnotation } : new List<FrameworkElement>());

            foreach (var ann in toDelete)
            {
                ChartCanvas.Children.Remove(ann);
                annotations.Remove(ann);
            }
            selectedAnnotations.Clear();
            selectedAnnotation = null;
        }

        #endregion

        #region Mouse Events

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
        }

        private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ChartCanvas);
            var now = DateTime.Now;

            var hitElement = FindAnnotationAt(pos);

            if (hitElement != null)
            {
                bool ctrlPressed = Keyboard.Modifiers == ModifierKeys.Control;

                if (ctrlPressed)
                {
                    // Toggle selection
                    if (selectedAnnotations.Contains(hitElement))
                    {
                        selectedAnnotations.Remove(hitElement);
                        HighlightAnnotation(hitElement, false);
                    }
                    else
                    {
                        selectedAnnotations.Add(hitElement);
                        HighlightAnnotation(hitElement, true);
                    }
                }
                else
                {
                    bool isDoubleClick = (now - lastClickTime).TotalMilliseconds < 300 && lastClickedElement == hitElement;

                    if (isDoubleClick)
                    {
                        if (hitElement is Border border)
                        {
                            if (border.Child is TextBlock)
                                EditTextAnnotation(border);
                            else if (border.Child is Canvas lineCanvas && lineCanvas.Tag is LineShapeInfo)
                                EditLineAnnotation(border);
                        }
                    }
                    else
                    {
                        if (!selectedAnnotations.Contains(hitElement))
                        {
                            ClearSelection();
                            SelectAnnotation(hitElement);
                        }

                        isDragging = true;
                        dragStart = pos;
                        elementStart = new Point(Canvas.GetLeft(hitElement), Canvas.GetTop(hitElement));
                        ChartCanvas.CaptureMouse();
                    }
                }

                lastClickTime = now;
                lastClickedElement = hitElement;
                return;
            }

            // Start selection rectangle
            ClearSelection();
            isSelecting = true;
            selectionStart = pos;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, pos.X);
            Canvas.SetTop(SelectionRect, pos.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            ChartCanvas.CaptureMouse();

            // Double-click on empty area opens settings
            if ((now - lastClickTime).TotalMilliseconds < 300 && lastClickedElement == null)
            {
                OpenSettingsDialog();
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
                double w = Math.Abs(pos.X - selectionStart.X);
                double h = Math.Abs(pos.Y - selectionStart.Y);

                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = w;
                SelectionRect.Height = h;
                return;
            }

            if (!isDragging) return;

            bool shiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            double dx = pos.X - dragStart.X;
            double dy = pos.Y - dragStart.Y;

            double newX = elementStart.X + dx;
            double newY = elementStart.Y + dy;

            // Shift: Align to nearest X-axis data point
            if (shiftPressed && chartDataPoints.Count > 0)
            {
                double minDist = double.MaxValue;
                double nearestX = newX;

                foreach (var pt in chartDataPoints)
                {
                    double dist = Math.Abs(newX - pt.X);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestX = pt.X;
                    }
                }

                if (minDist < 20) newX = nearestX;
            }

            if (selectedAnnotation != null)
            {
                Canvas.SetLeft(selectedAnnotation, newX);
                Canvas.SetTop(selectedAnnotation, newY);
            }

            // Move all selected annotations together
            if (selectedAnnotations.Count > 1)
            {
                foreach (var ann in selectedAnnotations)
                {
                    if (ann != selectedAnnotation)
                    {
                        double annX = Canvas.GetLeft(ann) + dx;
                        double annY = Canvas.GetTop(ann) + dy;
                        Canvas.SetLeft(ann, annX);
                        Canvas.SetTop(ann, annY);
                    }
                }
                dragStart = pos;
            }
        }

        private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelecting)
            {
                // Select annotations within rectangle
                double x1 = Canvas.GetLeft(SelectionRect);
                double y1 = Canvas.GetTop(SelectionRect);
                double x2 = x1 + SelectionRect.Width;
                double y2 = y1 + SelectionRect.Height;

                foreach (var ann in annotations)
                {
                    double annX = Canvas.GetLeft(ann);
                    double annY = Canvas.GetTop(ann);

                    if (annX >= x1 && annX <= x2 && annY >= y1 && annY <= y2)
                    {
                        selectedAnnotations.Add(ann);
                        HighlightAnnotation(ann, true);
                    }
                }

                SelectionRect.Visibility = Visibility.Collapsed;
                isSelecting = false;
            }

            isDragging = false;
            isResizing = false;
            currentResizeHandle = "";
            currentLineCanvas = null;
            ChartCanvas.ReleaseMouseCapture();
        }

        #endregion

        #region Selection Helpers

        private void ClearSelection()
        {
            foreach (var ann in selectedAnnotations)
            {
                HighlightAnnotation(ann, false);
            }
            selectedAnnotations.Clear();

            if (selectedAnnotation != null)
            {
                HighlightAnnotation(selectedAnnotation, false);
                selectedAnnotation = null;
            }
        }

        private void HighlightAnnotation(FrameworkElement element, bool highlight)
        {
            if (element is Border border)
            {
                if (highlight)
                {
                    border.Tag = border.BorderBrush;
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.BorderBrush = border.Tag as Brush ?? Brushes.Transparent;
                    if (border.Child is TextBlock)
                    {
                        var fmt = border.DataContext as TextFormatInfo;
                        border.BorderThickness = fmt?.ShowBorder == true ? new Thickness(1) : new Thickness(0);
                    }
                    else
                    {
                        border.BorderThickness = new Thickness(0);
                    }
                }
            }
        }

        private FrameworkElement? FindAnnotationAt(Point pos)
        {
            foreach (var ann in annotations)
            {
                double left = Canvas.GetLeft(ann);
                double top = Canvas.GetTop(ann);
                double right = left + ann.ActualWidth;
                double bottom = top + ann.ActualHeight;

                if (ann is Border border && border.Child is Canvas canvas && canvas.Tag is LineShapeInfo info)
                {
                    if (info.Type == "Line" || info.Type == "Arrow")
                    {
                        Point p1 = new Point(left + info.StartX, top + info.StartY);
                        Point p2 = new Point(left + info.EndX, top + info.EndY);
                        double dist = DistanceToLineSegment(pos, p1, p2);
                        if (dist < 10) return ann;
                        continue;
                    }
                }

                if (pos.X >= left - 5 && pos.X <= right + 5 && pos.Y >= top - 5 && pos.Y <= bottom + 5)
                {
                    return ann;
                }
            }
            return null;
        }

        private double DistanceToLineSegment(Point p, Point p1, Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared == 0) return Math.Sqrt((p.X - p1.X) * (p.X - p1.X) + (p.Y - p1.Y) * (p.Y - p1.Y));

            double t = Math.Max(0, Math.Min(1, ((p.X - p1.X) * dx + (p.Y - p1.Y) * dy) / lengthSquared));
            double projX = p1.X + t * dx;
            double projY = p1.Y + t * dy;

            return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
        }

        private void SelectAnnotation(FrameworkElement? element)
        {
            selectedAnnotation = element;
            if (element != null)
            {
                selectedAnnotations.Add(element);
                HighlightAnnotation(element, true);
            }
        }

        #endregion

        #region Clipboard Operations

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            CopyChartToClipboard();
        }

        private void CopyChartToClipboard()
        {
            var renderBitmap = new RenderTargetBitmap(
                (int)ChartCanvas.ActualWidth, (int)ChartCanvas.ActualHeight,
                96, 96, PixelFormats.Pbgra32);

            renderBitmap.Render(ChartCanvas);

            Clipboard.SetImage(renderBitmap);
            MessageBox.Show("Chart copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Statistics

        private void OpenStatistics_Click(object sender, RoutedEventArgs e)
        {
            var statsWindow = new StatisticsWindow(dataSeries);
            statsWindow.Owner = this;
            statsWindow.Show();
        }

        #endregion

        #region Text Annotation

        private void AddTextBox_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextAnnotationDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                CreateTextAnnotation(dialog.AnnotationText, dialog.SelectedFontSize,
                    dialog.SelectedFontFamily, dialog.IsBold, dialog.IsItalic,
                    dialog.ShowBorder, dialog.TextColor);
            }
        }

        private void CreateTextAnnotation(string text, double fontSize, FontFamily fontFamily,
            bool isBold, bool isItalic, bool showBorder, Color textColor, double x = 100, double y = 100)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = fontFamily,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal,
                Foreground = new SolidColorBrush(textColor),
                Padding = new Thickness(5),
                TextWrapping = TextWrapping.Wrap
            };

            var border = new Border
            {
                Child = textBlock,
                BorderThickness = showBorder ? new Thickness(1) : new Thickness(0),
                BorderBrush = showBorder ? Brushes.Black : Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                Tag = showBorder ? Brushes.Black : Brushes.Transparent
            };

            border.DataContext = new TextFormatInfo
            {
                FontSize = fontSize,
                FontFamily = fontFamily,
                IsBold = isBold,
                IsItalic = isItalic,
                ShowBorder = showBorder,
                TextColor = textColor
            };

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            ChartCanvas.Children.Add(border);
            annotations.Add(border);
        }

        private void EditTextAnnotation(Border border)
        {
            if (border.Child is not TextBlock textBlock) return;

            var format = border.DataContext as TextFormatInfo ?? new TextFormatInfo();

            var dialog = new TextAnnotationDialog(textBlock.Text, format);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                textBlock.Text = dialog.AnnotationText;
                textBlock.FontSize = dialog.SelectedFontSize;
                textBlock.FontFamily = dialog.SelectedFontFamily;
                textBlock.FontWeight = dialog.IsBold ? FontWeights.Bold : FontWeights.Normal;
                textBlock.FontStyle = dialog.IsItalic ? FontStyles.Italic : FontStyles.Normal;
                textBlock.Foreground = new SolidColorBrush(dialog.TextColor);

                border.BorderThickness = dialog.ShowBorder ? new Thickness(1) : new Thickness(0);
                border.BorderBrush = dialog.ShowBorder ? Brushes.Black : Brushes.Transparent;
                border.Tag = dialog.ShowBorder ? Brushes.Black : Brushes.Transparent;

                border.DataContext = new TextFormatInfo
                {
                    FontSize = dialog.SelectedFontSize,
                    FontFamily = dialog.SelectedFontFamily,
                    IsBold = dialog.IsBold,
                    IsItalic = dialog.IsItalic,
                    ShowBorder = dialog.ShowBorder,
                    TextColor = dialog.TextColor
                };
            }
        }

        private void OpenSymbolPicker_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SymbolPickerDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedSymbol))
            {
                CreateTextAnnotation(dialog.SelectedSymbol, 14, new FontFamily("Segoe UI"),
                    false, false, false, Colors.Black);
            }
        }

        #endregion

        #region Line Annotation

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string lineType)
            {
                CreateLineAnnotation(lineType);
            }
        }

        private void EditLineAnnotation(Border border)
        {
            if (border.Child is not Canvas lineCanvas || lineCanvas.Tag is not LineShapeInfo info) return;

            var dialog = new LineSettingsDialog(info);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                info.Thickness = dialog.LineThickness;
                info.Stroke = new SolidColorBrush(dialog.LineColor);
                info.ArrowDirection = dialog.ArrowDirection;

                RedrawLineShape(lineCanvas, info);
                UpdateHandlePositions(lineCanvas);
            }
        }

        private void CreateLineAnnotation(string lineType)
        {
            var container = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };

            var info = new LineShapeInfo
            {
                Type = lineType,
                Stroke = Brushes.Black,
                Thickness = 2,
                Width = 100,
                Height = 40,
                LeftHeight = 40,
                RightHeight = 40,
                ArrowDirection = "Right",
                StartX = 5,
                StartY = 10,
                EndX = 105,
                EndY = 10
            };

            switch (lineType)
            {
                case "Line":
                case "Arrow":
                    container.Width = 110;
                    container.Height = 20;
                    info.Height = 20;
                    break;
                case "UShape":
                case "HShape":
                    container.Width = 100;
                    container.Height = 40;
                    break;
            }

            container.Tag = info;
            RedrawLineShape(container, info);
            AddResizeHandles(container, lineType);

            var border = new Border
            {
                Child = container,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll
            };

            Canvas.SetLeft(border, 150);
            Canvas.SetTop(border, 150);
            ChartCanvas.Children.Add(border);
            annotations.Add(border);
        }

        private void CreateLineFromInfo(LineShapeInfo info, double x, double y)
        {
            var container = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
            container.Width = info.Width + 10;
            container.Height = info.Height;

            var newInfo = CloneLineInfo(info);
            container.Tag = newInfo;
            RedrawLineShape(container, newInfo);
            AddResizeHandles(container, newInfo.Type);

            var border = new Border
            {
                Child = container,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll
            };

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            ChartCanvas.Children.Add(border);
            annotations.Add(border);
        }

        private LineShapeInfo CloneLineInfo(LineShapeInfo info)
        {
            return new LineShapeInfo
            {
                Type = info.Type,
                Stroke = info.Stroke,
                Thickness = info.Thickness,
                Width = info.Width,
                Height = info.Height,
                LeftHeight = info.LeftHeight,
                RightHeight = info.RightHeight,
                ArrowDirection = info.ArrowDirection,
                StartX = info.StartX,
                StartY = info.StartY,
                EndX = info.EndX,
                EndY = info.EndY
            };
        }

        private void RedrawLineShape(Canvas container, LineShapeInfo info)
        {
            ClearShapeLines(container);

            switch (info.Type)
            {
                case "Line":
                    var line = new Line
                    {
                        X1 = info.StartX,
                        Y1 = info.StartY,
                        X2 = info.EndX,
                        Y2 = info.EndY,
                        Stroke = info.Stroke,
                        StrokeThickness = info.Thickness,
                        Tag = "ShapeLine"
                    };
                    container.Children.Add(line);
                    break;

                case "Arrow":
                    DrawArrowShape(container, info);
                    break;

                case "UShape":
                    DrawUShape(container, info);
                    break;

                case "HShape":
                    DrawHShape(container, info);
                    break;
            }
        }

        private void DrawArrowShape(Canvas container, LineShapeInfo info)
        {
            double dx = info.EndX - info.StartX;
            double dy = info.EndY - info.StartY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0) length = 1;

            double ux = dx / length, uy = dy / length;
            double px = -uy, py = ux;
            double arrowSize = 10;
            string dir = info.ArrowDirection ?? "Right";

            double lsx = info.StartX, lsy = info.StartY, lex = info.EndX, ley = info.EndY;

            if (dir == "Left" || dir == "Both") { lsx += ux * arrowSize; lsy += uy * arrowSize; }
            if (dir == "Right" || dir == "Both") { lex -= ux * arrowSize; ley -= uy * arrowSize; }

            container.Children.Add(new Line
            {
                X1 = lsx,
                Y1 = lsy,
                X2 = lex,
                Y2 = ley,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });

            if (dir == "Right" || dir == "Both")
            {
                container.Children.Add(new Polygon
                {
                    Points = new PointCollection {
                        new Point(info.EndX - ux * arrowSize + px * arrowSize * 0.5, info.EndY - uy * arrowSize + py * arrowSize * 0.5),
                        new Point(info.EndX, info.EndY),
                        new Point(info.EndX - ux * arrowSize - px * arrowSize * 0.5, info.EndY - uy * arrowSize - py * arrowSize * 0.5)
                    },
                    Fill = info.Stroke,
                    Tag = "ShapeLine"
                });
            }

            if (dir == "Left" || dir == "Both")
            {
                container.Children.Add(new Polygon
                {
                    Points = new PointCollection {
                        new Point(info.StartX + ux * arrowSize + px * arrowSize * 0.5, info.StartY + uy * arrowSize + py * arrowSize * 0.5),
                        new Point(info.StartX, info.StartY),
                        new Point(info.StartX + ux * arrowSize - px * arrowSize * 0.5, info.StartY + uy * arrowSize - py * arrowSize * 0.5)
                    },
                    Fill = info.Stroke,
                    Tag = "ShapeLine"
                });
            }
        }

        private void DrawUShape(Canvas container, LineShapeInfo info)
        {
            container.Children.Add(new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = 0,
                Y2 = info.LeftHeight,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
            container.Children.Add(new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = info.Width,
                Y2 = 0,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
            container.Children.Add(new Line
            {
                X1 = info.Width,
                Y1 = 0,
                X2 = info.Width,
                Y2 = info.RightHeight,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
        }

        private void DrawHShape(Canvas container, LineShapeInfo info)
        {
            double midY = info.Height / 2;
            container.Children.Add(new Line
            {
                X1 = 0,
                Y1 = midY - info.LeftHeight / 2,
                X2 = 0,
                Y2 = midY + info.LeftHeight / 2,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
            container.Children.Add(new Line
            {
                X1 = 0,
                Y1 = midY,
                X2 = info.Width,
                Y2 = midY,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
            container.Children.Add(new Line
            {
                X1 = info.Width,
                Y1 = midY - info.RightHeight / 2,
                X2 = info.Width,
                Y2 = midY + info.RightHeight / 2,
                Stroke = info.Stroke,
                StrokeThickness = info.Thickness,
                Tag = "ShapeLine"
            });
        }

        private void ClearShapeLines(Canvas container)
        {
            var toRemove = container.Children.OfType<FrameworkElement>().Where(c => c.Tag?.ToString() != "Handle").ToList();
            foreach (var item in toRemove) container.Children.Remove(item);
        }

        private void AddResizeHandles(Canvas container, string lineType)
        {
            var color = new SolidColorBrush(Color.FromRgb(33, 150, 243));

            switch (lineType)
            {
                case "Line":
                case "Arrow":
                    AddHandle(container, "Start", color, Cursors.Cross);
                    AddHandle(container, "End", color, Cursors.Cross);
                    break;
                case "UShape":
                    AddHandle(container, "LeftBottom", color, Cursors.SizeNS);
                    AddHandle(container, "RightBottom", color, Cursors.SizeNS);
                    AddHandle(container, "TopRight", color, Cursors.SizeWE);
                    break;
                case "HShape":
                    AddHandle(container, "LeftTop", color, Cursors.SizeNS);
                    AddHandle(container, "LeftBottom", color, Cursors.SizeNS);
                    AddHandle(container, "RightTop", color, Cursors.SizeNS);
                    AddHandle(container, "RightBottom", color, Cursors.SizeNS);
                    AddHandle(container, "RightMid", color, Cursors.SizeWE);
                    break;
            }
            UpdateHandlePositions(container);
        }

        private void AddHandle(Canvas container, string name, Brush color, Cursor cursor)
        {
            var handle = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = color,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Cursor = cursor,
                Tag = "Handle"
            };
            handle.SetValue(FrameworkElement.NameProperty, name);
            handle.MouseLeftButtonDown += Handle_MouseDown;
            handle.MouseMove += Handle_MouseMove;
            handle.MouseLeftButtonUp += Handle_MouseUp;
            container.Children.Add(handle);
        }

        private void UpdateHandlePositions(Canvas container)
        {
            if (container.Tag is not LineShapeInfo info) return;

            foreach (var child in container.Children.OfType<Ellipse>())
            {
                if (child.Tag?.ToString() != "Handle") continue;
                string name = child.GetValue(FrameworkElement.NameProperty) as string ?? "";

                switch (name)
                {
                    case "Start": Canvas.SetLeft(child, info.StartX - 5); Canvas.SetTop(child, info.StartY - 5); break;
                    case "End": Canvas.SetLeft(child, info.EndX - 5); Canvas.SetTop(child, info.EndY - 5); break;
                    case "LeftBottom":
                        Canvas.SetLeft(child, -5);
                        Canvas.SetTop(child, info.Type == "UShape" ? info.LeftHeight - 5 : info.Height / 2 + info.LeftHeight / 2 - 5); break;
                    case "LeftTop": Canvas.SetLeft(child, -5); Canvas.SetTop(child, info.Height / 2 - info.LeftHeight / 2 - 5); break;
                    case "RightBottom":
                        Canvas.SetLeft(child, info.Width - 5);
                        Canvas.SetTop(child, info.Type == "UShape" ? info.RightHeight - 5 : info.Height / 2 + info.RightHeight / 2 - 5); break;
                    case "RightTop": Canvas.SetLeft(child, info.Width - 5); Canvas.SetTop(child, info.Height / 2 - info.RightHeight / 2 - 5); break;
                    case "TopRight": Canvas.SetLeft(child, info.Width - 5); Canvas.SetTop(child, -5); break;
                    case "RightMid": Canvas.SetLeft(child, info.Width - 5); Canvas.SetTop(child, info.Height / 2 - 5); break;
                }
            }
        }

        private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse handle && handle.Parent is Canvas container)
            {
                isResizing = true;
                currentResizeHandle = handle.GetValue(FrameworkElement.NameProperty) as string ?? "";
                currentLineCanvas = container;
                dragStart = e.GetPosition(ChartCanvas);
                handle.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Handle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isResizing || currentLineCanvas == null) return;
            if (currentLineCanvas.Tag is not LineShapeInfo info) return;

            var pos = e.GetPosition(ChartCanvas);
            var border = currentLineCanvas.Parent as Border;
            if (border == null) return;

            double containerLeft = Canvas.GetLeft(border);
            double containerTop = Canvas.GetTop(border);
            double localX = pos.X - containerLeft;
            double localY = pos.Y - containerTop;

            bool shiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (currentResizeHandle == "Start" || currentResizeHandle == "End")
            {
                double anchorX = currentResizeHandle == "Start" ? info.EndX : info.StartX;
                double anchorY = currentResizeHandle == "Start" ? info.EndY : info.StartY;

                double targetX = localX, targetY = localY;

                if (shiftPressed)
                {
                    double dx = localX - anchorX, dy = localY - anchorY;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length > 0)
                    {
                        double angle = Math.Atan2(dy, dx);
                        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
                        targetX = anchorX + length * Math.Cos(snapped);
                        targetY = anchorY + length * Math.Sin(snapped);
                    }
                }

                if (currentResizeHandle == "Start") { info.StartX = targetX; info.StartY = targetY; }
                else { info.EndX = targetX; info.EndY = targetY; }

                double minX = Math.Min(info.StartX, info.EndX) - 15;
                double minY = Math.Min(info.StartY, info.EndY) - 15;

                if (minX < 0) { info.StartX -= minX; info.EndX -= minX; Canvas.SetLeft(border, containerLeft + minX); }
                if (minY < 0) { info.StartY -= minY; info.EndY -= minY; Canvas.SetTop(border, containerTop + minY); }

                currentLineCanvas.Width = Math.Max(info.StartX, info.EndX) + 15;
                currentLineCanvas.Height = Math.Max(info.StartY, info.EndY) + 15;
            }
            else
            {
                double dx = pos.X - dragStart.X, dy = pos.Y - dragStart.Y;

                switch (currentResizeHandle)
                {
                    case "TopRight": case "RightMid": info.Width = Math.Max(20, info.Width + dx); currentLineCanvas.Width = info.Width; break;
                    case "LeftBottom":
                        if (info.Type == "UShape") { info.LeftHeight = Math.Max(10, info.LeftHeight + dy); info.Height = Math.Max(info.LeftHeight, info.RightHeight); }
                        else info.LeftHeight = Math.Max(10, info.LeftHeight + dy * 2);
                        break;
                    case "LeftTop": info.LeftHeight = Math.Max(10, info.LeftHeight - dy * 2); break;
                    case "RightBottom":
                        if (info.Type == "UShape") { info.RightHeight = Math.Max(10, info.RightHeight + dy); info.Height = Math.Max(info.LeftHeight, info.RightHeight); }
                        else info.RightHeight = Math.Max(10, info.RightHeight + dy * 2);
                        break;
                    case "RightTop": info.RightHeight = Math.Max(10, info.RightHeight - dy * 2); break;
                }

                if (info.Type == "HShape") { info.Height = Math.Max(info.LeftHeight, info.RightHeight) + 10; currentLineCanvas.Height = info.Height; }
                if (info.Type == "UShape") currentLineCanvas.Height = info.Height;
                dragStart = pos;
            }

            RedrawLineShape(currentLineCanvas, info);
            UpdateHandlePositions(currentLineCanvas);
            e.Handled = true;
        }

        private void Handle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isResizing = false;
            currentResizeHandle = "";
            currentLineCanvas = null;
            if (sender is Ellipse handle) handle.ReleaseMouseCapture();
            e.Handled = true;
        }

        #endregion

        #region Delete

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedAnnotations();
        }

        #endregion

        #region Settings Dialog

        private void OpenSettingsDialog()
        {
            var dialog = new ChartSettingsWindow(dataSeries, chartTitle, xAxisTitle, yAxisTitle, chartType);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                chartTitle = dialog.ChartTitle;
                xAxisTitle = dialog.XAxisTitle;
                yAxisTitle = dialog.YAxisTitle;
                DrawChart();
            }
        }

        #endregion

        #region Chart Drawing

        private void DrawChart()
        {
            var savedAnnotations = annotations.ToList();
            var positions = savedAnnotations.Select(a => new Point(Canvas.GetLeft(a), Canvas.GetTop(a))).ToList();

            ChartCanvas.Children.Clear();
            LegendPanel.Children.Clear();
            chartDataPoints.Clear();

            if (dataSeries == null || dataSeries.Count == 0) return;

            double width = ChartCanvas.ActualWidth, height = ChartCanvas.ActualHeight;
            if (width < 100 || height < 100) return;

            double plotWidth = width - marginLeft - marginRight;
            double plotHeight = height - marginTop - marginBottom;

            double xMin = dataSeries.SelectMany(s => s.XValues).DefaultIfEmpty(0).Min();
            double xMax = dataSeries.SelectMany(s => s.XValues).DefaultIfEmpty(1).Max();
            double yMin = 0;
            double yMax = dataSeries.SelectMany(s => s.YValues).DefaultIfEmpty(1).Max();

            double xPad = (xMax - xMin) * 0.1, yPad = yMax * 0.1;
            if (xPad == 0) xPad = 1; if (yPad == 0) yPad = 1;
            xMin -= xPad; xMax += xPad; yMax += yPad;

            if (errorType != "None")
            {
                foreach (var s in dataSeries)
                {
                    for (int i = 0; i < s.YValues.Count; i++)
                    {
                        double err = 0;
                        if (errorType == "SD" && i < s.YErrors.Count)
                            err = s.YErrors[i];
                        else if (errorType == "SEM" && i < s.SEMValues.Count)
                            err = s.SEMValues[i];
                        else if (errorType == "95CI" && i < s.SEMValues.Count && i < s.NValues.Count)
                        {
                            double sem = s.SEMValues[i];
                            int n = s.NValues[i];
                            err = GetTCritical(n - 1) * sem;
                        }
                        yMax = Math.Max(yMax, s.YValues[i] + err);
                    }
                }
            }

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

            DrawGridLines(plotWidth, plotHeight);
            DrawAxes(plotWidth, plotHeight, xMin, xMax, yMin, yMax);

            foreach (var series in dataSeries)
                DrawSeries(series, plotWidth, plotHeight, xMin, xMax, yMin, yMax);

            DrawTitle();
            BuildLegend();

            for (int i = 0; i < savedAnnotations.Count; i++)
            {
                ChartCanvas.Children.Add(savedAnnotations[i]);
                Canvas.SetLeft(savedAnnotations[i], positions[i].X);
                Canvas.SetTop(savedAnnotations[i], positions[i].Y);
            }
            annotations = savedAnnotations;
        }

        private void DrawTitle()
        {
            double width = ChartCanvas.ActualWidth;
            string[] parts = chartTitle.Split('|');
            string main = parts[0].Trim();
            string sub = parts.Length > 1 ? parts[1].Trim() : "";

            if (!string.IsNullOrEmpty(main))
            {
                var t = new TextBlock { Text = main, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, (width - t.DesiredSize.Width) / 2);
                Canvas.SetTop(t, 8);
                ChartCanvas.Children.Add(t);
            }

            if (!string.IsNullOrEmpty(sub))
            {
                var s = new TextBlock { Text = sub, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)) };
                s.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(s, (width - s.DesiredSize.Width) / 2);
                Canvas.SetTop(s, 28);
                ChartCanvas.Children.Add(s);
            }
        }

        private void DrawGridLines(double plotWidth, double plotHeight)
        {
            var brush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            for (int i = 0; i <= 5; i++)
            {
                double y = marginTop + plotHeight - (plotHeight * i / 5);
                ChartCanvas.Children.Add(new Line { X1 = marginLeft, Y1 = y, X2 = marginLeft + plotWidth, Y2 = y, Stroke = brush, StrokeThickness = 1 });
                double x = marginLeft + (plotWidth * i / 5);
                ChartCanvas.Children.Add(new Line { X1 = x, Y1 = marginTop, X2 = x, Y2 = marginTop + plotHeight, Stroke = brush, StrokeThickness = 1 });
            }
        }

        private void DrawAxes(double plotWidth, double plotHeight, double xMin, double xMax, double yMin, double yMax)
        {
            for (int i = 0; i <= 5; i++)
            {
                double yVal = yMin + (yMax - yMin) * i / 5;
                double y = marginTop + plotHeight - (plotHeight * i / 5);
                var lbl = new TextBlock { Text = yVal.ToString("F1"), FontSize = 10, Foreground = Brushes.Black };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, marginLeft - lbl.DesiredSize.Width - 5);
                Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
                ChartCanvas.Children.Add(lbl);

                double xVal = xMin + (xMax - xMin) * i / 5;
                double x = marginLeft + (plotWidth * i / 5);
                var xlbl = new TextBlock { Text = xVal.ToString("F1"), FontSize = 10, Foreground = Brushes.Black };
                xlbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(xlbl, x - xlbl.DesiredSize.Width / 2);
                Canvas.SetTop(xlbl, marginTop + plotHeight + 5);
                ChartCanvas.Children.Add(xlbl);
            }

            var xt = new TextBlock { Text = xAxisTitle, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
            xt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(xt, marginLeft + (plotWidth - xt.DesiredSize.Width) / 2);
            Canvas.SetTop(xt, marginTop + plotHeight + 25);
            ChartCanvas.Children.Add(xt);

            var yt = new TextBlock
            {
                Text = yAxisTitle,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                RenderTransform = new RotateTransform(-90)
            };
            yt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(yt, 10);
            Canvas.SetTop(yt, marginTop + (plotHeight + yt.DesiredSize.Width) / 2);
            ChartCanvas.Children.Add(yt);
        }

        private void DrawSeries(ChartDataSeries series, double plotWidth, double plotHeight, double xMin, double xMax, double yMin, double yMax)
        {
            if (series.XValues.Count == 0 || series.YValues.Count == 0) return;

            var brush = new SolidColorBrush(series.LineColor);
            var points = new List<Point>();

            for (int i = 0; i < series.XValues.Count && i < series.YValues.Count; i++)
            {
                double x = marginLeft + ((series.XValues[i] - xMin) / (xMax - xMin)) * plotWidth;
                double y = marginTop + plotHeight - ((series.YValues[i] - yMin) / (yMax - yMin)) * plotHeight;
                points.Add(new Point(x, y));
                chartDataPoints.Add(new Point(x, y)); // Store for alignment
            }

            if (chartType == "Line" && points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                    ChartCanvas.Children.Add(new Line
                    {
                        X1 = points[i].X,
                        Y1 = points[i].Y,
                        X2 = points[i + 1].X,
                        Y2 = points[i + 1].Y,
                        Stroke = brush,
                        StrokeThickness = series.LineThickness
                    });
            }

            if (errorType != "None")
            {
                for (int i = 0; i < points.Count; i++)
                {
                    double err = GetErrorValue(series, i, yMax - yMin, plotHeight);
                    if (err > 0)
                        DrawErrorBar(points[i], err, brush);
                }
            }

            foreach (var pt in points) DrawMarker(pt, series.MarkerSize, series.MarkerShape, brush);
        }

        private void DrawErrorBar(Point c, double err, Brush brush)
        {
            ChartCanvas.Children.Add(new Line { X1 = c.X, Y1 = c.Y - err, X2 = c.X, Y2 = c.Y + err, Stroke = brush, StrokeThickness = 1 });
            ChartCanvas.Children.Add(new Line { X1 = c.X - 4, Y1 = c.Y - err, X2 = c.X + 4, Y2 = c.Y - err, Stroke = brush, StrokeThickness = 1 });
            ChartCanvas.Children.Add(new Line { X1 = c.X - 4, Y1 = c.Y + err, X2 = c.X + 4, Y2 = c.Y + err, Stroke = brush, StrokeThickness = 1 });
        }

        private double GetErrorValue(ChartDataSeries series, int index, double yRange, double plotHeight)
        {
            double errValue = 0;

            if (errorType == "SD" && index < series.YErrors.Count)
            {
                errValue = series.YErrors[index];
            }
            else if (errorType == "SEM" && index < series.SEMValues.Count)
            {
                errValue = series.SEMValues[index];
            }
            else if (errorType == "95CI" && index < series.SEMValues.Count && index < series.NValues.Count)
            {
                double sem = series.SEMValues[index];
                int n = series.NValues[index];
                double tCrit = GetTCritical(n - 1);
                errValue = tCrit * sem;
            }

            return (errValue / yRange) * plotHeight;
        }

        private double GetTCritical(int df)
        {
            if (df <= 0) return 1.96;
            if (df == 1) return 12.706;
            if (df == 2) return 4.303;
            if (df == 3) return 3.182;
            if (df == 4) return 2.776;
            if (df == 5) return 2.571;
            if (df <= 10) return 2.228;
            if (df <= 20) return 2.086;
            if (df <= 30) return 2.042;
            return 1.96;
        }

        private void DrawMarker(Point c, int size, string shape, Brush brush)
        {
            Shape marker;
            switch (shape)
            {
                case "Square":
                    marker = new Rectangle { Width = size, Height = size, Fill = brush };
                    Canvas.SetLeft(marker, c.X - size / 2); Canvas.SetTop(marker, c.Y - size / 2);
                    break;
                case "Triangle":
                    ChartCanvas.Children.Add(new Polygon
                    {
                        Points = new PointCollection {
                        new Point(c.X, c.Y - size / 2), new Point(c.X - size / 2, c.Y + size / 2), new Point(c.X + size / 2, c.Y + size / 2)
                    },
                        Fill = brush
                    });
                    return;
                case "Diamond":
                    ChartCanvas.Children.Add(new Polygon
                    {
                        Points = new PointCollection {
                        new Point(c.X, c.Y - size / 2), new Point(c.X + size / 2, c.Y),
                        new Point(c.X, c.Y + size / 2), new Point(c.X - size / 2, c.Y)
                    },
                        Fill = brush
                    });
                    return;
                default:
                    marker = new Ellipse { Width = size, Height = size, Fill = brush };
                    Canvas.SetLeft(marker, c.X - size / 2); Canvas.SetTop(marker, c.Y - size / 2);
                    break;
            }
            ChartCanvas.Children.Add(marker);
        }

        private void BuildLegend()
        {
            foreach (var s in dataSeries)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 10, 5) };
                sp.Children.Add(new Rectangle { Width = 16, Height = 16, Fill = new SolidColorBrush(s.LineColor), Margin = new Thickness(0, 0, 5, 0) });
                sp.Children.Add(new TextBlock { Text = s.Name, VerticalAlignment = VerticalAlignment.Center });
                LegendPanel.Children.Add(sp);
            }
        }

        #endregion

        #region Save

        private void SaveAsPng_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "chart.png" };
            if (dlg.ShowDialog() == true)
            {
                var bmp = new RenderTargetBitmap((int)ChartCanvas.ActualWidth, (int)ChartCanvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(ChartCanvas);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var s = File.Create(dlg.FileName)) enc.Save(s);
                MessageBox.Show("Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveAsSvg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "SVG File|*.svg", FileName = "chart.svg" };
            if (dlg.ShowDialog() == true)
            {
                using (var w = new StreamWriter(dlg.FileName))
                {
                    w.WriteLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{ChartCanvas.ActualWidth}\" height=\"{ChartCanvas.ActualHeight}\">");
                    w.WriteLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
                    foreach (var c in ChartCanvas.Children)
                    {
                        if (c is Line l)
                        {
                            var col = (l.Stroke as SolidColorBrush)?.Color ?? Colors.Black;
                            w.WriteLine($"<line x1=\"{l.X1}\" y1=\"{l.Y1}\" x2=\"{l.X2}\" y2=\"{l.Y2}\" stroke=\"#{col.R:X2}{col.G:X2}{col.B:X2}\" stroke-width=\"{l.StrokeThickness}\"/>");
                        }
                        else if (c is Ellipse el && el.Tag?.ToString() != "Handle")
                        {
                            var col = (el.Fill as SolidColorBrush)?.Color ?? Colors.Black;
                            double cx = Canvas.GetLeft(el) + el.Width / 2, cy = Canvas.GetTop(el) + el.Height / 2;
                            w.WriteLine($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{el.Width / 2}\" fill=\"#{col.R:X2}{col.G:X2}{col.B:X2}\"/>");
                        }
                    }
                    w.WriteLine("</svg>");
                }
                MessageBox.Show("Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion
    }

    #region Helper Classes

    public class TextFormatInfo
    {
        public double FontSize { get; set; } = 12;
        public FontFamily FontFamily { get; set; } = new FontFamily("Segoe UI");
        public bool IsBold { get; set; } = false;
        public bool IsItalic { get; set; } = false;
        public bool ShowBorder { get; set; } = false;
        public Color TextColor { get; set; } = Colors.Black;
    }

    public class LineShapeInfo
    {
        public string Type { get; set; } = "Line";
        public Brush Stroke { get; set; } = Brushes.Black;
        public double Thickness { get; set; } = 2;
        public double Width { get; set; } = 100;
        public double Height { get; set; } = 40;
        public double LeftHeight { get; set; } = 40;
        public double RightHeight { get; set; } = 40;
        public string ArrowDirection { get; set; } = "Right";
        public double StartX { get; set; } = 0;
        public double StartY { get; set; } = 5;
        public double EndX { get; set; } = 100;
        public double EndY { get; set; } = 5;
    }

    public class AnnotationClipboardItem
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
        public TextFormatInfo? FormatInfo { get; set; }
        public LineShapeInfo? LineInfo { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
    }

    #endregion
}