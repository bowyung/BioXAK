using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BioSAK.Pages
{
    public partial class ProteinBCA : Page
    {
        // === 資料模型 ===
        public class StandardPoint : INotifyPropertyChanged
        {
            private double _conc; private double _od1; private double _od2;
            public double Concentration { get => _conc; set { _conc = value; OnPropertyChanged(); } }
            public double OD1 { get => _od1; set { _od1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Average)); } }
            public double OD2 { get => _od2; set { _od2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Average)); } }
            public double Average => (OD1 + OD2) / 2.0;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class SampleData : INotifyPropertyChanged
        {
            public string? ID { get; set; } = "1";
            public double OD1 { get; set; }
            public double OD2 { get; set; }
            // Dilution 已移除，改用 Global Setting

            private double _calcConc;
            public double CalculatedConc { get => _calcConc; set { _calcConc = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class RecipeData
        {
            public string? SampleID { get; set; }
            public double SampleVol { get; set; }
            public double WaterVol { get; set; }
            public double BufferVol { get; set; }
        }

        // === 變數 ===
        public ObservableCollection<StandardPoint> Standards { get; set; }
        public ObservableCollection<SampleData> Samples { get; set; }
        public ObservableCollection<RecipeData> Recipes { get; set; }

        private double _slope = 0;
        private double _intercept = 0;

        public ProteinBCA()
        {
            InitializeComponent();

            Standards = new ObservableCollection<StandardPoint>
            {
                new StandardPoint { Concentration = 0 },
                new StandardPoint { Concentration = 125 },
                new StandardPoint { Concentration = 250 },
                new StandardPoint { Concentration = 500 },
                new StandardPoint { Concentration = 1000 }
            };
            GridStandards.ItemsSource = Standards;

            Samples = new ObservableCollection<SampleData>
            {
                new SampleData { ID = "1", OD1=0, OD2=0 }
            };
            GridSamples.ItemsSource = Samples;

            Recipes = new ObservableCollection<RecipeData>();
            GridRecipe.ItemsSource = Recipes;
        }

        // === 1. 刪除功能 ===
        private void DeleteStandardRow_Click(object sender, RoutedEventArgs e)
        {
            var list = GridStandards.SelectedItems.OfType<StandardPoint>().ToList();
            foreach (var item in list) Standards.Remove(item);
        }

        private void DeleteSampleRow_Click(object sender, RoutedEventArgs e)
        {
            var list = GridSamples.SelectedItems.OfType<SampleData>().ToList();
            foreach (var item in list) Samples.Remove(item);
        }

        // === 2. 智慧貼上功能 (核心修改) ===
        // 這一段邏輯改為從「滑鼠選取的格子」開始貼上，並正確對應欄位

        private void GridSamples_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteToGrid(GridSamples, Samples);
                e.Handled = true;
            }
        }

        private void GridStandards_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteToGrid(GridStandards, Standards);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 通用的貼上邏輯：偵測目前選取的 Cell (Row/Column) 並貼上資料
        /// </summary>
        private void PasteToGrid<T>(DataGrid grid, ObservableCollection<T> collection) where T : new()
        {
            string clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText)) return;

            string[] rows = clipboardText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0) return;

            // 1. 取得起始位置
            int startRowIndex = 0;
            int startColIndex = 0;

            // 正確：直接檢查 IsValid 即可
            if (grid.CurrentCell.IsValid)
            {
                // 取得當前 Item 在 Collection 中的 Index
                startRowIndex = grid.Items.IndexOf(grid.CurrentCell.Item);

                // 取得當前 Column 的 DisplayIndex
                if (grid.CurrentColumn != null)
                {
                    startColIndex = grid.CurrentColumn.DisplayIndex;
                }
            }

            // 如果選取位置無效，預設從 0,0 開始
            if (startRowIndex < 0) startRowIndex = 0;

            // 2. 迴圈填入
            for (int r = 0; r < rows.Length; r++)
            {
                string[] cells = rows[r].Split('\t');
                int targetRowIndex = startRowIndex + r;

                // 如果目標行超過目前資料長度，則新增一行
                T dataItem;
                if (targetRowIndex < collection.Count)
                {
                    dataItem = collection[targetRowIndex];
                }
                else
                {
                    dataItem = new T();
                    // 特殊處理：如果是 SampleData，自動生成 ID
                    if (dataItem is SampleData sample)
                    {
                        sample.ID = (targetRowIndex + 1).ToString();
                    }
                    collection.Add(dataItem);
                }

                // 3. 欄位填入 (根據 startColIndex 偏移)
                for (int c = 0; c < cells.Length; c++)
                {
                    int targetColIndex = startColIndex + c;
                    string val = cells[c];

                    if (string.IsNullOrWhiteSpace(val)) continue;
                    if (!double.TryParse(val, out double numVal)) continue; // 這裡假設貼上的都是數字，如果是 ID (字串) 需額外判斷

                    // 根據 Data Type 分配數值
                    if (dataItem is SampleData s)
                    {
                        // GridSamples Columns: [0]=ID, [1]=OD1, [2]=OD2
                        if (targetColIndex == 0) s.ID = val; // ID 是字串
                        else if (targetColIndex == 1) s.OD1 = numVal;
                        else if (targetColIndex == 2) s.OD2 = numVal;
                    }
                    else if (dataItem is StandardPoint std)
                    {
                        // GridStandards Columns: [0]=Conc, [1]=OD1, [2]=OD2
                        if (targetColIndex == 0) std.Concentration = numVal;
                        else if (targetColIndex == 1) std.OD1 = numVal;
                        else if (targetColIndex == 2) std.OD2 = numVal;
                    }
                }
            }
        }

        // === 3. 計算與匯出 ===

        private void CalculateAll_Click(object sender, RoutedEventArgs e)
        {
            PerformRegression();

            // 讀取 Global Settings
            if (!double.TryParse(TxtTargetMass.Text, out double targetMass)) targetMass = 20;
            if (!double.TryParse(TxtTotalVol.Text, out double totalVol)) totalVol = 20;
            if (!double.TryParse(TxtGlobalDilution.Text, out double globalDilution)) globalDilution = 1;

            double bufferFold = 4;
            if (ComboBuffer.SelectedItem is ComboBoxItem item)
            {
                string? content = item.Content?.ToString();
                if (content != null) double.TryParse(content.Replace("x", ""), out bufferFold);
            }

            double reqBufferVol = totalVol / bufferFold;
            Recipes.Clear();

            foreach (var sample in Samples)
            {
                double avgOD = (sample.OD1 + sample.OD2) / 2.0;

                // Conc = Slope * OD + Intercept
                double rawConc_ng_ul = (_slope * avgOD) + _intercept;

                // 使用 Global Dilution 計算
                double finalConc_ug_ul = (rawConc_ng_ul / 1000.0) * globalDilution;

                if (finalConc_ug_ul < 0) finalConc_ug_ul = 0;
                sample.CalculatedConc = finalConc_ug_ul;

                double reqSampleVol = 0;
                double reqWaterVol = 0;

                if (finalConc_ug_ul > 0)
                {
                    reqSampleVol = targetMass / finalConc_ug_ul;
                    reqWaterVol = totalVol - reqBufferVol - reqSampleVol;
                }

                // 數值防呆
                if (reqWaterVol < 0) { reqWaterVol = 0; reqSampleVol = totalVol - reqBufferVol; }

                Recipes.Add(new RecipeData
                {
                    SampleID = sample.ID,
                    SampleVol = reqSampleVol,
                    WaterVol = reqWaterVol,
                    BufferVol = reqBufferVol
                });
            }
        }

        private void CopyRecipe_Click(object sender, RoutedEventArgs e)
        {
            GridRecipe.SelectAllCells();
            GridRecipe.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
            ApplicationCommands.Copy.Execute(null, GridRecipe);
            GridRecipe.UnselectAllCells();
        }

        // === 4. 回歸與繪圖 (邏輯保持不變) ===
        private void PerformRegression()
        {
            var points = Standards.Where(p => p.Average > 0 || p.Concentration == 0).ToList();
            if (points.Count < 2) return;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int n = points.Count;

            foreach (var p in points)
            {
                double x = p.Average; double y = p.Concentration;
                sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
            }

            double denominator = (n * sumX2 - sumX * sumX);
            if (Math.Abs(denominator) < 1e-9) return;

            _slope = (n * sumXY - sumX * sumY) / denominator;
            _intercept = (sumY - _slope * sumX) / n;

            double meanY = sumY / n;
            double ssTot = points.Sum(p => Math.Pow(p.Concentration - meanY, 2));
            double ssRes = points.Sum(p => Math.Pow(p.Concentration - (_slope * p.Average + _intercept), 2));
            double r2 = (ssTot != 0) ? 1 - (ssRes / ssTot) : 0;

            EquationText.Text = $"Conc = {_slope:F2} * OD + {_intercept:F2}";
            R2Text.Text = $"R² = {r2:F4}";

            DrawChart(points);
        }

        private void DrawChart(List<StandardPoint> points)
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.ActualWidth; double h = ChartCanvas.ActualHeight;
            if (w == 0 || h == 0) return;

            double maxX = points.Max(p => p.Average);
            double maxY = points.Max(p => p.Concentration);
            maxX = (maxX == 0) ? 1 : maxX * 1.1;
            maxY = (maxY == 0) ? 1 : maxY * 1.1;

            foreach (var p in points)
            {
                double px = (p.Average / maxX) * w;
                double py = h - ((p.Concentration / maxY) * h);
                Ellipse point = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Blue };
                Canvas.SetLeft(point, px - 3); Canvas.SetTop(point, py - 3);
                ChartCanvas.Children.Add(point);
            }

            double x1 = 0, x2 = maxX;
            double y1 = _slope * x1 + _intercept;
            double y2 = _slope * x2 + _intercept;

            Line line = new Line
            {
                X1 = (x1 / maxX) * w,
                Y1 = h - (y1 / maxY) * h,
                X2 = (x2 / maxX) * w,
                Y2 = h - (y2 / maxY) * h,
                Stroke = Brushes.Red,
                StrokeThickness = 1.5
            };
            ChartCanvas.Children.Add(line);
        }
    }
}