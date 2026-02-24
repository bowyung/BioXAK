using System;
using System.Collections.Generic;
using System.Linq;

namespace BioSAK.Services
{
    /// <summary>
    /// Kaplan-Meier 存活分析服務
    /// 提供 KM 曲線計算、統計量計算和 Log-Rank 檢定
    /// </summary>
    public static class KaplanMeierService
    {
        /// <summary>
        /// 計算 Kaplan-Meier 存活曲線
        /// </summary>
        /// <param name="data">存活資料列表 (SurvivalDays, IsEvent)</param>
        /// <returns>KM 曲線點列表</returns>
        public static List<KMPoint> CalculateSurvivalCurve(List<(int SurvivalDays, bool IsEvent)> data)
        {
            if (data == null || data.Count == 0)
                return new List<KMPoint>();

            // 按時間排序
            var sorted = data.OrderBy(d => d.SurvivalDays).ToList();
            var curve = new List<KMPoint>();

            // 初始點 (t=0, S=1.0)
            curve.Add(new KMPoint
            {
                Time = 0,
                Survival = 1.0,
                AtRisk = sorted.Count,
                Events = 0,
                Censored = 0
            });

            double survival = 1.0;
            int atRisk = sorted.Count;
            int i = 0;

            while (i < sorted.Count)
            {
                int currentTime = sorted[i].SurvivalDays;
                int events = 0;
                int censored = 0;

                // 計算同一時間點的事件數和截斷數
                while (i < sorted.Count && sorted[i].SurvivalDays == currentTime)
                {
                    if (sorted[i].IsEvent)
                        events++;
                    else
                        censored++;
                    i++;
                }

                // 如果有事件發生，更新存活率
                if (events > 0)
                {
                    survival *= (double)(atRisk - events) / atRisk;
                }

                curve.Add(new KMPoint
                {
                    Time = currentTime,
                    Survival = survival,
                    AtRisk = atRisk,
                    Events = events,
                    Censored = censored
                });

                atRisk -= (events + censored);
            }

            return curve;
        }

        /// <summary>
        /// 計算中位存活時間 (50% 存活率對應的時間)
        /// </summary>
        public static int? CalculateMedianSurvival(List<KMPoint> curve)
        {
            if (curve == null || curve.Count < 2)
                return null;

            for (int i = 1; i < curve.Count; i++)
            {
                if (curve[i].Survival <= 0.5)
                {
                    // 返回第一個存活率 <= 50% 的時間點
                    return curve[i].Time;
                }
            }

            // 如果存活率始終 > 50%，返回 null (表示中位數未達到)
            return null;
        }

        /// <summary>
        /// 計算平均存活時間 (受限平均存活時間 - RMST)
        /// 使用曲線下面積計算
        /// </summary>
        public static double CalculateMeanSurvival(List<KMPoint> curve)
        {
            if (curve == null || curve.Count < 2)
                return 0;

            double area = 0;
            for (int i = 1; i < curve.Count; i++)
            {
                // 矩形面積 (上一個存活率 × 時間間隔)
                double width = curve[i].Time - curve[i - 1].Time;
                double height = curve[i - 1].Survival;
                area += width * height;
            }

            return area;
        }

        /// <summary>
        /// Log-Rank 檢定 (比較兩組存活曲線)
        /// </summary>
        /// <param name="group1">第一組資料</param>
        /// <param name="group2">第二組資料</param>
        /// <returns>(卡方值, P值)</returns>
        public static (double ChiSquare, double PValue) LogRankTest(
            List<(int SurvivalDays, bool IsEvent)> group1,
            List<(int SurvivalDays, bool IsEvent)> group2)
        {
            if (group1 == null || group2 == null || group1.Count < 2 || group2.Count < 2)
                return (0, 1.0);

            // 合併所有獨特的事件時間點
            var allTimes = group1.Where(g => g.IsEvent).Select(g => g.SurvivalDays)
                .Union(group2.Where(g => g.IsEvent).Select(g => g.SurvivalDays))
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (allTimes.Count == 0)
                return (0, 1.0);

            double O1 = 0; // 觀察事件數 (組1)
            double E1 = 0; // 期望事件數 (組1)
            double V = 0;  // 變異數

            // 初始化風險人數
            var group1Sorted = group1.OrderBy(g => g.SurvivalDays).ToList();
            var group2Sorted = group2.OrderBy(g => g.SurvivalDays).ToList();

            int n1 = group1.Count;  // 組1 風險人數
            int n2 = group2.Count;  // 組2 風險人數
            int idx1 = 0, idx2 = 0;

            foreach (var t in allTimes)
            {
                // 計算此時間點之前離開的人數
                while (idx1 < group1Sorted.Count && group1Sorted[idx1].SurvivalDays < t)
                {
                    n1--;
                    idx1++;
                }
                while (idx2 < group2Sorted.Count && group2Sorted[idx2].SurvivalDays < t)
                {
                    n2--;
                    idx2++;
                }

                if (n1 + n2 <= 1) continue;

                // 計算此時間點的事件數
                int d1 = group1.Count(g => g.SurvivalDays == t && g.IsEvent);
                int d2 = group2.Count(g => g.SurvivalDays == t && g.IsEvent);
                int d = d1 + d2;

                if (d == 0) continue;

                int N = n1 + n2;

                // 觀察值和期望值
                O1 += d1;
                E1 += (double)n1 * d / N;

                // 變異數 (超幾何分佈變異數)
                if (N > 1)
                {
                    V += (double)n1 * n2 * d * (N - d) / (N * N * (N - 1));
                }

                // 更新風險人數 (扣除此時間點的事件和截斷)
                while (idx1 < group1Sorted.Count && group1Sorted[idx1].SurvivalDays == t)
                {
                    n1--;
                    idx1++;
                }
                while (idx2 < group2Sorted.Count && group2Sorted[idx2].SurvivalDays == t)
                {
                    n2--;
                    idx2++;
                }
            }

            if (V <= 0)
                return (0, 1.0);

            // 卡方統計量
            double chiSquare = (O1 - E1) * (O1 - E1) / V;

            // P值 (卡方分佈, 自由度=1)
            double pValue = ChiSquaredPValue(chiSquare, 1);

            return (chiSquare, pValue);
        }

        /// <summary>
        /// 卡方分佈 P 值計算 (使用近似公式)
        /// </summary>
        private static double ChiSquaredPValue(double chiSquare, int df)
        {
            if (chiSquare <= 0) return 1.0;
            if (double.IsNaN(chiSquare) || double.IsInfinity(chiSquare)) return 1.0;

            // 使用正則化不完全 Gamma 函數
            // P(χ² > x) = 1 - P(χ² ≤ x) = 1 - γ(df/2, x/2) / Γ(df/2)
            double a = df / 2.0;
            double x = chiSquare / 2.0;

            return 1.0 - RegularizedGammaP(a, x);
        }

        /// <summary>
        /// 正則化不完全 Gamma 函數 P(a, x) = γ(a, x) / Γ(a)
        /// </summary>
        private static double RegularizedGammaP(double a, double x)
        {
            if (x < 0 || a <= 0) return 0;
            if (x == 0) return 0;
            if (x < a + 1)
            {
                // 使用級數展開
                return GammaPSeries(a, x);
            }
            else
            {
                // 使用連分數
                return 1.0 - GammaQCF(a, x);
            }
        }

        private static double GammaPSeries(double a, double x)
        {
            double sum = 1.0 / a;
            double term = sum;
            for (int n = 1; n < 200; n++)
            {
                term *= x / (a + n);
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        private static double GammaQCF(double a, double x)
        {
            double b = x + 1 - a;
            double c = 1.0 / 1e-30;
            double d = 1.0 / b;
            double h = d;
            for (int i = 1; i < 200; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = an * d + b;
                if (Math.Abs(d) < 1e-30) d = 1e-30;
                c = b + an / c;
                if (Math.Abs(c) < 1e-30) c = 1e-30;
                d = 1.0 / d;
                double delta = d * c;
                h *= delta;
                if (Math.Abs(delta - 1) < 1e-15) break;
            }
            return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }

        private static double LogGamma(double x)
        {
            // Lanczos approximation
            double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                          -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
            double y = x;
            double tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++)
            {
                y += 1;
                ser += c[j] / y;
            }
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }
    }

    #region Kaplan-Meier Models

    /// <summary>
    /// KM 曲線上的一個點
    /// </summary>
    public class KMPoint
    {
        public int Time { get; set; }
        public double Survival { get; set; }
        public int AtRisk { get; set; }
        public int Events { get; set; }
        public int Censored { get; set; }

        public double TimeMonths => Time / 30.44;
        public double TimeYears => Time / 365.25;
        public double SurvivalPercent => Survival * 100;
    }

    /// <summary>
    /// 單組 KM 分析結果
    /// </summary>
    public class KMAnalysisResult
    {
        public string GroupName { get; set; }
        public List<KMPoint> Curve { get; set; }
        public int TotalSamples { get; set; }
        public int Events { get; set; }
        public int Censored { get; set; }
        public int? MedianSurvival { get; set; }  // days
        public double MeanSurvival { get; set; }  // days

        public double? MedianSurvivalMonths => MedianSurvival.HasValue ? MedianSurvival.Value / 30.44 : null;
        public double MeanSurvivalMonths => MeanSurvival / 30.44;
    }

    /// <summary>
    /// 高表現 vs 低表現的 KM 比較結果
    /// </summary>
    public class KMComparisonResult
    {
        public KMAnalysisResult LowExpression { get; set; }
        public KMAnalysisResult HighExpression { get; set; }
        public double LogRankChiSquare { get; set; }
        public double LogRankPValue { get; set; }
        public double CutoffPercentile { get; set; }
        public double CutoffValue { get; set; }

        public string PValueDisplay => LogRankPValue < 0.001 ? $"{LogRankPValue:E2}" : $"{LogRankPValue:F4}";
        public bool IsSignificant => LogRankPValue < 0.05;
    }

    #endregion
}
