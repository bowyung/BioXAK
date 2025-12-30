using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BioSAK
{
    public partial class MWCal : Page
    {
        public MWCal()
        {
            InitializeComponent();
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow digits, decimal point, and scientific notation
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != 'e' && c != 'E' && c != '-' && c != '+')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Parse values (empty string = null)
                double? mw = ParseDouble(txtMW.Text);
                double? volume = ParseDouble(txtVolume.Text);
                double? mass = ParseDouble(txtMass.Text);
                double? concentration = ParseDouble(txtConcentration.Text);

                // Get units
                string volumeUnit = ((ComboBoxItem)cboVolumeUnit.SelectedItem).Content.ToString()!;
                string massUnit = ((ComboBoxItem)cboMassUnit.SelectedItem).Content.ToString()!;
                string concUnit = ((ComboBoxItem)cboConcUnit.SelectedItem).Content.ToString()!;

                // Count empty fields
                int emptyCount = 0;
                string emptyField = "";

                if (!mw.HasValue) { emptyCount++; emptyField = "M.W."; }
                if (!volume.HasValue) { emptyCount++; emptyField = "Volume"; }
                if (!mass.HasValue) { emptyCount++; emptyField = "Mass"; }
                if (!concentration.HasValue) { emptyCount++; emptyField = "Concentration"; }

                if (emptyCount == 0)
                {
                    // All fields filled - calculate and display concentration
                    double result = CalculateConcentration(mw!.Value, volume!.Value, volumeUnit, mass!.Value, massUnit, concUnit);
                    txtConcentration.Text = FormatResult(result);
                    txtResult.Text = $"Concentration = {FormatResult(result)} {concUnit}";
                    SetResultStyle(true);
                }
                else if (emptyCount == 1)
                {
                    // One field empty - calculate it
                    double result = 0;
                    string resultUnit = "";

                    if (!mw.HasValue)
                    {
                        // Calculate M.W. - only possible for molar concentrations
                        if (concUnit == "w/v%")
                        {
                            txtResult.Text = "Cannot calculate M.W. from w/v% concentration";
                            SetResultStyle(false);
                            return;
                        }
                        result = CalculateMW(volume!.Value, volumeUnit, mass!.Value, massUnit, concentration!.Value, concUnit);
                        txtMW.Text = FormatResult(result);
                        resultUnit = "g/mol";
                        emptyField = "M.W.";
                    }
                    else if (!volume.HasValue)
                    {
                        result = CalculateVolume(mw!.Value, mass!.Value, massUnit, concentration!.Value, concUnit);
                        // Convert result (in base unit) to selected unit
                        double displayResult = ConvertVolumeFromBase(result, volumeUnit);
                        txtVolume.Text = FormatResult(displayResult);
                        result = displayResult;
                        resultUnit = volumeUnit;
                        emptyField = "Volume";
                    }
                    else if (!mass.HasValue)
                    {
                        result = CalculateMass(mw!.Value, volume!.Value, volumeUnit, concentration!.Value, concUnit);
                        // Convert result (in base unit) to selected unit
                        double displayResult = ConvertMassFromBase(result, massUnit);
                        txtMass.Text = FormatResult(displayResult);
                        result = displayResult;
                        resultUnit = massUnit;
                        emptyField = "Mass";
                    }
                    else if (!concentration.HasValue)
                    {
                        result = CalculateConcentration(mw!.Value, volume!.Value, volumeUnit, mass!.Value, massUnit, concUnit);
                        txtConcentration.Text = FormatResult(result);
                        resultUnit = concUnit;
                        emptyField = "Concentration";
                    }

                    txtResult.Text = $"{emptyField} = {FormatResult(result)} {resultUnit}";
                    SetResultStyle(true);
                }
                else
                {
                    txtResult.Text = $"Please fill in at least 3 fields (currently {4 - emptyCount} filled)";
                    SetResultStyle(false);
                }
            }
            catch (Exception ex)
            {
                txtResult.Text = $"Error: {ex.Message}";
                SetResultStyle(false);
            }
        }

        private double? ParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (double.TryParse(text, out double result))
                return result;
            return null;
        }

        private string FormatResult(double value)
        {
            if (Math.Abs(value) < 0.001 || Math.Abs(value) >= 1000000)
                return value.ToString("E4");
            else if (Math.Abs(value) < 1)
                return value.ToString("F6").TrimEnd('0').TrimEnd('.');
            else
                return value.ToString("F4").TrimEnd('0').TrimEnd('.');
        }

        #region Unit Conversions

        // Convert volume to nL
        private double ConvertVolumeToNL(double value, string unit)
        {
            return unit switch
            {
                "L" => value * 1e9,
                "mL" => value * 1e6,
                "μL" => value * 1e3,
                "nL" => value,
                _ => value
            };
        }

        // Convert volume to L (base unit for calculations)
        private double ConvertVolumeToL(double value, string unit)
        {
            return unit switch
            {
                "L" => value,
                "mL" => value * 1e-3,
                "μL" => value * 1e-6,
                "nL" => value * 1e-9,
                _ => value
            };
        }

        // Convert from L to target unit
        private double ConvertVolumeFromBase(double valueInL, string targetUnit)
        {
            return targetUnit switch
            {
                "L" => valueInL,
                "mL" => valueInL * 1e3,
                "μL" => valueInL * 1e6,
                "nL" => valueInL * 1e9,
                _ => valueInL
            };
        }

        // Convert mass to pg
        private double ConvertMassToPg(double value, string unit)
        {
            return unit switch
            {
                "g" => value * 1e12,
                "mg" => value * 1e9,
                "μg" => value * 1e6,
                "ng" => value * 1e3,
                "pg" => value,
                _ => value
            };
        }

        // Convert mass to g (base unit for calculations)
        private double ConvertMassToG(double value, string unit)
        {
            return unit switch
            {
                "g" => value,
                "mg" => value * 1e-3,
                "μg" => value * 1e-6,
                "ng" => value * 1e-9,
                "pg" => value * 1e-12,
                _ => value
            };
        }

        // Convert from g to target unit
        private double ConvertMassFromBase(double valueInG, string targetUnit)
        {
            return targetUnit switch
            {
                "g" => valueInG,
                "mg" => valueInG * 1e3,
                "μg" => valueInG * 1e6,
                "ng" => valueInG * 1e9,
                "pg" => valueInG * 1e12,
                _ => valueInG
            };
        }

        // Convert concentration from mM to target unit
        private double ConvertConcentrationFromMM(double mM, string targetUnit)
        {
            return targetUnit switch
            {
                "M" => mM / 1000,
                "mM" => mM,
                "μM" => mM * 1000,
                "nM" => mM * 1e6,
                _ => mM
            };
        }

        // Convert concentration to M (base unit)
        private double ConvertConcentrationToM(double value, string unit)
        {
            return unit switch
            {
                "M" => value,
                "mM" => value * 1e-3,
                "μM" => value * 1e-6,
                "nM" => value * 1e-9,
                _ => value
            };
        }

        #endregion

        #region Calculation Methods

        /// <summary>
        /// Calculate concentration given M.W., volume, and mass
        /// </summary>
        private double CalculateConcentration(double mw, double volume, string volumeUnit,
            double mass, string massUnit, string concUnit)
        {
            if (concUnit == "w/v%")
            {
                // w/v% = (mass in g / volume in mL) * 100
                double massG = ConvertMassToG(mass, massUnit);
                double volumeML = ConvertVolumeToL(volume, volumeUnit) * 1000; // L to mL
                return (massG / volumeML) * 100;
            }
            else
            {
                // Molar concentration
                // Convert to pg and nL, then calculate mM
                double massPg = ConvertMassToPg(mass, massUnit);
                double volumeNL = ConvertVolumeToNL(volume, volumeUnit);

                // pg / (g/mol) / nL = pmol / nL = mM
                double mM = massPg / mw / volumeNL;

                return ConvertConcentrationFromMM(mM, concUnit);
            }
        }

        /// <summary>
        /// Calculate mass given M.W., volume, and concentration
        /// Returns mass in grams (base unit)
        /// </summary>
        private double CalculateMass(double mw, double volume, string volumeUnit,
            double concentration, string concUnit)
        {
            if (concUnit == "w/v%")
            {
                // mass (g) = w/v% * volume (mL) / 100
                double volumeML = ConvertVolumeToL(volume, volumeUnit) * 1000;
                return concentration * volumeML / 100;
            }
            else
            {
                // mass = concentration (M) * M.W. (g/mol) * volume (L)
                double concM = ConvertConcentrationToM(concentration, concUnit);
                double volumeL = ConvertVolumeToL(volume, volumeUnit);
                return concM * mw * volumeL;
            }
        }

        /// <summary>
        /// Calculate volume given M.W., mass, and concentration
        /// Returns volume in L (base unit)
        /// </summary>
        private double CalculateVolume(double mw, double mass, string massUnit,
            double concentration, string concUnit)
        {
            if (concUnit == "w/v%")
            {
                // volume (mL) = mass (g) * 100 / w/v%
                double massG = ConvertMassToG(mass, massUnit);
                double volumeML = massG * 100 / concentration;
                return volumeML / 1000; // Convert to L
            }
            else
            {
                // volume (L) = mass (g) / (concentration (M) * M.W. (g/mol))
                double massG = ConvertMassToG(mass, massUnit);
                double concM = ConvertConcentrationToM(concentration, concUnit);
                return massG / (concM * mw);
            }
        }

        /// <summary>
        /// Calculate M.W. given volume, mass, and concentration
        /// Only valid for molar concentrations
        /// </summary>
        private double CalculateMW(double volume, string volumeUnit, double mass, string massUnit,
            double concentration, string concUnit)
        {
            // M.W. = mass (g) / (concentration (M) * volume (L))
            double massG = ConvertMassToG(mass, massUnit);
            double concM = ConvertConcentrationToM(concentration, concUnit);
            double volumeL = ConvertVolumeToL(volume, volumeUnit);
            return massG / (concM * volumeL);
        }

        #endregion

        private void SetResultStyle(bool success)
        {
            if (success)
            {
                // 直接使用變數名稱 ResultBorder，而不是用 txtResult.Parent 去抓
                ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(232, 245, 233)); // #E8F5E9
                ResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80)); // #4CAF50
                txtResult.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(46, 125, 50)); // #2E7D32
            }
            else
            {
                ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 235, 238)); // #FFEBEE
                ResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(244, 67, 54)); // #F44336
                txtResult.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(198, 40, 40)); // #C62828
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtMW.Text = "";
            txtVolume.Text = "";
            txtMass.Text = "";
            txtConcentration.Text = "";
            txtResult.Text = "Enter values and click Calculate";
            SetResultStyle(true);
        }
    }
}
