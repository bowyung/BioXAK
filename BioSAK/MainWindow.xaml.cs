using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using BioSAK.Pages;

namespace BioSAK
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // 只要有導航就隱藏歡迎圖片
            Welcome.Visibility = Visibility.Hidden;
        }

        private void NavigateToPage(Page page)
        {
            MainFrame.Navigate(page);
        }

        // ===== TCGA DATABASE =====
        private void TcgaAnalysis_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new TcgaAnalysisPage());
        }

        private void GeneIdConverter_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new GeneIdConverterPage());
        }

        // ===== Flow Cytometry =====
        private void Flow_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new FlowCytometryAnalyzer());
        }

        // ===== Graphs =====
        private void GraphsGenerator_Click(object sender, RoutedEventArgs e)
        {
            var selector = new GraphTypeSelector();
            selector.Owner = this;
            if (selector.ShowDialog() == true)
            {
                if (selector.SelectedType == "XY")
                {
                    MainFrame.Navigate(new GraphGen());
                }
                else if (selector.SelectedType == "Column")
                {
                    MainFrame.Navigate(new GraphGen());
                }
                else if (selector.SelectedType == "Grouped")
                {
                    MainFrame.Navigate(new GraphGen());
                }
            }
        }

        // ===== DNA Tools =====
        private void NucleotideComplementary_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new NucleotideComplementary());
        }
        private void PrimerDesign_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new PrimerDesignerPage());
        }
        // ===== CRISPR / Gene Editing =====
        private void sgRNADesigner_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new sgRNADesignerPage());
        }

        // ===== CLONING =====
        private void VectorDesigner_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new VectorDesignerPage());
        }

        // ===== Restriction Enzyme =====
        private void RestrictionEnzymePattern_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new RestrictionEnzymePatternPage());
        }

        private void RestrictionEnzymePredictor_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new RestrictionEnzymePredictorPage());
        }

        // ===== Protein =====
        private void WesternBlotOrganizer_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new WesternBlotOrganizer());
        }
        private void ProteinConcentrationCalculator_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new ProteinBCA());
        }

        // ===== Chemical =====
        private void Concalculator_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new MWCal());
        }

        private void MWCalculator_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new ConcCal());
        }

        // ===== Help =====
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }
    }
}
