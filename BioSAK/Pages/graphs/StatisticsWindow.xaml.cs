using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace BioSAK
{
    public partial class StatisticsWindow : Window
    {
        private List<ChartDataSeries> dataSeries;
        private List<double> allXValues = new List<double>();
        private string selectedMethod = "";
        private string chartType = ""; // "Column" or "MultiGroup"
        
        // Setup controls (dynamic)
        private ComboBox? xValueCombo;
        private ComboBox? series1Combo;
        private ComboBox? series2Combo;
        private ComboBox? singleSeriesCombo;
        private ListBox? multiSeriesListBox;
        private ListBox? multiXListBox;
        
        // T-Test controls
        private RadioButton? tTestModeAll;
        private RadioButton? tTestModeSelect;
        private CheckBox? tTestPairedCheck;
        private CheckBox? tTestAutoVarianceCheck;
        private ComboBox? tTestTailCombo;
        
        // One-Way ANOVA controls
        private CheckBox? anovaPostHocCheck;
        private ListBox? anovaPostHocGroupsListBox;
        
        // Two-Way ANOVA controls
        private CheckBox? twoWayIncludeInteraction;
        private ComboBox? twoWaySsTypeCombo;
        
        // X value to label mapping
        private Dictionary<double, string> xValueToLabel = new Dictionary<double, string>();

        public StatisticsWindow(List<ChartDataSeries> data, string chartType = "")
        {
            InitializeComponent();
            dataSeries = data;
            this.chartType = chartType;
            
            // Collect all unique X values
            allXValues = dataSeries
                .SelectMany(s => s.XValues)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            
            // Build X value to label mapping
            foreach (var series in dataSeries)
            {
                for (int i = 0; i < series.XValues.Count && i < series.XLabels.Count; i++)
                {
                    double xVal = series.XValues[i];
                    if (!xValueToLabel.ContainsKey(xVal) && !string.IsNullOrEmpty(series.XLabels[i]))
                    {
                        xValueToLabel[xVal] = series.XLabels[i];
                    }
                }
            }

            // Update UI based on chart type
            UpdateMethodDescriptions();
            MethodRegression.IsChecked = true;
        }
        
        /// <summary>
        /// Get display label for X value (uses text label if available, otherwise numeric)
        /// </summary>
        private string GetXLabel(double x)
        {
            if (xValueToLabel.TryGetValue(x, out string? label) && !string.IsNullOrEmpty(label))
                return label;
            return x.ToString("G6");
        }

        private void UpdateMethodDescriptions()
        {
            if (chartType == "Column")
            {
                // For Column chart, each series is a bar with replicates
                MethodRegression.Visibility = Visibility.Collapsed; // Not applicable for Column
                
                // Update T-Test description
                var tTestDesc = MethodTTest.Content as StackPanel;
                if (tTestDesc != null && tTestDesc.Children.Count > 1)
                {
                    ((TextBlock)tTestDesc.Children[1]).Text = "Compare two bars (columns) using their replicate data";
                }
                
                // Update ANOVA Same X description
                var anovaSameXDesc = MethodOneWayAnovaSameX.Content as StackPanel;
                if (anovaSameXDesc != null)
                {
                    ((TextBlock)anovaSameXDesc.Children[0]).Text = "📐 One-Way ANOVA (Compare Bars)";
                    if (anovaSameXDesc.Children.Count > 1)
                        ((TextBlock)anovaSameXDesc.Children[1]).Text = "Compare multiple bars using their replicate data";
                }
                
                // Hide ANOVA Same Series (not applicable)
                MethodOneWayAnovaSameRow.Visibility = Visibility.Collapsed;
                MethodTwoWayAnova.Visibility = Visibility.Collapsed;
                
                // Update Descriptive
                var descDesc = MethodDescriptive.Content as StackPanel;
                if (descDesc != null && descDesc.Children.Count > 1)
                {
                    ((TextBlock)descDesc.Children[1]).Text = "Mean, SD, SEM, Min, Max, Median for each bar (column)";
                }
                
                // Set default selection to T-Test since Regression is hidden
                MethodTTest.IsChecked = true;
            }
            else if (chartType == "MultiGroup")
            {
                // For Multi Factors, update descriptions
                var anovaSameXDesc = MethodOneWayAnovaSameX.Content as StackPanel;
                if (anovaSameXDesc != null)
                {
                    ((TextBlock)anovaSameXDesc.Children[0]).Text = "📐 One-Way ANOVA (Compare Groups at Same X)";
                }
                
                var anovaSameSeriesDesc = MethodOneWayAnovaSameRow.Content as StackPanel;
                if (anovaSameSeriesDesc != null)
                {
                    ((TextBlock)anovaSameSeriesDesc.Children[0]).Text = "📐 One-Way ANOVA (Compare X Values in Same Group)";
                }
            }
        }

        #region Navigation

        private void NextToSetup_Click(object sender, RoutedEventArgs e)
        {
            // Determine which method is selected
            if (MethodRegression.IsChecked == true) selectedMethod = "Regression";
            else if (MethodTTest.IsChecked == true) selectedMethod = "TTest";
            else if (MethodOneWayAnovaSameX.IsChecked == true) selectedMethod = "ANOVA_SameX";
            else if (MethodOneWayAnovaSameRow.IsChecked == true) selectedMethod = "ANOVA_SameSeries";
            else if (MethodTwoWayAnova.IsChecked == true) selectedMethod = "TwoWayANOVA";
            else if (MethodDescriptive.IsChecked == true) selectedMethod = "Descriptive";
            else
            {
                MessageBox.Show("Please select a statistical method.", "Selection Required", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build setup UI based on method
            BuildSetupUI();
            
            MethodSelectionPanel.Visibility = Visibility.Collapsed;
            SetupPanel.Visibility = Visibility.Visible;
        }

        private void BackToMethodSelection_Click(object sender, RoutedEventArgs e)
        {
            SetupPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
            MethodSelectionPanel.Visibility = Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Build Setup UI

        private void BuildSetupUI()
        {
            SetupContent.Children.Clear();
            
            switch (selectedMethod)
            {
                case "Regression":
                    SetupTitle.Text = "Step 2: Linear Regression";
                    BuildRegressionSetup();
                    break;
                case "TTest":
                    SetupTitle.Text = "Step 2: T-Test Setup";
                    BuildTTestSetup();
                    break;
                case "ANOVA_SameX":
                    SetupTitle.Text = "Step 2: One-Way ANOVA (Compare Series at Same X)";
                    BuildAnovaSameXSetup();
                    break;
                case "ANOVA_SameSeries":
                    SetupTitle.Text = "Step 2: One-Way ANOVA (Compare X Values in Same Series)";
                    BuildAnovaSameSeriesSetup();
                    break;
                case "TwoWayANOVA":
                    SetupTitle.Text = "Step 2: Two-Way ANOVA Setup";
                    BuildTwoWayAnovaSetup();
                    break;
                case "Descriptive":
                    SetupTitle.Text = "Step 2: Descriptive Statistics";
                    BuildDescriptiveSetup();
                    break;
            }
        }

        private void BuildRegressionSetup()
        {
            var info = new TextBlock
            {
                Text = "Linear regression will be calculated for each series (X vs Y mean values).\n" +
                       "Results include: R², Pearson r, Slope, Intercept, and significance test for slope ≠ 0.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 20)
            };
            SetupContent.Children.Add(info);

            var label = new TextBlock { Text = "Series to analyze:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
            SetupContent.Children.Add(label);

            multiSeriesListBox = new ListBox { Height = 150, SelectionMode = SelectionMode.Multiple };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Margin = new Thickness(5) };
                multiSeriesListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiSeriesListBox);
        }

        private void BuildTTestSetup()
        {
            var info = new TextBlock
            {
                Text = chartType == "Column" 
                    ? "Compare bars using their replicate data with t-test."
                    : "Compare series at a specific X value using their replicate data.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            SetupContent.Children.Add(info);

            // === Comparison Mode ===
            var modeLabel = new TextBlock { Text = "Comparison Mode:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            SetupContent.Children.Add(modeLabel);

            tTestModeAll = new RadioButton 
            { 
                Content = "Compare all pairs", 
                GroupName = "TTestMode", 
                IsChecked = true,
                Margin = new Thickness(10, 0, 0, 5)
            };
            tTestModeAll.Checked += TTestMode_Changed;
            SetupContent.Children.Add(tTestModeAll);

            tTestModeSelect = new RadioButton 
            { 
                Content = "Select two groups to compare", 
                GroupName = "TTestMode",
                Margin = new Thickness(10, 0, 0, 10)
            };
            tTestModeSelect.Checked += TTestMode_Changed;
            SetupContent.Children.Add(tTestModeSelect);

            // === X Value Selection (for Multi Factors only) ===
            if (chartType != "Column")
            {
                AddLabelAndCombo("Select X value:", out xValueCombo, allXValues.Select(x => GetXLabel(x)).ToList());
            }

            // === Group Selection (for "all pairs" mode) ===
            var groupLabel = new TextBlock 
            { 
                Text = chartType == "Column" ? "Select Bars to compare:" : "Select Series to compare:", 
                FontWeight = FontWeights.SemiBold, 
                Margin = new Thickness(0, 10, 0, 8) 
            };
            SetupContent.Children.Add(groupLabel);

            multiSeriesListBox = new ListBox { Height = 120, SelectionMode = SelectionMode.Multiple };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Tag = s.Name, Margin = new Thickness(5) };
                multiSeriesListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiSeriesListBox);

            // === Two-group selection (initially hidden) ===
            var selectPanel = new StackPanel { Tag = "SelectPanel", Visibility = Visibility.Collapsed };
            
            var label1 = new TextBlock { Text = chartType == "Column" ? "Bar 1:" : "Series 1:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) };
            selectPanel.Children.Add(label1);
            series1Combo = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var s in dataSeries) series1Combo.Items.Add(s.Name);
            if (series1Combo.Items.Count > 0) series1Combo.SelectedIndex = 0;
            selectPanel.Children.Add(series1Combo);

            var label2 = new TextBlock { Text = chartType == "Column" ? "Bar 2:" : "Series 2:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) };
            selectPanel.Children.Add(label2);
            series2Combo = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var s in dataSeries) series2Combo.Items.Add(s.Name);
            if (series2Combo.Items.Count > 1) series2Combo.SelectedIndex = 1;
            selectPanel.Children.Add(series2Combo);

            SetupContent.Children.Add(selectPanel);

            // === T-Test Parameters ===
            var paramLabel = new TextBlock { Text = "T-Test Parameters:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) };
            SetupContent.Children.Add(paramLabel);

            var paramPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

            // Paired option
            tTestPairedCheck = new CheckBox 
            { 
                Content = "Paired t-test", 
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Use when samples are related (e.g., before/after measurements on same subjects)"
            };
            paramPanel.Children.Add(tTestPairedCheck);

            // Auto variance check
            tTestAutoVarianceCheck = new CheckBox 
            { 
                Content = "Auto-detect variance equality (F-test, α=0.05)", 
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "If checked: F-test p>0.05 → Student's t-test, else → Welch's t-test\nIf unchecked: always use Welch's t-test"
            };
            paramPanel.Children.Add(tTestAutoVarianceCheck);

            // Tail selection
            var tailPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            tailPanel.Children.Add(new TextBlock { Text = "Tail:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            tTestTailCombo = new ComboBox { Width = 150 };
            tTestTailCombo.Items.Add("Two-tailed");
            tTestTailCombo.Items.Add("One-tailed (Group 1>Group 2)");
            tTestTailCombo.Items.Add("One-tailed (Group 1<Group 2)");
            tTestTailCombo.SelectedIndex = 0;
            tailPanel.Children.Add(tTestTailCombo);
            paramPanel.Children.Add(tailPanel);

            SetupContent.Children.Add(paramPanel);

            // Update visibility based on mode
            UpdateTTestModeVisibility();
        }

        private void TTestMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTTestModeVisibility();
        }

        private void UpdateTTestModeVisibility()
        {
            if (multiSeriesListBox == null) return;

            bool isAllMode = tTestModeAll?.IsChecked == true;

            // Show/hide group list vs two-group selection
            multiSeriesListBox.Visibility = isAllMode ? Visibility.Visible : Visibility.Collapsed;
            
            // Find and update SelectPanel visibility
            foreach (var child in SetupContent.Children)
            {
                if (child is StackPanel sp && sp.Tag?.ToString() == "SelectPanel")
                {
                    sp.Visibility = isAllMode ? Visibility.Collapsed : Visibility.Visible;
                }
            }

            // Update label above multiSeriesListBox
            int listIndex = SetupContent.Children.IndexOf(multiSeriesListBox);
            if (listIndex > 0 && SetupContent.Children[listIndex - 1] is TextBlock labelAbove)
            {
                labelAbove.Visibility = isAllMode ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BuildAnovaSameXSetup()
        {
            var info = new TextBlock
            {
                Text = chartType == "Column" 
                    ? "Compare multiple bars using One-Way ANOVA.\n" +
                      "Variance equality is auto-detected via Levene's test (α=0.05).\n" +
                      "• Equal variances: Standard One-Way ANOVA\n" +
                      "• Unequal variances: Welch's ANOVA"
                    : "Compare multiple series at a specific X value using One-Way ANOVA.\n" +
                      "Variance equality is auto-detected via Levene's test.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            SetupContent.Children.Add(info);

            // X value selection (for Multi Factors only)
            if (chartType != "Column")
            {
                AddLabelAndCombo("Select X value:", out xValueCombo, allXValues.Select(x => GetXLabel(x)).ToList());
            }

            // Group selection
            var groupLabel = new TextBlock 
            { 
                Text = chartType == "Column" ? "Select Bars to compare (at least 3):" : "Select Series to compare (at least 3):", 
                FontWeight = FontWeights.SemiBold, 
                Margin = new Thickness(0, 10, 0, 8) 
            };
            SetupContent.Children.Add(groupLabel);

            multiSeriesListBox = new ListBox { Height = 120, SelectionMode = SelectionMode.Multiple };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Tag = s.Name, Margin = new Thickness(5) };
                multiSeriesListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiSeriesListBox);

            // Post-hoc test options
            var postHocLabel = new TextBlock { Text = "Post-hoc Tests:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) };
            SetupContent.Children.Add(postHocLabel);

            anovaPostHocCheck = new CheckBox 
            { 
                Content = "Perform post-hoc pairwise comparisons", 
                IsChecked = true,
                Margin = new Thickness(10, 0, 0, 5),
                ToolTip = "If equal variances: Tukey's HSD\nIf unequal variances: Games-Howell"
            };
            anovaPostHocCheck.Checked += AnovaPostHocCheck_Changed;
            anovaPostHocCheck.Unchecked += AnovaPostHocCheck_Changed;
            SetupContent.Children.Add(anovaPostHocCheck);

            var postHocInfo = new TextBlock
            {
                Text = "Post-hoc method is auto-selected based on Levene's test:\n" +
                       "• Levene's p > 0.05 → Tukey's HSD\n" +
                       "• Levene's p ≤ 0.05 → Games-Howell",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                FontSize = 11,
                Margin = new Thickness(25, 0, 0, 10)
            };
            SetupContent.Children.Add(postHocInfo);

            // Post-hoc group selection
            var postHocGroupLabel = new TextBlock 
            { 
                Text = "Select groups for post-hoc (or leave all checked for all pairs):", 
                Margin = new Thickness(10, 5, 0, 5),
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
            };
            SetupContent.Children.Add(postHocGroupLabel);

            anovaPostHocGroupsListBox = new ListBox { Height = 100, SelectionMode = SelectionMode.Multiple, Margin = new Thickness(10, 0, 0, 0) };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Tag = s.Name, Margin = new Thickness(5) };
                anovaPostHocGroupsListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(anovaPostHocGroupsListBox);
        }

        private void AnovaPostHocCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (anovaPostHocGroupsListBox != null)
            {
                anovaPostHocGroupsListBox.IsEnabled = anovaPostHocCheck?.IsChecked == true;
            }
        }

        private void BuildAnovaSameSeriesSetup()
        {
            var info = new TextBlock
            {
                Text = "Compare different X values within one series.\n" +
                       "Uses replicate data at each selected X point.\n" +
                       "Variance equality is auto-detected via Levene's test.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            SetupContent.Children.Add(info);

            // Series selection
            AddLabelAndCombo("Select Series:", out singleSeriesCombo, dataSeries.Select(s => s.Name).ToList());

            // X values selection (multiple)
            var label = new TextBlock { Text = "Select X values to compare (at least 3):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) };
            SetupContent.Children.Add(label);

            multiXListBox = new ListBox { Height = 120, SelectionMode = SelectionMode.Multiple };
            foreach (var x in allXValues)
            {
                var cb = new CheckBox { Content = GetXLabel(x), IsChecked = true, Tag = x, Margin = new Thickness(5) };
                multiXListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiXListBox);

            // Post-hoc test options
            var postHocLabel = new TextBlock { Text = "Post-hoc Tests:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) };
            SetupContent.Children.Add(postHocLabel);

            anovaPostHocCheck = new CheckBox 
            { 
                Content = "Perform post-hoc pairwise comparisons", 
                IsChecked = true,
                Margin = new Thickness(10, 0, 0, 5),
                ToolTip = "If equal variances: Tukey's HSD\nIf unequal variances: Games-Howell"
            };
            SetupContent.Children.Add(anovaPostHocCheck);
        }

        private void BuildTwoWayAnovaSetup()
        {
            var info = new TextBlock
            {
                Text = "Two-Way ANOVA analyzes:\n" +
                       "• Main effect of Series (Factor A)\n" +
                       "• Main effect of X value (Factor B)\n" +
                       "• Interaction between Series × X (optional)\n\n" +
                       "Requires multiple series and multiple X values with replicates.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            SetupContent.Children.Add(info);

            // Series selection
            var label1 = new TextBlock { Text = "Select Series (Factor A, at least 2):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            SetupContent.Children.Add(label1);

            multiSeriesListBox = new ListBox { Height = 100, SelectionMode = SelectionMode.Multiple };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Tag = s.Name, Margin = new Thickness(5) };
                multiSeriesListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiSeriesListBox);

            // X values selection
            var label2 = new TextBlock { Text = "Select X values (Factor B, at least 2):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) };
            SetupContent.Children.Add(label2);

            multiXListBox = new ListBox { Height = 100, SelectionMode = SelectionMode.Multiple };
            foreach (var x in allXValues)
            {
                var cb = new CheckBox { Content = GetXLabel(x), IsChecked = true, Tag = x, Margin = new Thickness(5) };
                multiXListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiXListBox);

            // ANOVA Options
            var optLabel = new TextBlock { Text = "ANOVA Options:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) };
            SetupContent.Children.Add(optLabel);

            var optPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

            // Include interaction
            twoWayIncludeInteraction = new CheckBox 
            { 
                Content = "Include interaction term (A × B)", 
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "If checked, tests whether the effect of Factor A depends on Factor B"
            };
            optPanel.Children.Add(twoWayIncludeInteraction);

            // Sum of Squares Type
            var ssPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            ssPanel.Children.Add(new TextBlock 
            { 
                Text = "Sum of Squares Type:", 
                VerticalAlignment = VerticalAlignment.Center, 
                Margin = new Thickness(0, 0, 10, 0) 
            });
            
            twoWaySsTypeCombo = new ComboBox { Width = 180 };
            twoWaySsTypeCombo.Items.Add("Type III (Marginal) - Default");
            twoWaySsTypeCombo.Items.Add("Type I (Sequential)");
            twoWaySsTypeCombo.Items.Add("Type II (Hierarchical)");
            twoWaySsTypeCombo.SelectedIndex = 0;
            twoWaySsTypeCombo.ToolTip = "Type I: Sequential - order matters\n" +
                                        "Type II: Hierarchical - each effect adjusted for others at same level\n" +
                                        "Type III: Marginal - each effect adjusted for all others (recommended for unbalanced designs)";
            ssPanel.Children.Add(twoWaySsTypeCombo);
            optPanel.Children.Add(ssPanel);

            SetupContent.Children.Add(optPanel);

            // Post-hoc explanation
            var postHocInfo = new TextBlock
            {
                Text = "Post-hoc Tests (auto-performed):\n" +
                       "• If interaction p < 0.05: Simple Main Effects analysis\n" +
                       "• If interaction p ≥ 0.05: Main Effects pairwise comparisons",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            };
            SetupContent.Children.Add(postHocInfo);
        }

        private void BuildDescriptiveSetup()
        {
            string infoText = chartType == "Column" 
                ? "Calculate descriptive statistics for each bar (column).\n" +
                  "Uses all replicate values for each bar.\n" +
                  "Includes: N, Mean, SD, SEM, Min, Max, Median, 95% CI."
                : "Calculate descriptive statistics for each series.\n" +
                  "Includes: N, Mean, SD, SEM, Min, Max, Median, 95% CI.";
            
            var info = new TextBlock
            {
                Text = infoText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 20)
            };
            SetupContent.Children.Add(info);

            string labelText = chartType == "Column" ? "Bars to analyze:" : "Series to analyze:";
            var label = new TextBlock { Text = labelText, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
            SetupContent.Children.Add(label);

            multiSeriesListBox = new ListBox { Height = 180, SelectionMode = SelectionMode.Multiple };
            foreach (var s in dataSeries)
            {
                var cb = new CheckBox { Content = s.Name, IsChecked = true, Margin = new Thickness(5) };
                multiSeriesListBox.Items.Add(cb);
            }
            SetupContent.Children.Add(multiSeriesListBox);
        }

        private void AddLabelAndCombo(string labelText, out ComboBox combo, List<string> items)
        {
            var label = new TextBlock { Text = labelText, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) };
            SetupContent.Children.Add(label);

            combo = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var item in items) combo.Items.Add(item);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            SetupContent.Children.Add(combo);
        }

        #endregion

        #region Run Analysis

        private void RunAnalysis_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DataTable? results = null;

                switch (selectedMethod)
                {
                    case "Regression":
                        results = RunRegression();
                        ResultsTitle.Text = "📈 Linear Regression Results";
                        break;
                    case "TTest":
                        results = RunTTest();
                        ResultsTitle.Text = "🔬 T-Test Results";
                        break;
                    case "ANOVA_SameX":
                        results = RunAnovaSameX();
                        ResultsTitle.Text = "📐 One-Way ANOVA Results (Same X)";
                        break;
                    case "ANOVA_SameSeries":
                        results = RunAnovaSameSeries();
                        ResultsTitle.Text = "📐 One-Way ANOVA Results (Same Series)";
                        break;
                    case "TwoWayANOVA":
                        results = RunTwoWayAnova();
                        ResultsTitle.Text = "📊 Two-Way ANOVA Results";
                        break;
                    case "Descriptive":
                        results = RunDescriptive();
                        ResultsTitle.Text = "📋 Descriptive Statistics";
                        break;
                }

                if (results != null && results.Rows.Count > 0)
                {
                    ResultsGrid.AutoGenerateColumns = true;
                    ResultsGrid.ItemsSource = results.DefaultView;
                    
                    SetupPanel.Visibility = Visibility.Collapsed;
                    ResultsPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analysis error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Statistical Methods

        private DataTable RunRegression()
        {
            var dt = new DataTable();
            dt.Columns.Add("Series", typeof(string));
            dt.Columns.Add("N", typeof(int));
            dt.Columns.Add("R²", typeof(string));
            dt.Columns.Add("Pearson r", typeof(string));
            dt.Columns.Add("Slope", typeof(string));
            dt.Columns.Add("Intercept", typeof(string));
            dt.Columns.Add("SE(slope)", typeof(string));
            dt.Columns.Add("t-value", typeof(string));
            dt.Columns.Add("p-value", typeof(string));
            dt.Columns.Add("Sig.", typeof(string));

            var selectedSeries = GetSelectedSeriesFromListBox(multiSeriesListBox);

            foreach (var series in selectedSeries)
            {
                int n = Math.Min(series.XValues.Count, series.YValues.Count);
                if (n < 2) continue;

                var x = series.XValues.Take(n).ToList();
                var y = series.YValues.Take(n).ToList();

                double xMean = x.Average(), yMean = y.Average();
                double ssxy = 0, ssxx = 0, ssyy = 0;

                for (int i = 0; i < n; i++)
                {
                    ssxy += (x[i] - xMean) * (y[i] - yMean);
                    ssxx += Math.Pow(x[i] - xMean, 2);
                    ssyy += Math.Pow(y[i] - yMean, 2);
                }

                if (ssxx < 1e-10) continue;

                double slope = ssxy / ssxx;
                double intercept = yMean - slope * xMean;
                double rSquared = ssyy > 0 ? Math.Pow(ssxy, 2) / (ssxx * ssyy) : 0;
                double pearsonR = ssyy > 0 ? ssxy / Math.Sqrt(ssxx * ssyy) : 0;

                double ssRes = 0;
                for (int i = 0; i < n; i++)
                    ssRes += Math.Pow(y[i] - (slope * x[i] + intercept), 2);

                double mse = n > 2 ? ssRes / (n - 2) : 0;
                double seSlope = Math.Sqrt(mse / ssxx);
                double tValue = seSlope > 1e-10 ? slope / seSlope : 0;
                double pValue = CalculateTwoTailedTPValue(Math.Abs(tValue), n - 2);

                dt.Rows.Add(series.Name, n, rSquared.ToString("F4"), pearsonR.ToString("F4"),
                    slope.ToString("F4"), intercept.ToString("F4"), seSlope.ToString("F4"),
                    tValue.ToString("F3"), FormatPValue(pValue), GetSig(pValue));
            }

            return dt;
        }

        private DataTable RunTTest()
        {
            var dt = new DataTable();
            
            bool isAllPairs = tTestModeAll?.IsChecked == true;
            bool isPaired = tTestPairedCheck?.IsChecked == true;
            bool autoVariance = tTestAutoVarianceCheck?.IsChecked == true;
            int tailType = tTestTailCombo?.SelectedIndex ?? 0; // 0=two-tailed, 1=one-tailed(>), 2=one-tailed(<)

            // Setup columns
            if (chartType != "Column")
            {
                dt.Columns.Add("X Value", typeof(string));
            }
            dt.Columns.Add("Group 1", typeof(string));
            dt.Columns.Add("N₁", typeof(int));
            dt.Columns.Add("Mean₁", typeof(string));
            dt.Columns.Add("SD₁", typeof(string));
            dt.Columns.Add("Group 2", typeof(string));
            dt.Columns.Add("N₂", typeof(int));
            dt.Columns.Add("Mean₂", typeof(string));
            dt.Columns.Add("SD₂", typeof(string));
            dt.Columns.Add("Test Type", typeof(string));
            dt.Columns.Add("t", typeof(string));
            dt.Columns.Add("df", typeof(string));
            dt.Columns.Add("p-value", typeof(string));
            dt.Columns.Add("Sig.", typeof(string));

            // Get X value for Multi Factors
            double xVal = 0;
            if (chartType != "Column")
            {
                if (xValueCombo?.SelectedIndex < 0)
                {
                    MessageBox.Show("Please select an X value.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return dt;
                }
                xVal = allXValues[xValueCombo!.SelectedIndex];
            }

            // Get groups to compare
            List<(string name, List<double> data)> groups = new List<(string, List<double>)>();

            if (isAllPairs)
            {
                var selectedSeries = GetSelectedSeriesFromListBox(multiSeriesListBox);
                if (selectedSeries.Count < 2)
                {
                    MessageBox.Show("Please select at least 2 groups.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return dt;
                }

                foreach (var s in selectedSeries)
                {
                    var data = chartType == "Column" ? GetAllReplicatesForSeries(s) : GetReplicatesAtX(s, xVal);
                    if (data.Count >= 2)
                    {
                        groups.Add((s.Name, data));
                    }
                }
            }
            else
            {
                // Select two mode
                if (series1Combo?.SelectedIndex < 0 || series2Combo?.SelectedIndex < 0)
                {
                    MessageBox.Show("Please select both groups.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return dt;
                }

                if (series1Combo!.SelectedIndex == series2Combo!.SelectedIndex)
                {
                    MessageBox.Show("Please select two different groups.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return dt;
                }

                var s1 = dataSeries[series1Combo.SelectedIndex];
                var s2 = dataSeries[series2Combo.SelectedIndex];

                var data1 = chartType == "Column" ? GetAllReplicatesForSeries(s1) : GetReplicatesAtX(s1, xVal);
                var data2 = chartType == "Column" ? GetAllReplicatesForSeries(s2) : GetReplicatesAtX(s2, xVal);

                groups.Add((s1.Name, data1));
                groups.Add((s2.Name, data2));
            }

            if (groups.Count < 2)
            {
                MessageBox.Show("Need at least 2 groups with sufficient data.", "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            // Perform t-tests for all pairs
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var g1 = groups[i];
                    var g2 = groups[j];

                    if (g1.data.Count < 2 || g2.data.Count < 2)
                        continue;

                    // Check for paired t-test requirements
                    if (isPaired && g1.data.Count != g2.data.Count)
                    {
                        // Skip this pair if paired is selected but counts don't match
                        var row = dt.NewRow();
                        if (chartType != "Column") row["X Value"] = GetXLabel(xVal);
                        row["Group 1"] = g1.name;
                        row["N₁"] = g1.data.Count;
                        row["Mean₁"] = g1.data.Average().ToString("F4");
                        row["SD₁"] = CalcSD(g1.data).ToString("F4");
                        row["Group 2"] = g2.name;
                        row["N₂"] = g2.data.Count;
                        row["Mean₂"] = g2.data.Average().ToString("F4");
                        row["SD₂"] = CalcSD(g2.data).ToString("F4");
                        row["Test Type"] = "N/A";
                        row["t"] = "-";
                        row["df"] = "-";
                        row["p-value"] = "N≠N (paired requires equal N)";
                        row["Sig."] = "-";
                        dt.Rows.Add(row);
                        continue;
                    }

                    string testType;
                    double t, df, p;

                    if (isPaired)
                    {
                        // Paired t-test
                        (t, df, p) = PairedTTest(g1.data, g2.data);
                        testType = "Paired";
                    }
                    else
                    {
                        // Independent t-test - check variance equality if auto
                        bool useWelch = true;
                        string varianceNote = "";

                        if (autoVariance)
                        {
                            var (fStat, fP) = FTestForVarianceEquality(g1.data, g2.data);
                            useWelch = fP <= 0.05; // Use Welch if variances are unequal
                            varianceNote = fP > 0.05 ? " (F-test p=" + fP.ToString("F3") + ", equal var)" 
                                                     : " (F-test p=" + fP.ToString("F3") + ", unequal var)";
                        }

                        if (useWelch)
                        {
                            (t, df, p) = WelchTTest(g1.data, g2.data);
                            testType = "Welch" + (autoVariance ? varianceNote : "");
                        }
                        else
                        {
                            (t, df, p) = StudentTTest(g1.data, g2.data);
                            testType = "Student" + varianceNote;
                        }
                    }

                    // Adjust p-value for one-tailed test
                    double pDisplay = p;
                    if (tailType == 1) // One-tailed (>)
                    {
                        pDisplay = t > 0 ? p / 2 : 1 - p / 2;
                        testType += ", 1-tail(>)";
                    }
                    else if (tailType == 2) // One-tailed (<)
                    {
                        pDisplay = t < 0 ? p / 2 : 1 - p / 2;
                        testType += ", 1-tail(<)";
                    }
                    else
                    {
                        testType += ", 2-tail";
                    }

                    var dataRow = dt.NewRow();
                    if (chartType != "Column") dataRow["X Value"] = GetXLabel(xVal);
                    dataRow["Group 1"] = g1.name;
                    dataRow["N₁"] = g1.data.Count;
                    dataRow["Mean₁"] = g1.data.Average().ToString("F4");
                    dataRow["SD₁"] = CalcSD(g1.data).ToString("F4");
                    dataRow["Group 2"] = g2.name;
                    dataRow["N₂"] = g2.data.Count;
                    dataRow["Mean₂"] = g2.data.Average().ToString("F4");
                    dataRow["SD₂"] = CalcSD(g2.data).ToString("F4");
                    dataRow["Test Type"] = testType;
                    dataRow["t"] = t.ToString("F4");
                    dataRow["df"] = df.ToString("F1");
                    dataRow["p-value"] = FormatPValue(pDisplay);
                    dataRow["Sig."] = GetSig(pDisplay);
                    dt.Rows.Add(dataRow);
                }
            }

            return dt;
        }

        /// <summary>
        /// Paired t-test for dependent samples
        /// </summary>
        private (double t, double df, double p) PairedTTest(List<double> g1, List<double> g2)
        {
            int n = Math.Min(g1.Count, g2.Count);
            if (n < 2) return (0, 0, 1);

            var diffs = new List<double>();
            for (int i = 0; i < n; i++)
                diffs.Add(g1[i] - g2[i]);

            double meanD = diffs.Average();
            double sdD = CalcSD(diffs);
            double seD = sdD / Math.Sqrt(n);

            if (seD < 1e-10) return (0, n - 1, 1);

            double t = meanD / seD;
            double df = n - 1;
            double p = CalculateTwoTailedTPValue(Math.Abs(t), df);

            return (t, df, p);
        }

        /// <summary>
        /// Student's t-test (assumes equal variances)
        /// </summary>
        private (double t, double df, double p) StudentTTest(List<double> g1, List<double> g2)
        {
            double m1 = g1.Average(), m2 = g2.Average();
            int n1 = g1.Count, n2 = g2.Count;
            double df = n1 + n2 - 2;

            // Pooled variance
            double ss1 = g1.Sum(v => Math.Pow(v - m1, 2));
            double ss2 = g2.Sum(v => Math.Pow(v - m2, 2));
            double pooledVar = (ss1 + ss2) / df;

            double se = Math.Sqrt(pooledVar * (1.0 / n1 + 1.0 / n2));
            if (se < 1e-10) return (0, df, 1);

            double t = (m1 - m2) / se;
            double p = CalculateTwoTailedTPValue(Math.Abs(t), df);

            return (t, df, p);
        }

        /// <summary>
        /// F-test for equality of variances
        /// </summary>
        private (double f, double p) FTestForVarianceEquality(List<double> g1, List<double> g2)
        {
            double v1 = CalcVar(g1);
            double v2 = CalcVar(g2);

            if (v2 < 1e-10 && v1 < 1e-10) return (1, 1); // Both have zero variance
            if (v2 < 1e-10) return (double.PositiveInfinity, 0);
            if (v1 < 1e-10) return (0, 0);

            // F is ratio of larger to smaller variance
            double f = v1 >= v2 ? v1 / v2 : v2 / v1;
            int df1 = v1 >= v2 ? g1.Count - 1 : g2.Count - 1;
            int df2 = v1 >= v2 ? g2.Count - 1 : g1.Count - 1;

            // Two-tailed F-test p-value
            double p = 2 * CalculateFPValue(f, df1, df2);
            if (p > 1) p = 1;

            return (f, p);
        }

        private DataTable RunAnovaSameX()
        {
            var dt = new DataTable();

            var selectedSeries = GetSelectedSeriesFromListBox(multiSeriesListBox);
            if (selectedSeries.Count < 2)
            {
                MessageBox.Show("Please select at least 2 groups.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            // Collect replicate data from each series
            var groups = new List<List<double>>();
            var groupNames = new List<string>();

            if (chartType == "Column")
            {
                foreach (var s in selectedSeries)
                {
                    var reps = GetAllReplicatesForSeries(s);
                    if (reps.Count > 0)
                    {
                        groups.Add(reps);
                        groupNames.Add(s.Name);
                    }
                }
            }
            else
            {
                if (xValueCombo?.SelectedIndex < 0)
                {
                    MessageBox.Show("Please select an X value.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return dt;
                }

                double xVal = allXValues[xValueCombo!.SelectedIndex];

                foreach (var s in selectedSeries)
                {
                    var reps = GetReplicatesAtX(s, xVal);
                    if (reps.Count > 0)
                    {
                        groups.Add(reps);
                        groupNames.Add(s.Name);
                    }
                }
            }

            if (groups.Count < 2)
            {
                MessageBox.Show("Need data from at least 2 groups.", "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            // Levene's test for homogeneity of variances
            var (leveneF, leveneP) = LevenesTest(groups);
            bool equalVariances = leveneP > 0.05 || double.IsNaN(leveneP);

            // Setup columns
            dt.Columns.Add("Source", typeof(string));
            dt.Columns.Add("SS", typeof(string));
            dt.Columns.Add("df", typeof(string));
            dt.Columns.Add("MS", typeof(string));
            dt.Columns.Add("F", typeof(string));
            dt.Columns.Add("p-value", typeof(string));
            dt.Columns.Add("Sig.", typeof(string));

            string anovaType;
            double f, p;
            int dfBetween, dfWithin;
            double ssBetween, ssWithin, ssTotal;

            if (equalVariances)
            {
                // Standard One-Way ANOVA
                anovaType = "Standard One-Way ANOVA";
                var result = CalculateOneWayAnova(groups);
                ssBetween = result.ssBetween;
                ssWithin = result.ssWithin;
                ssTotal = result.ssTotal;
                dfBetween = result.dfBetween;
                dfWithin = result.dfWithin;
                f = result.f;
                p = result.p;

                dt.Rows.Add($"ANOVA Type: {anovaType}", "", "", "", "", "", "");
                dt.Rows.Add($"Levene's Test: F={leveneF:F3}, p={FormatPValue(leveneP)} (Equal variances assumed)", "", "", "", "", "", "");
                dt.Rows.Add("", "", "", "", "", "", "");
                dt.Rows.Add("Between Groups", ssBetween.ToString("F4"), dfBetween.ToString(), 
                    (ssBetween / dfBetween).ToString("F4"), f.ToString("F4"), FormatPValue(p), GetSig(p));
                dt.Rows.Add("Within Groups", ssWithin.ToString("F4"), dfWithin.ToString(), 
                    (dfWithin > 0 ? ssWithin / dfWithin : 0).ToString("F4"), "-", "-", "-");
                dt.Rows.Add("Total", ssTotal.ToString("F4"), (dfBetween + dfWithin).ToString(), "-", "-", "-", "-");
            }
            else
            {
                // Welch's ANOVA
                anovaType = "Welch's ANOVA";
                var (welchF, welchDf1, welchDf2, welchP) = WelchsAnova(groups);
                f = welchF;
                p = welchP;
                dfBetween = (int)welchDf1;

                dt.Rows.Add($"ANOVA Type: {anovaType}", "", "", "", "", "", "");
                dt.Rows.Add($"Levene's Test: F={leveneF:F3}, p={FormatPValue(leveneP)} (Unequal variances)", "", "", "", "", "", "");
                dt.Rows.Add("", "", "", "", "", "", "");
                dt.Rows.Add("Welch's F", "-", $"{welchDf1:F1}, {welchDf2:F1}", "-", f.ToString("F4"), FormatPValue(p), GetSig(p));
            }

            // Group Summary
            dt.Rows.Add("", "", "", "", "", "", "");
            dt.Rows.Add("Group Summary:", "", "", "", "", "", "");
            for (int i = 0; i < groups.Count; i++)
            {
                dt.Rows.Add($"  {groupNames[i]}", $"N={groups[i].Count}", "", 
                    $"Mean={groups[i].Average():F4}", $"SD={CalcSD(groups[i]):F4}", "", "");
            }

            // Post-hoc tests if requested
            if (anovaPostHocCheck?.IsChecked == true)
            {
                dt.Rows.Add("", "", "", "", "", "", "");
                
                // Get selected groups for post-hoc
                var postHocGroups = GetSelectedSeriesNamesFromListBox(anovaPostHocGroupsListBox);
                if (postHocGroups.Count == 0) postHocGroups = groupNames;

                // Warning if ANOVA not significant
                if (p >= 0.05)
                {
                    dt.Rows.Add("Note: ANOVA p ≥ 0.05; post-hoc results should be interpreted with caution.", "", "", "", "", "", "");
                    dt.Rows.Add("", "", "", "", "", "", "");
                }

                if (equalVariances)
                {
                    // Tukey's HSD
                    dt.Rows.Add("Post-hoc: Tukey's HSD (equal variances)", "", "", "", "", "", "");
                    dt.Rows.Add("Comparison", "Mean Diff", "", "SE", "q", "p-value", "Sig.");
                    var tukeyResults = TukeyHSD(groups, groupNames, postHocGroups);
                    if (tukeyResults.Count == 0)
                    {
                        dt.Rows.Add("  No comparisons (select at least 2 groups)", "", "", "", "", "", "");
                    }
                    else
                    {
                        foreach (var row in tukeyResults)
                        {
                            dt.Rows.Add($"  {row.comparison}", row.meanDiff, "", row.se, row.q, FormatPValue(row.p), GetSig(row.p));
                        }
                    }
                }
                else
                {
                    // Games-Howell
                    dt.Rows.Add("Post-hoc: Games-Howell (unequal variances)", "", "", "", "", "", "");
                    dt.Rows.Add("Comparison", "Mean Diff", "df", "SE", "t", "p-value", "Sig.");
                    var ghResults = GamesHowell(groups, groupNames, postHocGroups);
                    if (ghResults.Count == 0)
                    {
                        dt.Rows.Add("  No comparisons (select at least 2 groups)", "", "", "", "", "", "");
                    }
                    else
                    {
                        foreach (var row in ghResults)
                        {
                            dt.Rows.Add($"  {row.comparison}", row.meanDiff, row.df, row.se, row.t, FormatPValue(row.p), GetSig(row.p));
                        }
                    }
                }
            }

            return dt;
        }

        /// <summary>
        /// Levene's test for homogeneity of variances
        /// </summary>
        private (double f, double p) LevenesTest(List<List<double>> groups)
        {
            if (groups.Count < 2) return (double.NaN, double.NaN);

            // Calculate absolute deviations from group medians
            var deviations = new List<List<double>>();
            foreach (var g in groups)
            {
                if (g.Count == 0) return (double.NaN, double.NaN);
                double median = CalcMedian(g);
                deviations.Add(g.Select(x => Math.Abs(x - median)).ToList());
            }

            // Perform ANOVA on deviations
            var result = CalculateOneWayAnova(deviations);
            return (result.f, result.p);
        }

        /// <summary>
        /// Welch's ANOVA for unequal variances
        /// </summary>
        private (double f, double df1, double df2, double p) WelchsAnova(List<List<double>> groups)
        {
            int k = groups.Count;
            var weights = new double[k];
            var means = new double[k];
            var vars = new double[k];
            var ns = new int[k];

            for (int i = 0; i < k; i++)
            {
                ns[i] = groups[i].Count;
                means[i] = groups[i].Average();
                vars[i] = CalcVar(groups[i]);
                weights[i] = vars[i] > 1e-10 ? ns[i] / vars[i] : ns[i] * 1e10;
            }

            double sumWeights = weights.Sum();
            double grandMean = 0;
            for (int i = 0; i < k; i++)
                grandMean += weights[i] * means[i];
            grandMean /= sumWeights;

            // Welch's F
            double numerator = 0;
            for (int i = 0; i < k; i++)
                numerator += weights[i] * Math.Pow(means[i] - grandMean, 2);
            numerator /= (k - 1);

            double lambda = 0;
            for (int i = 0; i < k; i++)
            {
                double wi = weights[i] / sumWeights;
                lambda += Math.Pow(1 - wi, 2) / (ns[i] - 1);
            }
            lambda *= 3.0 / (k * k - 1);

            double denominator = 1 + 2 * (k - 2) * lambda / (k * k - 1);
            double f = numerator / denominator;

            // Degrees of freedom
            double df1 = k - 1;
            double df2 = 1 / lambda;

            double p = CalculateFPValue(f, (int)Math.Round(df1), (int)Math.Round(df2));
            return (f, df1, df2, p);
        }

        /// <summary>
        /// Tukey's HSD post-hoc test
        /// </summary>
        private List<(string comparison, string meanDiff, string se, string q, double p)> TukeyHSD(
            List<List<double>> groups, List<string> names, List<string> selectedNames)
        {
            var results = new List<(string, string, string, string, double)>();
            
            if (groups.Count < 2) return results;
            
            // Calculate MSE
            var allData = groups.SelectMany(g => g).ToList();
            int totalN = allData.Count;
            int k = groups.Count;
            
            if (totalN <= k) return results;
            
            double mse = groups.Sum(g => g.Sum(v => Math.Pow(v - g.Average(), 2))) / (totalN - k);

            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    // Include pair if either group is in selected list, or if all groups selected
                    bool includeI = selectedNames.Count == 0 || selectedNames.Contains(names[i]);
                    bool includeJ = selectedNames.Count == 0 || selectedNames.Contains(names[j]);
                    
                    if (!includeI && !includeJ)
                        continue;

                    double meanDiff = groups[i].Average() - groups[j].Average();
                    double se = Math.Sqrt(mse * (1.0 / groups[i].Count + 1.0 / groups[j].Count) / 2);
                    double q = se > 1e-10 ? Math.Abs(meanDiff) / se : 0;
                    
                    // Approximate p-value using studentized range distribution
                    double pVal = CalculateTukeyP(q, k, totalN - k);

                    results.Add(($"{names[i]} vs {names[j]}", meanDiff.ToString("F4"), se.ToString("F4"), q.ToString("F4"), pVal));
                }
            }
            return results;
        }

        /// <summary>
        /// Games-Howell post-hoc test for unequal variances
        /// </summary>
        private List<(string comparison, string meanDiff, string df, string se, string t, double p)> GamesHowell(
            List<List<double>> groups, List<string> names, List<string> selectedNames)
        {
            var results = new List<(string, string, string, string, string, double)>();

            if (groups.Count < 2) return results;

            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    // Include pair if either group is in selected list, or if all groups selected
                    bool includeI = selectedNames.Count == 0 || selectedNames.Contains(names[i]);
                    bool includeJ = selectedNames.Count == 0 || selectedNames.Contains(names[j]);
                    
                    if (!includeI && !includeJ)
                        continue;

                    var g1 = groups[i];
                    var g2 = groups[j];
                    
                    if (g1.Count < 2 || g2.Count < 2) continue;
                    
                    double m1 = g1.Average(), m2 = g2.Average();
                    double v1 = CalcVar(g1), v2 = CalcVar(g2);
                    int n1 = g1.Count, n2 = g2.Count;

                    double se = Math.Sqrt(v1 / n1 + v2 / n2);
                    double meanDiff = m1 - m2;
                    double t = se > 1e-10 ? Math.Abs(meanDiff) / se : 0;

                    // Welch-Satterthwaite df
                    double num = Math.Pow(v1 / n1 + v2 / n2, 2);
                    double den = Math.Pow(v1 / n1, 2) / (n1 - 1) + Math.Pow(v2 / n2, 2) / (n2 - 1);
                    double df = den > 0 ? num / den : n1 + n2 - 2;

                    double pVal = CalculateTwoTailedTPValue(t, df);

                    results.Add(($"{names[i]} vs {names[j]}", meanDiff.ToString("F4"), df.ToString("F1"), se.ToString("F4"), t.ToString("F4"), pVal));
                }
            }
            return results;
        }

        /// <summary>
        /// Approximate Tukey p-value using studentized range distribution
        /// </summary>
        private double CalculateTukeyP(double q, int k, int df)
        {
            // Simplified approximation - convert to F and use F distribution
            // This is an approximation; exact requires studentized range tables
            double fApprox = q * q / 2;
            return CalculateFPValue(fApprox, k - 1, df);
        }

        private List<string> GetSelectedSeriesNamesFromListBox(ListBox? lb)
        {
            var result = new List<string>();
            if (lb == null) return result;

            foreach (var item in lb.Items)
            {
                if (item is CheckBox cb && cb.IsChecked == true)
                    result.Add(cb.Content?.ToString() ?? "");
            }
            return result;
        }

        private DataTable RunAnovaSameSeries()
        {
            var dt = new DataTable();

            if (singleSeriesCombo?.SelectedIndex < 0)
            {
                MessageBox.Show("Please select a series.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            var selectedXValues = GetSelectedXValues();
            if (selectedXValues.Count < 2)
            {
                MessageBox.Show("Please select at least 2 X values.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            var series = dataSeries[singleSeriesCombo!.SelectedIndex];

            // Collect replicate data at each X
            var groups = new List<List<double>>();
            var groupLabels = new List<string>();

            foreach (var x in selectedXValues)
            {
                var reps = GetReplicatesAtX(series, x);
                if (reps.Count > 0)
                {
                    groups.Add(reps);
                    groupLabels.Add(GetXLabel(x));
                }
            }

            if (groups.Count < 2)
            {
                MessageBox.Show("Need data at least 2 X values.", "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            // Levene's test for homogeneity of variances
            var (leveneF, leveneP) = LevenesTest(groups);
            bool equalVariances = leveneP > 0.05 || double.IsNaN(leveneP);

            // Setup columns
            dt.Columns.Add("Source", typeof(string));
            dt.Columns.Add("SS", typeof(string));
            dt.Columns.Add("df", typeof(string));
            dt.Columns.Add("MS", typeof(string));
            dt.Columns.Add("F", typeof(string));
            dt.Columns.Add("p-value", typeof(string));
            dt.Columns.Add("Sig.", typeof(string));

            string anovaType;
            double f, p;

            if (equalVariances)
            {
                // Standard One-Way ANOVA
                anovaType = "Standard One-Way ANOVA";
                var result = CalculateOneWayAnova(groups);

                dt.Rows.Add($"ANOVA Type: {anovaType}", "", "", "", "", "", "");
                dt.Rows.Add($"Levene's Test: F={leveneF:F3}, p={FormatPValue(leveneP)} (Equal variances assumed)", "", "", "", "", "", "");
                dt.Rows.Add("", "", "", "", "", "", "");
                dt.Rows.Add("Between X Values", result.ssBetween.ToString("F4"), result.dfBetween.ToString(), 
                    (result.ssBetween / result.dfBetween).ToString("F4"), result.f.ToString("F4"), FormatPValue(result.p), GetSig(result.p));
                dt.Rows.Add("Within X Values", result.ssWithin.ToString("F4"), result.dfWithin.ToString(), 
                    (result.dfWithin > 0 ? result.ssWithin / result.dfWithin : 0).ToString("F4"), "-", "-", "-");
                dt.Rows.Add("Total", result.ssTotal.ToString("F4"), (result.dfBetween + result.dfWithin).ToString(), "-", "-", "-", "-");
                
                f = result.f;
                p = result.p;
            }
            else
            {
                // Welch's ANOVA
                anovaType = "Welch's ANOVA";
                var (welchF, welchDf1, welchDf2, welchP) = WelchsAnova(groups);
                f = welchF;
                p = welchP;

                dt.Rows.Add($"ANOVA Type: {anovaType}", "", "", "", "", "", "");
                dt.Rows.Add($"Levene's Test: F={leveneF:F3}, p={FormatPValue(leveneP)} (Unequal variances)", "", "", "", "", "", "");
                dt.Rows.Add("", "", "", "", "", "", "");
                dt.Rows.Add("Welch's F", "-", $"{welchDf1:F1}, {welchDf2:F1}", "-", f.ToString("F4"), FormatPValue(p), GetSig(p));
            }

            // Group Summary
            dt.Rows.Add("", "", "", "", "", "", "");
            dt.Rows.Add("Group Summary:", "", "", "", "", "", "");
            for (int i = 0; i < groups.Count; i++)
            {
                dt.Rows.Add($"  {groupLabels[i]}", $"N={groups[i].Count}", "",
                    $"Mean={groups[i].Average():F4}", $"SD={CalcSD(groups[i]):F4}", "", "");
            }

            // Post-hoc tests if requested
            if (anovaPostHocCheck?.IsChecked == true)
            {
                dt.Rows.Add("", "", "", "", "", "", "");

                // Warning if ANOVA not significant
                if (p >= 0.05)
                {
                    dt.Rows.Add("Note: ANOVA p ≥ 0.05; post-hoc results should be interpreted with caution.", "", "", "", "", "", "");
                    dt.Rows.Add("", "", "", "", "", "", "");
                }

                if (equalVariances)
                {
                    dt.Rows.Add("Post-hoc: Tukey's HSD (equal variances)", "", "", "", "", "", "");
                    dt.Rows.Add("Comparison", "Mean Diff", "", "SE", "q", "p-value", "Sig.");
                    var tukeyResults = TukeyHSD(groups, groupLabels, groupLabels);
                    foreach (var row in tukeyResults)
                    {
                        dt.Rows.Add($"  {row.comparison}", row.meanDiff, "", row.se, row.q, FormatPValue(row.p), GetSig(row.p));
                    }
                }
                else
                {
                    dt.Rows.Add("Post-hoc: Games-Howell (unequal variances)", "", "", "", "", "", "");
                    dt.Rows.Add("Comparison", "Mean Diff", "df", "SE", "t", "p-value", "Sig.");
                    var ghResults = GamesHowell(groups, groupLabels, groupLabels);
                    foreach (var row in ghResults)
                    {
                        dt.Rows.Add($"  {row.comparison}", row.meanDiff, row.df, row.se, row.t, FormatPValue(row.p), GetSig(row.p));
                    }
                }
            }

            return dt;
        }

        private DataTable RunTwoWayAnova()
        {
            var dt = new DataTable();
            dt.Columns.Add("Source", typeof(string));
            dt.Columns.Add("SS", typeof(string));
            dt.Columns.Add("df", typeof(string));
            dt.Columns.Add("MS", typeof(string));
            dt.Columns.Add("F", typeof(string));
            dt.Columns.Add("p-value", typeof(string));
            dt.Columns.Add("Sig.", typeof(string));

            var selectedSeries = GetSelectedSeriesFromListBox(multiSeriesListBox);
            var selectedXValues = GetSelectedXValues();

            if (selectedSeries.Count < 2 || selectedXValues.Count < 2)
            {
                MessageBox.Show("Please select at least 2 series and 2 X values.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return dt;
            }

            bool includeInteraction = twoWayIncludeInteraction?.IsChecked ?? true;
            int ssType = (twoWaySsTypeCombo?.SelectedIndex ?? 0) switch
            {
                1 => 1, // Type I
                2 => 2, // Type II
                _ => 3  // Type III (default)
            };

            // Build data matrix: data[series][xValue] = list of replicates
            var dataMatrix = new Dictionary<string, Dictionary<double, List<double>>>();
            
            foreach (var s in selectedSeries)
            {
                dataMatrix[s.Name] = new Dictionary<double, List<double>>();
                foreach (var x in selectedXValues)
                {
                    var reps = GetReplicatesAtX(s, x);
                    if (reps.Count > 0)
                        dataMatrix[s.Name][x] = reps;
                }
            }

            // Check for balanced design
            int totalN = 0;
            var allData = new List<double>();
            var cellNs = new Dictionary<(string, double), int>();

            foreach (var s in selectedSeries)
            {
                foreach (var x in selectedXValues)
                {
                    if (!dataMatrix[s.Name].ContainsKey(x) || dataMatrix[s.Name][x].Count == 0)
                    {
                        MessageBox.Show($"Missing data for {s.Name} at {GetXLabel(x)}.\nTwo-way ANOVA requires data at all combinations.", 
                            "Incomplete Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return dt;
                    }
                    cellNs[(s.Name, x)] = dataMatrix[s.Name][x].Count;
                    totalN += dataMatrix[s.Name][x].Count;
                    allData.AddRange(dataMatrix[s.Name][x]);
                }
            }

            // Check if design is balanced
            bool isBalanced = cellNs.Values.Distinct().Count() == 1;

            int a = selectedSeries.Count;  // Factor A levels (Series)
            int b = selectedXValues.Count; // Factor B levels (X values)
            double grandMean = allData.Average();

            // Calculate means
            var seriesMeans = new Dictionary<string, double>();
            var xMeans = new Dictionary<double, double>();
            var cellMeans = new Dictionary<(string, double), double>();

            foreach (var s in selectedSeries)
            {
                var sData = new List<double>();
                foreach (var x in selectedXValues)
                    sData.AddRange(dataMatrix[s.Name][x]);
                seriesMeans[s.Name] = sData.Average();
            }

            foreach (var x in selectedXValues)
            {
                var xData = new List<double>();
                foreach (var s in selectedSeries)
                    xData.AddRange(dataMatrix[s.Name][x]);
                xMeans[x] = xData.Average();
            }

            foreach (var s in selectedSeries)
                foreach (var x in selectedXValues)
                    cellMeans[(s.Name, x)] = dataMatrix[s.Name][x].Average();

            // Calculate SS based on Type
            double ssA = 0, ssB = 0, ssAB = 0, ssError = 0;

            // Error SS is always the same
            foreach (var s in selectedSeries)
            {
                foreach (var x in selectedXValues)
                {
                    foreach (var val in dataMatrix[s.Name][x])
                        ssError += Math.Pow(val - cellMeans[(s.Name, x)], 2);
                }
            }

            double ssTotal = allData.Sum(v => Math.Pow(v - grandMean, 2));

            // Type III (Marginal) - Each effect adjusted for all others
            if (ssType == 3 || !isBalanced)
            {
                // For unbalanced designs, always use Type III
                foreach (var s in selectedSeries)
                {
                    int nPerSeries = selectedXValues.Sum(x => cellNs[(s.Name, x)]);
                    ssA += nPerSeries * Math.Pow(seriesMeans[s.Name] - grandMean, 2);
                }
                
                foreach (var x in selectedXValues)
                {
                    int nPerX = selectedSeries.Sum(s => cellNs[(s.Name, x)]);
                    ssB += nPerX * Math.Pow(xMeans[x] - grandMean, 2);
                }

                if (includeInteraction)
                {
                    foreach (var s in selectedSeries)
                    {
                        foreach (var x in selectedXValues)
                        {
                            int n = cellNs[(s.Name, x)];
                            double cellDev = cellMeans[(s.Name, x)] - seriesMeans[s.Name] - xMeans[x] + grandMean;
                            ssAB += n * Math.Pow(cellDev, 2);
                        }
                    }
                }
            }
            else if (ssType == 1)
            {
                // Type I (Sequential) - A first, then B|A, then AB|A,B
                foreach (var s in selectedSeries)
                {
                    int nPerSeries = selectedXValues.Sum(x => cellNs[(s.Name, x)]);
                    ssA += nPerSeries * Math.Pow(seriesMeans[s.Name] - grandMean, 2);
                }
                
                // B adjusted for A
                ssB = ssTotal - ssA - ssError;
                if (includeInteraction)
                {
                    foreach (var s in selectedSeries)
                    {
                        foreach (var x in selectedXValues)
                        {
                            int n = cellNs[(s.Name, x)];
                            double cellDev = cellMeans[(s.Name, x)] - seriesMeans[s.Name] - xMeans[x] + grandMean;
                            ssAB += n * Math.Pow(cellDev, 2);
                        }
                    }
                    ssB = ssTotal - ssA - ssAB - ssError;
                }
            }
            else // Type II
            {
                // Type II (Hierarchical) - Each main effect adjusted for other main effect only
                foreach (var s in selectedSeries)
                {
                    int nPerSeries = selectedXValues.Sum(x => cellNs[(s.Name, x)]);
                    ssA += nPerSeries * Math.Pow(seriesMeans[s.Name] - grandMean, 2);
                }
                
                foreach (var x in selectedXValues)
                {
                    int nPerX = selectedSeries.Sum(s => cellNs[(s.Name, x)]);
                    ssB += nPerX * Math.Pow(xMeans[x] - grandMean, 2);
                }

                if (includeInteraction)
                {
                    ssAB = ssTotal - ssA - ssB - ssError;
                    if (ssAB < 0) ssAB = 0;
                }
            }

            int dfA = a - 1;
            int dfB = b - 1;
            int dfAB = includeInteraction ? dfA * dfB : 0;
            int dfError = totalN - (includeInteraction ? a * b : a + b - 1);
            if (dfError < 1) dfError = 1;
            int dfTotal = totalN - 1;

            double msA = dfA > 0 ? ssA / dfA : 0;
            double msB = dfB > 0 ? ssB / dfB : 0;
            double msAB = dfAB > 0 ? ssAB / dfAB : 0;
            double msError = dfError > 0 ? ssError / dfError : 0;

            double fA = msError > 1e-10 ? msA / msError : 0;
            double fB = msError > 1e-10 ? msB / msError : 0;
            double fAB = msError > 1e-10 ? msAB / msError : 0;

            double pA = CalculateFPValue(fA, dfA, dfError);
            double pB = CalculateFPValue(fB, dfB, dfError);
            double pAB = includeInteraction ? CalculateFPValue(fAB, dfAB, dfError) : 1;

            // Header info
            string ssTypeStr = ssType switch { 1 => "Type I (Sequential)", 2 => "Type II (Hierarchical)", _ => "Type III (Marginal)" };
            dt.Rows.Add($"Sum of Squares: {ssTypeStr}", "", "", "", "", "", "");
            dt.Rows.Add($"Design: {(isBalanced ? "Balanced" : "Unbalanced")}", "", "", "", "", "", "");
            dt.Rows.Add("", "", "", "", "", "", "");

            // ANOVA table
            dt.Rows.Add("Series (Factor A)", ssA.ToString("F4"), dfA.ToString(), msA.ToString("F4"), fA.ToString("F4"), FormatPValue(pA), GetSig(pA));
            dt.Rows.Add("X Value (Factor B)", ssB.ToString("F4"), dfB.ToString(), msB.ToString("F4"), fB.ToString("F4"), FormatPValue(pB), GetSig(pB));
            
            if (includeInteraction)
            {
                dt.Rows.Add("Interaction (A×B)", ssAB.ToString("F4"), dfAB.ToString(), msAB.ToString("F4"), fAB.ToString("F4"), FormatPValue(pAB), GetSig(pAB));
            }
            
            dt.Rows.Add("Error", ssError.ToString("F4"), dfError.ToString(), msError.ToString("F4"), "-", "-", "-");
            dt.Rows.Add("Total", ssTotal.ToString("F4"), dfTotal.ToString(), "-", "-", "-", "-");

            // Post-hoc analysis
            dt.Rows.Add("", "", "", "", "", "", "");
            
            if (includeInteraction && pAB < 0.05)
            {
                // Simple Main Effects
                dt.Rows.Add("Post-hoc: Simple Main Effects (Interaction p < 0.05)", "", "", "", "", "", "");
                dt.Rows.Add("", "", "", "", "", "", "");
                
                // Effect of Factor A at each level of Factor B
                dt.Rows.Add("Effect of Series at each X value:", "", "", "", "", "", "");
                foreach (var x in selectedXValues)
                {
                    var groupsAtX = new List<List<double>>();
                    var namesAtX = new List<string>();
                    foreach (var s in selectedSeries)
                    {
                        groupsAtX.Add(dataMatrix[s.Name][x]);
                        namesAtX.Add(s.Name);
                    }
                    var (_, _, _, _, _, _, fSimple, pSimple) = CalculateOneWayAnova(groupsAtX);
                    dt.Rows.Add($"  {GetXLabel(x)}", "", "", "", fSimple.ToString("F4"), FormatPValue(pSimple), GetSig(pSimple));
                }

                dt.Rows.Add("", "", "", "", "", "", "");
                dt.Rows.Add("Effect of X value at each Series:", "", "", "", "", "", "");
                foreach (var s in selectedSeries)
                {
                    var groupsAtS = new List<List<double>>();
                    foreach (var x in selectedXValues)
                        groupsAtS.Add(dataMatrix[s.Name][x]);
                    var (_, _, _, _, _, _, fSimple, pSimple) = CalculateOneWayAnova(groupsAtS);
                    dt.Rows.Add($"  {s.Name}", "", "", "", fSimple.ToString("F4"), FormatPValue(pSimple), GetSig(pSimple));
                }
            }
            else
            {
                // Main Effects Tests
                dt.Rows.Add("Post-hoc: Main Effects Tests (Interaction p ≥ 0.05 or excluded)", "", "", "", "", "", "");
                
                if (pA < 0.05)
                {
                    dt.Rows.Add("", "", "", "", "", "", "");
                    dt.Rows.Add("Pairwise comparisons for Factor A (Series):", "", "", "", "", "", "");
                    for (int i = 0; i < selectedSeries.Count; i++)
                    {
                        for (int j = i + 1; j < selectedSeries.Count; j++)
                        {
                            var s1 = selectedSeries[i];
                            var s2 = selectedSeries[j];
                            var d1 = selectedXValues.SelectMany(x => dataMatrix[s1.Name][x]).ToList();
                            var d2 = selectedXValues.SelectMany(x => dataMatrix[s2.Name][x]).ToList();
                            var (t, df, p) = WelchTTest(d1, d2);
                            dt.Rows.Add($"  {s1.Name} vs {s2.Name}", $"Δ={(d1.Average() - d2.Average()):F4}", df.ToString("F1"), "", t.ToString("F4"), FormatPValue(p), GetSig(p));
                        }
                    }
                }

                if (pB < 0.05)
                {
                    dt.Rows.Add("", "", "", "", "", "", "");
                    dt.Rows.Add("Pairwise comparisons for Factor B (X values):", "", "", "", "", "", "");
                    for (int i = 0; i < selectedXValues.Count; i++)
                    {
                        for (int j = i + 1; j < selectedXValues.Count; j++)
                        {
                            var x1 = selectedXValues[i];
                            var x2 = selectedXValues[j];
                            var d1 = selectedSeries.SelectMany(s => dataMatrix[s.Name][x1]).ToList();
                            var d2 = selectedSeries.SelectMany(s => dataMatrix[s.Name][x2]).ToList();
                            var (t, df, p) = WelchTTest(d1, d2);
                            dt.Rows.Add($"  {GetXLabel(x1)} vs {GetXLabel(x2)}", $"Δ={(d1.Average() - d2.Average()):F4}", df.ToString("F1"), "", t.ToString("F4"), FormatPValue(p), GetSig(p));
                        }
                    }
                }
            }

            // Cell means summary
            dt.Rows.Add("", "", "", "", "", "", "");
            dt.Rows.Add("Cell Means:", "", "", "", "", "", "");
            foreach (var s in selectedSeries)
            {
                var row = $"  {s.Name}: ";
                foreach (var x in selectedXValues)
                {
                    row += $"{GetXLabel(x)}→{cellMeans[(s.Name, x)]:F3}  ";
                }
                dt.Rows.Add(row, "", "", "", "", "", "");
            }

            return dt;
        }

        private DataTable RunDescriptive()
        {
            var dt = new DataTable();
            
            if (chartType == "Column")
            {
                dt.Columns.Add("Bar", typeof(string));
            }
            else
            {
                dt.Columns.Add("Series", typeof(string));
            }
            
            dt.Columns.Add("N", typeof(int));
            dt.Columns.Add("Mean", typeof(string));
            dt.Columns.Add("SD", typeof(string));
            dt.Columns.Add("SEM", typeof(string));
            dt.Columns.Add("Min", typeof(string));
            dt.Columns.Add("Max", typeof(string));
            dt.Columns.Add("Median", typeof(string));
            dt.Columns.Add("95% CI", typeof(string));

            var selectedSeries = GetSelectedSeriesFromListBox(multiSeriesListBox);

            foreach (var series in selectedSeries)
            {
                // For Column chart, use all replicates; for Multi Factors, use YValues
                var values = (chartType == "Column") 
                    ? GetAllReplicatesForSeries(series) 
                    : series.YValues;
                    
                if (values.Count == 0) continue;

                int n = values.Count;
                double mean = values.Average();
                double sd = CalcSD(values);
                double sem = sd / Math.Sqrt(n);
                double tCrit = GetTCritical(n - 1);

                dt.Rows.Add(series.Name, n, mean.ToString("F4"), sd.ToString("F4"), sem.ToString("F4"),
                    values.Min().ToString("F4"), values.Max().ToString("F4"), CalcMedian(values).ToString("F4"),
                    $"[{(mean - tCrit * sem):F3}, {(mean + tCrit * sem):F3}]");
            }

            return dt;
        }

        #endregion

        #region Helper Methods

        private List<ChartDataSeries> GetSelectedSeriesFromListBox(ListBox? lb)
        {
            var result = new List<ChartDataSeries>();
            if (lb == null) return result;

            foreach (var item in lb.Items)
            {
                if (item is CheckBox cb && cb.IsChecked == true)
                {
                    var series = dataSeries.FirstOrDefault(s => s.Name == cb.Content?.ToString());
                    if (series != null) result.Add(series);
                }
            }
            return result;
        }

        private List<double> GetSelectedXValues()
        {
            var result = new List<double>();
            if (multiXListBox == null) return result;

            foreach (var item in multiXListBox.Items)
            {
                if (item is CheckBox cb && cb.IsChecked == true && cb.Tag is double x)
                    result.Add(x);
            }
            return result;
        }

        private List<double> GetReplicatesAtX(ChartDataSeries series, double xVal)
        {
            for (int i = 0; i < series.XValues.Count; i++)
            {
                if (Math.Abs(series.XValues[i] - xVal) < 1e-9)
                {
                    if (i < series.RawReplicates.Count && series.RawReplicates[i].Count > 0)
                        return series.RawReplicates[i];
                    
                    // If no raw replicates, return single mean value
                    if (i < series.YValues.Count)
                        return new List<double> { series.YValues[i] };
                }
            }
            return new List<double>();
        }

        /// <summary>
        /// Get all replicate values for a series (used for Column chart where each series is one bar)
        /// </summary>
        private List<double> GetAllReplicatesForSeries(ChartDataSeries series)
        {
            var result = new List<double>();
            
            // For Column chart, replicates are stored in RawReplicates[0]
            if (series.RawReplicates.Count > 0 && series.RawReplicates[0].Count > 0)
            {
                result.AddRange(series.RawReplicates[0]);
            }
            else
            {
                // Fallback: collect from all RawReplicates
                foreach (var reps in series.RawReplicates)
                {
                    if (reps != null && reps.Count > 0)
                        result.AddRange(reps);
                }
                
                // If still empty, use YValues
                if (result.Count == 0 && series.YValues.Count > 0)
                {
                    result.AddRange(series.YValues);
                }
            }
            
            return result;
        }

        private (double t, double df, double p) WelchTTest(List<double> g1, List<double> g2)
        {
            double m1 = g1.Average(), m2 = g2.Average();
            double v1 = CalcVar(g1), v2 = CalcVar(g2);
            int n1 = g1.Count, n2 = g2.Count;

            double se = Math.Sqrt(v1 / n1 + v2 / n2);
            if (se < 1e-10) return (0, n1 + n2 - 2, 1);

            double t = (m1 - m2) / se;

            double num = Math.Pow(v1 / n1 + v2 / n2, 2);
            double den = Math.Pow(v1 / n1, 2) / (n1 - 1) + Math.Pow(v2 / n2, 2) / (n2 - 1);
            double df = den > 0 ? num / den : n1 + n2 - 2;

            double p = CalculateTwoTailedTPValue(Math.Abs(t), df);
            return (t, df, p);
        }

        private (double ssBetween, double ssWithin, double ssTotal, int dfBetween, int dfWithin, int dfTotal, double f, double p) 
            CalculateOneWayAnova(List<List<double>> groups)
        {
            var allData = groups.SelectMany(g => g).ToList();
            double grandMean = allData.Average();
            int k = groups.Count;
            int totalN = allData.Count;

            double ssBetween = groups.Sum(g => g.Count * Math.Pow(g.Average() - grandMean, 2));
            double ssWithin = groups.Sum(g => g.Sum(v => Math.Pow(v - g.Average(), 2)));
            double ssTotal = allData.Sum(v => Math.Pow(v - grandMean, 2));

            int dfBetween = k - 1;
            int dfWithin = totalN - k;
            int dfTotal = totalN - 1;

            double msBetween = dfBetween > 0 ? ssBetween / dfBetween : 0;
            double msWithin = dfWithin > 0 ? ssWithin / dfWithin : 0;
            double f = msWithin > 1e-10 ? msBetween / msWithin : 0;
            double p = CalculateFPValue(f, dfBetween, dfWithin);

            return (ssBetween, ssWithin, ssTotal, dfBetween, dfWithin, dfTotal, f, p);
        }

        private double CalcSD(List<double> vals)
        {
            if (vals.Count < 2) return 0;
            double m = vals.Average();
            return Math.Sqrt(vals.Sum(v => Math.Pow(v - m, 2)) / (vals.Count - 1));
        }

        private double CalcVar(List<double> vals)
        {
            double sd = CalcSD(vals);
            return sd * sd;
        }

        private double CalcMedian(List<double> vals)
        {
            var sorted = vals.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 0 ? (sorted[n / 2 - 1] + sorted[n / 2]) / 2 : sorted[n / 2];
        }

        private double GetTCritical(int df)
        {
            if (df <= 0) return 1.96;
            if (df == 1) return 12.706; if (df == 2) return 4.303;
            if (df <= 5) return 2.571; if (df <= 10) return 2.228;
            if (df <= 20) return 2.086; if (df <= 30) return 2.042;
            return 1.96;
        }

        private double CalculateTwoTailedTPValue(double t, double df)
        {
            if (df < 1) return 1;
            double x = df / (df + t * t);
            return BetaInc(df / 2.0, 0.5, x);
        }

        private double CalculateFPValue(double f, int df1, int df2)
        {
            if (f < 0 || df1 < 1 || df2 < 1) return 1;
            double x = df2 / (df2 + df1 * f);
            return BetaInc(df2 / 2.0, df1 / 2.0, x);
        }

        private double BetaInc(double a, double b, double x)
        {
            if (x <= 0) return 0; if (x >= 1) return 1;
            double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
            return x < (a + 1) / (a + b + 2) ? bt * BetaCF(a, b, x) / a : 1 - bt * BetaCF(b, a, 1 - x) / b;
        }

        private double BetaCF(double a, double b, double x)
        {
            double qab = a + b, qap = a + 1, qam = a - 1;
            double c = 1, d = 1 - qab * x / qap;
            if (Math.Abs(d) < 1e-10) d = 1e-10;
            d = 1 / d;
            double h = d;

            for (int m = 1; m <= 100; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1 + aa * d; if (Math.Abs(d) < 1e-10) d = 1e-10;
                c = 1 + aa / c; if (Math.Abs(c) < 1e-10) c = 1e-10;
                d = 1 / d; h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1 + aa * d; if (Math.Abs(d) < 1e-10) d = 1e-10;
                c = 1 + aa / c; if (Math.Abs(c) < 1e-10) c = 1e-10;
                d = 1 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1) < 1e-10) break;
            }
            return h;
        }

        private double LogGamma(double x)
        {
            double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 1.208650973866179e-3, -5.395239384953e-6 };
            double tmp = x + 5.5 - (x + 0.5) * Math.Log(x + 5.5);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) ser += c[j] / (x + 1 + j);
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        private string FormatPValue(double p)
        {
            if (double.IsNaN(p)) return "N/A";
            if (p < 0.0001) return "< 0.0001";
            return p.ToString("F4");
        }

        private string GetSig(double p)
        {
            if (double.IsNaN(p)) return "-";
            if (p < 0.001) return "***";
            if (p < 0.01) return "**";
            if (p < 0.05) return "*";
            return "ns";
        }

        #endregion

        #region Copy Results
        private void ResultsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 處理 Ctrl+C 複製選中的儲存格
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedCells();
                e.Handled = true;
            }
        }

        private void CopySelectedCells()
        {
            if (ResultsGrid.SelectedCells.Count == 0)
            {
                // 如果沒有選中的儲存格，複製整個表格
                CopyResults_Click(null, null);
                return;
            }

            var sb = new StringBuilder();

            // 獲取所有選中的儲存格，按行和列排序
            var selectedCells = ResultsGrid.SelectedCells
                .Select(cell => new
                {
                    RowIndex = ResultsGrid.Items.IndexOf(cell.Item),
                    ColumnIndex = cell.Column.DisplayIndex,
                    Cell = cell
                })
                .OrderBy(x => x.RowIndex)
                .ThenBy(x => x.ColumnIndex)
                .ToList();

            if (selectedCells.Count == 0) return;

            // 檢查是否只選中了單一儲存格
            if (selectedCells.Count == 1)
            {
                var cell = selectedCells[0].Cell;
                var cellContent = GetCellValue(cell);
                Clipboard.SetText(cellContent);
                return;
            }

            // 多個儲存格：按行組織
            int currentRow = -1;
            int lastColumnIndex = -1;

            foreach (var item in selectedCells)
            {
                if (currentRow != item.RowIndex)
                {
                    // 新的一行
                    if (currentRow >= 0)
                    {
                        sb.AppendLine();
                    }
                    currentRow = item.RowIndex;
                    lastColumnIndex = item.ColumnIndex;
                }
                else
                {
                    // 同一行，添加分隔符
                    // 填充跳過的列
                    while (lastColumnIndex < item.ColumnIndex - 1)
                    {
                        sb.Append("\t");
                        lastColumnIndex++;
                    }
                    sb.Append("\t");
                    lastColumnIndex = item.ColumnIndex;
                }

                var cellValue = GetCellValue(item.Cell);
                sb.Append(cellValue);
            }

            Clipboard.SetText(sb.ToString());
        }

        private string GetCellValue(DataGridCellInfo cellInfo)
        {
            if (cellInfo.Column == null || cellInfo.Item == null)
                return "";

            // 獲取綁定的屬性值
            var binding = (cellInfo.Column as DataGridBoundColumn)?.Binding as Binding;
            if (binding != null && cellInfo.Item is System.Data.DataRowView rowView)
            {
                var columnName = binding.Path.Path;
                if (rowView.Row.Table.Columns.Contains(columnName))
                {
                    var value = rowView[columnName];
                    return value?.ToString() ?? "";
                }
            }

            // 備用方法：嘗試直接從列名獲取
            if (cellInfo.Item is System.Data.DataRowView drv)
            {
                try
                {
                    var columnHeader = cellInfo.Column.Header?.ToString();
                    if (!string.IsNullOrEmpty(columnHeader) && drv.Row.Table.Columns.Contains(columnHeader))
                    {
                        return drv[columnHeader]?.ToString() ?? "";
                    }
                }
                catch
                {
                    // 忽略錯誤
                }
            }

            return "";
        }
        private void CopyResults_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.ItemsSource is DataView dv && dv.Table != null)
            {
                var sb = new StringBuilder();
                var dt = dv.Table;

                // Headers
                sb.AppendLine(string.Join("\t", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));

                // Rows
                foreach (DataRow row in dt.Rows)
                    sb.AppendLine(string.Join("\t", row.ItemArray.Select(v => v?.ToString() ?? "")));

                Clipboard.SetText(sb.ToString());
                MessageBox.Show("Results copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
       

        

        


        #endregion
    }
}
