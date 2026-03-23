using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BioSAK
{
    public partial class ChartSettingsWindow : Window
    {
        private List<ChartDataSeries>? dataSeries;
        private int selectedIndex = 0;
        private bool isInitialized = false;

        public string ChartTitle { get; private set; } = "";
        public string XAxisTitle { get; private set; } = "";
        public string YAxisTitle { get; private set; } = "";
        public string SelectedChartType { get; private set; } = "Line";
        public bool ShowConnectLines { get; private set; } = false;
        public bool ShowRegression { get; private set; } = false;
        public int RegressionDegree { get; private set; } = 1;
        public bool ShowRegressionEquation { get; private set; } = true;

        // Axis settings
        public bool IsXAutoScale { get; private set; } = true;
        public double? XMin { get; private set; }
        public double? XMax { get; private set; }
        public double? XInterval { get; private set; }
        public bool XLogScaleEnabled { get; private set; } = false;
        public double XLogBaseValue { get; private set; } = 10;

        public bool IsYAutoScale { get; private set; } = true;
        public double? YMin { get; private set; }
        public double? YMax { get; private set; }
        public double? YInterval { get; private set; }
        public bool YLogScaleEnabled { get; private set; } = false;
        public double YLogBaseValue { get; private set; } = 10;

        public ChartSettingsWindow(List<ChartDataSeries> series, string chartTitle, string xTitle, string yTitle, string chartType,
            bool showConnectLines = false, bool showRegression = false, int regressionDegree = 1, bool showRegressionEquation = true,
            bool xAutoScale = true, double? xMin = null, double? xMax = null, double? xInterval = null, bool xLog = false, double xLogBase = 10,
            bool yAutoScale = true, double? yMin = null, double? yMax = null, double? yInterval = null, bool yLog = false, double yLogBase = 10)
        {
            InitializeComponent();

            dataSeries = series;
            ChartTitle = chartTitle;
            XAxisTitle = xTitle;
            YAxisTitle = yTitle;
            SelectedChartType = chartType;
            ShowConnectLines = showConnectLines;
            ShowRegression = showRegression;
            RegressionDegree = regressionDegree;
            ShowRegressionEquation = showRegressionEquation;
            IsXAutoScale = xAutoScale; XMin = xMin; XMax = xMax; XInterval = xInterval;
            XLogScaleEnabled = xLog; XLogBaseValue = xLogBase;
            IsYAutoScale = yAutoScale; YMin = yMin; YMax = yMax; YInterval = yInterval;
            YLogScaleEnabled = yLog; YLogBaseValue = yLogBase;

            // Initialize UI after loading
            this.Loaded += (s, e) =>
            {
                // Parse two-line title
                string[] titleParts = chartTitle.Split('|');
                ChartTitleBox.Text = titleParts[0].Trim();
                ChartSubtitleBox.Text = titleParts.Length > 1 ? titleParts[1].Trim() : "";

                XAxisTitleBox.Text = xTitle;
                YAxisTitleBox.Text = yTitle;

                // Set chart type
                switch (chartType)
                {
                    case "Line":
                        LineTypeRadio.IsChecked = true;
                        break;
                    case "Scatter":
                        ScatterTypeRadio.IsChecked = true;
                        break;
                    case "Volcano":
                        VolcanoTypeRadio.IsChecked = true;
                        break;
                }

                // Scatter connect lines
                ConnectLinesCheck.IsChecked = showConnectLines;

                // Regression settings
                RegressionCheck.IsChecked = showRegression;
                RegressionOptions.IsEnabled = showRegression;
                DegreeSlider.Value = regressionDegree;
                DegreeLabel.Text = regressionDegree.ToString();
                ShowEquationCheck.IsChecked = showRegressionEquation;
                UpdateDegreeHint(regressionDegree);

                // Axis settings
                XAutoScale.IsChecked = xAutoScale;
                XMinBox.IsEnabled = !xAutoScale; XMaxBox.IsEnabled = !xAutoScale; XIntervalBox.IsEnabled = !xAutoScale;
                if (xMin.HasValue) XMinBox.Text = xMin.Value.ToString("G6");
                if (xMax.HasValue) XMaxBox.Text = xMax.Value.ToString("G6");
                if (xInterval.HasValue) XIntervalBox.Text = xInterval.Value.ToString("G6");
                XLogScale.IsChecked = xLog;
                XLogBase.Text = xLogBase.ToString();
                XLogBase.IsEnabled = xLog;

                YAutoScale.IsChecked = yAutoScale;
                YMinBox.IsEnabled = !yAutoScale; YMaxBox.IsEnabled = !yAutoScale; YIntervalBox.IsEnabled = !yAutoScale;
                if (yMin.HasValue) YMinBox.Text = yMin.Value.ToString("G6");
                if (yMax.HasValue) YMaxBox.Text = yMax.Value.ToString("G6");
                if (yInterval.HasValue) YIntervalBox.Text = yInterval.Value.ToString("G6");
                YLogScale.IsChecked = yLog;
                YLogBase.Text = yLogBase.ToString();
                YLogBase.IsEnabled = yLog;

                // Populate series selector
                SeriesSelector.Items.Clear();
                for (int i = 0; i < dataSeries.Count; i++)
                {
                    SeriesSelector.Items.Add(dataSeries[i].Name);
                }

                if (SeriesSelector.Items.Count > 0)
                {
                    SeriesSelector.SelectedIndex = 0;
                }

                isInitialized = true;
            };
        }

        private void SeriesSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataSeries == null || SeriesSelector.SelectedIndex < 0 || SeriesSelector.SelectedIndex >= dataSeries.Count)
                return;

            selectedIndex = SeriesSelector.SelectedIndex;
            var series = dataSeries[selectedIndex];

            // Temporarily disable to prevent event loops
            isInitialized = false;

            // Update UI to reflect selected series
            ColorPreview.Background = new SolidColorBrush(series.LineColor);
            LineThicknessSlider.Value = series.LineThickness;
            LineThicknessLabel.Text = ((int)series.LineThickness).ToString();
            MarkerSizeSlider.Value = series.MarkerSize;
            MarkerSizeLabel.Text = series.MarkerSize.ToString();

            // Set marker shape
            switch (series.MarkerShape)
            {
                case "Circle":
                    MarkerShapeCombo.SelectedIndex = 0;
                    break;
                case "Square":
                    MarkerShapeCombo.SelectedIndex = 1;
                    break;
                case "Triangle":
                    MarkerShapeCombo.SelectedIndex = 2;
                    break;
                case "Diamond":
                    MarkerShapeCombo.SelectedIndex = 3;
                    break;
                default:
                    MarkerShapeCombo.SelectedIndex = 0;
                    break;
            }

            isInitialized = true;
        }

        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            if (dataSeries == null || selectedIndex >= dataSeries.Count) return;

            var colorDialog = new ColorPickerDialog(dataSeries[selectedIndex].LineColor);
            colorDialog.Owner = this;

            if (colorDialog.ShowDialog() == true)
            {
                dataSeries[selectedIndex].LineColor = colorDialog.SelectedColor;
                ColorPreview.Background = new SolidColorBrush(colorDialog.SelectedColor);
            }
        }

        private void LineThickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isInitialized || dataSeries == null || LineThicknessLabel == null) return;

            LineThicknessLabel.Text = ((int)LineThicknessSlider.Value).ToString();
            if (selectedIndex < dataSeries.Count)
            {
                dataSeries[selectedIndex].LineThickness = LineThicknessSlider.Value;
            }
        }

        private void MarkerSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isInitialized || dataSeries == null || MarkerSizeLabel == null) return;

            MarkerSizeLabel.Text = ((int)MarkerSizeSlider.Value).ToString();
            if (selectedIndex < dataSeries.Count)
            {
                dataSeries[selectedIndex].MarkerSize = (int)MarkerSizeSlider.Value;
            }
        }

        private void MarkerShape_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized || dataSeries == null || MarkerShapeCombo.SelectedItem == null) return;

            if (MarkerShapeCombo.SelectedItem is ComboBoxItem item && selectedIndex < dataSeries.Count)
            {
                dataSeries[selectedIndex].MarkerShape = item.Content?.ToString() ?? "Circle";
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Combine title and subtitle with | separator
            string mainTitle = ChartTitleBox.Text.Trim();
            string subtitle = ChartSubtitleBox.Text.Trim();

            if (!string.IsNullOrEmpty(subtitle))
            {
                ChartTitle = $"{mainTitle}|{subtitle}";
            }
            else
            {
                ChartTitle = mainTitle;
            }

            XAxisTitle = XAxisTitleBox.Text;
            YAxisTitle = YAxisTitleBox.Text;

            if (LineTypeRadio.IsChecked == true)
                SelectedChartType = "Line";
            else if (ScatterTypeRadio.IsChecked == true)
                SelectedChartType = "Scatter";
            else if (VolcanoTypeRadio.IsChecked == true)
                SelectedChartType = "Volcano";

            ShowConnectLines = ConnectLinesCheck.IsChecked == true;
            ShowRegression = RegressionCheck.IsChecked == true;
            RegressionDegree = (int)DegreeSlider.Value;
            ShowRegressionEquation = ShowEquationCheck.IsChecked == true;

            // Axis settings
            IsXAutoScale = XAutoScale.IsChecked == true;
            XMin = double.TryParse(XMinBox.Text, out double xmin) ? xmin : null;
            XMax = double.TryParse(XMaxBox.Text, out double xmax) ? xmax : null;
            XInterval = double.TryParse(XIntervalBox.Text, out double xint) ? xint : null;
            XLogScaleEnabled = XLogScale.IsChecked == true;
            XLogBaseValue = double.TryParse(XLogBase.Text, out double xlb) && xlb > 1 ? xlb : 10;

            IsYAutoScale = YAutoScale.IsChecked == true;
            YMin = double.TryParse(YMinBox.Text, out double ymin) ? ymin : null;
            YMax = double.TryParse(YMaxBox.Text, out double ymax) ? ymax : null;
            YInterval = double.TryParse(YIntervalBox.Text, out double yint) ? yint : null;
            YLogScaleEnabled = YLogScale.IsChecked == true;
            YLogBaseValue = double.TryParse(YLogBase.Text, out double ylb) && ylb > 1 ? ylb : 10;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RegressionCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (RegressionOptions != null)
                RegressionOptions.IsEnabled = RegressionCheck.IsChecked == true;
        }

        private void Degree_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DegreeLabel == null) return;
            int deg = (int)DegreeSlider.Value;
            DegreeLabel.Text = deg.ToString();
            UpdateDegreeHint(deg);
        }

        private void UpdateDegreeHint(int degree)
        {
            if (DegreeHint == null) return;
            DegreeHint.Text = degree switch
            {
                1 => "(Linear)",
                2 => "(Quadratic)",
                3 => "(Cubic)",
                _ => $"(Degree {degree})"
            };
        }

        private void AxisAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (XMinBox == null || YMinBox == null) return; // Not loaded yet

            bool xAuto = XAutoScale.IsChecked == true;
            XMinBox.IsEnabled = !xAuto;
            XMaxBox.IsEnabled = !xAuto;
            XIntervalBox.IsEnabled = !xAuto;

            bool yAuto = YAutoScale.IsChecked == true;
            YMinBox.IsEnabled = !yAuto;
            YMaxBox.IsEnabled = !yAuto;
            YIntervalBox.IsEnabled = !yAuto;
        }

        private void LogScale_Changed(object sender, RoutedEventArgs e)
        {
            if (XLogBase == null) return; // Not loaded yet
            XLogBase.IsEnabled = XLogScale.IsChecked == true;
            YLogBase.IsEnabled = YLogScale.IsChecked == true;
        }
    }
}