using System.Windows;
using System.Windows.Media;

namespace BioSAK
{
    public partial class AxisSettingsDialog : Window
    {
        public bool IsYAxis { get; private set; }
        
        // Scale settings
        public bool AutoScale { get; private set; } = true;
        public bool LogScale { get; private set; } = false;
        public double MinValue { get; private set; } = 0;
        public double MaxValue { get; private set; } = 100;
        
        // Tick mark settings
        public double MainScaleInterval { get; private set; } = 0; // 0 = auto
        public bool ShowSubScale { get; private set; } = false;
        public int SubScaleDivisions { get; private set; } = 5;
        
        // Axis break settings
        public bool EnableBreak { get; private set; } = false;
        public double BreakStart { get; private set; } = 0;
        public double BreakEnd { get; private set; } = 0;
        
        // Grid line settings
        public bool ShowGridLines { get; private set; } = false;
        public string GridLineStyle { get; private set; } = "Solid";
        public Color GridLineColor { get; private set; } = Color.FromRgb(224, 224, 224);
        
        // Title
        public string AxisTitle { get; private set; } = "";

        private Color currentGridColor = Color.FromRgb(224, 224, 224);

        public AxisSettingsDialog(bool isYAxis, string currentTitle, double currentMin, double currentMax,
            bool showGrid = false, string gridStyle = "Solid", Color? gridColor = null, double mainInterval = 0,
            bool showSubScale = false, int subDivisions = 5, bool logScale = false)
        {
            InitializeComponent();
            
            IsYAxis = isYAxis;
            TitleText.Text = isYAxis ? "Y Axis Settings" : "X Axis Settings";
            
            AxisTitleBox.Text = currentTitle;
            MinValueBox.Text = currentMin.ToString("G4");
            MaxValueBox.Text = currentMax.ToString("G4");
            
            // Log scale
            LogScaleCheck.IsChecked = logScale;
            
            // Tick marks
            MainScaleIntervalBox.Text = mainInterval.ToString("G4");
            ShowSubScaleCheck.IsChecked = showSubScale;
            SubScaleDivisionsBox.Text = subDivisions.ToString();
            SubScaleDivisionsBox.IsEnabled = showSubScale;
            
            // Grid lines
            ShowGridLines = showGrid;
            ShowGridLinesCheck.IsChecked = showGrid;
            GridLineStyle = gridStyle;
            if (gridColor.HasValue)
            {
                currentGridColor = gridColor.Value;
                GridLineColorPreview.Background = new SolidColorBrush(currentGridColor);
            }
            
            // Set grid line style combo
            foreach (System.Windows.Controls.ComboBoxItem item in GridLineStyleCombo.Items)
            {
                if (item.Content?.ToString() == gridStyle)
                {
                    GridLineStyleCombo.SelectedItem = item;
                    break;
                }
            }
            
            // Set initial enabled states
            MinValueBox.IsEnabled = false;
            MaxValueBox.IsEnabled = false;
            BreakStartBox.IsEnabled = false;
            BreakEndBox.IsEnabled = false;
            GridLineStyleCombo.IsEnabled = showGrid;
            GridLineColorBtn.IsEnabled = showGrid;
            
            // Add event handlers after initialization
            AutoScaleCheck.Checked += AutoScale_Changed;
            AutoScaleCheck.Unchecked += AutoScale_Changed;
            EnableBreakCheck.Checked += EnableBreak_Changed;
            EnableBreakCheck.Unchecked += EnableBreak_Changed;
            ShowGridLinesCheck.Checked += ShowGridLines_Changed;
            ShowGridLinesCheck.Unchecked += ShowGridLines_Changed;
            ShowSubScaleCheck.Checked += ShowSubScale_Changed;
            ShowSubScaleCheck.Unchecked += ShowSubScale_Changed;
        }

        private void AutoScale_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = AutoScaleCheck.IsChecked != true;
            MinValueBox.IsEnabled = enabled;
            MaxValueBox.IsEnabled = enabled;
        }

        private void EnableBreak_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = EnableBreakCheck.IsChecked == true;
            BreakStartBox.IsEnabled = enabled;
            BreakEndBox.IsEnabled = enabled;
        }

        private void ShowGridLines_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ShowGridLinesCheck.IsChecked == true;
            GridLineStyleCombo.IsEnabled = enabled;
            GridLineColorBtn.IsEnabled = enabled;
        }

        private void ShowSubScale_Changed(object sender, RoutedEventArgs e)
        {
            SubScaleDivisionsBox.IsEnabled = ShowSubScaleCheck.IsChecked == true;
        }

        private void GridLineColor_Click(object sender, RoutedEventArgs e)
        {
            OpenColorPicker();
        }

        private void GridLineColorPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ShowGridLinesCheck.IsChecked == true)
                OpenColorPicker();
        }

        private void OpenColorPicker()
        {
            var dialog = new ColorPickerDialog(currentGridColor);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                currentGridColor = dialog.SelectedColor;
                GridLineColorPreview.Background = new SolidColorBrush(currentGridColor);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Parse scale values
            AutoScale = AutoScaleCheck.IsChecked == true;
            LogScale = LogScaleCheck.IsChecked == true;
            
            if (!AutoScale)
            {
                if (!double.TryParse(MinValueBox.Text, out double min))
                {
                    MessageBox.Show("Invalid minimum value.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!double.TryParse(MaxValueBox.Text, out double max))
                {
                    MessageBox.Show("Invalid maximum value.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MinValue = min;
                MaxValue = max;
            }
            
            // Parse tick mark settings
            if (!double.TryParse(MainScaleIntervalBox.Text, out double mainInt))
                mainInt = 0;
            MainScaleInterval = mainInt;
            
            ShowSubScale = ShowSubScaleCheck.IsChecked == true;
            if (!int.TryParse(SubScaleDivisionsBox.Text, out int subDiv) || subDiv < 2)
                subDiv = 5;
            SubScaleDivisions = subDiv;
            
            // Parse break values
            EnableBreak = EnableBreakCheck.IsChecked == true;
            if (EnableBreak)
            {
                if (!double.TryParse(BreakStartBox.Text, out double breakStart) ||
                    !double.TryParse(BreakEndBox.Text, out double breakEnd))
                {
                    MessageBox.Show("Invalid break values.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                BreakStart = breakStart;
                BreakEnd = breakEnd;
            }
            
            // Grid line settings
            ShowGridLines = ShowGridLinesCheck.IsChecked == true;
            GridLineStyle = (GridLineStyleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Solid";
            GridLineColor = currentGridColor;
            
            // Title
            AxisTitle = AxisTitleBox.Text;

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
