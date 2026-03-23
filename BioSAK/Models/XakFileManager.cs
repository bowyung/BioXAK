using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BioSAK
{
    /// <summary>
    /// Central manager for .xak file save/load operations.
    /// All modules call XakFileManager.Save() / XakFileManager.Open()
    /// and populate/consume XakDocument accordingly.
    /// </summary>
    public static class XakFileManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Show SaveFileDialog and serialize XakDocument to .xak (JSON).
        /// Returns the saved file path, or null if cancelled.
        /// </summary>
        public static string? Save(XakDocument doc, string suggestedName = "")
        {
            if (string.IsNullOrEmpty(suggestedName))
                suggestedName = $"bioxak_{doc.Module.ToLower()}";

            doc.SavedAt = DateTime.Now.ToString("o");

            var dlg = new SaveFileDialog
            {
                Filter = "BioXAK File (*.xak)|*.xak",
                DefaultExt = ".xak",
                FileName = suggestedName
            };

            if (dlg.ShowDialog() != true) return null;

            try
            {
                string json = JsonSerializer.Serialize(doc, JsonOptions);
                File.WriteAllText(dlg.FileName, json, Encoding.UTF8);

                int dataSize = json.Length;
                string sizeStr = dataSize > 1_048_576
                    ? $"{dataSize / 1_048_576.0:F1} MB"
                    : $"{dataSize / 1024.0:F0} KB";

                MessageBox.Show(
                    $"Saved successfully!\n{dlg.FileName}\n\n" +
                    $"Module: {doc.Module}\n" +
                    $"Size: {sizeStr}",
                    "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                return dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save .xak file:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Show OpenFileDialog and deserialize .xak file.
        /// Returns the XakDocument, or null if cancelled/failed.
        /// Optionally filter by expected module name.
        /// </summary>
        public static XakDocument? Open(string? expectedModule = null)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "BioXAK File (*.xak)|*.xak|All Files (*.*)|*.*",
                DefaultExt = ".xak"
            };

            if (dlg.ShowDialog() != true) return null;

            return LoadFromPath(dlg.FileName, expectedModule);
        }

        /// <summary>
        /// Load .xak from a specific file path (no dialog).
        /// </summary>
        public static XakDocument? LoadFromPath(string filePath, string? expectedModule = null)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                var doc = JsonSerializer.Deserialize<XakDocument>(json, JsonOptions);

                if (doc == null)
                {
                    MessageBox.Show("Invalid .xak file format.",
                        "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                // Version check
                if (doc.Version != "2.0" && doc.Version != "1.0")
                {
                    var result = MessageBox.Show(
                        $"This file was saved with version {doc.Version}.\n" +
                        $"Current version is 2.0. Some data may not load correctly.\n\nContinue?",
                        "Version Mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return null;
                }

                // Module check
                if (expectedModule != null && doc.Module != expectedModule)
                {
                    var result = MessageBox.Show(
                        $"This file was saved from \"{doc.Module}\" module,\n" +
                        $"but you are opening it in \"{expectedModule}\".\n\n" +
                        $"The data may not be compatible. Continue?",
                        "Module Mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return null;
                }

                MessageBox.Show(
                    $"Loaded successfully!\n{Path.GetFileName(filePath)}\n\n" +
                    $"Module: {doc.Module}\n" +
                    $"Saved: {doc.SavedAt}",
                    "Load Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                return doc;
            }
            catch (JsonException)
            {
                // Try loading as v1.0 format (backward compatibility)
                return TryLoadV1(filePath, expectedModule);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load .xak file:\n{ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Backward compatibility: try to load v1.0 format (GraphGen only)
        /// and convert to v2.0 XakDocument.
        /// </summary>
        private static XakDocument? TryLoadV1(string filePath, string? expectedModule)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                var v1Options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var v1 = JsonSerializer.Deserialize<GraphGenData>(json, v1Options);

                if (v1 == null || v1.Data == null) return null;

                // Check if it looks like a v1 GraphGen file
                if (v1.ChartType == null && v1.YColumns == 0) return null;

                var doc = new XakDocument
                {
                    Version = "2.0",
                    Module = "GraphGen",
                    SavedAt = "",
                    Description = "Converted from v1.0 format",
                    GraphGen = v1
                };

                if (expectedModule != null && expectedModule != "GraphGen")
                {
                    MessageBox.Show("This is a v1.0 GraphGen file and cannot be loaded in this module.",
                        "Incompatible", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                MessageBox.Show(
                    $"Loaded v1.0 file (auto-converted to v2.0)\n{Path.GetFileName(filePath)}",
                    "Load Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                return doc;
            }
            catch
            {
                MessageBox.Show("The selected file is not a valid .xak file.",
                    "Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  IMAGE HELPERS (for WB module)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Convert BitmapImage to Base64 PNG string.
        /// </summary>
        public static string ImageToBase64(BitmapImage image)
        {
            if (image == null) return "";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// Convert Base64 PNG string back to BitmapImage.
        /// </summary>
        public static BitmapImage? Base64ToImage(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  COLOR HELPERS
        // ════════════════════════════════════════════════════════════

        /// <summary>Convert Color to hex string "#RRGGBB"</summary>
        public static string ColorToHex(System.Windows.Media.Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>Parse hex string "#RRGGBB" to Color</summary>
        public static System.Windows.Media.Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) return System.Windows.Media.Colors.Black;
            try
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return System.Windows.Media.Colors.Black;
            }
        }
    }
}