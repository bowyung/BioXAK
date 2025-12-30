using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BioSAK
{
    public partial class SymbolPickerDialog : Window
    {
        public string SelectedSymbol { get; private set; } = "";

        private static List<string> recentSymbols = new List<string>();

        // Greek lowercase letters
        private readonly (string symbol, string name)[] greekLower = new[]
        {
            ("α", "alpha"), ("β", "beta"), ("γ", "gamma"), ("δ", "delta"),
            ("ε", "epsilon"), ("ζ", "zeta"), ("η", "eta"), ("θ", "theta"),
            ("ι", "iota"), ("κ", "kappa"), ("λ", "lambda"), ("μ", "mu"),
            ("ν", "nu"), ("ξ", "xi"), ("ο", "omicron"), ("π", "pi"),
            ("ρ", "rho"), ("σ", "sigma"), ("τ", "tau"), ("υ", "upsilon"),
            ("φ", "phi"), ("χ", "chi"), ("ψ", "psi"), ("ω", "omega")
        };

        // Greek uppercase letters
        private readonly (string symbol, string name)[] greekUpper = new[]
        {
            ("Α", "Alpha"), ("Β", "Beta"), ("Γ", "Gamma"), ("Δ", "Delta"),
            ("Ε", "Epsilon"), ("Ζ", "Zeta"), ("Η", "Eta"), ("Θ", "Theta"),
            ("Ι", "Iota"), ("Κ", "Kappa"), ("Λ", "Lambda"), ("Μ", "Mu"),
            ("Ν", "Nu"), ("Ξ", "Xi"), ("Ο", "Omicron"), ("Π", "Pi"),
            ("Ρ", "Rho"), ("Σ", "Sigma"), ("Τ", "Tau"), ("Υ", "Upsilon"),
            ("Φ", "Phi"), ("Χ", "Chi"), ("Ψ", "Psi"), ("Ω", "Omega")
        };

        // Math symbols
        private readonly (string symbol, string name)[] mathSymbols = new[]
        {
            ("±", "plus-minus"), ("×", "multiply"), ("÷", "divide"), ("≠", "not equal"),
            ("≈", "approximately"), ("≤", "less or equal"), ("≥", "greater or equal"), ("∞", "infinity"),
            ("√", "square root"), ("∑", "sum"), ("∏", "product"), ("∫", "integral"),
            ("∂", "partial"), ("∇", "nabla"), ("∈", "element of"), ("∉", "not element"),
            ("⊂", "subset"), ("⊃", "superset"), ("∪", "union"), ("∩", "intersection"),
            ("∅", "empty set"), ("∀", "for all"), ("∃", "exists"), ("¬", "not"),
            ("∧", "and"), ("∨", "or"), ("⊕", "xor"), ("°", "degree")
        };

        // Subscript and superscript
        private readonly (string symbol, string name)[] subSupSymbols = new[]
        {
            ("⁰", "superscript 0"), ("¹", "superscript 1"), ("²", "superscript 2"), ("³", "superscript 3"),
            ("⁴", "superscript 4"), ("⁵", "superscript 5"), ("⁶", "superscript 6"), ("⁷", "superscript 7"),
            ("⁸", "superscript 8"), ("⁹", "superscript 9"), ("⁺", "superscript +"), ("⁻", "superscript -"),
            ("₀", "subscript 0"), ("₁", "subscript 1"), ("₂", "subscript 2"), ("₃", "subscript 3"),
            ("₄", "subscript 4"), ("₅", "subscript 5"), ("₆", "subscript 6"), ("₇", "subscript 7"),
            ("₈", "subscript 8"), ("₉", "subscript 9"), ("₊", "subscript +"), ("₋", "subscript -"),
            ("ⁿ", "superscript n"), ("ˣ", "superscript x")
        };

        // Arrows
        private readonly (string symbol, string name)[] arrowSymbols = new[]
        {
            ("→", "right arrow"), ("←", "left arrow"), ("↑", "up arrow"), ("↓", "down arrow"),
            ("↔", "left right"), ("↕", "up down"), ("⇒", "double right"), ("⇐", "double left"),
            ("⇔", "double left right"), ("↗", "upper right"), ("↘", "lower right"), ("↙", "lower left"),
            ("↖", "upper left"), ("⟶", "long right"), ("⟵", "long left"), ("⟷", "long both")
        };

        // Other symbols
        private readonly (string symbol, string name)[] otherSymbols = new[]
        {
            ("•", "bullet"), ("◦", "white bullet"), ("‣", "triangular bullet"), ("★", "star"),
            ("☆", "white star"), ("✓", "check"), ("✗", "cross"), ("♠", "spade"),
            ("♣", "club"), ("♥", "heart"), ("♦", "diamond"), ("©", "copyright"),
            ("®", "registered"), ("™", "trademark"), ("§", "section"), ("¶", "paragraph"),
            ("†", "dagger"), ("‡", "double dagger"), ("‰", "per mille"), ("′", "prime"),
            ("″", "double prime"), ("‴", "triple prime"), ("Å", "angstrom"), ("℃", "celsius"),
            ("℉", "fahrenheit"), ("№", "numero")
        };

        public SymbolPickerDialog()
        {
            InitializeComponent();

            this.Loaded += (s, e) =>
            {
                PopulateSymbols(GreekPanel, greekLower);
                PopulateSymbols(GreekUpperPanel, greekUpper);
                PopulateSymbols(MathPanel, mathSymbols);
                PopulateSymbols(SubSupPanel, subSupSymbols);
                PopulateSymbols(ArrowPanel, arrowSymbols);
                PopulateSymbols(OtherPanel, otherSymbols);
                PopulateRecentSymbols();
            };
        }

        private void PopulateSymbols(WrapPanel panel, (string symbol, string name)[] symbols)
        {
            foreach (var (symbol, name) in symbols)
            {
                var btn = CreateSymbolButton(symbol, name);
                panel.Children.Add(btn);
            }
        }

        private void PopulateRecentSymbols()
        {
            RecentPanel.Children.Clear();
            foreach (var symbol in recentSymbols)
            {
                var btn = CreateSymbolButton(symbol, "Recent");
                RecentPanel.Children.Add(btn);
            }
        }

        private Button CreateSymbolButton(string symbol, string name)
        {
            var btn = new Button
            {
                Content = symbol,
                Width = 40,
                Height = 40,
                Margin = new Thickness(3),
                FontSize = 18,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = name,
                ToolTip = name
            };

            btn.Click += (s, e) =>
            {
                SelectedSymbol = symbol;
                PreviewSymbol.Text = symbol;
                SymbolName.Text = name;
            };

            btn.MouseDoubleClick += (s, e) =>
            {
                SelectedSymbol = symbol;
                AddToRecent(symbol);
                DialogResult = true;
                Close();
            };

            return btn;
        }

        private void AddToRecent(string symbol)
        {
            if (recentSymbols.Contains(symbol))
            {
                recentSymbols.Remove(symbol);
            }
            recentSymbols.Insert(0, symbol);
            if (recentSymbols.Count > 20)
            {
                recentSymbols.RemoveAt(recentSymbols.Count - 1);
            }
        }

        private void Insert_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SelectedSymbol))
            {
                AddToRecent(SelectedSymbol);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a symbol first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
