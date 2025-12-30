using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BioSAK
{
    public partial class TextAnnotationDialog : Window
    {
        public string AnnotationText { get; private set; } = "";
        public double SelectedFontSize { get; private set; } = 12;
        public FontFamily SelectedFontFamily { get; private set; } = new FontFamily("Segoe UI");
        public bool IsBold { get; private set; } = false;
        public bool IsItalic { get; private set; } = false;
        public bool ShowBorder { get; private set; } = false;
        public Color TextColor { get; private set; } = Colors.Black;

        public TextAnnotationDialog()
        {
            InitializeComponent();
            SetupEventHandlers();
            UpdatePreview();
        }

        public TextAnnotationDialog(string existingText, TextFormatInfo format)
        {
            InitializeComponent();

            // Set existing values
            TextInput.Text = existingText;
            TextColor = format.TextColor;
            ColorPreview.Background = new SolidColorBrush(format.TextColor);
            BoldCheck.IsChecked = format.IsBold;
            ItalicCheck.IsChecked = format.IsItalic;
            BorderCheck.IsChecked = format.ShowBorder;

            // Find and select font family
            foreach (ComboBoxItem item in FontFamilyCombo.Items)
            {
                if (item.Content.ToString() == format.FontFamily.Source)
                {
                    FontFamilyCombo.SelectedItem = item;
                    break;
                }
            }

            // Find and select font size
            foreach (ComboBoxItem item in FontSizeCombo.Items)
            {
                if (item.Content.ToString() == format.FontSize.ToString())
                {
                    FontSizeCombo.SelectedItem = item;
                    break;
                }
            }

            SetupEventHandlers();
            UpdatePreview();
        }

        private void SetupEventHandlers()
        {
            TextInput.TextChanged += (s, e) => UpdatePreview();
            FontFamilyCombo.SelectionChanged += (s, e) => UpdatePreview();
            FontSizeCombo.SelectionChanged += (s, e) => UpdatePreview();
            BoldCheck.Checked += (s, e) => UpdatePreview();
            BoldCheck.Unchecked += (s, e) => UpdatePreview();
            ItalicCheck.Checked += (s, e) => UpdatePreview();
            ItalicCheck.Unchecked += (s, e) => UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (PreviewText == null) return;

            string text = string.IsNullOrEmpty(TextInput.Text) ? "Preview Text" : TextInput.Text;
            PreviewText.Text = text;

            if (FontFamilyCombo.SelectedItem is ComboBoxItem fontItem)
            {
                PreviewText.FontFamily = new FontFamily(fontItem.Content.ToString() ?? "Segoe UI");
            }

            if (FontSizeCombo.SelectedItem is ComboBoxItem sizeItem)
            {
                if (double.TryParse(sizeItem.Content.ToString(), out double size))
                {
                    PreviewText.FontSize = size;
                }
            }

            PreviewText.FontWeight = BoldCheck.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            PreviewText.FontStyle = ItalicCheck.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            PreviewText.Foreground = new SolidColorBrush(TextColor);
        }

        private void InsertSymbol_Click(object sender, RoutedEventArgs e)
        {
            var symbolPicker = new SymbolPickerDialog();
            symbolPicker.Owner = this;
            if (symbolPicker.ShowDialog() == true && !string.IsNullOrEmpty(symbolPicker.SelectedSymbol))
            {
                int caretIndex = TextInput.CaretIndex;
                TextInput.Text = TextInput.Text.Insert(caretIndex, symbolPicker.SelectedSymbol);
                TextInput.CaretIndex = caretIndex + symbolPicker.SelectedSymbol.Length;
                TextInput.Focus();
            }
        }

        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new ColorPickerDialog(TextColor);
            colorDialog.Owner = this;
            if (colorDialog.ShowDialog() == true)
            {
                TextColor = colorDialog.SelectedColor;
                ColorPreview.Background = new SolidColorBrush(TextColor);
                UpdatePreview();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            AnnotationText = TextInput.Text;

            if (FontFamilyCombo.SelectedItem is ComboBoxItem fontItem)
            {
                SelectedFontFamily = new FontFamily(fontItem.Content.ToString() ?? "Segoe UI");
            }

            if (FontSizeCombo.SelectedItem is ComboBoxItem sizeItem)
            {
                if (double.TryParse(sizeItem.Content.ToString(), out double size))
                {
                    SelectedFontSize = size;
                }
            }

            IsBold = BoldCheck.IsChecked == true;
            IsItalic = ItalicCheck.IsChecked == true;
            ShowBorder = BorderCheck.IsChecked == true;

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
