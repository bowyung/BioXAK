using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using BioSAK.Services;

namespace BioSAK.Pages
{
    public partial class TcgaAnalysisPage : Page
    {
        private TcgaGeneHelper _geneHelper;
        private readonly TcgaDataService _dataService;
        private ObservableCollection<TcgaProjectIndex> _availableCancers;
        private ObservableCollection<TcgaProjectIndex> _selectedCancers;
        private List<TcgaProjectIndex> _allProjects;

        // Box Plot data
        private List<BoxPlotStatsRow> _currentBoxPlotStats;
        // Correlation data
        private double[,] _currentCorrelationMatrix;
        private List<string> _currentCorrelationGenes;
        // Scatter data
        private List<ScatterDataPoint> _currentScatterPoints;
        private string _currentGeneX, _currentGeneY, _currentCancer;
        private bool _geneHelperReady = false;
        private double _scatterR, _scatterR2, _scatterPValue, _scatterSlope, _scatterIntercept;
        private int _scatterN;

        // Kaplan-Meier data
        private GeneSurvivalResult _currentSurvivalData;
        private KMComparisonResult _currentKMResult;
        private string _currentKMGene, _currentKMCancer;


        // Volcano data
        private VolcanoPlotResult _currentVolcanoData;
        private string _currentVolcanoCancer;
        private WriteableBitmap _volcanoBitmap;  // 效能優化用

        // Co-Expression data
        private GeneCorrelationResult _currentCoExprData;
        private List<CoExprDisplayRow> _currentCoExprDisplayRows;
        private string _currentCoExprCancer;

        // Colors
        private static readonly Color NormalColor = Color.FromRgb(52, 152, 219);
        private static readonly Color TumorColor = Color.FromRgb(231, 76, 60);
        private static readonly Brush NormalBrush = new SolidColorBrush(NormalColor);
        private static readonly Brush TumorBrush = new SolidColorBrush(TumorColor);
        private static readonly Color HighExprColor = Color.FromRgb(231, 76, 60);
        private static readonly Color LowExprColor = Color.FromRgb(52, 152, 219);
        private static readonly Brush HighExprBrush = new SolidColorBrush(HighExprColor);
        private static readonly Brush LowExprBrush = new SolidColorBrush(LowExprColor);

        public TcgaAnalysisPage()
        {
            InitializeComponent();
            _dataService = new TcgaDataService();
            _availableCancers = new ObservableCollection<TcgaProjectIndex>();
            _selectedCancers = new ObservableCollection<TcgaProjectIndex>();

            AvailableCancerList.ItemsSource = _availableCancers;
            SelectedCancerList.ItemsSource = _selectedCancers;
            _geneHelper = new TcgaGeneHelper();
            Loaded += TcgaAnalysisPage_Loaded;

            // 攔截 DataGrid 欄位點擊排序，改用 stable sort 邏輯
            CoExprDataGrid.Sorting += CoExprDataGrid_Sorting;
        }

        private string _coExprSortColumn = "|r|"; // 記錄目前排序欄位
        private bool _coExprSortAscending = false;
        private string _coExprPrevSortColumn = "P-value"; // 上一個排序欄位
        private bool _coExprPrevSortAscending = true;    // 上一個排序方向

        private void CoExprDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            e.Column.SortDirection = null;

            string header = e.Column.Header?.ToString() ?? "";

            if (_coExprSortColumn == header)
            {
                // 同欄位再點：切換方向，次要排序不變
                _coExprSortAscending = !_coExprSortAscending;
            }
            else
            {
                // 換欄位：舊的主排序變成次要排序
                _coExprPrevSortColumn = _coExprSortColumn;
                _coExprPrevSortAscending = _coExprSortAscending;
                _coExprSortColumn = header;
                _coExprSortAscending = header == "P-value" || header == "FDR";
            }

            e.Column.SortDirection = _coExprSortAscending
                ? System.ComponentModel.ListSortDirection.Ascending
                : System.ComponentModel.ListSortDirection.Descending;

            ApplyCoExprSort();
        }

        private IOrderedEnumerable<CoExprDisplayRow> ApplySecondarySort(
            IOrderedEnumerable<CoExprDisplayRow> primary)
        {
            // 次要排序：上一個主排序的欄位＋方向
            return _coExprPrevSortColumn switch
            {
                "P-value" => _coExprPrevSortAscending
                    ? primary.ThenBy(r => r.PValue)
                    : primary.ThenByDescending(r => r.PValue),
                "FDR" => _coExprPrevSortAscending
                    ? primary.ThenBy(r => r.FDR)
                    : primary.ThenByDescending(r => r.FDR),
                "Direction" => _coExprPrevSortAscending
                    ? primary.ThenBy(r => r.Direction)
                    : primary.ThenByDescending(r => r.Direction),
                "Gene" => _coExprPrevSortAscending
                    ? primary.ThenBy(r => r.GeneName)
                    : primary.ThenByDescending(r => r.GeneName),
                _ => _coExprPrevSortAscending  // |r|
                    ? primary.ThenBy(r => r.AbsR)
                    : primary.ThenByDescending(r => r.AbsR),
            };
        }

        private void ApplyCoExprSort()
        {
            if (_currentCoExprDisplayRows == null) return;

            IOrderedEnumerable<CoExprDisplayRow> primary = _coExprSortColumn switch
            {
                "P-value" => _coExprSortAscending
                    ? _currentCoExprDisplayRows.OrderBy(r => r.PValue)
                    : _currentCoExprDisplayRows.OrderByDescending(r => r.PValue),
                "FDR" => _coExprSortAscending
                    ? _currentCoExprDisplayRows.OrderBy(r => r.FDR)
                    : _currentCoExprDisplayRows.OrderByDescending(r => r.FDR),
                "Direction" => _coExprSortAscending
                    ? _currentCoExprDisplayRows.OrderBy(r => r.Direction)
                    : _currentCoExprDisplayRows.OrderByDescending(r => r.Direction),
                "Gene" => _coExprSortAscending
                    ? _currentCoExprDisplayRows.OrderBy(r => r.GeneName)
                    : _currentCoExprDisplayRows.OrderByDescending(r => r.GeneName),
                _ => _coExprSortAscending
                    ? _currentCoExprDisplayRows.OrderBy(r => r.AbsR)
                    : _currentCoExprDisplayRows.OrderByDescending(r => r.AbsR),
            };

            var sorted = ApplySecondarySort(primary).ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Rank = i + 1;

            CoExprDataGrid.ItemsSource = null;
            CoExprDataGrid.ItemsSource = sorted;
        }

        #region Progress Bar
        private void ShowProgress(string title, int value = 0)
        {
            ProgressTitle.Text = title;
            ProgressBar.Value = value;
            ProgressText.Text = $"{value}%";
            ProgressOverlay.Visibility = Visibility.Visible;
        }

        private async void TcgaAnalysisPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadProjectsAsync();
            try
            {
                _geneHelperReady = await _geneHelper.InitializeAsync();
            }
            catch { }
        }

        private void UpdateProgress(int value, string message = null)
        {
            ProgressBar.Value = value;
            ProgressText.Text = message ?? $"{value}%";
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void HideProgress() => ProgressOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region Initialization
        private async Task LoadProjectsAsync()
        {
            try
            {
                ShowProgress("Loading TCGA projects...");

                // ★ 新增：資料不存在時自動觸發下載流程
                if (!_dataService.IsDataAvailable())
                {
                    HideProgress();

                    var downloadWin = new TcgaDataDownloadWindow();
                    downloadWin.Owner = Window.GetWindow(this);
                    bool? result = downloadWin.ShowDialog();

                    // 使用者跳過或下載失敗 → 直接返回
                    if (result != true || !downloadWin.DownloadCompleted)
                    {
                        return;
                    }

                    // 下載完成 → 重新顯示進度並繼續載入
                    ShowProgress("Loading TCGA projects...");
                }
                // ★ 原本的 MessageBox 警告整段刪除（約 5 行）

                _allProjects = await _dataService.GetProjectIndexAsync();
                _allProjects = _allProjects.OrderBy(p => p.CancerCode).ToList();

                _availableCancers.Clear();
                foreach (var p in _allProjects) _availableCancers.Add(p);

                SingleCancerComboBox.ItemsSource = _allProjects;
                SingleCancerComboBox.DisplayMemberPath = "DisplayName";
                if (_allProjects.Count > 0) SingleCancerComboBox.SelectedIndex = 0;

                HideProgress();
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error loading data: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetSelectedCondition()
        {
            if (ConditionTumorOnly.IsChecked == true) return "Tumor";
            if (ConditionNormalOnly.IsChecked == true) return "Normal";
            return "Both";
        }

        private List<TcgaProjectIndex> GetSelectedProjects()
        {
            if (SingleSelectRadio.IsChecked == true)
            {
                if (SingleCancerComboBox.SelectedItem is TcgaProjectIndex p)
                    return new List<TcgaProjectIndex> { p };
                return new List<TcgaProjectIndex>();
            }
            return _selectedCancers.ToList();
        }
        #endregion

        #region Cancer Selection
        private void SelectionMode_Changed(object sender, RoutedEventArgs e)
        {
            if (SingleSelectRadio == null || SingleSelectPanel == null || MultiSelectPanel == null) return;
            SingleSelectPanel.Visibility = SingleSelectRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            MultiSelectPanel.Visibility = SingleSelectRadio.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SingleCancerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SingleCancerComboBox.SelectedItem is TcgaProjectIndex p)
            {
                string survivalInfo = p.HasSurvivalData ? $"\nSurvival data: {p.n_survival_available} (Alive: {p.n_alive}, Dead: {p.n_dead})" : "";
                SingleCancerInfoText.Text = $"Samples: {p.n_samples} (Tumor: {p.n_tumor}, Normal: {p.n_normal})\nGenes: {p.n_genes:N0}{survivalInfo}";
            }
        }

        private void AvailableCancerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AvailableCancerList.SelectedItem is TcgaProjectIndex item)
            {
                _selectedCancers.Add(item);
                _availableCancers.Remove(item);
                UpdateSelectedCount();
            }
        }

        private void SelectedCancerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedCancerList.SelectedItem is TcgaProjectIndex item)
            {
                _selectedCancers.Remove(item);
                int idx = _allProjects.IndexOf(item);
                int insert = _availableCancers.Count;
                for (int i = 0; i < _availableCancers.Count; i++)
                {
                    if (_allProjects.IndexOf(_availableCancers[i]) > idx) { insert = i; break; }
                }
                _availableCancers.Insert(insert, item);
                UpdateSelectedCount();
            }
        }

        private void AddSelectedCancers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in AvailableCancerList.SelectedItems.Cast<TcgaProjectIndex>().ToList())
            {
                _selectedCancers.Add(item);
                _availableCancers.Remove(item);
            }
            UpdateSelectedCount();
        }

        private void RemoveSelectedCancers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in SelectedCancerList.SelectedItems.Cast<TcgaProjectIndex>().ToList())
            {
                _selectedCancers.Remove(item);
                int idx = _allProjects.IndexOf(item);
                int insert = _availableCancers.Count;
                for (int i = 0; i < _availableCancers.Count; i++)
                {
                    if (_allProjects.IndexOf(_availableCancers[i]) > idx) { insert = i; break; }
                }
                _availableCancers.Insert(insert, item);
            }
            UpdateSelectedCount();
        }

        private void AddAllCancers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _availableCancers.ToList()) _selectedCancers.Add(item);
            _availableCancers.Clear();
            UpdateSelectedCount();
        }

        private void RemoveAllCancers_Click(object sender, RoutedEventArgs e)
        {
            _selectedCancers.Clear();
            _availableCancers.Clear();
            foreach (var p in _allProjects) _availableCancers.Add(p);
            UpdateSelectedCount();
        }

        private void UpdateSelectedCount() => SelectedCountText.Text = $" ({_selectedCancers.Count})";
        #endregion

        #region Box Plot Analysis (Tab 1) - FIXED

        private async void AnalyzeGeneExpression_Click(object sender, RoutedEventArgs e)
        {
            var geneId = BoxPlotGeneIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(geneId))
            {
                MessageBox.Show("Please enter a gene ID.");
                return;
            }

            var projects = GetSelectedProjects();
            if (projects.Count == 0)
            {
                MessageBox.Show("Please select at least one cancer type.");
                return;
            }

            // 轉換為 Ensembl ID（TCGA JSON 只含 gene_ids，不受 GTF 命名版本影響）
            if (_geneHelperReady)
            {
                geneId = _geneHelper.ResolveForTcga(geneId);
            }

            string condition = GetSelectedCondition();

            try
            {
                ShowProgress("Analyzing gene expression...", 0);

                var stats = new List<BoxPlotStatsRow>();
                var allTumorValues = new Dictionary<string, List<double>>();
                var allNormalValues = new Dictionary<string, List<double>>();

                int total = projects.Count;
                int processed = 0;

                foreach (var project in projects)
                {
                    processed++;
                    UpdateProgress(processed * 80 / total, $"Loading {project.CancerCode}...");

                    var expr = await _dataService.GetGeneExpressionAsync(project.project_id, geneId);
                    if (expr == null) continue;

                    var row = new BoxPlotStatsRow
                    {
                        CancerCode = project.CancerCode,
                        GeneId = expr.GeneId,
                        GeneName = !string.IsNullOrEmpty(expr.GeneName) ? expr.GeneName : geneId,
                        TumorN = expr.TumorValues.Count,
                        NormalN = expr.NormalValues.Count
                    };

                    // gene_names 為空或 ENSG 時用 GeneIdService 轉回 Symbol
                    if ((string.IsNullOrEmpty(row.GeneName) || row.GeneName.StartsWith("ENSG")) && _geneHelperReady)
                        row.GeneName = _geneHelper.ToSymbol(row.GeneId) ?? row.GeneId;

                    if (expr.TumorValues.Count > 0)
                    {
                        var tLog = expr.TumorValues.Select(v => Math.Log2(v + 1)).ToList();
                        row.TumorMean = tLog.Average();
                        row.TumorSd = StatisticsService.StandardDeviation(tLog);
                        var q = StatisticsService.Quartiles(tLog);
                        row.TumorQ1 = q.Q1;
                        row.TumorMedian = q.Median;
                        row.TumorQ3 = q.Q3;
                        allTumorValues[project.CancerCode] = tLog;
                    }

                    if (expr.NormalValues.Count > 0)
                    {
                        var nLog = expr.NormalValues.Select(v => Math.Log2(v + 1)).ToList();
                        row.NormalMean = nLog.Average();
                        row.NormalSd = StatisticsService.StandardDeviation(nLog);
                        var q = StatisticsService.Quartiles(nLog);
                        row.NormalQ1 = q.Q1;
                        row.NormalMedian = q.Median;
                        row.NormalQ3 = q.Q3;
                        allNormalValues[project.CancerCode] = nLog;
                    }

                    // Calculate p-value if both groups have data
                    if (expr.TumorValues.Count >= 3 && expr.NormalValues.Count >= 3)
                    {
                        var tLog = allTumorValues.ContainsKey(project.CancerCode) ? allTumorValues[project.CancerCode] : expr.TumorValues.Select(v => Math.Log2(v + 1)).ToList();
                        var nLog = allNormalValues.ContainsKey(project.CancerCode) ? allNormalValues[project.CancerCode] : expr.NormalValues.Select(v => Math.Log2(v + 1)).ToList();
                        row.PValue = StatisticsService.WelchTTest(tLog, nLog);
                    }
                    else
                    {
                        row.PValue = 1.0;
                    }

                    stats.Add(row);
                }

                if (stats.Count == 0)
                {
                    HideProgress();
                    BoxPlotResultText.Text = $"Gene '{geneId}' not found in selected cancer types.";
                    return;
                }

                // FDR correction
                UpdateProgress(85, "Calculating FDR...");
                var pValues = stats.Select(s => s.PValue).ToList();
                var fdrs = StatisticsService.BenjaminiHochbergFDR(pValues);
                for (int i = 0; i < stats.Count; i++)
                {
                    stats[i].FDR = fdrs[i];
                }

                _currentBoxPlotStats = stats;

                // Draw box plot
                UpdateProgress(90, "Drawing plot...");
                bool showTumor = condition != "Normal";
                bool showNormal = condition != "Tumor";
                DrawBoxPlot(stats, allTumorValues, allNormalValues, geneId, showTumor, showNormal);

                // Update stats grid
                BoxPlotStatsGrid.ItemsSource = stats.OrderBy(s => s.PValue).ToList();

                HideProgress();
                BoxPlotResultText.Text = $"Gene: {stats.First().GeneName} | {stats.Count} cancer types analyzed";
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyBoxPlotStats_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBoxPlotStats == null || _currentBoxPlotStats.Count == 0)
            {
                MessageBox.Show("No data to copy.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Cancer\tGene\tTumor_N\tTumor_Mean\tTumor_SD\tNormal_N\tNormal_Mean\tNormal_SD\tP-value\tFDR");
            foreach (var row in _currentBoxPlotStats.OrderBy(s => s.PValue))
            {
                sb.AppendLine($"{row.CancerCode}\t{row.GeneName}\t{row.TumorN}\t{row.TumorMean:F4}\t{row.TumorSd:F4}\t{row.NormalN}\t{row.NormalMean:F4}\t{row.NormalSd:F4}\t{row.PValue:E2}\t{row.FDR:E2}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Statistics copied to clipboard!");
        }

        private void DrawBoxPlot(List<BoxPlotStatsRow> stats,
            Dictionary<string, List<double>> tumorVals,
            Dictionary<string, List<double>> normalVals,
            string geneId, bool showTumor, bool showNormal)
        {
            BoxPlotCanvas.Children.Clear();

            int nCancers = stats.Count;
            int groupsPerCancer = (showTumor ? 1 : 0) + (showNormal ? 1 : 0);
            if (groupsPerCancer == 0) return;

            double boxWidth = 30;
            double groupSpacing = 20;
            double cancerSpacing = 50;
            double groupWidth = groupsPerCancer * boxWidth + (groupsPerCancer - 1) * groupSpacing;

            double marginLeft = 70, marginRight = 100, marginTop = 50, marginBottom = 80;
            double plotWidth = nCancers * groupWidth + (nCancers - 1) * cancerSpacing;
            double chartWidth = Math.Max(800, plotWidth + marginLeft + marginRight);
            double chartHeight = 500;
            double plotHeight = chartHeight - marginTop - marginBottom;

            BoxPlotCanvas.Width = chartWidth;
            BoxPlotCanvas.Height = chartHeight;

            // Find Y range
            var allValues = new List<double>();
            foreach (var kv in tumorVals) if (showTumor) allValues.AddRange(kv.Value);
            foreach (var kv in normalVals) if (showNormal) allValues.AddRange(kv.Value);

            if (allValues.Count == 0) return;

            double minY = allValues.Min();
            double maxY = allValues.Max();
            double padding = (maxY - minY) * 0.1;
            minY -= padding;
            maxY += padding;

            // Background
            AddRect(BoxPlotCanvas, marginLeft, marginTop, plotWidth, plotHeight, Brushes.White, Brushes.LightGray);

            // Y-axis grid lines
            for (int i = 0; i <= 5; i++)
            {
                double yVal = minY + (maxY - minY) * i / 5;
                double y = marginTop + plotHeight - plotHeight * i / 5;
                AddLine(BoxPlotCanvas, marginLeft, y, marginLeft + plotWidth, y, Brushes.LightGray, 0.5, true);
                AddText(BoxPlotCanvas, yVal.ToString("F1"), marginLeft - 45, y - 8, 9, Brushes.Gray);
            }

            // Draw boxes for each cancer
            double currentX = marginLeft + groupWidth / 2;
            foreach (var stat in stats)
            {
                double boxX = currentX - groupWidth / 2;

                if (showNormal && normalVals.ContainsKey(stat.CancerCode))
                {
                    DrawSingleBox(BoxPlotCanvas, boxX + boxWidth / 2, normalVals[stat.CancerCode],
                        minY, maxY, marginTop, plotHeight, boxWidth, NormalBrush);
                    boxX += boxWidth + groupSpacing;
                }

                if (showTumor && tumorVals.ContainsKey(stat.CancerCode))
                {
                    DrawSingleBox(BoxPlotCanvas, boxX + boxWidth / 2, tumorVals[stat.CancerCode],
                        minY, maxY, marginTop, plotHeight, boxWidth, TumorBrush);
                }

                // Cancer label
                AddText(BoxPlotCanvas, stat.CancerCode, currentX - 15, marginTop + plotHeight + 10, 10, Brushes.Black);

                // P-value annotation
                if (stat.PValue < 0.05)
                {
                    string sig = stat.PValue < 0.001 ? "***" : (stat.PValue < 0.01 ? "**" : "*");
                    AddText(BoxPlotCanvas, sig, currentX - 5, marginTop - 15, 12, Brushes.Red, FontWeights.Bold);
                }

                currentX += groupWidth + cancerSpacing;
            }

            // Y-axis label
            AddRotatedText(BoxPlotCanvas, "Expression (log2)", 15, chartHeight / 2 + 50, -90, 13);

            // Legend
            DrawLegend(BoxPlotCanvas, chartWidth - 95, marginTop + 10, showTumor, showNormal);

            // Title
            var geneName = stats.FirstOrDefault()?.GeneName ?? geneId;
            AddText(BoxPlotCanvas, $"Gene Expression: {geneName}", marginLeft, 15, 16, Brushes.Black, FontWeights.Bold);
        }

        private void DrawSingleBox(Canvas c, double cx, List<double> vals, double minY, double maxY,
            double marginTop, double plotH, double boxW, Brush color)
        {
            if (vals.Count == 0) return;
            var sorted = vals.OrderBy(v => v).ToList();
            int n = sorted.Count;
            double median = n % 2 == 0 ? (sorted[n / 2 - 1] + sorted[n / 2]) / 2 : sorted[n / 2];
            double q1 = sorted[Math.Max(0, (int)(n * 0.25))];
            double q3 = sorted[Math.Min(n - 1, (int)(n * 0.75))];
            double iqr = q3 - q1;
            double wLo = sorted.Where(v => v >= q1 - 1.5 * iqr).DefaultIfEmpty(q1).Min();
            double wHi = sorted.Where(v => v <= q3 + 1.5 * iqr).DefaultIfEmpty(q3).Max();

            Func<double, double> toY = v => marginTop + plotH - (v - minY) / (maxY - minY) * plotH;

            var rnd = new Random(vals.GetHashCode());
            var dotColor = ((SolidColorBrush)color).Color;
            foreach (var v in vals)
            {
                double y = toY(v);
                double jitter = (rnd.NextDouble() - 0.5) * boxW * 0.7;
                var dot = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(dotColor) { Opacity = 0.4 } };
                Canvas.SetLeft(dot, cx + jitter - 2);
                Canvas.SetTop(dot, y - 2);
                c.Children.Add(dot);
            }

            double yQ1 = toY(q1), yQ3 = toY(q3), yMed = toY(median), yLo = toY(wLo), yHi = toY(wHi);
            AddRect(c, cx - boxW / 2, Math.Min(yQ1, yQ3), boxW, Math.Abs(yQ1 - yQ3), Brushes.Transparent, color, 2);
            AddLine(c, cx - boxW / 2, yMed, cx + boxW / 2, yMed, color, 3);
            AddLine(c, cx, yQ3, cx, yHi, color, 1.5);
            AddLine(c, cx, yQ1, cx, yLo, color, 1.5);
            AddLine(c, cx - boxW / 4, yHi, cx + boxW / 4, yHi, color, 1.5);
            AddLine(c, cx - boxW / 4, yLo, cx + boxW / 4, yLo, color, 1.5);
        }

        private void DrawLegend(Canvas c, double x, double y, bool showT, bool showN)
        {
            int items = (showN ? 1 : 0) + (showT ? 1 : 0);
            AddRect(c, x, y, 80, 20 + items * 20, Brushes.White, Brushes.LightGray);
            int i = 0;
            if (showN) { AddRect(c, x + 8, y + 10 + i * 20, 14, 12, NormalBrush, NormalBrush); AddText(c, "Normal", x + 28, y + 8 + i * 20, 11); i++; }
            if (showT) { AddRect(c, x + 8, y + 10 + i * 20, 14, 12, TumorBrush, TumorBrush); AddText(c, "Tumor", x + 28, y + 8 + i * 20, 11); }
        }

        private void ExportBoxPlotImage_Click(object sender, RoutedEventArgs e) => ExportCanvasToPng(BoxPlotCanvas, "BoxPlot");

        private void ExportBoxPlotCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBoxPlotStats == null || _currentBoxPlotStats.Count == 0) { MessageBox.Show("No data."); return; }
            var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"BoxPlot_Stats_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Cancer,Gene,Tumor_N,Tumor_Mean,Tumor_SD,Normal_N,Normal_Mean,Normal_SD,PValue,FDR");
                foreach (var row in _currentBoxPlotStats.OrderBy(s => s.PValue))
                    sb.AppendLine($"{row.CancerCode},{row.GeneName},{row.TumorN},{row.TumorMean:F4},{row.TumorSd:F4},{row.NormalN},{row.NormalMean:F4},{row.NormalSd:F4},{row.PValue:E4},{row.FDR:E4}");
                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }
        #endregion
        #region Correlation Heatmap (Tab 2)
        private async void CalculateCorrelation_Click(object sender, RoutedEventArgs e)
        {
            var genes = CorrelationGenesTextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            if (genes.Count < 2) { MessageBox.Show("Enter at least 2 genes."); return; }

            if (_geneHelperReady)
                genes = _geneHelper.ResolveListForTcga(genes);

            var projects = GetSelectedProjects();
            if (projects.Count == 0) { MessageBox.Show("Select a cancer type."); return; }

            string condition = GetSelectedCondition();

            try
            {
                ShowProgress("Calculating correlations...", 0);
                var project = projects.First();
                UpdateProgress(20, $"Loading {project.CancerCode} data...");

                var multiExpr = await _dataService.GetMultiGeneExpressionAsync(project.project_id, genes, condition);
                if (multiExpr == null || multiExpr.GeneNames.Count < 2) { HideProgress(); HeatmapResultText.Text = "Not enough genes found."; return; }

                int n = multiExpr.GeneNames.Count;
                double[,] corrMatrix = new double[n, n];

                UpdateProgress(50, "Computing correlation matrix...");
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        corrMatrix[i, j] = i == j ? 1.0 : StatisticsService.PearsonCorrelation(multiExpr.Expressions[i], multiExpr.Expressions[j]).r;

                UpdateProgress(80, "Drawing heatmap...");
                _currentCorrelationMatrix = corrMatrix;

                // gene_names 為空時用 GeneIdService 批次轉回 Symbol
                var displayNames = multiExpr.GeneNames.ToList();
                if (_geneHelperReady)
                {
                    var symbolMap = GetSymbolMap(multiExpr.GeneIds);
                    for (int i = 0; i < displayNames.Count; i++)
                    {
                        if (string.IsNullOrEmpty(displayNames[i]) && symbolMap.TryGetValue(multiExpr.GeneIds[i], out var sym))
                            displayNames[i] = sym;
                        if (string.IsNullOrEmpty(displayNames[i]))
                            displayNames[i] = multiExpr.GeneIds[i]; // fallback: 顯示 ENSG
                    }
                }
                _currentCorrelationGenes = displayNames;

                DrawHeatmap(corrMatrix, displayNames, project.CancerCode);
                HideProgress();
                HeatmapResultText.Text = $"{project.CancerCode} | {n} genes | {multiExpr.Expressions[0].Count} samples | {condition}";
            }
            catch (Exception ex) { HideProgress(); MessageBox.Show($"Error: {ex.Message}"); }
        }

        private void DrawHeatmap(double[,] matrix, List<string> names, string cancer)
        {
            HeatmapCanvas.Children.Clear();
            int n = names.Count;
            double cellSize = 50;
            double plotSize = Math.Max(400, Math.Min(800, n * cellSize));
            double marginL = 120, marginT = 120, marginR = 30, marginB = 30;
            double chartWidth = plotSize + marginL + marginR;
            double chartHeight = plotSize + marginT + marginB;

            HeatmapCanvas.Width = chartWidth;
            HeatmapCanvas.Height = chartHeight;

            double cell = plotSize / n;

            // 畫格子與數值
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double v = matrix[i, j];
                    var rect = new Rectangle { Width = cell - 1, Height = cell - 1, Fill = new SolidColorBrush(HeatColor(v)) };
                    Canvas.SetLeft(rect, marginL + j * cell);
                    Canvas.SetTop(rect, marginT + i * cell);
                    HeatmapCanvas.Children.Add(rect);
                    if (cell > 30)
                        AddText(HeatmapCanvas, v.ToString("F2"),
                            marginL + j * cell + cell / 2 - 12,
                            marginT + i * cell + cell / 2 - 7,
                            Math.Min(cell / 4, 10),
                            Math.Abs(v) > 0.5 ? Brushes.White : Brushes.Black,
                            FontWeights.SemiBold);
                }
            }

            // 行標籤（左側，水平）
            for (int i = 0; i < n; i++)
                AddText(HeatmapCanvas, names[i],
                    5, marginT + i * cell + cell / 2 - 7,
                    11, Brushes.Black, FontWeights.SemiBold);

            // 列標籤（上方，旋轉 -45°）
            for (int j = 0; j < n; j++)
            {
                var tb = new TextBlock
                {
                    Text = names[j],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black,
                    RenderTransform = new RotateTransform(-45),
                    RenderTransformOrigin = new Point(0, 1)
                };
                Canvas.SetLeft(tb, marginL + j * cell + cell / 2 - 4);
                Canvas.SetTop(tb, marginT - 10);
                HeatmapCanvas.Children.Add(tb);
            }

            AddText(HeatmapCanvas, $"{cancer}: Gene Correlation", marginL, 10, 16, Brushes.Black, FontWeights.Bold);
        }

        private Color HeatColor(double v)
        {
            v = Math.Max(-1, Math.Min(1, v));
            if (v >= 0) return Color.FromRgb((byte)(255 * (1 - v)), (byte)(255 * (1 - v)), 255);
            else return Color.FromRgb(255, (byte)(255 * (1 + v)), (byte)(255 * (1 + v)));
        }

        private void ExportHeatmapImage_Click(object sender, RoutedEventArgs e) => ExportCanvasToPng(HeatmapCanvas, "Heatmap");
        private void ExportHeatmapCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCorrelationMatrix == null) { MessageBox.Show("No data."); return; }
            var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"Correlation_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.Append("Gene");
                foreach (var g in _currentCorrelationGenes) sb.Append($",{g}");
                sb.AppendLine();
                for (int i = 0; i < _currentCorrelationGenes.Count; i++)
                {
                    sb.Append(_currentCorrelationGenes[i]);
                    for (int j = 0; j < _currentCorrelationGenes.Count; j++) sb.Append($",{_currentCorrelationMatrix[i, j]:F4}");
                    sb.AppendLine();
                }
                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }
        #endregion

        #region Scatter Plot (Tab 3)
        public class ScatterDataPoint
        {
            public double X { get; set; }
            public double Y { get; set; }
            public string Condition { get; set; }
            public string SampleId { get; set; }
        }

        private void CopyScatterStats_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentGeneX)) { MessageBox.Show("No data."); return; }
            var stats = $"Cancer\t{_currentCancer}\nGene X\t{_currentGeneX}\nGene Y\t{_currentGeneY}\nN\t{_scatterN}\nPearson R\t{_scatterR:F4}\nR²\t{_scatterR2:F4}\nP-value\t{_scatterPValue:E2}\nSlope\t{_scatterSlope:F4}\nIntercept\t{_scatterIntercept:F4}";
            Clipboard.SetText(stats);
            MessageBox.Show("Copied!");
        }

        private void CopyScatterR2_Click(object sender, RoutedEventArgs e) { Clipboard.SetText($"{_scatterR2:F4}"); MessageBox.Show($"R² = {_scatterR2:F4} copied!"); }
        private void CopyScatterEquation_Click(object sender, RoutedEventArgs e) { Clipboard.SetText($"Y = {_scatterSlope:F4}X + {_scatterIntercept:F4}"); MessageBox.Show("Equation copied!"); }

        private async void PlotScatter_Click(object sender, RoutedEventArgs e)
        {
            var geneX = ScatterGeneXTextBox.Text.Trim();
            var geneY = ScatterGeneYTextBox.Text.Trim();
            if (string.IsNullOrEmpty(geneX) || string.IsNullOrEmpty(geneY)) { MessageBox.Show("Enter both genes."); return; }

            if (_geneHelperReady)
            {
                geneX = _geneHelper.ResolveForTcga(geneX);
                geneY = _geneHelper.ResolveForTcga(geneY);
            }

            var projects = GetSelectedProjects();
            if (projects.Count == 0) { MessageBox.Show("Select a cancer type."); return; }

            string condition = GetSelectedCondition();

            try
            {
                ShowProgress("Creating scatter plot...", 0);
                var project = projects.First();
                UpdateProgress(30, $"Loading {project.CancerCode}...");

                var twoGene = await _dataService.GetTwoGeneExpressionAsync(project.project_id, geneX, geneY);
                if (twoGene == null) { HideProgress(); ScatterResultText.Text = "Gene(s) not found."; return; }

                UpdateProgress(60, "Processing data...");
                var points = new List<ScatterDataPoint>();
                bool showT = condition != "Normal", showN = condition != "Tumor";

                if (showT) foreach (var p in twoGene.TumorPairs) points.Add(new ScatterDataPoint { X = p.X, Y = p.Y, Condition = "Tumor", SampleId = p.SampleId });
                if (showN) foreach (var p in twoGene.NormalPairs) points.Add(new ScatterDataPoint { X = p.X, Y = p.Y, Condition = "Normal", SampleId = p.SampleId });

                if (points.Count == 0) { HideProgress(); ScatterResultText.Text = "No data for condition."; return; }

                UpdateProgress(80, "Drawing plot...");
                _currentScatterPoints = points;
                _currentGeneX = !string.IsNullOrEmpty(twoGene.Gene1Name) ? twoGene.Gene1Name : geneX;
                _currentGeneY = !string.IsNullOrEmpty(twoGene.Gene2Name) ? twoGene.Gene2Name : geneY;
                // gene_names 為空時用 ENSG 轉回 Symbol
                if (_geneHelperReady)
                {
                    if (string.IsNullOrEmpty(_currentGeneX) || _currentGeneX.StartsWith("ENSG"))
                        _currentGeneX = _geneHelper.ToSymbol(_currentGeneX) ?? _currentGeneX;
                    if (string.IsNullOrEmpty(_currentGeneY) || _currentGeneY.StartsWith("ENSG"))
                        _currentGeneY = _geneHelper.ToSymbol(_currentGeneY) ?? _currentGeneY;
                }
                _currentCancer = project.CancerCode;

                // log2(x+1) 轉換（與 Box Plot 一致）
                var xs = points.Select(p => Math.Log2(p.X + 1)).ToList();
                var ys = points.Select(p => Math.Log2(p.Y + 1)).ToList();
                // 同步更新 points 的座標供繪圖使用
                for (int i = 0; i < points.Count; i++) { points[i].X = xs[i]; points[i].Y = ys[i]; }
                var (r, pval, n) = StatisticsService.PearsonCorrelation(xs, ys);
                var (slope, intercept, r2) = StatisticsService.LinearRegression(xs, ys);

                DrawScatter(points, _currentGeneX, _currentGeneY, slope, intercept, project.CancerCode, showT, showN);
                HideProgress();

                _scatterR = r; _scatterR2 = r2; _scatterPValue = pval; _scatterSlope = slope; _scatterIntercept = intercept; _scatterN = n;

                ScatterResultText.Text = $"Cancer: {project.CancerCode}\nGene X: {_currentGeneX}\nGene Y: {_currentGeneY}\n━━━━━━━━━━━━━━━━━━━━\nN = {n}\nPearson R = {r:F4}\nR² = {r2:F4}\nP-value = {pval:E2}\n━━━━━━━━━━━━━━━━━━━━\nRegression: Y = {slope:F4}X + {intercept:F4}";
            }
            catch (Exception ex) { HideProgress(); MessageBox.Show($"Error: {ex.Message}"); }
        }

        private void DrawScatter(List<ScatterDataPoint> pts, string gX, string gY, double slope, double intercept, string cancer, bool showT, bool showN)
        {
            ScatterPlotCanvas.Children.Clear();
            double chartWidth = Math.Max(600, Math.Min(900, 500 + pts.Count * 0.5));
            double chartHeight = chartWidth * 0.85;
            ScatterPlotCanvas.Width = chartWidth;
            ScatterPlotCanvas.Height = chartHeight;

            double marginL = 70, marginR = 100, marginT = 50, marginB = 70;
            double plotW = chartWidth - marginL - marginR;
            double plotH = chartHeight - marginT - marginB;

            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double padX = (maxX - minX) * 0.1, padY = (maxY - minY) * 0.1;
            minX -= padX; maxX += padX; minY -= padY; maxY += padY;

            AddRect(ScatterPlotCanvas, marginL, marginT, plotW, plotH, Brushes.White, Brushes.LightGray);

            for (int i = 0; i <= 5; i++)
            {
                double xv = minX + (maxX - minX) * i / 5;
                double x = marginL + (xv - minX) / (maxX - minX) * plotW;
                AddLine(ScatterPlotCanvas, x, marginT, x, marginT + plotH, Brushes.LightGray, 0.5, true);
                AddText(ScatterPlotCanvas, xv.ToString("F1"), x - 12, marginT + plotH + 8, 9, Brushes.Gray);

                double yv = minY + (maxY - minY) * i / 5;
                double y = marginT + plotH - (yv - minY) / (maxY - minY) * plotH;
                AddLine(ScatterPlotCanvas, marginL, y, marginL + plotW, y, Brushes.LightGray, 0.5, true);
                AddText(ScatterPlotCanvas, yv.ToString("F1"), marginL - 40, y - 8, 9, Brushes.Gray);
            }

            double ly1 = slope * minX + intercept, ly2 = slope * maxX + intercept;
            double sy1 = marginT + plotH - (ly1 - minY) / (maxY - minY) * plotH;
            double sy2 = marginT + plotH - (ly2 - minY) / (maxY - minY) * plotH;
            AddLine(ScatterPlotCanvas, marginL, sy1, marginL + plotW, sy2, Brushes.Red, 2, true);

            foreach (var pt in pts)
            {
                double x = marginL + (pt.X - minX) / (maxX - minX) * plotW;
                double y = marginT + plotH - (pt.Y - minY) / (maxY - minY) * plotH;
                var color = pt.Condition == "Normal" ? Color.FromArgb(180, NormalColor.R, NormalColor.G, NormalColor.B) : Color.FromArgb(180, TumorColor.R, TumorColor.G, TumorColor.B);
                var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color) };
                Canvas.SetLeft(dot, x - 4); Canvas.SetTop(dot, y - 4);
                ScatterPlotCanvas.Children.Add(dot);
            }

            AddText(ScatterPlotCanvas, $"{gX} (log2)", marginL + plotW / 2 - 40, chartHeight - 25, 13, Brushes.Black, FontWeights.SemiBold);
            AddRotatedText(ScatterPlotCanvas, $"{gY} (log2)", 15, chartHeight / 2 + 40, -90, 13);
            AddText(ScatterPlotCanvas, $"{cancer}: {gX} vs {gY}", marginL, 15, 16, Brushes.Black, FontWeights.Bold);

            int items = (showN ? 1 : 0) + (showT ? 1 : 0);
            AddRect(ScatterPlotCanvas, chartWidth - 95, marginT + 10, 85, 15 + items * 20, Brushes.White, Brushes.LightGray);
            int li = 0;
            if (showN) { AddEllipse(ScatterPlotCanvas, chartWidth - 85, marginT + 18 + li * 20, 10, NormalBrush); AddText(ScatterPlotCanvas, "Normal", chartWidth - 70, marginT + 15 + li * 20, 11); li++; }
            if (showT) { AddEllipse(ScatterPlotCanvas, chartWidth - 85, marginT + 18 + li * 20, 10, TumorBrush); AddText(ScatterPlotCanvas, "Tumor", chartWidth - 70, marginT + 15 + li * 20, 11); }
        }

        private void ExportScatterImage_Click(object sender, RoutedEventArgs e) => ExportCanvasToPng(ScatterPlotCanvas, "ScatterPlot");
        private void ExportScatterCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScatterPoints == null || _currentScatterPoints.Count == 0) { MessageBox.Show("No data."); return; }
            var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"Scatter_{_currentCancer}_{_currentGeneX}_vs_{_currentGeneY}_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"SampleID,{_currentGeneX},{_currentGeneY},Condition");
                foreach (var pt in _currentScatterPoints) sb.AppendLine($"{pt.SampleId},{pt.X:F4},{pt.Y:F4},{pt.Condition}");
                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }
        #endregion

        #region Kaplan-Meier Survival Analysis (Tab 4) - NEW

        private async void LoadSurvivalData_Click(object sender, RoutedEventArgs e)
        {
            var geneId = KMGeneIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(geneId)) { MessageBox.Show("Please enter a gene ID."); return; }

            if (_geneHelperReady)
                geneId = _geneHelper.ResolveForTcga(geneId);

            var projects = GetSelectedProjects();
            if (projects.Count == 0) { MessageBox.Show("Please select a cancer type."); return; }

            var project = projects.First();

            try
            {
                ShowProgress("Loading survival data...", 0);
                UpdateProgress(30, "Checking survival data availability...");

                bool hasSurvival = await _dataService.HasSurvivalDataAsync(project.project_id);
                if (!hasSurvival)
                {
                    HideProgress();
                    MessageBox.Show($"No survival data available for {project.CancerCode}.\nPlease regenerate the TCGA data with survival information.",
                        "No Survival Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UpdateProgress(50, $"Loading {project.CancerCode} survival data...");
                var survivalData = await _dataService.GetGeneSurvivalDataAsync(project.project_id, geneId);

                if (survivalData == null || survivalData.TotalSamples < 10)
                {
                    HideProgress();
                    MessageBox.Show($"Insufficient survival data for gene '{geneId}' in {project.CancerCode}.",
                        "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _currentSurvivalData = survivalData;
                _currentKMGene = survivalData.GeneName;
                // gene_names 為空時用 ENSG 轉回 Symbol
                if (_geneHelperReady && (string.IsNullOrEmpty(_currentKMGene) || _currentKMGene.StartsWith("ENSG")))
                    _currentKMGene = _geneHelper.ToSymbol(geneId) ?? geneId;
                _currentKMCancer = project.CancerCode;

                UpdateProgress(80, "Generating Kaplan-Meier plot...");

                // Draw with current slider value
                double percentile = KMPercentileSlider.Value;
                UpdateKaplanMeierPlot(percentile);

                HideProgress();
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // REAL-TIME slider update
        private void KMPercentileSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (KMPercentileText == null) return;

            double percentile = e.NewValue;
            KMPercentileText.Text = $"{percentile:F0}%";
            KMHighPercentText.Text = $"{100 - percentile:F0}%";
            KMLowPercentText.Text = $"{percentile:F0}%";

            // If we have data loaded, update the plot in real-time
            if (_currentSurvivalData != null && _currentSurvivalData.TotalSamples > 0)
            {
                UpdateKaplanMeierPlot(percentile);
            }
        }

        private void UpdateKaplanMeierPlot(double percentile)
        {
            if (_currentSurvivalData == null) return;

            // Sort samples by expression
            var sorted = _currentSurvivalData.Samples.OrderBy(s => s.Expression).ToList();
            int n = sorted.Count;
            int cutoffIndex = (int)Math.Round(n * percentile / 100.0);
            cutoffIndex = Math.Max(1, Math.Min(n - 1, cutoffIndex));

            double cutoffValue = sorted[cutoffIndex].Expression;

            // Split into low and high expression groups
            var lowGroup = sorted.Take(cutoffIndex).Select(s => (s.SurvivalDays, s.IsEvent)).ToList();
            var highGroup = sorted.Skip(cutoffIndex).Select(s => (s.SurvivalDays, s.IsEvent)).ToList();

            // Calculate Kaplan-Meier curves
            var lowCurve = KaplanMeierService.CalculateSurvivalCurve(lowGroup);
            var highCurve = KaplanMeierService.CalculateSurvivalCurve(highGroup);

            // Calculate statistics
            var lowMedian = KaplanMeierService.CalculateMedianSurvival(lowCurve);
            var highMedian = KaplanMeierService.CalculateMedianSurvival(highCurve);
            var lowMean = KaplanMeierService.CalculateMeanSurvival(lowCurve);
            var highMean = KaplanMeierService.CalculateMeanSurvival(highCurve);

            // Log-Rank test
            var (chiSq, pValue) = KaplanMeierService.LogRankTest(lowGroup, highGroup);

            // Store result
            _currentKMResult = new KMComparisonResult
            {
                LowExpression = new KMAnalysisResult
                {
                    GroupName = "Low Expression",
                    Curve = lowCurve,
                    TotalSamples = lowGroup.Count,
                    Events = lowGroup.Count(s => s.IsEvent),
                    Censored = lowGroup.Count(s => !s.IsEvent),
                    MedianSurvival = lowMedian,
                    MeanSurvival = lowMean
                },
                HighExpression = new KMAnalysisResult
                {
                    GroupName = "High Expression",
                    Curve = highCurve,
                    TotalSamples = highGroup.Count,
                    Events = highGroup.Count(s => s.IsEvent),
                    Censored = highGroup.Count(s => !s.IsEvent),
                    MedianSurvival = highMedian,
                    MeanSurvival = highMean
                },
                LogRankChiSquare = chiSq,
                LogRankPValue = pValue,
                CutoffPercentile = percentile,
                CutoffValue = cutoffValue
            };

            // Draw the plot
            DrawKaplanMeierPlot(_currentKMResult);

            // Update statistics text
            KMHighGroupStats.Text = $"N = {_currentKMResult.HighExpression.TotalSamples}\n" +
                                    $"Events = {_currentKMResult.HighExpression.Events}\n" +
                                    $"Censored = {_currentKMResult.HighExpression.Censored}\n" +
                                    $"Median: {(_currentKMResult.HighExpression.MedianSurvivalMonths?.ToString("F1") ?? "N/A")} months\n" +
                                    $"Mean: {_currentKMResult.HighExpression.MeanSurvivalMonths:F1} months";

            KMLowGroupStats.Text = $"N = {_currentKMResult.LowExpression.TotalSamples}\n" +
                                   $"Events = {_currentKMResult.LowExpression.Events}\n" +
                                   $"Censored = {_currentKMResult.LowExpression.Censored}\n" +
                                   $"Median: {(_currentKMResult.LowExpression.MedianSurvivalMonths?.ToString("F1") ?? "N/A")} months\n" +
                                   $"Mean: {_currentKMResult.LowExpression.MeanSurvivalMonths:F1} months";

            // Log-Rank test result
            string sigText = pValue < 0.05 ? "✅ Significant" : "⚪ Not Significant";
            KMLogRankText.Text = $"Log-Rank Test:  χ² = {chiSq:F3}  |  P-value = {_currentKMResult.PValueDisplay}  |  {sigText}";
            KMLogRankText.Foreground = pValue < 0.05 ? Brushes.Green : Brushes.Gray;
        }

        private void DrawKaplanMeierPlot(KMComparisonResult result)
        {
            KMPlotCanvas.Children.Clear();

            double chartWidth = 800, chartHeight = 500;
            double marginL = 70, marginR = 150, marginT = 50, marginB = 70;
            double plotW = chartWidth - marginL - marginR;
            double plotH = chartHeight - marginT - marginB;

            // Find max time
            int maxTime = Math.Max(
                result.HighExpression.Curve.Max(p => p.Time),
                result.LowExpression.Curve.Max(p => p.Time));
            maxTime = (int)(Math.Ceiling(maxTime / 365.0) * 365); // Round to years

            // Background
            AddRect(KMPlotCanvas, marginL, marginT, plotW, plotH, Brushes.White, Brushes.LightGray);

            // Grid lines
            for (int i = 0; i <= 5; i++)
            {
                double y = marginT + plotH - plotH * i / 5;
                AddLine(KMPlotCanvas, marginL, y, marginL + plotW, y, Brushes.LightGray, 0.5, true);
                AddText(KMPlotCanvas, $"{i * 20}%", marginL - 40, y - 8, 10, Brushes.Gray);
            }

            int yearMax = maxTime / 365;
            for (int yr = 0; yr <= yearMax; yr++)
            {
                double x = marginL + plotW * yr / yearMax;
                AddLine(KMPlotCanvas, x, marginT, x, marginT + plotH, Brushes.LightGray, 0.5, true);
                AddText(KMPlotCanvas, $"{yr}", x - 5, marginT + plotH + 10, 10, Brushes.Gray);
            }

            // Draw curves (step function)
            DrawKMCurve(KMPlotCanvas, result.HighExpression.Curve, maxTime, marginL, marginT, plotW, plotH, HighExprBrush, true);
            DrawKMCurve(KMPlotCanvas, result.LowExpression.Curve, maxTime, marginL, marginT, plotW, plotH, LowExprBrush, true);

            // Axis labels
            AddText(KMPlotCanvas, "Time (years)", marginL + plotW / 2 - 40, chartHeight - 25, 13, Brushes.Black, FontWeights.SemiBold);
            AddRotatedText(KMPlotCanvas, "Survival Probability", 15, chartHeight / 2 + 60, -90, 13);

            // Title
            AddText(KMPlotCanvas, $"{_currentKMCancer}: {_currentKMGene} Survival Analysis", marginL, 15, 16, Brushes.Black, FontWeights.Bold);

            // Legend
            double legX = chartWidth - 140, legY = marginT + 10;
            AddRect(KMPlotCanvas, legX, legY, 130, 80, Brushes.White, Brushes.LightGray);
            AddRect(KMPlotCanvas, legX + 10, legY + 15, 20, 3, HighExprBrush, HighExprBrush);
            AddText(KMPlotCanvas, $"High (n={result.HighExpression.TotalSamples})", legX + 35, legY + 10, 10, Brushes.Black);
            AddRect(KMPlotCanvas, legX + 10, legY + 40, 20, 3, LowExprBrush, LowExprBrush);
            AddText(KMPlotCanvas, $"Low (n={result.LowExpression.TotalSamples})", legX + 35, legY + 35, 10, Brushes.Black);
            AddText(KMPlotCanvas, $"p = {result.PValueDisplay}", legX + 10, legY + 58, 10, result.IsSignificant ? Brushes.Green : Brushes.Gray, FontWeights.Bold);
        }

        private void DrawKMCurve(Canvas c, List<KMPoint> curve, int maxTime, double marginL, double marginT, double plotW, double plotH, Brush color, bool showCensored)
        {
            if (curve.Count < 2) return;

            for (int i = 1; i < curve.Count; i++)
            {
                double x1 = marginL + plotW * curve[i - 1].Time / maxTime;
                double x2 = marginL + plotW * curve[i].Time / maxTime;
                double y1 = marginT + plotH - plotH * curve[i - 1].Survival;
                double y2 = marginT + plotH - plotH * curve[i].Survival;

                // Horizontal line (step)
                AddLine(c, x1, y1, x2, y1, color, 2.5);
                // Vertical line (drop)
                if (Math.Abs(y1 - y2) > 0.5)
                    AddLine(c, x2, y1, x2, y2, color, 2.5);

                // Censored mark (tick mark)
                if (showCensored && curve[i].Censored > 0)
                {
                    AddLine(c, x2, y2 - 5, x2, y2 + 5, color, 1.5);
                }
            }
        }

        private void CopyKMStats_Click(object sender, RoutedEventArgs e)
        {
            if (_currentKMResult == null) { MessageBox.Show("No data."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"Gene\t{_currentKMGene}");
            sb.AppendLine($"Cancer\t{_currentKMCancer}");
            sb.AppendLine($"Cutoff Percentile\t{_currentKMResult.CutoffPercentile:F0}%");
            sb.AppendLine($"Cutoff Value\t{_currentKMResult.CutoffValue:F3}");
            sb.AppendLine();
            sb.AppendLine($"High Expression Group");
            sb.AppendLine($"  N\t{_currentKMResult.HighExpression.TotalSamples}");
            sb.AppendLine($"  Events\t{_currentKMResult.HighExpression.Events}");
            sb.AppendLine($"  Median Survival (months)\t{_currentKMResult.HighExpression.MedianSurvivalMonths?.ToString("F1") ?? "N/A"}");
            sb.AppendLine($"  Mean Survival (months)\t{_currentKMResult.HighExpression.MeanSurvivalMonths:F1}");
            sb.AppendLine();
            sb.AppendLine($"Low Expression Group");
            sb.AppendLine($"  N\t{_currentKMResult.LowExpression.TotalSamples}");
            sb.AppendLine($"  Events\t{_currentKMResult.LowExpression.Events}");
            sb.AppendLine($"  Median Survival (months)\t{_currentKMResult.LowExpression.MedianSurvivalMonths?.ToString("F1") ?? "N/A"}");
            sb.AppendLine($"  Mean Survival (months)\t{_currentKMResult.LowExpression.MeanSurvivalMonths:F1}");
            sb.AppendLine();
            sb.AppendLine($"Log-Rank Test");
            sb.AppendLine($"  Chi-Square\t{_currentKMResult.LogRankChiSquare:F3}");
            sb.AppendLine($"  P-value\t{_currentKMResult.PValueDisplay}");

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Statistics copied!");
        }

        private void ExportKMImage_Click(object sender, RoutedEventArgs e) => ExportCanvasToPng(KMPlotCanvas, "KaplanMeier");

        private void ExportKMCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentKMResult == null) { MessageBox.Show("No data."); return; }

            var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"KM_{_currentKMCancer}_{_currentKMGene}_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Group,Time_Days,Time_Months,Survival_Probability,At_Risk,Events,Censored");

                foreach (var pt in _currentKMResult.HighExpression.Curve)
                    sb.AppendLine($"High,{pt.Time},{pt.TimeMonths:F1},{pt.Survival:F4},{pt.AtRisk},{pt.Events},{pt.Censored}");

                foreach (var pt in _currentKMResult.LowExpression.Curve)
                    sb.AppendLine($"Low,{pt.Time},{pt.TimeMonths:F1},{pt.Survival:F4},{pt.AtRisk},{pt.Events},{pt.Censored}");

                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }
        #endregion

        #region Volcano Plot (Tab 5) - NEW

        private List<VolcanoPointViewModel> _volcanoDisplayList;
        private List<VolcanoPointViewModel> _volcanoFullList;

        /// <summary>
        /// Volcano Point 的 ViewModel，新增 IsHighlighted 屬性
        /// </summary>
        public class VolcanoPointViewModel : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isHighlighted;

            public string GeneId { get; set; }
            public string GeneName { get; set; }
            public double Log2FoldChange { get; set; }
            public double PValue { get; set; }
            public double NegLog10PValue { get; set; }
            public double FDR { get; set; }
            public double TumorMean { get; set; }
            public double NormalMean { get; set; }

            public bool IsHighlighted
            {
                get => _isHighlighted;
                set
                {
                    if (_isHighlighted != value)
                    {
                        _isHighlighted = value;
                        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHighlighted)));
                    }
                }
            }

            public string PValueDisplay => PValue < 0.001 ? $"{PValue:E2}" : $"{PValue:F4}";
            public string FDRDisplay => FDR < 0.001 ? $"{FDR:E2}" : $"{FDR:F4}";

            public bool IsSignificant(double fdrThreshold = 0.05, double fcThreshold = 1.0)
                => FDR < fdrThreshold && Math.Abs(Log2FoldChange) > fcThreshold;

            public string Regulation
            {
                get
                {
                    if (!IsSignificant()) return "NS";
                    return Log2FoldChange > 0 ? "Up" : "Down";
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            // 從 VolcanoPoint 轉換
            public static VolcanoPointViewModel FromVolcanoPoint(VolcanoPoint p)
            {
                return new VolcanoPointViewModel
                {
                    GeneId = p.GeneId,
                    GeneName = p.GeneName,
                    Log2FoldChange = p.Log2FoldChange,
                    PValue = p.PValue,
                    NegLog10PValue = p.NegLog10PValue,
                    FDR = p.FDR,
                    TumorMean = p.TumorMean,
                    NormalMean = p.NormalMean,
                    IsHighlighted = false
                };
            }
        }

        private async void GenerateVolcano_Click(object sender, RoutedEventArgs e)
        {
            var projects = GetSelectedProjects();
            if (projects.Count == 0) { MessageBox.Show("Please select a cancer type."); return; }

            var project = projects.First();

            if (project.n_tumor < 3 || project.n_normal < 3)
            {
                MessageBox.Show($"{project.CancerCode} doesn't have enough Tumor/Normal samples for comparison.\n(Need at least 3 each)",
                    "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ShowProgress("Generating Volcano Plot...", 0);
                UpdateProgress(5, "Loading data from TCGA database...");

                var progress = new Progress<int>(p => UpdateProgress(p, $"Processing genes... {p}%"));
                var volcanoData = await _dataService.GetVolcanoDataAsync(project.project_id, progress);

                if (volcanoData == null || volcanoData.Points.Count == 0)
                {
                    HideProgress();
                    MessageBox.Show("Failed to generate volcano plot data.");
                    return;
                }

                _currentVolcanoData = volcanoData;
                _currentVolcanoCancer = project.CancerCode;

                // 轉換為 ViewModel
                UpdateProgress(95, "Preparing display data...");
                _volcanoFullList = volcanoData.Points
                    .Select(p => VolcanoPointViewModel.FromVolcanoPoint(p))
                    .ToList();

                // gene_names 為空時批次轉回 Symbol
                if (_geneHelperReady)
                {
                    var symbolMap = GetSymbolMap(_volcanoFullList.Select(v => v.GeneId));
                    foreach (var v in _volcanoFullList)
                        if (string.IsNullOrEmpty(v.GeneName) && symbolMap.TryGetValue(v.GeneId, out var sym))
                            v.GeneName = sym;
                }

                _volcanoDisplayList = _volcanoFullList;

                UpdateProgress(98, "Drawing plot...");
                UpdateVolcanoPlot();

                HideProgress();

                // 顯示資料來源資訊
                VolcanoResultText.Text = $"📊 Data Source: {project.project_id}\n" +
                                          $"Cancer: {project.CancerCode} | " +
                                          $"Genes analyzed: {volcanoData.Points.Count:N0} | " +
                                          $"Tumor samples: {project.n_tumor} | " +
                                          $"Normal samples: {project.n_normal}\n" +
                                          $"⚠️ Data loaded from TCGA binary files (real clinical data)";
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void VolcanoThreshold_Changed(object sender, TextChangedEventArgs e)
        {
            if (_currentVolcanoData != null)
                UpdateVolcanoPlot();
        }

        // 搜尋功能
        private void VolcanoSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_volcanoFullList == null) return;

            string searchText = VolcanoSearchTextBox.Text?.Trim().ToUpper() ?? "";

            if (string.IsNullOrEmpty(searchText))
            {
                _volcanoDisplayList = _volcanoFullList;
            }
            else
            {
                _volcanoDisplayList = _volcanoFullList
                    .Where(p => p.GeneName.ToUpper().Contains(searchText) ||
                               p.GeneId.ToUpper().Contains(searchText))
                    .ToList();
            }

            UpdateVolcanoTable();
        }

        private void VolcanoSearchClear_Click(object sender, RoutedEventArgs e)
        {
            VolcanoSearchTextBox.Text = "";
            _volcanoDisplayList = _volcanoFullList;
            UpdateVolcanoTable();
        }

        // 勾選高亮功能
        private void VolcanoHighlightCheckbox_Click(object sender, RoutedEventArgs e)
        {
            // 重新繪製圖表以顯示高亮
            UpdateVolcanoPlotWithHighlights();
            UpdateHighlightedCount();
        }

        private void VolcanoHighlightSelected_Click(object sender, RoutedEventArgs e)
        {
            // 將目前選中的列設為高亮
            foreach (var item in VolcanoStatsGrid.SelectedItems)
            {
                if (item is VolcanoPointViewModel vm)
                {
                    vm.IsHighlighted = true;
                }
            }

            UpdateVolcanoPlotWithHighlights();
            UpdateHighlightedCount();
        }

        private void VolcanoClearHighlights_Click(object sender, RoutedEventArgs e)
        {
            if (_volcanoFullList == null) return;

            foreach (var p in _volcanoFullList)
            {
                p.IsHighlighted = false;
            }

            UpdateVolcanoPlotWithHighlights();
            UpdateHighlightedCount();
        }

        private void UpdateHighlightedCount()
        {
            if (_volcanoFullList == null)
            {
                VolcanoHighlightedCountText.Text = "0";
                return;
            }

            int count = _volcanoFullList.Count(p => p.IsHighlighted);
            VolcanoHighlightedCountText.Text = count.ToString();
        }

        private void UpdateVolcanoPlot()
        {
            if (_currentVolcanoData == null) return;

            if (!double.TryParse(VolcanoFdrTextBox.Text, out double fdrThreshold)) fdrThreshold = 0.05;
            if (!double.TryParse(VolcanoFcTextBox.Text, out double fcThreshold)) fcThreshold = 1.0;

            fdrThreshold = Math.Max(0.001, Math.Min(0.5, fdrThreshold));
            fcThreshold = Math.Max(0, Math.Min(5, fcThreshold));

            DrawVolcanoPlotWithHighlights(_volcanoFullList, fdrThreshold, fcThreshold);
            UpdateVolcanoTable();
        }

        private void UpdateVolcanoPlotWithHighlights()
        {
            if (_volcanoFullList == null) return;

            if (!double.TryParse(VolcanoFdrTextBox.Text, out double fdrThreshold)) fdrThreshold = 0.05;
            if (!double.TryParse(VolcanoFcTextBox.Text, out double fcThreshold)) fcThreshold = 1.0;

            DrawVolcanoPlotWithHighlights(_volcanoFullList, fdrThreshold, fcThreshold);
        }

        private void UpdateVolcanoTable()
        {
            if (_volcanoDisplayList == null) return;

            if (!double.TryParse(VolcanoFdrTextBox.Text, out double fdrThreshold)) fdrThreshold = 0.05;
            if (!double.TryParse(VolcanoFcTextBox.Text, out double fcThreshold)) fcThreshold = 1.0;

            List<VolcanoPointViewModel> displayGenes;
            string searchText = VolcanoSearchTextBox?.Text?.Trim().ToUpper() ?? "";

            if (!string.IsNullOrEmpty(searchText))
            {
                // 搜尋模式：顯示所有符合搜尋的基因（不論是否顯著）
                displayGenes = _volcanoDisplayList
                    .OrderByDescending(p => p.FDR < fdrThreshold && Math.Abs(p.Log2FoldChange) > fcThreshold) // 顯著的排前面
                    .ThenBy(p => p.PValue)
                    .ToList();
            }
            else
            {
                // 一般模式：只顯示顯著基因（無上限）
                displayGenes = _volcanoDisplayList
                    .Where(p => p.FDR < fdrThreshold && Math.Abs(p.Log2FoldChange) > fcThreshold)
                    .OrderBy(p => p.PValue)
                    .ToList();
            }

            VolcanoStatsGrid.ItemsSource = displayGenes;

            int upCount = _volcanoFullList?.Count(p => p.FDR < fdrThreshold && p.Log2FoldChange > fcThreshold) ?? 0;
            int downCount = _volcanoFullList?.Count(p => p.FDR < fdrThreshold && p.Log2FoldChange < -fcThreshold) ?? 0;
            int totalGenes = _volcanoFullList?.Count ?? 0;
            int sigInDisplay = displayGenes.Count(p => p.FDR < fdrThreshold && Math.Abs(p.Log2FoldChange) > fcThreshold);

            if (!string.IsNullOrEmpty(searchText))
            {
                VolcanoGeneCountText.Text = $" (Found: {displayGenes.Count}, Significant: {sigInDisplay})";
            }
            else
            {
                VolcanoGeneCountText.Text = $" (Significant: {displayGenes.Count}, Up: {upCount}, Down: {downCount})";
            }
            VolcanoSummaryText.Text = $"🔴 Up: {upCount}  |  🔵 Down: {downCount}  |  ⚪ NS: {totalGenes - upCount - downCount}";
        }



        private void DrawVolcanoPlotWithHighlights(List<VolcanoPointViewModel> data, double fdrThreshold, double fcThreshold)
        {
            VolcanoPlotCanvas.Children.Clear();

            if (data == null || data.Count == 0) return;

            int width = (int)VolcanoPlotCanvas.Width;   // 900
            int height = (int)VolcanoPlotCanvas.Height; // 600
            int marginL = 70, marginR = 50, marginT = 50, marginB = 70;
            int plotW = width - marginL - marginR;
            int plotH = height - marginT - marginB;

            // 計算軸範圍
            double maxFC = data.Max(p => Math.Abs(p.Log2FoldChange));
            maxFC = Math.Ceiling(Math.Min(maxFC, 10));

            // Y軸：根據實際數據的最大值，取整到合適的上限
            double rawMaxP = data.Max(p => p.NegLog10PValue);
            double maxNegLogP;
            if (rawMaxP <= 10) maxNegLogP = Math.Ceiling(rawMaxP / 2) * 2;        // 取整到 2 的倍數
            else if (rawMaxP <= 30) maxNegLogP = Math.Ceiling(rawMaxP / 5) * 5;   // 取整到 5 的倍數
            else if (rawMaxP <= 100) maxNegLogP = Math.Ceiling(rawMaxP / 10) * 10; // 取整到 10 的倍數
            else maxNegLogP = Math.Ceiling(rawMaxP / 20) * 20;                     // 取整到 20 的倍數

            // 使用 WriteableBitmap 繪製所有點 (效能優化)
            if (_volcanoBitmap == null || _volcanoBitmap.PixelWidth != width || _volcanoBitmap.PixelHeight != height)
            {
                _volcanoBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            }

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            // 填充白色背景
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;     // B
                pixels[i + 1] = 255; // G
                pixels[i + 2] = 255; // R
                pixels[i + 3] = 255; // A
            }

            // 繪製繪圖區邊框
            DrawPixelRect(pixels, stride, width, height, marginL, marginT, plotW, plotH, 200, 200, 200);

            // 計算格線間隔 - Y軸 (根據maxNegLogP決定)
            double yGridInterval;
            if (maxNegLogP <= 10) yGridInterval = 2;
            else if (maxNegLogP <= 30) yGridInterval = 5;
            else if (maxNegLogP <= 100) yGridInterval = 10;
            else yGridInterval = 20;

            // 計算格線間隔 - X軸
            double xGridInterval;
            if (maxFC <= 4) xGridInterval = 1;
            else if (maxFC <= 8) xGridInterval = 2;
            else xGridInterval = 4;

            // 繪製 Y 軸格線
            for (double yVal = 0; yVal <= maxNegLogP; yVal += yGridInterval)
            {
                int y = marginT + plotH - (int)(plotH * yVal / maxNegLogP);
                DrawPixelHLine(pixels, stride, width, marginL, marginL + plotW, y, 230, 230, 230);
            }

            // 繪製 X 軸格線
            for (double fc = -maxFC; fc <= maxFC; fc += xGridInterval)
            {
                int x = marginL + (int)(plotW * (fc + maxFC) / (2 * maxFC));
                DrawPixelVLine(pixels, stride, height, x, marginT, marginT + plotH, 230, 230, 230);
            }

            // 繪製閾值線
            int fdrLineY = marginT + plotH - (int)(plotH * (-Math.Log10(fdrThreshold)) / maxNegLogP);
            if (fdrLineY > marginT && fdrLineY < marginT + plotH)
                DrawPixelDashedHLine(pixels, stride, width, marginL, marginL + plotW, fdrLineY, 128, 128, 128);

            int fcLeftX = marginL + (int)(plotW * (-fcThreshold + maxFC) / (2 * maxFC));
            int fcRightX = marginL + (int)(plotW * (fcThreshold + maxFC) / (2 * maxFC));
            DrawPixelDashedVLine(pixels, stride, height, fcLeftX, marginT, marginT + plotH, 128, 128, 128);
            DrawPixelDashedVLine(pixels, stride, height, fcRightX, marginT, marginT + plotH, 128, 128, 128);

            // 繪製所有數據點 (直接操作像素)
            int upCount = 0, downCount = 0;

            foreach (var pt in data.Where(p => !p.IsHighlighted))
            {
                int x = marginL + (int)(plotW * (pt.Log2FoldChange + maxFC) / (2 * maxFC));
                int y = marginT + plotH - (int)(plotH * Math.Min(pt.NegLog10PValue, maxNegLogP) / maxNegLogP);

                if (x < marginL || x >= marginL + plotW || y < marginT || y >= marginT + plotH)
                    continue;

                byte r, g, b, a;
                int radius;

                if (pt.FDR < fdrThreshold && pt.Log2FoldChange > fcThreshold)
                {
                    r = 231; g = 76; b = 60; a = 200;  // 紅色
                    radius = 3;
                    upCount++;
                }
                else if (pt.FDR < fdrThreshold && pt.Log2FoldChange < -fcThreshold)
                {
                    r = 52; g = 152; b = 219; a = 200;  // 藍色
                    radius = 3;
                    downCount++;
                }
                else
                {
                    r = 160; g = 160; b = 160; a = 100;  // 灰色
                    radius = 2;
                }

                DrawPixelCircle(pixels, stride, width, height, x, y, radius, r, g, b, a);
            }

            // 寫入 bitmap
            _volcanoBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            // 加入 Image (只有 1 個元素！)
            var image = new Image
            {
                Source = _volcanoBitmap,
                Width = width,
                Height = height
            };
            VolcanoPlotCanvas.Children.Add(image);

            // Y 軸刻度標籤
            for (double yVal = 0; yVal <= maxNegLogP; yVal += yGridInterval)
            {
                double y = marginT + plotH - plotH * yVal / maxNegLogP;
                AddText(VolcanoPlotCanvas, yVal.ToString("F0"), marginL - 35, y - 8, 9, Brushes.Gray);
            }

            // X 軸刻度標籤
            for (double fc = -maxFC; fc <= maxFC; fc += xGridInterval)
            {
                double x = marginL + plotW * (fc + maxFC) / (2 * maxFC);
                AddText(VolcanoPlotCanvas, fc.ToString("F0"), x - 8, marginT + plotH + 10, 9, Brushes.Gray);
            }

            // 軸標題
            AddText(VolcanoPlotCanvas, "log2(Fold Change)", marginL + plotW / 2 - 60, height - 25, 13, Brushes.Black, FontWeights.SemiBold);
            AddRotatedText(VolcanoPlotCanvas, "-log10(P-value)", 15, height / 2 + 50, -90, 13);

            // 圖表標題
            AddText(VolcanoPlotCanvas, $"{_currentVolcanoCancer}: Tumor vs Normal", marginL, 15, 16, Brushes.Black, FontWeights.Bold);

            // 圖例
            double legX = marginL + plotW - 120, legY = marginT + 10;
            AddRect(VolcanoPlotCanvas, legX, legY, 115, 95, Brushes.White, Brushes.LightGray);
            AddEllipse(VolcanoPlotCanvas, legX + 10, legY + 14, 8, new SolidColorBrush(Color.FromRgb(231, 76, 60)));
            AddText(VolcanoPlotCanvas, $"Up ({upCount:N0})", legX + 25, legY + 8, 10);
            AddEllipse(VolcanoPlotCanvas, legX + 10, legY + 34, 8, new SolidColorBrush(Color.FromRgb(52, 152, 219)));
            AddText(VolcanoPlotCanvas, $"Down ({downCount:N0})", legX + 25, legY + 28, 10);
            AddEllipse(VolcanoPlotCanvas, legX + 10, legY + 54, 8, new SolidColorBrush(Color.FromRgb(160, 160, 160)));
            AddText(VolcanoPlotCanvas, $"NS ({data.Count - upCount - downCount:N0})", legX + 25, legY + 48, 10);

            // Highlighted legend
            var highlightDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                Stroke = Brushes.Black,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(highlightDot, legX + 8);
            Canvas.SetTop(highlightDot, legY + 72);
            VolcanoPlotCanvas.Children.Add(highlightDot);
            AddText(VolcanoPlotCanvas, "Highlighted", legX + 25, legY + 68, 10);

            // 繪製高亮的點 (用傳統 Ellipse，因為數量少)
            foreach (var pt in data.Where(p => p.IsHighlighted))
            {
                double x = marginL + plotW * (pt.Log2FoldChange + maxFC) / (2 * maxFC);
                double y = marginT + plotH - plotH * Math.Min(pt.NegLog10PValue, maxNegLogP) / maxNegLogP;

                if (x < marginL || x > marginL + plotW) continue;

                Color fillColor;
                if (pt.FDR < fdrThreshold && pt.Log2FoldChange > fcThreshold)
                    fillColor = Color.FromRgb(231, 76, 60);
                else if (pt.FDR < fdrThreshold && pt.Log2FoldChange < -fcThreshold)
                    fillColor = Color.FromRgb(52, 152, 219);
                else
                    fillColor = Color.FromRgb(255, 152, 0);

                var dot = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(fillColor),
                    Stroke = Brushes.Black,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(dot, x - 7);
                Canvas.SetTop(dot, y - 7);
                VolcanoPlotCanvas.Children.Add(dot);

                var label = new TextBlock
                {
                    Text = pt.GeneName,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    Padding = new Thickness(2)
                };
                double labelX = x + 8;
                if (labelX + 60 > marginL + plotW) labelX = x - 60;
                Canvas.SetLeft(label, labelX);
                Canvas.SetTop(label, y - 10);
                VolcanoPlotCanvas.Children.Add(label);
            }

            // 更新統計
            VolcanoSummaryText.Text = $"🔴 Up: {upCount:N0}  |  🔵 Down: {downCount:N0}  |  ⚪ NS: {data.Count - upCount - downCount:N0}";
        }

        // 像素繪圖輔助方法
        private void DrawPixelCircle(byte[] pixels, int stride, int width, int height,
            int cx, int cy, int radius, byte r, byte g, byte b, byte a)
        {
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= r2)
                    {
                        int x = cx + dx;
                        int y = cy + dy;
                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            int idx = y * stride + x * 4;
                            float alpha = a / 255f;
                            pixels[idx] = (byte)(b * alpha + pixels[idx] * (1 - alpha));
                            pixels[idx + 1] = (byte)(g * alpha + pixels[idx + 1] * (1 - alpha));
                            pixels[idx + 2] = (byte)(r * alpha + pixels[idx + 2] * (1 - alpha));
                            pixels[idx + 3] = 255;
                        }
                    }
                }
            }
        }

        private void DrawPixelRect(byte[] pixels, int stride, int width, int height,
            int rx, int ry, int rw, int rh, byte r, byte g, byte b)
        {
            for (int x = rx; x < rx + rw && x < width; x++)
            {
                SetPixelSafe(pixels, stride, width, height, x, ry, r, g, b);
                SetPixelSafe(pixels, stride, width, height, x, ry + rh - 1, r, g, b);
            }
            for (int y = ry; y < ry + rh && y < height; y++)
            {
                SetPixelSafe(pixels, stride, width, height, rx, y, r, g, b);
                SetPixelSafe(pixels, stride, width, height, rx + rw - 1, y, r, g, b);
            }
        }

        private void DrawPixelHLine(byte[] pixels, int stride, int width, int x1, int x2, int y, byte r, byte g, byte b)
        {
            for (int x = x1; x <= x2; x++)
                SetPixelSafe(pixels, stride, width, 600, x, y, r, g, b);
        }

        private void DrawPixelVLine(byte[] pixels, int stride, int height, int x, int y1, int y2, byte r, byte g, byte b)
        {
            for (int y = y1; y <= y2; y++)
                SetPixelSafe(pixels, stride, 900, height, x, y, r, g, b);
        }

        private void DrawPixelDashedHLine(byte[] pixels, int stride, int width, int x1, int x2, int y, byte r, byte g, byte b)
        {
            for (int x = x1; x <= x2; x++)
                if ((x / 4) % 2 == 0)
                    SetPixelSafe(pixels, stride, width, 600, x, y, r, g, b);
        }

        private void DrawPixelDashedVLine(byte[] pixels, int stride, int height, int x, int y1, int y2, byte r, byte g, byte b)
        {
            for (int y = y1; y <= y2; y++)
                if ((y / 4) % 2 == 0)
                    SetPixelSafe(pixels, stride, 900, height, x, y, r, g, b);
        }

        private void SetPixelSafe(byte[] pixels, int stride, int width, int height, int x, int y, byte r, byte g, byte b)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                int idx = y * stride + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;
            }
        }


        private void CopyVolcanoStats_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVolcanoData == null) { MessageBox.Show("No data."); return; }

            if (!double.TryParse(VolcanoFdrTextBox.Text, out double fdrThreshold)) fdrThreshold = 0.05;
            if (!double.TryParse(VolcanoFcTextBox.Text, out double fcThreshold)) fcThreshold = 1.0;

            var sigGenes = _currentVolcanoData.Points
                .Where(p => p.FDR < fdrThreshold && Math.Abs(p.Log2FoldChange) > fcThreshold)
                .OrderBy(p => p.PValue)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Gene\tGeneID\tLog2FC\tP-value\tFDR\tRegulation\tTumorMean\tNormalMean");
            foreach (var g in sigGenes)
                sb.AppendLine($"{g.GeneName}\t{g.GeneId}\t{g.Log2FoldChange:F3}\t{g.PValue:E2}\t{g.FDR:E2}\t{g.Regulation}\t{g.TumorMean:F3}\t{g.NormalMean:F3}");

            Clipboard.SetText(sb.ToString());
            MessageBox.Show($"Copied {sigGenes.Count} significant genes!");
        }

        private void ExportVolcanoImage_Click(object sender, RoutedEventArgs e) => ExportCanvasToPng(VolcanoPlotCanvas, "VolcanoPlot");

        private void ExportVolcanoCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVolcanoData == null) { MessageBox.Show("No data."); return; }

            var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"Volcano_{_currentVolcanoCancer}_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Gene,GeneID,Log2FC,PValue,NegLog10P,FDR,Regulation,TumorMean,NormalMean,IsHighlighted");
                foreach (var g in _volcanoFullList?.OrderBy(p => p.PValue) ?? _currentVolcanoData.Points.Select(VolcanoPointViewModel.FromVolcanoPoint).OrderBy(p => p.PValue))
                    sb.AppendLine($"{g.GeneName},{g.GeneId},{g.Log2FoldChange:F4},{g.PValue:E4},{g.NegLog10PValue:F4},{g.FDR:E4},{g.Regulation},{g.TumorMean:F4},{g.NormalMean:F4},{g.IsHighlighted}");

                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }

        #endregion
        #region Helper Methods

        /// <summary>
        /// 批次將 ENSG ID 轉回 Symbol，回傳 id→symbol 的字典
        /// 適用於 gene_names 全空的新版 JSON
        /// </summary>
        private Dictionary<string, string> GetSymbolMap(IEnumerable<string> geneIds)
        {
            if (!_geneHelperReady) return new Dictionary<string, string>();
            return _geneHelper.ToSymbols(geneIds.Where(id => !string.IsNullOrEmpty(id)).Distinct());
        }
        private void AddRect(Canvas c, double x, double y, double w, double h, Brush fill, Brush stroke, double strokeW = 1)
        {
            var r = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = strokeW };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r);
        }

        private void AddLine(Canvas c, double x1, double y1, double x2, double y2, Brush stroke, double w, bool dash = false)
        {
            var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = w };
            if (dash) l.StrokeDashArray = new DoubleCollection { 2, 2 };
            c.Children.Add(l);
        }

        private void AddText(Canvas c, string text, double x, double y, double size, Brush fg = null, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, Foreground = fg ?? Brushes.Black };
            if (weight.HasValue) t.FontWeight = weight.Value;
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y); c.Children.Add(t);
        }

        private void AddRotatedText(Canvas c, string text, double x, double y, double angle, double size)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, RenderTransform = new RotateTransform(angle) };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y); c.Children.Add(t);
        }

        private void AddEllipse(Canvas c, double x, double y, double size, Brush fill)
        {
            var e = new Ellipse { Width = size, Height = size, Fill = fill };
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e);
        }

        private void ExportCanvasToPng(Canvas canvas, string prefix)
        {
            if (canvas.Children.Count == 0) { MessageBox.Show("No chart to export."); return; }

            var dialog = new SaveFileDialog { Filter = "PNG files (*.png)|*.png", FileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
            if (dialog.ShowDialog() == true)
            {
                canvas.Measure(new Size(canvas.Width, canvas.Height));
                canvas.Arrange(new Rect(new Size(canvas.Width, canvas.Height)));

                var renderBitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(canvas);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using (var stream = File.Create(dialog.FileName))
                    encoder.Save(stream);

                MessageBox.Show($"Exported to:\n{dialog.FileName}");
            }
        }

        private void CopyHeatmapStats_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCorrelationMatrix == null) { MessageBox.Show("No data."); return; }
            var sb = new StringBuilder();
            sb.Append("Gene");
            foreach (var g in _currentCorrelationGenes) sb.Append($"\t{g}");
            sb.AppendLine();
            for (int i = 0; i < _currentCorrelationGenes.Count; i++)
            {
                sb.Append(_currentCorrelationGenes[i]);
                for (int j = 0; j < _currentCorrelationGenes.Count; j++) sb.Append($"\t{_currentCorrelationMatrix[i, j]:F4}");
                sb.AppendLine();
            }
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Copied!");
        }
        #endregion

        #region Co-Expression Analysis (Tab 6)

        public class CoExprDisplayRow
        {
            public int Rank { get; set; }
            public string GeneName { get; set; }
            public string GeneId { get; set; }
            public double PearsonR { get; set; }
            public double AbsR => Math.Abs(PearsonR);
            public double PValue { get; set; }
            public double FDR { get; set; }
            public string Direction => PearsonR > 0 ? "Positive" : "Negative";
            public int N { get; set; }

            public string PValueDisplay => PValue < 0.001 ? $"{PValue:E2}" : $"{PValue:F4}";
            public string FDRDisplay => FDR < 0.001 ? $"{FDR:E2}" : $"{FDR:F4}";
        }

        private async void AnalyzeCoExpression_Click(object sender, RoutedEventArgs e)
        {
            var targetGene = CoExprGeneTextBox.Text.Trim();
            if (string.IsNullOrEmpty(targetGene)) { MessageBox.Show("Please enter a target gene."); return; }

            var projects = GetSelectedProjects();
            if (projects.Count == 0) { MessageBox.Show("Select a cancer type."); return; }

            string condition = GetSelectedCondition();

            try
            {
                ShowProgress("Analyzing co-expression...", 0);
                var project = projects.First();
                _currentCoExprCancer = project.CancerCode;

                // 嘗試基因名稱轉換
                string resolvedGene = targetGene;
                if (_geneHelperReady)
                {
                    var ensg = _geneHelper.ResolveForTcga(targetGene);
                    if (!string.IsNullOrEmpty(ensg)) resolvedGene = ensg;
                }

                UpdateProgress(10, $"Computing correlations for {resolvedGene} in {project.CancerCode}...");

                var progress = new Progress<int>(v => UpdateProgress(10 + v * 80 / 100,
                    $"Scanning genes... {v}%"));

                _currentCoExprData = await Task.Run(() =>
                    _dataService.GetGeneCorrelationAsync(project.project_id, resolvedGene, condition, progress));

                if (_currentCoExprData == null)
                {
                    HideProgress();
                    CoExprSummaryText.Text = $"Gene '{targetGene}' not found or insufficient data.";
                    return;
                }

                UpdateProgress(95, "Applying filters...");
                ApplyCoExprFilters();

                HideProgress();
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void CoExprFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentCoExprData == null) return;
            ApplyCoExprFilters();
        }

        private void CoExprSort_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentCoExprData == null) return;
            ApplyCoExprFilters();
        }

        private void ApplyCoExprFilters()
        {
            if (_currentCoExprData == null) return;

            if (!double.TryParse(CoExprFdrTextBox.Text.Trim(), out double fdrThreshold))
                fdrThreshold = 0.05;
            if (!double.TryParse(CoExprMinRTextBox.Text.Trim(), out double minAbsR))
                minAbsR = 0.3;

            var filtered = _currentCoExprData.CorrelatedGenes
                .Where(g => g.FDR < fdrThreshold && g.AbsR >= minAbsR);

            // 排序：同值時用其他條件作次要排序（stable sort）
            if (CoExprSortPValue.IsChecked == true)
                filtered = filtered.OrderBy(g => g.PValue)
                                   .ThenByDescending(g => g.AbsR);
            else if (CoExprSortR.IsChecked == true)
                filtered = filtered.OrderByDescending(g => g.PearsonR)
                                   .ThenBy(g => g.PValue);
            else // 預設 |r| 降序
                filtered = filtered.OrderByDescending(g => g.AbsR)
                                   .ThenBy(g => g.PValue);

            var list = filtered.ToList();

            _currentCoExprDisplayRows = list.Select((g, i) => new CoExprDisplayRow
            {
                Rank = i + 1,
                GeneName = g.GeneName,
                GeneId = g.GeneId,
                PearsonR = g.PearsonR,
                PValue = g.PValue,
                FDR = g.FDR,
                N = g.N
            }).ToList();

            // gene_names 為空時，批次用 GeneIdService 轉換 ENSG → Symbol 補回
            if (_geneHelperReady)
            {
                var emptyRows = _currentCoExprDisplayRows
                    .Where(r => string.IsNullOrEmpty(r.GeneName) && !string.IsNullOrEmpty(r.GeneId))
                    .ToList();
                if (emptyRows.Count > 0)
                {
                    var symbolMap = _geneHelper.ToSymbols(emptyRows.Select(r => r.GeneId));
                    foreach (var row in emptyRows)
                        if (symbolMap.TryGetValue(row.GeneId, out var sym) && !string.IsNullOrEmpty(sym))
                            row.GeneName = sym;
                }
            }

            CoExprDataGrid.ItemsSource = _currentCoExprDisplayRows;

            int totalSig = _currentCoExprData.CorrelatedGenes.Count(g => g.FDR < fdrThreshold);
            int posCount = list.Count(r => r.PearsonR > 0);
            int negCount = list.Count(r => r.PearsonR < 0);

            CoExprSummaryText.Text = $"Target: {_currentCoExprData.TargetGeneName} ({_currentCoExprData.TargetGeneId})  |  " +
                                     $"Cancer: {_currentCoExprCancer}  |  Condition: {_currentCoExprData.Condition}  |  " +
                                     $"Samples: {_currentCoExprData.SampleCount}  |  " +
                                     $"Significant (FDR<{fdrThreshold}): {totalSig}  |  " +
                                     $"Showing (|r|≥{minAbsR}): {_currentCoExprDisplayRows.Count} (↑{posCount} ↓{negCount})";

            CoExprResultText.Text = $"Total genes scanned: {_currentCoExprData.CorrelatedGenes.Count:N0}";
        }

        private void CoExprSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentCoExprDisplayRows == null) return;

            var search = CoExprSearchTextBox.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(search))
            {
                CoExprDataGrid.ItemsSource = _currentCoExprDisplayRows;
                return;
            }

            var filtered = _currentCoExprDisplayRows
                .Where(r => r.GeneName.ToUpper().Contains(search) || r.GeneId.ToUpper().Contains(search))
                .ToList();
            CoExprDataGrid.ItemsSource = filtered;
        }

        private void CopyCoExprTable_Click(object sender, RoutedEventArgs e)
        {
            if (CoExprDataGrid.ItemsSource == null) { MessageBox.Show("No data."); return; }

            // 複製當前 DataGrid 顯示的內容（包含搜尋過濾後的結果）
            var visibleRows = (CoExprDataGrid.ItemsSource as IEnumerable<CoExprDisplayRow>)?.ToList();
            if (visibleRows == null || visibleRows.Count == 0) { MessageBox.Show("No data."); return; }

            var sb = new StringBuilder();
            // 標題列與 DataGrid 欄位完全一致
            sb.AppendLine("#\tGene\tGene ID\tPearson r\t|r|\tP-value\tFDR\tDirection\tN");
            foreach (var r in visibleRows)
            {
                sb.AppendLine($"{r.Rank}\t{r.GeneName}\t{r.GeneId}\t{r.PearsonR:F4}\t{r.AbsR:F4}\t{r.PValueDisplay}\t{r.FDRDisplay}\t{r.Direction}\t{r.N}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show($"Copied {visibleRows.Count} rows!");
        }

        private void ExportCoExprCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCoExprData == null) { MessageBox.Show("No data."); return; }

            var visibleRows = (CoExprDataGrid.ItemsSource as IEnumerable<CoExprDisplayRow>)?.ToList();
            if (visibleRows == null || visibleRows.Count == 0) { MessageBox.Show("No data."); return; }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"CoExpression_{_currentCoExprCancer}_{_currentCoExprData.TargetGeneName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("#,Gene,Gene ID,Pearson r,|r|,P-value,FDR,Direction,N");
                foreach (var r in visibleRows)
                    sb.AppendLine($"{r.Rank},{r.GeneName},{r.GeneId},{r.PearsonR:F6},{r.AbsR:F6},{r.PValue:E4},{r.FDR:E4},{r.Direction},{r.N}");

                File.WriteAllText(dialog.FileName, sb.ToString());
                MessageBox.Show($"Exported {visibleRows.Count} rows to:\n{dialog.FileName}");
            }
        }

        private void CoExprScatterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCoExprData == null) return;
            if (sender is Button btn && btn.Tag is CoExprDisplayRow row)
            {
                // TargetGeneName 可能為空（JSON gene_names 全空），改用 TargetGeneId
                var targetDisplay = !string.IsNullOrEmpty(_currentCoExprData.TargetGeneName)
                    ? _currentCoExprData.TargetGeneName
                    : _currentCoExprData.TargetGeneId;
                var geneDisplay = !string.IsNullOrEmpty(row.GeneName)
                    ? row.GeneName
                    : row.GeneId;

                ScatterGeneXTextBox.Text = targetDisplay;
                ScatterGeneYTextBox.Text = geneDisplay;

                // 切換到 Scatter tab (index 2)
                AnalysisTabControl.SelectedIndex = 2;

                // 自動觸發繪圖
                PlotScatter_Click(this, new RoutedEventArgs());
            }
        }

        #endregion
    }
}