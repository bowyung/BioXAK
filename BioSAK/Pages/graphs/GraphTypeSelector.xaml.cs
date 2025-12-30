using System.Windows;
using System.Windows.Controls;

namespace BioSAK
{
    public partial class GraphTypeSelector : Window
    {
        public string SelectedType { get; private set; } = "";
        public bool LoadSampleData { get; private set; } = false;

        public GraphTypeSelector()
        {
            InitializeComponent();
        }

        private void XYButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = "XY";
            LoadSampleData = LoadSampleCheck.IsChecked == true;
            NavigateToGraphGen("XY", useReplicateMode: false);
        }

        private void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = "Column";
            LoadSampleData = LoadSampleCheck.IsChecked == true;
            NavigateToGraphGen("Column", useReplicateMode: false);
        }

        private void GroupedButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = "MultiGroup";
            LoadSampleData = LoadSampleCheck.IsChecked == true;
            NavigateToGraphGen("MultiGroup", useReplicateMode: true);
        }

        private void NavigateToGraphGen(string chartType, bool useReplicateMode)
        {
            var mainWindow = Owner as MainWindow;
            if (mainWindow != null)
            {
                var graphGen = new GraphGen();
                mainWindow.MainFrame.Navigate(graphGen);
                
                // Set chart type first (affects X column behavior)
                graphGen.SetChartType(chartType);
                
                // Set Enter Replicates mode for Multi Factors
                if (useReplicateMode)
                {
                    graphGen.SetYDataMode(1); // 1 = Enter Replicates
                }
                
                if (LoadSampleData)
                {
                    graphGen.LoadSampleData(chartType);
                }
            }
            this.Close();
        }
    }
}
