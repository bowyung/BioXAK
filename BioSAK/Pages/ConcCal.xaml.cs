using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BioSAK
{
    public partial class ConcCal : Page
    {
        // Element data structure
        private class Element
        {
            public int Number { get; set; }
            public string Symbol { get; set; } = string.Empty;
            public double Weight { get; set; }
            public string Category { get; set; } = string.Empty;
        }

        // All elements dictionary
        private readonly Dictionary<string, Element> elements = new Dictionary<string, Element>
        {
            {"H", new Element { Number = 1, Symbol = "H", Weight = 1.008, Category = "nonmetal" }},
            {"He", new Element { Number = 2, Symbol = "He", Weight = 4.003, Category = "noble" }},
            {"Li", new Element { Number = 3, Symbol = "Li", Weight = 6.941, Category = "alkali" }},
            {"Be", new Element { Number = 4, Symbol = "Be", Weight = 9.012, Category = "alkaline" }},
            {"B", new Element { Number = 5, Symbol = "B", Weight = 10.81, Category = "metalloid" }},
            {"C", new Element { Number = 6, Symbol = "C", Weight = 12.01, Category = "nonmetal" }},
            {"N", new Element { Number = 7, Symbol = "N", Weight = 14.01, Category = "nonmetal" }},
            {"O", new Element { Number = 8, Symbol = "O", Weight = 16.00, Category = "nonmetal" }},
            {"F", new Element { Number = 9, Symbol = "F", Weight = 19.00, Category = "halogen" }},
            {"Ne", new Element { Number = 10, Symbol = "Ne", Weight = 20.18, Category = "noble" }},
            {"Na", new Element { Number = 11, Symbol = "Na", Weight = 22.99, Category = "alkali" }},
            {"Mg", new Element { Number = 12, Symbol = "Mg", Weight = 24.31, Category = "alkaline" }},
            {"Al", new Element { Number = 13, Symbol = "Al", Weight = 26.98, Category = "post-transition" }},
            {"Si", new Element { Number = 14, Symbol = "Si", Weight = 28.09, Category = "metalloid" }},
            {"P", new Element { Number = 15, Symbol = "P", Weight = 30.97, Category = "nonmetal" }},
            {"S", new Element { Number = 16, Symbol = "S", Weight = 32.07, Category = "nonmetal" }},
            {"Cl", new Element { Number = 17, Symbol = "Cl", Weight = 35.45, Category = "halogen" }},
            {"Ar", new Element { Number = 18, Symbol = "Ar", Weight = 39.95, Category = "noble" }},
            {"K", new Element { Number = 19, Symbol = "K", Weight = 39.10, Category = "alkali" }},
            {"Ca", new Element { Number = 20, Symbol = "Ca", Weight = 40.08, Category = "alkaline" }},
            {"Sc", new Element { Number = 21, Symbol = "Sc", Weight = 44.96, Category = "transition" }},
            {"Ti", new Element { Number = 22, Symbol = "Ti", Weight = 47.87, Category = "transition" }},
            {"V", new Element { Number = 23, Symbol = "V", Weight = 50.94, Category = "transition" }},
            {"Cr", new Element { Number = 24, Symbol = "Cr", Weight = 52.00, Category = "transition" }},
            {"Mn", new Element { Number = 25, Symbol = "Mn", Weight = 54.94, Category = "transition" }},
            {"Fe", new Element { Number = 26, Symbol = "Fe", Weight = 55.85, Category = "transition" }},
            {"Co", new Element { Number = 27, Symbol = "Co", Weight = 58.93, Category = "transition" }},
            {"Ni", new Element { Number = 28, Symbol = "Ni", Weight = 58.69, Category = "transition" }},
            {"Cu", new Element { Number = 29, Symbol = "Cu", Weight = 63.55, Category = "transition" }},
            {"Zn", new Element { Number = 30, Symbol = "Zn", Weight = 65.38, Category = "transition" }},
            {"Ga", new Element { Number = 31, Symbol = "Ga", Weight = 69.72, Category = "post-transition" }},
            {"Ge", new Element { Number = 32, Symbol = "Ge", Weight = 72.63, Category = "metalloid" }},
            {"As", new Element { Number = 33, Symbol = "As", Weight = 74.92, Category = "metalloid" }},
            {"Se", new Element { Number = 34, Symbol = "Se", Weight = 78.97, Category = "nonmetal" }},
            {"Br", new Element { Number = 35, Symbol = "Br", Weight = 79.90, Category = "halogen" }},
            {"Kr", new Element { Number = 36, Symbol = "Kr", Weight = 83.80, Category = "noble" }},
            {"Rb", new Element { Number = 37, Symbol = "Rb", Weight = 85.47, Category = "alkali" }},
            {"Sr", new Element { Number = 38, Symbol = "Sr", Weight = 87.62, Category = "alkaline" }},
            {"Y", new Element { Number = 39, Symbol = "Y", Weight = 88.91, Category = "transition" }},
            {"Zr", new Element { Number = 40, Symbol = "Zr", Weight = 91.22, Category = "transition" }},
            {"Nb", new Element { Number = 41, Symbol = "Nb", Weight = 92.91, Category = "transition" }},
            {"Mo", new Element { Number = 42, Symbol = "Mo", Weight = 95.95, Category = "transition" }},
            {"Tc", new Element { Number = 43, Symbol = "Tc", Weight = 98.00, Category = "transition" }},
            {"Ru", new Element { Number = 44, Symbol = "Ru", Weight = 101.1, Category = "transition" }},
            {"Rh", new Element { Number = 45, Symbol = "Rh", Weight = 102.9, Category = "transition" }},
            {"Pd", new Element { Number = 46, Symbol = "Pd", Weight = 106.4, Category = "transition" }},
            {"Ag", new Element { Number = 47, Symbol = "Ag", Weight = 107.9, Category = "transition" }},
            {"Cd", new Element { Number = 48, Symbol = "Cd", Weight = 112.4, Category = "transition" }},
            {"In", new Element { Number = 49, Symbol = "In", Weight = 114.8, Category = "post-transition" }},
            {"Sn", new Element { Number = 50, Symbol = "Sn", Weight = 118.7, Category = "post-transition" }},
            {"Sb", new Element { Number = 51, Symbol = "Sb", Weight = 121.8, Category = "metalloid" }},
            {"Te", new Element { Number = 52, Symbol = "Te", Weight = 127.6, Category = "metalloid" }},
            {"I", new Element { Number = 53, Symbol = "I", Weight = 126.9, Category = "halogen" }},
            {"Xe", new Element { Number = 54, Symbol = "Xe", Weight = 131.3, Category = "noble" }},
            {"Cs", new Element { Number = 55, Symbol = "Cs", Weight = 132.9, Category = "alkali" }},
            {"Ba", new Element { Number = 56, Symbol = "Ba", Weight = 137.3, Category = "alkaline" }},
            {"La", new Element { Number = 57, Symbol = "La", Weight = 138.9, Category = "lanthanide" }},
            {"Ce", new Element { Number = 58, Symbol = "Ce", Weight = 140.1, Category = "lanthanide" }},
            {"Pr", new Element { Number = 59, Symbol = "Pr", Weight = 140.9, Category = "lanthanide" }},
            {"Nd", new Element { Number = 60, Symbol = "Nd", Weight = 144.2, Category = "lanthanide" }},
            {"Pm", new Element { Number = 61, Symbol = "Pm", Weight = 145.0, Category = "lanthanide" }},
            {"Sm", new Element { Number = 62, Symbol = "Sm", Weight = 150.4, Category = "lanthanide" }},
            {"Eu", new Element { Number = 63, Symbol = "Eu", Weight = 152.0, Category = "lanthanide" }},
            {"Gd", new Element { Number = 64, Symbol = "Gd", Weight = 157.3, Category = "lanthanide" }},
            {"Tb", new Element { Number = 65, Symbol = "Tb", Weight = 158.9, Category = "lanthanide" }},
            {"Dy", new Element { Number = 66, Symbol = "Dy", Weight = 162.5, Category = "lanthanide" }},
            {"Ho", new Element { Number = 67, Symbol = "Ho", Weight = 164.9, Category = "lanthanide" }},
            {"Er", new Element { Number = 68, Symbol = "Er", Weight = 167.3, Category = "lanthanide" }},
            {"Tm", new Element { Number = 69, Symbol = "Tm", Weight = 168.9, Category = "lanthanide" }},
            {"Yb", new Element { Number = 70, Symbol = "Yb", Weight = 173.0, Category = "lanthanide" }},
            {"Lu", new Element { Number = 71, Symbol = "Lu", Weight = 175.0, Category = "lanthanide" }},
            {"Hf", new Element { Number = 72, Symbol = "Hf", Weight = 178.5, Category = "transition" }},
            {"Ta", new Element { Number = 73, Symbol = "Ta", Weight = 180.9, Category = "transition" }},
            {"W", new Element { Number = 74, Symbol = "W", Weight = 183.8, Category = "transition" }},
            {"Re", new Element { Number = 75, Symbol = "Re", Weight = 186.2, Category = "transition" }},
            {"Os", new Element { Number = 76, Symbol = "Os", Weight = 190.2, Category = "transition" }},
            {"Ir", new Element { Number = 77, Symbol = "Ir", Weight = 192.2, Category = "transition" }},
            {"Pt", new Element { Number = 78, Symbol = "Pt", Weight = 195.1, Category = "transition" }},
            {"Au", new Element { Number = 79, Symbol = "Au", Weight = 197.0, Category = "transition" }},
            {"Hg", new Element { Number = 80, Symbol = "Hg", Weight = 200.6, Category = "transition" }},
            {"Tl", new Element { Number = 81, Symbol = "Tl", Weight = 204.4, Category = "post-transition" }},
            {"Pb", new Element { Number = 82, Symbol = "Pb", Weight = 207.2, Category = "post-transition" }},
            {"Bi", new Element { Number = 83, Symbol = "Bi", Weight = 209.0, Category = "post-transition" }},
            {"Po", new Element { Number = 84, Symbol = "Po", Weight = 209.0, Category = "metalloid" }},
            {"At", new Element { Number = 85, Symbol = "At", Weight = 210.0, Category = "halogen" }},
            {"Rn", new Element { Number = 86, Symbol = "Rn", Weight = 222.0, Category = "noble" }},
            {"Fr", new Element { Number = 87, Symbol = "Fr", Weight = 223.0, Category = "alkali" }},
            {"Ra", new Element { Number = 88, Symbol = "Ra", Weight = 226.0, Category = "alkaline" }},
            {"Ac", new Element { Number = 89, Symbol = "Ac", Weight = 227.0, Category = "actinide" }},
            {"Th", new Element { Number = 90, Symbol = "Th", Weight = 232.0, Category = "actinide" }},
            {"Pa", new Element { Number = 91, Symbol = "Pa", Weight = 231.0, Category = "actinide" }},
            {"U", new Element { Number = 92, Symbol = "U", Weight = 238.0, Category = "actinide" }},
            {"Np", new Element { Number = 93, Symbol = "Np", Weight = 237.0, Category = "actinide" }},
            {"Pu", new Element { Number = 94, Symbol = "Pu", Weight = 244.0, Category = "actinide" }},
            {"Am", new Element { Number = 95, Symbol = "Am", Weight = 243.0, Category = "actinide" }},
            {"Cm", new Element { Number = 96, Symbol = "Cm", Weight = 247.0, Category = "actinide" }},
            {"Bk", new Element { Number = 97, Symbol = "Bk", Weight = 247.0, Category = "actinide" }},
            {"Cf", new Element { Number = 98, Symbol = "Cf", Weight = 251.0, Category = "actinide" }},
            {"Es", new Element { Number = 99, Symbol = "Es", Weight = 252.0, Category = "actinide" }},
            {"Fm", new Element { Number = 100, Symbol = "Fm", Weight = 257.0, Category = "actinide" }},
            {"Md", new Element { Number = 101, Symbol = "Md", Weight = 258.0, Category = "actinide" }},
            {"No", new Element { Number = 102, Symbol = "No", Weight = 259.0, Category = "actinide" }},
            {"Lr", new Element { Number = 103, Symbol = "Lr", Weight = 262.0, Category = "actinide" }},
            {"Rf", new Element { Number = 104, Symbol = "Rf", Weight = 267.0, Category = "transition" }},
            {"Db", new Element { Number = 105, Symbol = "Db", Weight = 270.0, Category = "transition" }},
            {"Sg", new Element { Number = 106, Symbol = "Sg", Weight = 271.0, Category = "transition" }},
            {"Bh", new Element { Number = 107, Symbol = "Bh", Weight = 270.0, Category = "transition" }},
            {"Hs", new Element { Number = 108, Symbol = "Hs", Weight = 277.0, Category = "transition" }},
            {"Mt", new Element { Number = 109, Symbol = "Mt", Weight = 276.0, Category = "transition" }},
            {"Ds", new Element { Number = 110, Symbol = "Ds", Weight = 281.0, Category = "transition" }},
            {"Rg", new Element { Number = 111, Symbol = "Rg", Weight = 282.0, Category = "transition" }},
            {"Cn", new Element { Number = 112, Symbol = "Cn", Weight = 285.0, Category = "transition" }},
            {"Nh", new Element { Number = 113, Symbol = "Nh", Weight = 286.0, Category = "post-transition" }},
            {"Fl", new Element { Number = 114, Symbol = "Fl", Weight = 289.0, Category = "post-transition" }},
            {"Mc", new Element { Number = 115, Symbol = "Mc", Weight = 290.0, Category = "post-transition" }},
            {"Lv", new Element { Number = 116, Symbol = "Lv", Weight = 293.0, Category = "post-transition" }},
            {"Ts", new Element { Number = 117, Symbol = "Ts", Weight = 294.0, Category = "halogen" }},
            {"Og", new Element { Number = 118, Symbol = "Og", Weight = 294.0, Category = "noble" }}
        };

        // Periodic table layout
        private readonly string[][] tableLayout = new string[][]
        {
            new[] {"H", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "He"},
            new[] {"Li", "Be", "", "", "", "", "", "", "", "", "", "", "B", "C", "N", "O", "F", "Ne"},
            new[] {"Na", "Mg", "", "", "", "", "", "", "", "", "", "", "Al", "Si", "P", "S", "Cl", "Ar"},
            new[] {"K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn", "Ga", "Ge", "As", "Se", "Br", "Kr"},
            new[] {"Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", "Sb", "Te", "I", "Xe"},
            new[] {"Cs", "Ba", "La", "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", "Tl", "Pb", "Bi", "Po", "At", "Rn"},
            new[] {"Fr", "Ra", "Ac", "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds", "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og"},
            new[] {"", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""},
            new[] {"", "", "*", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu", ""},
            new[] {"", "", "*", "Th", "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr", ""}
        };

        // Category colors
        private readonly Dictionary<string, Color> categoryColors = new Dictionary<string, Color>
        {
            {"alkali", Color.FromRgb(255, 107, 107)},
            {"alkaline", Color.FromRgb(255, 165, 2)},
            {"transition", Color.FromRgb(255, 217, 61)},
            {"post-transition", Color.FromRgb(107, 203, 119)},
            {"metalloid", Color.FromRgb(77, 150, 255)},
            {"nonmetal", Color.FromRgb(166, 108, 255)},
            {"halogen", Color.FromRgb(0, 212, 255)},
            {"noble", Color.FromRgb(255, 107, 157)},
            {"lanthanide", Color.FromRgb(129, 212, 250)},
            {"actinide", Color.FromRgb(255, 171, 145)}
        };

        public ConcCal()
        {
            InitializeComponent();
            BuildPeriodicTable();
        }

        private void BuildPeriodicTable()
        {
            for (int row = 0; row < tableLayout.Length; row++)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };

                for (int col = 0; col < tableLayout[row].Length; col++)
                {
                    string cell = tableLayout[row][col];

                    if (string.IsNullOrEmpty(cell))
                    {
                        // Empty cell
                        var empty = new Border
                        {
                            Width = 48,
                            Height = 48,
                            Margin = new Thickness(1),
                            Background = Brushes.Transparent
                        };
                        rowPanel.Children.Add(empty);
                    }
                    else if (cell == "*")
                    {
                        // Label for lanthanides/actinides
                        var label = new Border
                        {
                            Width = 48,
                            Height = 48,
                            Margin = new Thickness(1),
                            Background = Brushes.Transparent
                        };
                        var text = new TextBlock
                        {
                            Text = row == 8 ? "57-71" : "89-103",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromRgb(144, 164, 174)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        label.Child = text;
                        rowPanel.Children.Add(label);
                    }
                    else if (elements.ContainsKey(cell))
                    {
                        // Element button
                        var el = elements[cell];
                        var btn = CreateElementButton(el);
                        rowPanel.Children.Add(btn);
                    }
                }

                PeriodicTablePanel.Children.Add(rowPanel);
            }
        }

        private Button CreateElementButton(Element el)
        {
            var color = categoryColors.ContainsKey(el.Category)
                ? categoryColors[el.Category]
                : Color.FromRgb(55, 71, 79);

            var foreground = (el.Category == "transition")
                ? Brushes.Black
                : Brushes.White;

            var btn = new Button
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(1),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = el.Symbol
            };

            var content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            content.Children.Add(new TextBlock
            {
                Text = el.Number.ToString(),
                FontSize = 8,
                Foreground = foreground,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            content.Children.Add(new TextBlock
            {
                Text = el.Symbol,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            content.Children.Add(new TextBlock
            {
                Text = el.Weight.ToString("F2"),
                FontSize = 7,
                Foreground = foreground,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            btn.Content = content;
            btn.Click += ElementButton_Click;

            // Apply style for hover effect
            btn.Style = CreateElementButtonStyle(color);

            return btn;
        }

        private Style CreateElementButtonStyle(Color baseColor)
        {
            var style = new Style(typeof(Button));

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(baseColor));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(contentPresenter);

            template.VisualTree = border;

            // Mouse over trigger
            var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(1.1, 1.1)));
            mouseOverTrigger.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5)));
            template.Triggers.Add(mouseOverTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
        }

        private void ElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string symbol)
            {
                FormulaInput.Text += symbol;
                FormulaInput.Focus();
                FormulaInput.CaretIndex = FormulaInput.Text.Length;
            }
        }

        private void FormulaInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Calculate();
            }
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            Calculate();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            FormulaInput.Text = "";
            ElementBreakdown.Text = "-";
            TotalWeight.Text = "-";
        }

        private void Calculate()
        {
            string formula = FormulaInput.Text.Trim();

            if (string.IsNullOrEmpty(formula))
            {
                ElementBreakdown.Text = "-";
                TotalWeight.Text = "-";
                return;
            }

            var parsed = ParseFormula(formula);

            if (parsed == null)
            {
                ElementBreakdown.Text = "Invalid formula";
                ElementBreakdown.Foreground = Brushes.Red;
                TotalWeight.Text = "-";
                return;
            }

            ElementBreakdown.Foreground = Brushes.Black;

            var breakdownLines = new List<string>();
            double totalWeight = 0;

            foreach (var kvp in parsed.OrderBy(x => elements[x.Key].Number))
            {
                var el = elements[kvp.Key];
                double weight = el.Weight * kvp.Value;
                totalWeight += weight;
                breakdownLines.Add($"{kvp.Key}: {kvp.Value} × {el.Weight:F3} = {weight:F3}");
            }

            ElementBreakdown.Text = string.Join("\n", breakdownLines);
            TotalWeight.Text = $"{totalWeight:F4} g/mol";
        }

        private Dictionary<string, int>? ParseFormula(string formula)
        {
            var result = new Dictionary<string, int>();

            // Match element symbols (uppercase followed by optional lowercase) and optional numbers
            var regex = new Regex(@"([A-Z][a-z]?)(\d*)");
            var matches = regex.Matches(formula);

            foreach (Match match in matches)
            {
                string symbol = match.Groups[1].Value;
                string countStr = match.Groups[2].Value;
                int count = string.IsNullOrEmpty(countStr) ? 1 : int.Parse(countStr);

                if (!elements.ContainsKey(symbol))
                {
                    return null; // Unknown element
                }

                if (result.ContainsKey(symbol))
                    result[symbol] += count;
                else
                    result[symbol] = count;
            }

            return result.Count > 0 ? result : null;
        }
    }
}
