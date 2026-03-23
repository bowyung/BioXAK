using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace BioSAK
{
    public partial class GraphGen : Page
    {
        private DataTable dataTable = new DataTable();
        private int currentYColumns = 3;
        private int currentYReplicates = 1;
        private int currentXReplicates = 1;
        private int currentDataMode = 0;
        private string currentChartType = "XY"; // Track current chart type (default XY)
        private List<TextBox> columnTitleBoxes = new List<TextBox>();

        private const int DEFAULT_ROWS = 50;

        // Track last-applied settings for auto-apply dirty detection
        private int _lastAppliedYColumns = 3;
        private int _lastAppliedYReplicates = 1;
        private int _lastAppliedXReplicates = 1;
        private int _lastAppliedDataMode = 0;
        private bool _lastAppliedXHasRepeat = false;

        public GraphGen()
        {
            InitializeComponent();
            // Default to XY tab
            SwitchTab("XY");
        }

        private void InitializeDataGrid()
        {
            ApplySettings_Click(null, null);
        }

        #region Tab Switching

        private void TabXY_Click(object sender, RoutedEventArgs e) => SwitchTab("XY");
        private void TabColumn_Click(object sender, RoutedEventArgs e) => SwitchTab("Column");
        private void TabMultiGroup_Click(object sender, RoutedEventArgs e) => SwitchTab("MultiGroup");

        /// <summary>
        /// Switch chart type tab. Updates visuals, enables/disables X column settings,
        /// sets appropriate defaults for each mode, and optionally loads sample data.
        /// </summary>
        private void SwitchTab(string chartType)
        {
            currentChartType = chartType;
            UpdateTabVisuals(chartType);

            // Re-enable X column settings first (Column mode disables them)
            if (XNoRepeat != null) XNoRepeat.IsEnabled = true;
            if (XHasRepeat != null) XHasRepeat.IsEnabled = true;
            if (XRepeatCount != null) XRepeatCount.IsEnabled = XHasRepeat.IsChecked == true;

            switch (chartType)
            {
                case "Column":
                    // Disable X column settings for Column chart
                    if (XNoRepeat != null) XNoRepeat.IsEnabled = false;
                    if (XHasRepeat != null) XHasRepeat.IsEnabled = false;
                    if (XRepeatCount != null) XRepeatCount.IsEnabled = false;
                    // Reset Y data mode to Single Value
                    if (YDataMode != null) YDataMode.SelectedIndex = 0;
                    if (YRepeatCount != null) YRepeatCount.IsEnabled = false;
                    break;

                case "MultiGroup":
                    // Auto-set Enter Replicates mode for Multi Factors
                    if (YDataMode != null) YDataMode.SelectedIndex = 1;
                    if (YRepeatCount != null) YRepeatCount.IsEnabled = true;
                    break;

                case "XY":
                default:
                    // Default settings for XY
                    break;
            }

            ApplySettings_Click(null, null);
        }

        /// <summary>
        /// Update tab button visuals to indicate active tab.
        /// Active tab: white background, dark text, blue left/right/top border.
        /// Inactive tabs: gray background, muted text.
        /// </summary>
        private void UpdateTabVisuals(string activeTab)
        {
            var activeBackground = new SolidColorBrush(Colors.White);
            var activeForeground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            var activeBorder = new SolidColorBrush(Color.FromRgb(79, 195, 247)); // #4FC3F7

            var inactiveBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            var inactiveForeground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            var inactiveBorder = new SolidColorBrush(Color.FromRgb(224, 224, 224));

            // XY Tab
            TabXY.Background = activeTab == "XY" ? activeBackground : inactiveBackground;
            TabXY.Foreground = activeTab == "XY" ? activeForeground : inactiveForeground;
            TabXY.BorderBrush = activeTab == "XY" ? activeBorder : inactiveBorder;

            // Column Tab
            TabColumn.Background = activeTab == "Column" ? activeBackground : inactiveBackground;
            TabColumn.Foreground = activeTab == "Column" ? activeForeground : inactiveForeground;
            TabColumn.BorderBrush = activeTab == "Column" ? activeBorder : inactiveBorder;

            // MultiGroup Tab
            TabMultiGroup.Background = activeTab == "MultiGroup" ? activeBackground : inactiveBackground;
            TabMultiGroup.Foreground = activeTab == "MultiGroup" ? activeForeground : inactiveForeground;
            TabMultiGroup.BorderBrush = activeTab == "MultiGroup" ? activeBorder : inactiveBorder;
        }

        #endregion

        #region .xak Save/Load

        private void SaveXak_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);
                DataGridMain.CommitEdit(DataGridEditingUnit.Cell, true);

                var graphData = new GraphGenData
                {
                    ChartType = currentChartType,
                    YColumns = currentYColumns,
                    YDataMode = currentDataMode,
                    YReplicates = currentYReplicates,
                    XHasRepeat = XHasRepeat.IsChecked == true,
                    XReplicates = currentXReplicates
                };

                // Save column titles (only user-typed names, not placeholder defaults)
                foreach (var tb in columnTitleBoxes)
                {
                    string tag = tb.Tag?.ToString() ?? "";
                    if (string.IsNullOrEmpty(tag)) continue;
                    string placeholder = tb.DataContext as string ?? "";
                    bool isPlaceholder = IsShowingPlaceholder(tb, placeholder);
                    if (!isPlaceholder && !string.IsNullOrWhiteSpace(tb.Text))
                        graphData.ColumnTitles[tag] = tb.Text;
                }

                // Save data rows (only non-empty rows)
                foreach (DataRow row in dataTable.Rows)
                {
                    bool hasData = false;
                    var rowDict = new Dictionary<string, string>();
                    for (int c = 0; c < dataTable.Columns.Count; c++)
                    {
                        string colName = dataTable.Columns[c].ColumnName;
                        string val = row[c]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(val)) { hasData = true; rowDict[colName] = val; }
                    }
                    if (hasData) graphData.Data.Add(rowDict);
                }

                if (graphData.Data.Count == 0)
                {
                    MessageBox.Show("No data to save.", "Empty Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var doc = new XakDocument
                {
                    Module = "GraphGen",
                    GraphGen = graphData
                };

                XakFileManager.Save(doc, $"graph_{currentChartType.ToLower()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenXak_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = XakFileManager.Open("GraphGen");
                if (doc?.GraphGen == null) return;

                var g = doc.GraphGen;

                // Restore settings UI
                if (YColumnCount != null) YColumnCount.Text = g.YColumns.ToString();
                if (YDataMode != null) YDataMode.SelectedIndex = g.YDataMode;
                if (YRepeatCount != null)
                {
                    YRepeatCount.Text = g.YReplicates.ToString();
                    YRepeatCount.IsEnabled = g.YDataMode == 1;
                }
                if (g.XHasRepeat)
                {
                    if (XHasRepeat != null) XHasRepeat.IsChecked = true;
                    if (XRepeatCount != null) { XRepeatCount.Text = g.XReplicates.ToString(); XRepeatCount.IsEnabled = true; }
                }
                else
                {
                    if (XNoRepeat != null) XNoRepeat.IsChecked = true;
                    if (XRepeatCount != null) XRepeatCount.IsEnabled = false;
                }

                SwitchTab(g.ChartType);

                // Restore column titles
                foreach (var kvp in g.ColumnTitles)
                {
                    var tb = columnTitleBoxes.FirstOrDefault(t => t.Tag?.ToString() == kvp.Key);
                    if (tb != null && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        tb.Text = kvp.Value;
                        tb.Foreground = ActiveTextBrush;
                        tb.FontStyle = FontStyles.Normal;
                    }
                }

                // Restore data
                foreach (DataRow row in dataTable.Rows)
                    for (int i = 0; i < dataTable.Columns.Count; i++) row[i] = DBNull.Value;

                while (dataTable.Rows.Count < g.Data.Count)
                    dataTable.Rows.Add(dataTable.NewRow());

                for (int r = 0; r < g.Data.Count; r++)
                    foreach (var kvp in g.Data[r])
                        if (dataTable.Columns.Contains(kvp.Key))
                            dataTable.Rows[r][kvp.Key] = kvp.Value;

                DataGridMain.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Auto-Apply Settings & Keyboard Shortcuts

        /// <summary>
        /// When keyboard focus leaves the settings panel, check if any settings changed
        /// and auto-apply if needed. This eliminates the need for an "Apply Settings" button.
        /// </summary>
        private void SettingsPanel_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Only trigger when focus leaves the settings panel (not when entering)
            if ((bool)e.NewValue == false)
            {
                TryAutoApplySettings();
            }
        }

        /// <summary>
        /// Compare current UI values against last-applied values.
        /// If anything changed, rebuild the DataGrid.
        /// </summary>
        private void TryAutoApplySettings()
        {
            if (YColumnCount == null || YDataMode == null || YRepeatCount == null || XRepeatCount == null)
                return;

            int newYColumns = int.TryParse(YColumnCount.Text, out int yc) && yc >= 1 ? yc : currentYColumns;
            int newYReplicates = int.TryParse(YRepeatCount.Text, out int yr) && yr >= 1 ? yr : currentYReplicates;
            int newXReplicates = int.TryParse(XRepeatCount.Text, out int xr) && xr >= 1 ? xr : currentXReplicates;
            int newDataMode = YDataMode.SelectedIndex;
            bool newXHasRepeat = XHasRepeat.IsChecked == true;

            bool changed = newYColumns != _lastAppliedYColumns
                        || newYReplicates != _lastAppliedYReplicates
                        || newXReplicates != _lastAppliedXReplicates
                        || newDataMode != _lastAppliedDataMode
                        || newXHasRepeat != _lastAppliedXHasRepeat;

            if (changed)
            {
                ApplySettings_Click(null, null);
            }
        }

        /// <summary>
        /// Global keyboard shortcut handler for the page.
        /// Ctrl+S = Save .xak, Ctrl+O = Open .xak
        /// </summary>
        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.S:
                        SaveXak_Click(sender, e);
                        e.Handled = true;
                        break;
                    case Key.O:
                        OpenXak_Click(sender, e);
                        e.Handled = true;
                        break;
                }
            }
        }

        /// <summary>
        /// Load sample data button click - shows confirmation dialog first.
        /// </summary>
        private void LoadSampleData_Click(object sender, RoutedEventArgs e)
        {
            string chartTypeDisplay = currentChartType switch
            {
                "Column" => "Column",
                "MultiGroup" => "Multi Factors",
                _ => "X-Y Graph"
            };

            var result = MessageBox.Show(
                $"Load sample data for \"{chartTypeDisplay}\"?\n\nThis will clear all current data.",
                "Load Sample Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LoadSampleData(currentChartType);
            }
        }

        #endregion

        #region Settings & Grid Setup

        private void XRepeatChanged(object sender, RoutedEventArgs e)
        {
            if (XRepeatCount != null)
                XRepeatCount.IsEnabled = XHasRepeat.IsChecked == true;
        }

        private void YDataModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (YRepeatCount != null && YDataMode != null)
                YRepeatCount.IsEnabled = YDataMode.SelectedIndex == 1;
        }

        /// <summary>
        /// Set Y Data Mode programmatically (0=Single Value, 1=Enter Replicates, 2=Mean/SD/N)
        /// </summary>
        public void SetYDataMode(int mode)
        {
            if (YDataMode != null)
            {
                YDataMode.SelectedIndex = mode;
                if (YRepeatCount != null)
                    YRepeatCount.IsEnabled = mode == 1;
                ApplySettings_Click(null, null);
            }
        }

        /// <summary>
        /// Set chart type to control X column behavior (kept for backward compatibility)
        /// </summary>
        public void SetChartType(string chartType)
        {
            SwitchTab(chartType);
        }

        private void ApplySettings_Click(object? sender, RoutedEventArgs? e)
        {
            if (!int.TryParse(YColumnCount.Text, out currentYColumns) || currentYColumns < 1) currentYColumns = 3;
            if (!int.TryParse(YRepeatCount.Text, out currentYReplicates) || currentYReplicates < 1) currentYReplicates = 1;
            if (!int.TryParse(XRepeatCount.Text, out currentXReplicates) || currentXReplicates < 1) currentXReplicates = 1;
            if (XNoRepeat.IsChecked == true) currentXReplicates = 1;
            currentDataMode = YDataMode.SelectedIndex;
            BuildDataGrid();

            // Update tracking fields for dirty detection
            _lastAppliedYColumns = currentYColumns;
            _lastAppliedYReplicates = currentYReplicates;
            _lastAppliedXReplicates = currentXReplicates;
            _lastAppliedDataMode = currentDataMode;
            _lastAppliedXHasRepeat = XHasRepeat.IsChecked == true;
        }

        private void BuildDataGrid()
        {
            dataTable = new DataTable();
            DataGridMain.Columns.Clear();
            ColumnHeadersPanel.Children.Clear();
            columnTitleBoxes.Clear();

            int xSubCols = (XHasRepeat.IsChecked == true) ? currentXReplicates : 1;
            int ySubCols = currentDataMode == 1 ? currentYReplicates : (currentDataMode == 2 ? 3 : 1);

            // For Column chart, X is not needed (uses Y series names)
            bool isColumnChart = currentChartType == "Column";

            var xHeaderPanel = CreateXColumnHeader(xSubCols, isColumnChart);
            ColumnHeadersPanel.Children.Add(xHeaderPanel);

            for (int i = 0; i < xSubCols; i++)
            {
                string colName = $"X_{i}";
                dataTable.Columns.Add(colName, typeof(string));

                var xColumn = new DataGridTextColumn
                {
                    Header = xSubCols > 1 ? $"X{i + 1}" : "X",
                    Binding = new Binding(colName) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                    Width = 70,
                    IsReadOnly = isColumnChart
                };

                // Set gray background for Column chart X column
                if (isColumnChart)
                {
                    var style = new Style(typeof(DataGridCell));
                    style.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Color.FromRgb(230, 230, 230))));
                    style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Color.FromRgb(150, 150, 150))));
                    xColumn.CellStyle = style;
                }

                DataGridMain.Columns.Add(xColumn);
            }

            for (int y = 0; y < currentYColumns; y++)
            {
                var yHeaderPanel = CreateYColumnHeader($"Y{y + 1}", ySubCols, y);
                ColumnHeadersPanel.Children.Add(yHeaderPanel);

                if (currentDataMode == 2)
                {
                    string[] subHeaders = { "Mean", "SD", "N" };
                    for (int s = 0; s < 3; s++)
                    {
                        string colName = $"Y{y}_{subHeaders[s]}";
                        dataTable.Columns.Add(colName, typeof(string));
                        DataGridMain.Columns.Add(new DataGridTextColumn
                        {
                            Header = subHeaders[s],
                            Binding = new Binding(colName) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                            Width = 60
                        });
                    }
                }
                else
                {
                    for (int s = 0; s < ySubCols; s++)
                    {
                        string colName = $"Y{y}_{s}";
                        dataTable.Columns.Add(colName, typeof(string));
                        DataGridMain.Columns.Add(new DataGridTextColumn
                        {
                            Header = ySubCols > 1 ? $"Rep{s + 1}" : "Value",
                            Binding = new Binding(colName) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                            Width = 70
                        });
                    }
                }
            }

            for (int i = 0; i < DEFAULT_ROWS; i++)
                dataTable.Rows.Add(dataTable.NewRow());

            DataGridMain.ItemsSource = dataTable.DefaultView;
        }

        private Border CreateXColumnHeader(int subColumns, bool isDisabled = false)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                Background = isDisabled
                    ? new SolidColorBrush(Color.FromRgb(220, 220, 220))
                    : new SolidColorBrush(Color.FromRgb(230, 245, 255)),
                Margin = new Thickness(0, 0, 2, 0),
                Width = 70 * subColumns + (subColumns > 1 ? (subColumns - 1) * 2 : 0)
            };

            var stack = new StackPanel { Margin = new Thickness(5) };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            headerPanel.Children.Add(new TextBlock
            {
                Text = "X",
                FontWeight = FontWeights.Bold,
                Foreground = isDisabled
                    ? new SolidColorBrush(Color.FromRgb(150, 150, 150))
                    : new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                Margin = new Thickness(0, 0, 5, 0)
            });

            TextBox titleBox;
            if (isDisabled)
            {
                titleBox = new TextBox
                {
                    Text = "(Not used)",
                    Width = 70,
                    FontSize = 10,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    Tag = "X",
                    IsEnabled = false,
                    Background = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
                };
            }
            else
            {
                titleBox = CreatePlaceholderTextBox("X", "X", 50);
            }
            columnTitleBoxes.Add(titleBox);
            headerPanel.Children.Add(titleBox);

            stack.Children.Add(headerPanel);
            border.Child = stack;
            return border;
        }

        private Border CreateYColumnHeader(string title, int subColumns, int yIndex)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(255, 243, 224)),
                Margin = new Thickness(0, 0, 2, 0),
                Width = (currentDataMode == 2 ? 60 : 70) * subColumns + (subColumns > 1 ? (subColumns - 1) * 2 : 0)
            };

            var stack = new StackPanel { Margin = new Thickness(5) };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            headerPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                Margin = new Thickness(0, 0, 5, 0)
            });

            string placeholder = $"Series {yIndex + 1}";
            var titleBox = CreatePlaceholderTextBox(placeholder, $"Y{yIndex}", 70);
            columnTitleBoxes.Add(titleBox);
            headerPanel.Children.Add(titleBox);

            stack.Children.Add(headerPanel);
            border.Child = stack;
            return border;
        }

        #region Placeholder TextBox

        // Gray color used for placeholder state
        private static readonly SolidColorBrush PlaceholderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        private static readonly SolidColorBrush ActiveTextBrush = new SolidColorBrush(Colors.Black);

        /// <summary>
        /// Create a TextBox with placeholder (watermark) behavior.
        /// - Starts with gray placeholder text
        /// - On focus: clears if still showing placeholder, selects all if user text
        /// - On blur: restores placeholder if empty
        /// - Tag stores the column key (e.g. "X", "Y0")
        /// - DataContext stores the placeholder string for retrieval
        /// </summary>
        private TextBox CreatePlaceholderTextBox(string placeholder, string tag, double width)
        {
            var tb = new TextBox
            {
                Text = placeholder,
                Width = width,
                FontSize = 10,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Tag = tag,
                DataContext = placeholder, // Store placeholder text for reference
                Foreground = PlaceholderBrush,
                FontStyle = FontStyles.Italic
            };

            tb.GotFocus += PlaceholderTextBox_GotFocus;
            tb.LostFocus += PlaceholderTextBox_LostFocus;

            return tb;
        }

        private void PlaceholderTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is string placeholder)
            {
                if (IsShowingPlaceholder(tb, placeholder))
                {
                    // Clear placeholder — user starts with empty box
                    tb.Text = "";
                    tb.Foreground = ActiveTextBrush;
                    tb.FontStyle = FontStyles.Normal;
                }
                else
                {
                    // User has real text — select all for easy replacement
                    tb.SelectAll();
                }
            }
        }

        private void PlaceholderTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is string placeholder)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    // Restore placeholder
                    tb.Text = placeholder;
                    tb.Foreground = PlaceholderBrush;
                    tb.FontStyle = FontStyles.Italic;
                }
            }
        }

        /// <summary>
        /// Check if a TextBox is currently showing its placeholder text.
        /// </summary>
        private bool IsShowingPlaceholder(TextBox tb, string placeholder)
        {
            return tb.Text == placeholder && tb.Foreground == PlaceholderBrush;
        }

        /// <summary>
        /// Get the effective display text of a column title TextBox.
        /// Returns the user-typed text, or the placeholder (default name) if empty.
        /// </summary>
        private string GetColumnTitle(string tag)
        {
            var tb = columnTitleBoxes.FirstOrDefault(t => t.Tag?.ToString() == tag);
            if (tb == null) return "";

            string placeholder = tb.DataContext as string ?? "";

            // If showing placeholder or empty, return the placeholder as the display name
            if (IsShowingPlaceholder(tb, placeholder) || string.IsNullOrWhiteSpace(tb.Text))
                return placeholder;

            return tb.Text;
        }

        #endregion

        #endregion

        #region Data Operations

        private void SortXAscending_Click(object sender, RoutedEventArgs e) => SortByX(true);
        private void SortXDescending_Click(object sender, RoutedEventArgs e) => SortByX(false);

        private void SortByX(bool ascending)
        {
            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);
            DataGridMain.CommitEdit(DataGridEditingUnit.Cell, true);

            var rowsWithData = new List<(double xValue, DataRow row)>();

            foreach (DataRow row in dataTable.Rows)
            {
                string xStr = row["X_0"]?.ToString() ?? "";
                if (!double.TryParse(xStr, out double xValue)) continue;

                bool hasData = false;
                for (int c = 1; c < dataTable.Columns.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(row[c]?.ToString())) { hasData = true; break; }
                }

                if (hasData)
                {
                    var newRow = dataTable.NewRow();
                    for (int c = 0; c < dataTable.Columns.Count; c++) newRow[c] = row[c];
                    rowsWithData.Add((xValue, newRow));
                }
            }

            if (rowsWithData.Count == 0) { MessageBox.Show("No data to sort.", "Sort"); return; }

            var sorted = ascending ? rowsWithData.OrderBy(r => r.xValue).ToList() : rowsWithData.OrderByDescending(r => r.xValue).ToList();
            dataTable.Rows.Clear();

            foreach (var item in sorted)
            {
                var newRow = dataTable.NewRow();
                for (int c = 0; c < dataTable.Columns.Count; c++) newRow[c] = item.row[c];
                dataTable.Rows.Add(newRow);
            }

            for (int i = 0; i < DEFAULT_ROWS - sorted.Count; i++)
                dataTable.Rows.Add(dataTable.NewRow());

            DataGridMain.Items.Refresh();
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.V: PasteFromClipboard(false); e.Handled = true; break;
                    case Key.C: CopySelectedToClipboard(); e.Handled = true; break;
                    case Key.A: DataGridMain.SelectAll(); e.Handled = true; break;
                }
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedCells();
                e.Handled = true;
            }
        }

        private void DeleteSelectedCells()
        {
            var selectedCells = DataGridMain.SelectedCells;
            if (selectedCells.Count == 0) return;

            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);

            foreach (var cell in selectedCells)
            {
                if (cell.Item is DataRowView drv)
                {
                    int colIndex = DataGridMain.Columns.IndexOf(cell.Column);
                    if (colIndex >= 0 && colIndex < dataTable.Columns.Count)
                    {
                        drv.Row[colIndex] = DBNull.Value;
                    }
                }
            }
            DataGridMain.Items.Refresh();
        }

        private void CopySelectedToClipboard()
        {
            var selectedCells = DataGridMain.SelectedCells;
            if (selectedCells.Count == 0) return;

            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);

            var cellsByRow = new Dictionary<int, Dictionary<int, string>>();
            foreach (var cell in selectedCells)
            {
                int rowIndex = DataGridMain.Items.IndexOf(cell.Item);
                int colIndex = DataGridMain.Columns.IndexOf(cell.Column);
                if (!cellsByRow.ContainsKey(rowIndex)) cellsByRow[rowIndex] = new Dictionary<int, string>();
                if (cell.Item is DataRowView drv && colIndex >= 0 && colIndex < dataTable.Columns.Count)
                    cellsByRow[rowIndex][colIndex] = drv.Row[colIndex]?.ToString() ?? "";
            }

            if (cellsByRow.Count == 0) return;

            var sb = new StringBuilder();
            int minCol = cellsByRow.Values.SelectMany(d => d.Keys).Min();
            int maxCol = cellsByRow.Values.SelectMany(d => d.Keys).Max();

            foreach (var row in cellsByRow.OrderBy(r => r.Key))
            {
                var values = new List<string>();
                for (int col = minCol; col <= maxCol; col++)
                    values.Add(row.Value.ContainsKey(col) ? row.Value[col] : "");
                sb.AppendLine(string.Join("\t", values));
            }
            Clipboard.SetText(sb.ToString());
        }

        private void PasteFromClipboard(bool transpose)
        {
            if (!Clipboard.ContainsText()) return;

            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);

            string[] lines = Clipboard.GetText().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            var data = lines.Select(l => l.Split('\t')).ToArray();

            if (transpose && data.Length > 0)
            {
                int maxCols = data.Max(r => r.Length);
                var transposed = new string[maxCols][];
                for (int c = 0; c < maxCols; c++)
                {
                    transposed[c] = new string[data.Length];
                    for (int r = 0; r < data.Length; r++)
                        transposed[c][r] = c < data[r].Length ? data[r][c] : "";
                }
                data = transposed;
            }

            var currentCell = DataGridMain.CurrentCell;
            int startRow = 0, startCol = 0;
            if (currentCell.IsValid)
            {
                startRow = DataGridMain.Items.IndexOf(currentCell.Item);
                startCol = DataGridMain.Columns.IndexOf(currentCell.Column);
            }

            while (dataTable.Rows.Count < startRow + data.Length)
                dataTable.Rows.Add(dataTable.NewRow());

            for (int i = 0; i < data.Length; i++)
            {
                for (int j = 0; j < data[i].Length; j++)
                {
                    int colIndex = startCol + j;
                    if (colIndex < dataTable.Columns.Count && startRow + i < dataTable.Rows.Count)
                        dataTable.Rows[startRow + i][colIndex] = data[i][j].Trim();
                }
            }
            DataGridMain.Items.Refresh();
        }

        private void ImportClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridMain.Items.Count > 0 && DataGridMain.Columns.Count > 0)
                DataGridMain.CurrentCell = new DataGridCellInfo(DataGridMain.Items[0], DataGridMain.Columns[0]);
            PasteFromClipboard(false);
        }

        // Context menu handlers
        private void ContextCopy_Click(object sender, RoutedEventArgs e) => CopySelectedToClipboard();
        private void ContextPaste_Click(object sender, RoutedEventArgs e) => PasteFromClipboard(false);
        private void ContextPasteTransposed_Click(object sender, RoutedEventArgs e) => PasteFromClipboard(true);
        private void ContextDelete_Click(object sender, RoutedEventArgs e) => DeleteSelectedCells();
        private void ContextSelectAll_Click(object sender, RoutedEventArgs e) => DataGridMain.SelectAll();

        private void ClearAll_Click(object? sender, RoutedEventArgs? e)
        {
            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);
            DataGridMain.CommitEdit(DataGridEditingUnit.Cell, true);

            foreach (DataRow row in dataTable.Rows)
                for (int i = 0; i < dataTable.Columns.Count; i++) row[i] = DBNull.Value;
            DataGridMain.Items.Refresh();
        }

        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            HeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        #endregion

        #region Data Parsing

        /// <summary>
        /// Parse data based on Y Data Mode:
        /// Mode 0 (Single Value): Each row with same X is treated as a replicate. 
        ///                        Rows are grouped by X value, and SD/SEM/95CI are calculated.
        /// Mode 1 (Enter Replicates): Each row has multiple replicate columns per Y.
        /// Mode 2 (Mean/SD/N): User enters pre-calculated statistics.
        /// </summary>
        private List<ChartDataSeries> ParseData(bool allowTextX)
        {
            var seriesList = new List<ChartDataSeries>();
            int xSubCols = (XHasRepeat.IsChecked == true) ? currentXReplicates : 1;
            int ySubCols = currentDataMode == 1 ? currentYReplicates : (currentDataMode == 2 ? 3 : 1);

            // Column chart: special handling - each Y series is a single bar
            if (currentChartType == "Column")
            {
                return ParseDataColumnChart();
            }

            if (currentDataMode == 0)
            {
                // Single Value Mode: Group rows by X value, treat as replicates
                return ParseDataSingleValueMode(allowTextX, xSubCols);
            }
            else
            {
                // Mode 1 (replicates per row) or Mode 2 (Mean/SD/N)
                return ParseDataStandardMode(allowTextX, xSubCols, ySubCols);
            }
        }

        /// <summary>
        /// Column Chart: Each Y series becomes one bar, rows are replicates
        /// </summary>
        private List<ChartDataSeries> ParseDataColumnChart()
        {
            var seriesList = new List<ChartDataSeries>();

            for (int y = 0; y < currentYColumns; y++)
            {
                var series = new ChartDataSeries();
                string yTitle = GetColumnTitle($"Y{y}");
                series.Name = yTitle;

                // Collect all values for this Y column (replicates)
                var yVals = new List<double>();
                for (int row = 0; row < dataTable.Rows.Count; row++)
                {
                    string cellVal = dataTable.Rows[row][$"Y{y}_0"]?.ToString()?.Trim() ?? "";
                    if (double.TryParse(cellVal, out double val))
                        yVals.Add(val);
                }

                if (yVals.Count > 0)
                {
                    double mean = yVals.Average();
                    double sd = CalculateSD(yVals);
                    double sem = yVals.Count > 1 ? sd / Math.Sqrt(yVals.Count) : 0;

                    series.XLabels.Add(yTitle);  // Use Y series name as X label
                    series.XValues.Add(y);
                    series.YValues.Add(mean);
                    series.YErrors.Add(sd);
                    series.SEMValues.Add(sem);
                    series.NValues.Add(yVals.Count);
                    series.RawReplicates.Add(yVals);
                }

                seriesList.Add(series);
            }

            return seriesList;
        }

        /// <summary>
        /// Single Value Mode: Each row with same X value is treated as a replicate measurement.
        /// </summary>
        private List<ChartDataSeries> ParseDataSingleValueMode(bool allowTextX, int xSubCols)
        {
            var seriesList = new List<ChartDataSeries>();

            // Group data by X value for each Y series
            var groupedData = new Dictionary<int, Dictionary<string, List<double>>>();

            for (int y = 0; y < currentYColumns; y++)
                groupedData[y] = new Dictionary<string, List<double>>();

            // Collect all data points grouped by X
            for (int row = 0; row < dataTable.Rows.Count; row++)
            {
                string firstXVal = dataTable.Rows[row]["X_0"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(firstXVal)) continue;

                // Calculate X value (average if multiple X columns)
                var xVals = new List<double>();
                for (int x = 0; x < xSubCols; x++)
                {
                    string val = dataTable.Rows[row][$"X_{x}"]?.ToString() ?? "";
                    if (double.TryParse(val, out double d)) xVals.Add(d);
                }

                string xLabel;

                if (xVals.Count > 0)
                {
                    xLabel = firstXVal;
                }
                else if (allowTextX)
                {
                    xLabel = firstXVal;
                }
                else
                {
                    continue;
                }

                // Collect Y values for each series
                for (int y = 0; y < currentYColumns; y++)
                {
                    string yStr = dataTable.Rows[row][$"Y{y}_0"]?.ToString() ?? "";
                    if (double.TryParse(yStr, out double yVal))
                    {
                        if (!groupedData[y].ContainsKey(xLabel))
                            groupedData[y][xLabel] = new List<double>();
                        groupedData[y][xLabel].Add(yVal);
                    }
                }
            }

            // Build series from grouped data
            for (int y = 0; y < currentYColumns; y++)
            {
                var series = new ChartDataSeries
                {
                    Name = GetColumnTitle($"Y{y}"),
                    XValues = new List<double>(),
                    XLabels = new List<string>(),
                    YValues = new List<double>(),
                    YErrors = new List<double>(),
                    SEMValues = new List<double>(),
                    NValues = new List<int>(),
                    RawReplicates = new List<List<double>>()
                };

                int xIndex = 0;
                foreach (var kvp in groupedData[y])
                {
                    string xLabel = kvp.Key;
                    List<double> yVals = kvp.Value;

                    if (yVals.Count == 0) continue;

                    if (double.TryParse(xLabel, out double xNum))
                        series.XValues.Add(xNum);
                    else
                        series.XValues.Add(xIndex);

                    series.XLabels.Add(xLabel);

                    double mean = yVals.Average();
                    double sd = yVals.Count > 1 ? CalculateSD(yVals) : 0;
                    double sem = yVals.Count > 1 ? sd / Math.Sqrt(yVals.Count) : 0;

                    series.YValues.Add(mean);
                    series.YErrors.Add(sd);
                    series.SEMValues.Add(sem);
                    series.NValues.Add(yVals.Count);
                    series.RawReplicates.Add(new List<double>(yVals));

                    xIndex++;
                }

                if (series.YValues.Count > 0)
                    seriesList.Add(series);
            }

            return seriesList;
        }

        /// <summary>
        /// Standard Mode: Mode 1 (replicates per row) or Mode 2 (Mean/SD/N)
        /// </summary>
        private List<ChartDataSeries> ParseDataStandardMode(bool allowTextX, int xSubCols, int ySubCols)
        {
            var seriesList = new List<ChartDataSeries>();
            var xValues = new List<double>();
            var xLabels = new List<string>();
            var validRowIndices = new List<int>();

            for (int row = 0; row < dataTable.Rows.Count; row++)
            {
                string firstXVal = dataTable.Rows[row]["X_0"]?.ToString()?.Trim() ?? "";

                // Check if this row has any Y data
                bool hasYData = false;
                for (int y = 0; y < currentYColumns && !hasYData; y++)
                {
                    if (currentDataMode == 2)
                    {
                        string meanStr = dataTable.Rows[row][$"Y{y}_Mean"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(meanStr) && double.TryParse(meanStr, out _))
                            hasYData = true;
                    }
                    else
                    {
                        for (int s = 0; s < ySubCols && !hasYData; s++)
                        {
                            string val = dataTable.Rows[row][$"Y{y}_{s}"]?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(val) && double.TryParse(val, out _))
                                hasYData = true;
                        }
                    }
                }

                if (!hasYData)
                {
                    if (validRowIndices.Count > 0) break;
                    continue;
                }

                validRowIndices.Add(row);

                // Handle X value
                if (!string.IsNullOrEmpty(firstXVal))
                {
                    var xVals = new List<double>();
                    for (int x = 0; x < xSubCols; x++)
                    {
                        string val = dataTable.Rows[row][$"X_{x}"]?.ToString() ?? "";
                        if (double.TryParse(val, out double d)) xVals.Add(d);
                    }

                    if (xVals.Count > 0)
                    {
                        xValues.Add(xVals.Average());
                        xLabels.Add(firstXVal);
                    }
                    else if (allowTextX)
                    {
                        xValues.Add(xLabels.Count);
                        xLabels.Add(firstXVal);
                    }
                    else
                    {
                        validRowIndices.RemoveAt(validRowIndices.Count - 1);
                        continue;
                    }
                }
                else if (allowTextX)
                {
                    xValues.Add(xLabels.Count);
                    xLabels.Add($"Group {xLabels.Count + 1}");
                }
                else
                {
                    validRowIndices.RemoveAt(validRowIndices.Count - 1);
                    continue;
                }
            }

            // Parse each Y series
            for (int y = 0; y < currentYColumns; y++)
            {
                var series = new ChartDataSeries
                {
                    Name = GetColumnTitle($"Y{y}")
                };

                for (int i = 0; i < validRowIndices.Count; i++)
                {
                    int row = validRowIndices[i];

                    if (currentDataMode == 2)
                    {
                        // Mean/SD/N mode
                        string meanStr = dataTable.Rows[row][$"Y{y}_Mean"]?.ToString() ?? "";
                        string sdStr = dataTable.Rows[row][$"Y{y}_SD"]?.ToString() ?? "";
                        string nStr = dataTable.Rows[row][$"Y{y}_N"]?.ToString() ?? "";

                        if (double.TryParse(meanStr, out double mean))
                        {
                            series.XValues.Add(xValues[i]);
                            series.XLabels.Add(xLabels[i]);
                            series.YValues.Add(mean);
                            double sd = double.TryParse(sdStr, out double s) ? s : 0;
                            int n = int.TryParse(nStr, out int nn) ? nn : 1;
                            series.YErrors.Add(sd);
                            series.SEMValues.Add(n > 1 ? sd / Math.Sqrt(n) : 0);
                            series.NValues.Add(n);
                            series.RawReplicates.Add(new List<double>());
                        }
                    }
                    else
                    {
                        // Replicates mode
                        var yVals = new List<double>();
                        for (int s = 0; s < ySubCols; s++)
                        {
                            string val = dataTable.Rows[row][$"Y{y}_{s}"]?.ToString()?.Trim() ?? "";
                            if (double.TryParse(val, out double d)) yVals.Add(d);
                        }

                        if (yVals.Count > 0)
                        {
                            series.XValues.Add(xValues[i]);
                            series.XLabels.Add(xLabels[i]);
                            double mean = yVals.Average();
                            double sd = yVals.Count > 1 ? CalculateSD(yVals) : 0;
                            double sem = yVals.Count > 1 ? sd / Math.Sqrt(yVals.Count) : 0;

                            series.YValues.Add(mean);
                            series.YErrors.Add(sd);
                            series.SEMValues.Add(sem);
                            series.NValues.Add(yVals.Count);
                            series.RawReplicates.Add(new List<double>(yVals));
                        }
                    }
                }

                if (series.YValues.Count > 0) seriesList.Add(series);
            }

            return seriesList;
        }

        private double CalculateSD(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            return Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1));
        }

        #endregion

        #region Chart Generation

        private void GenerateLineChart_Click(object sender, RoutedEventArgs e) => GenerateChart("Line");
        private void GenerateScatterPlot_Click(object sender, RoutedEventArgs e) => GenerateChart("Scatter");
        private void GenerateVolcanoPlot_Click(object sender, RoutedEventArgs e) => GenerateChart("Volcano");
        private void GenerateColumnChart_Click(object sender, RoutedEventArgs e) => GenerateBarChart("Column");
        private void GenerateMultiGroupChart_Click(object sender, RoutedEventArgs e) => GenerateBarChart("MultiGroup");

        private void GenerateChart(string chartType)
        {
            // Commit any pending edits
            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);
            DataGridMain.CommitEdit(DataGridEditingUnit.Cell, true);

            var data = ParseData(allowTextX: false);
            if (data.Count == 0 || data.All(s => s.YValues.Count == 0))
            {
                MessageBox.Show("No valid data to plot.\n\nPlease check:\n• Enter numeric values in X and Y columns\n• Apply Settings after changing column settings\n• Data should not be empty",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var errorDialog = new ErrorBarDialog();
            errorDialog.Owner = Window.GetWindow(this);
            if (errorDialog.ShowDialog() != true) return;

            string xTitle = GetColumnTitle("X");
            var chartWindow = new ChartWindow(data, chartType, errorDialog.SelectedErrorType, xTitle);
            chartWindow.Owner = Window.GetWindow(this);
            chartWindow.Show();
        }

        private void GenerateBarChart(string chartType)
        {
            // Commit any pending edits
            DataGridMain.CommitEdit(DataGridEditingUnit.Row, true);
            DataGridMain.CommitEdit(DataGridEditingUnit.Cell, true);

            var data = ParseData(allowTextX: true);
            if (data.Count == 0 || data.All(s => s.YValues.Count == 0))
            {
                MessageBox.Show("No valid data to plot.\n\nPlease check:\n• Enter values in X column (text or numbers)\n• Enter numeric values in Y columns\n• Apply Settings after changing column settings",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var errorDialog = new ErrorBarDialog();
            errorDialog.Owner = Window.GetWindow(this);
            if (errorDialog.ShowDialog() != true) return;

            string xTitle = GetColumnTitle("X");
            var barChartWindow = new BarChartWindow(data, chartType, errorDialog.SelectedErrorType, xTitle,
                errorDialog.SelectedDirection);
            barChartWindow.Owner = Window.GetWindow(this);
            barChartWindow.Show();
        }

        #endregion

        #region Sample Data

        // Load sample data
        public void LoadSampleData(string chartType)
        {
            ClearAll_Click(null, null);

            // Helper to safely set cell value
            void SetCell(int row, string col, string val)
            {
                if (dataTable.Columns.Contains(col) && row < dataTable.Rows.Count)
                    dataTable.Rows[row][col] = val;
            }

            if (chartType == "MultiGroup")
            {
                SetCell(0, "X_0", "Control");
                SetCell(0, "Y0_0", "10"); SetCell(0, "Y0_1", "12"); SetCell(0, "Y0_2", "11");
                SetCell(0, "Y1_0", "15"); SetCell(0, "Y1_1", "14"); SetCell(0, "Y1_2", "16");
                SetCell(0, "Y2_0", "12"); SetCell(0, "Y2_1", "11"); SetCell(0, "Y2_2", "13");

                SetCell(1, "X_0", "Treatment A");
                SetCell(1, "Y0_0", "25"); SetCell(1, "Y0_1", "27"); SetCell(1, "Y0_2", "24");
                SetCell(1, "Y1_0", "30"); SetCell(1, "Y1_1", "32"); SetCell(1, "Y1_2", "29");
                SetCell(1, "Y2_0", "28"); SetCell(1, "Y2_1", "26"); SetCell(1, "Y2_2", "27");

                SetCell(2, "X_0", "Treatment B");
                SetCell(2, "Y0_0", "18"); SetCell(2, "Y0_1", "20"); SetCell(2, "Y0_2", "17");
                SetCell(2, "Y1_0", "22"); SetCell(2, "Y1_1", "24"); SetCell(2, "Y1_2", "21");
                SetCell(2, "Y2_0", "20"); SetCell(2, "Y2_1", "19"); SetCell(2, "Y2_2", "21");
            }
            else if (chartType == "Column")
            {
                SetCell(0, "Y0_0", "10"); SetCell(0, "Y1_0", "15"); SetCell(0, "Y2_0", "12");
                SetCell(1, "Y0_0", "12"); SetCell(1, "Y1_0", "14"); SetCell(1, "Y2_0", "11");
                SetCell(2, "Y0_0", "11"); SetCell(2, "Y1_0", "16"); SetCell(2, "Y2_0", "13");
            }
            else
            {
                SetCell(0, "X_0", "1"); SetCell(0, "Y0_0", "2.1"); SetCell(0, "Y1_0", "1.8");
                SetCell(1, "X_0", "2"); SetCell(1, "Y0_0", "4.2"); SetCell(1, "Y1_0", "3.5");
                SetCell(2, "X_0", "3"); SetCell(2, "Y0_0", "5.8"); SetCell(2, "Y1_0", "5.2");
                SetCell(3, "X_0", "4"); SetCell(3, "Y0_0", "8.1"); SetCell(3, "Y1_0", "7.0");
                SetCell(4, "X_0", "5"); SetCell(4, "Y0_0", "10.5"); SetCell(4, "Y1_0", "9.2");
            }

            DataGridMain.Items.Refresh();
        }

        #endregion
    }

    public class ChartDataSeries
    {
        public string Name { get; set; } = "";
        public List<double> XValues { get; set; } = new List<double>();
        public List<string> XLabels { get; set; } = new List<string>();
        public List<double> YValues { get; set; } = new List<double>();
        public List<double> YErrors { get; set; } = new List<double>();
        public List<double> SEMValues { get; set; } = new List<double>();
        public List<int> NValues { get; set; } = new List<int>();
        public List<List<double>> RawReplicates { get; set; } = new List<List<double>>();
        public Color LineColor { get; set; } = Colors.Blue;
        public double LineThickness { get; set; } = 2;
        public int MarkerSize { get; set; } = 8;
        public string MarkerShape { get; set; } = "Circle";
    }
}