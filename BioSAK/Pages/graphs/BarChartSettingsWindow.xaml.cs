using System.Collections.Generic;
using System.Windows;

namespace BioSAK
{
    public partial class BarChartSettingsWindow : Window
    {
        public string ChartTitle { get; private set; } = "";
        public string XAxisTitle { get; private set; } = "";
        public string YAxisTitle { get; private set; } = "";
        
        // Error bar settings
        public double ErrorBarThickness { get; private set; } = 1.5;
        
        // Data point settings
        public bool ShowDataPoints { get; private set; } = false;
        public double DataPointSize { get; private set; } = 5;

        public BarChartSettingsWindow(List<ChartDataSeries> data, string chartTitle, string xTitle, string yTitle,
            double errorBarThickness = 1.5, bool showDataPoints = false, double dataPointSize = 5)
        {
            InitializeComponent();

            ChartTitleBox.Text = chartTitle;
            XAxisTitleBox.Text = xTitle;
            YAxisTitleBox.Text = yTitle;
            
            ErrorBarThicknessSlider.Value = errorBarThickness;
            ErrorBarThicknessLabel.Text = errorBarThickness.ToString("F1");
            
            ShowDataPointsCheck.IsChecked = showDataPoints;
            DataPointSizeSlider.Value = dataPointSize;
            DataPointSizeLabel.Text = dataPointSize.ToString("F0");
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ErrorBarThicknessLabel != null && ErrorBarThicknessSlider != null)
                ErrorBarThicknessLabel.Text = ErrorBarThicknessSlider.Value.ToString("F1");
        }

        private void PointSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataPointSizeLabel != null && DataPointSizeSlider != null)
                DataPointSizeLabel.Text = DataPointSizeSlider.Value.ToString("F0");
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ChartTitle = ChartTitleBox.Text;
            XAxisTitle = XAxisTitleBox.Text;
            YAxisTitle = YAxisTitleBox.Text;
            
            ErrorBarThickness = ErrorBarThicknessSlider.Value;
            ShowDataPoints = ShowDataPointsCheck.IsChecked == true;
            DataPointSize = DataPointSizeSlider.Value;

            DialogResult = true;
            Close();
        }
    }
}
