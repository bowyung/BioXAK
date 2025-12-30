using System.Windows;

namespace BioSAK
{
    public partial class ErrorBarDialog : Window
    {
        public string SelectedErrorType { get; private set; } = "None";
        public string SelectedDirection { get; private set; } = "Both"; // Both, Up, Down

        public ErrorBarDialog()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (SDErrorBar.IsChecked == true)
                SelectedErrorType = "SD";
            else if (SEMErrorBar.IsChecked == true)
                SelectedErrorType = "SEM";
            else if (CI95ErrorBar.IsChecked == true)
                SelectedErrorType = "95CI";
            else
                SelectedErrorType = "None";

            // Get direction
            if (DirectionUp.IsChecked == true)
                SelectedDirection = "Up";
            else if (DirectionDown.IsChecked == true)
                SelectedDirection = "Down";
            else
                SelectedDirection = "Both";

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
