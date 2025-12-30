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

        public ChartSettingsWindow(List<ChartDataSeries> series, string chartTitle, string xTitle, string yTitle, string chartType)
        {
            InitializeComponent();

            dataSeries = series;
            ChartTitle = chartTitle;
            XAxisTitle = xTitle;
            YAxisTitle = yTitle;
            SelectedChartType = chartType;

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
