using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BioSAK.Models;

namespace BioSAK.Controls
{
    /// <summary>
    /// 電泳圖控件
    /// </summary>
    public partial class ElectrophoresisGelControl : UserControl
    {
        // Marker 類型
        public enum MarkerType
        {
            Standard,    // 10K, 9K, 8K, 7K, 6K, 5K, 4K, 3K, 2K, 1K, 500, 250
            HighResolution  // 10K, 5K, 3K, 1K, 900, 800, 700, 600, 500, 400, 300, 200, 100
        }

        // 標準 Marker 大小 (bp)
        private static readonly int[] StandardMarkerSizes = 
            { 10000, 9000, 8000, 7000, 6000, 5000, 4000, 3000, 2000, 1000, 500, 250 };

        // 高解析度 Marker 大小 (bp)
        private static readonly int[] HighResMarkerSizes = 
            { 10000, 5000, 3000, 1000, 900, 800, 700, 600, 500, 400, 300, 200, 100 };

        private MarkerType _currentMarkerType = MarkerType.Standard;
        private List<DnaFragment> _fragments = new List<DnaFragment>();

        // 電泳參數
        private const double MinBandY = 30;  // 頂部留白（給 >10K）
        private const double MaxBandY = 0.95; // 底部位置比例

        public ElectrophoresisGelControl()
        {
            InitializeComponent();
            this.Loaded += ElectrophoresisGelControl_Loaded;
            this.SizeChanged += ElectrophoresisGelControl_SizeChanged;
        }

        private void ElectrophoresisGelControl_Loaded(object sender, RoutedEventArgs e)
        {
            DrawMarkerLane();
        }

        private void ElectrophoresisGelControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawMarkerLane();
            DrawSampleLane();
        }

        /// <summary>
        /// 設定 Marker 類型
        /// </summary>
        public void SetMarkerType(MarkerType type)
        {
            _currentMarkerType = type;
            DrawMarkerLane();
            DrawSampleLane();
        }

        /// <summary>
        /// 設定要顯示的 DNA 片段
        /// </summary>
        public void SetFragments(List<DnaFragment> fragments)
        {
            _fragments = fragments ?? new List<DnaFragment>();
            DrawSampleLane();
        }

        /// <summary>
        /// 計算 DNA 大小對應的 Y 位置
        /// 使用對數刻度模擬電泳遷移率
        /// </summary>
        private double CalculateYPosition(int sizeInBp, double canvasHeight)
        {
            // 電泳中 DNA 遷移率與 log(分子量) 成反比
            // 較大的片段在上方（遷移較慢），較小的片段在下方（遷移較快）
            
            int[] markerSizes = _currentMarkerType == MarkerType.Standard 
                ? StandardMarkerSizes 
                : HighResMarkerSizes;

            double maxSize = markerSizes.Max();
            double minSize = markerSizes.Min();

            // 處理超過範圍的片段
            if (sizeInBp > maxSize)
                sizeInBp = (int)maxSize;
            if (sizeInBp < minSize)
                sizeInBp = (int)minSize;

            // 對數轉換
            double logMax = Math.Log10(maxSize);
            double logMin = Math.Log10(minSize);
            double logSize = Math.Log10(sizeInBp);

            // 計算相對位置 (0 = 頂部大片段, 1 = 底部小片段)
            double relativePosition = (logMax - logSize) / (logMax - logMin);

            // 轉換為 Canvas Y 座標
            double usableHeight = canvasHeight * MaxBandY - MinBandY;
            return MinBandY + (relativePosition * usableHeight);
        }

        /// <summary>
        /// 繪製 Marker Lane
        /// </summary>
        private void DrawMarkerLane()
        {
            MarkerCanvas.Children.Clear();

            double canvasHeight = MarkerCanvas.ActualHeight;
            double canvasWidth = MarkerCanvas.ActualWidth;

            if (canvasHeight <= 0 || canvasWidth <= 0)
                return;

            int[] markerSizes = _currentMarkerType == MarkerType.Standard 
                ? StandardMarkerSizes 
                : HighResMarkerSizes;

            foreach (int size in markerSizes)
            {
                double y = CalculateYPosition(size, canvasHeight);

                // 繪製 Band
                var band = new Border
                {
                    Width = canvasWidth - 20,
                    Height = 3,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0, 255, 100),
                        Color.FromRgb(0, 200, 80),
                        0),
                    CornerRadius = new CornerRadius(1),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0, 255, 100),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.6
                    }
                };

                Canvas.SetLeft(band, 10);
                Canvas.SetTop(band, y);
                MarkerCanvas.Children.Add(band);

                // 繪製大小標籤
                string label = size >= 1000 ? $"{size / 1000}K" : size.ToString();
                var textBlock = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontFamily = new FontFamily("Consolas")
                };

                Canvas.SetRight(textBlock, 5);
                Canvas.SetTop(textBlock, y - 6);
                MarkerCanvas.Children.Add(textBlock);
            }
        }

        /// <summary>
        /// 繪製 Sample Lane
        /// </summary>
        private void DrawSampleLane()
        {
            SampleCanvas.Children.Clear();

            double canvasHeight = SampleCanvas.ActualHeight;
            double canvasWidth = SampleCanvas.ActualWidth;

            if (canvasHeight <= 0 || canvasWidth <= 0 || _fragments.Count == 0)
                return;

            int[] markerSizes = _currentMarkerType == MarkerType.Standard 
                ? StandardMarkerSizes 
                : HighResMarkerSizes;

            double maxSize = markerSizes.Max();
            double minSize = markerSizes.Min();

            foreach (var fragment in _fragments)
            {
                double y;
                bool isOutOfRange = false;

                if (fragment.Size > maxSize)
                {
                    // 超過 10K 的片段，放在最頂端
                    y = 10;
                    isOutOfRange = true;
                }
                else if (fragment.Size < minSize)
                {
                    // 太小的片段，放在最底端
                    y = canvasHeight * MaxBandY;
                    isOutOfRange = true;
                }
                else
                {
                    y = CalculateYPosition(fragment.Size, canvasHeight);
                }

                // 計算 Band 強度（可根據片段大小調整）
                double intensity = Math.Min(1.0, Math.Max(0.3, Math.Log10(fragment.Size) / 4.0));

                // 繪製 Band
                Color bandColor = isOutOfRange 
                    ? Color.FromRgb(255, 150, 0)  // 橘色表示超出範圍
                    : Color.FromRgb(0, 255, 100);

                var band = new Border
                {
                    Width = canvasWidth - 20,
                    Height = 4,
                    Background = new LinearGradientBrush(
                        bandColor,
                        Color.FromArgb((byte)(255 * intensity), bandColor.R, bandColor.G, bandColor.B),
                        0),
                    CornerRadius = new CornerRadius(1),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = bandColor,
                        BlurRadius = 10,
                        ShadowDepth = 0,
                        Opacity = 0.7
                    },
                    ToolTip = $"{fragment.Size} bp"
                };

                Canvas.SetLeft(band, 10);
                Canvas.SetTop(band, y);
                SampleCanvas.Children.Add(band);

                // 繪製大小標籤
                string label = fragment.Size >= 1000 
                    ? $"{fragment.Size / 1000.0:F1}K" 
                    : fragment.Size.ToString();
                
                var textBlock = new TextBlock
                {
                    Text = label,
                    Foreground = isOutOfRange ? Brushes.Orange : Brushes.LightGreen,
                    FontSize = 8,
                    FontFamily = new FontFamily("Consolas")
                };

                Canvas.SetLeft(textBlock, 5);
                Canvas.SetTop(textBlock, y - 10);
                SampleCanvas.Children.Add(textBlock);
            }
        }

        /// <summary>
        /// 清除所有片段
        /// </summary>
        public void Clear()
        {
            _fragments.Clear();
            SampleCanvas.Children.Clear();
        }
    }
}
