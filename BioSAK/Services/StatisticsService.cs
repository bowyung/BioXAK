using System;
using System.Collections.Generic;
using System.Linq;

namespace BioSAK.Services
{
    /// <summary>
    /// 統計分析服務
    /// 提供各種生物統計分析所需的統計函數
    /// </summary>
    public static class StatisticsService
    {
        #region T-Tests

        /// <summary>
        /// Welch's t-test (不假設相等變異數)
        /// </summary>
        /// <param name="sample1">樣本1</param>
        /// <param name="sample2">樣本2</param>
        /// <returns>P值</returns>
        public static double WelchTTest(List<double> sample1, List<double> sample2)
        {
            if (sample1 == null || sample2 == null || sample1.Count < 2 || sample2.Count < 2)
                return 1.0;

            int n1 = sample1.Count;
            int n2 = sample2.Count;
            double mean1 = sample1.Average();
            double mean2 = sample2.Average();

            double var1 = sample1.Sum(x => (x - mean1) * (x - mean1)) / (n1 - 1);
            double var2 = sample2.Sum(x => (x - mean2) * (x - mean2)) / (n2 - 1);

            if (var1 + var2 == 0) return 1.0;

            // Welch's t-statistic
            double se = Math.Sqrt(var1 / n1 + var2 / n2);
            if (se == 0) return 1.0;

            double t = (mean1 - mean2) / se;

            // Welch-Satterthwaite 自由度
            double v1n = var1 / n1;
            double v2n = var2 / n2;
            double df = (v1n + v2n) * (v1n + v2n) /
                       (v1n * v1n / (n1 - 1) + v2n * v2n / (n2 - 1));

            // 計算雙尾 P 值
            return TDistributionPValue(Math.Abs(t), df) * 2;
        }

        /// <summary>
        /// Student's t-test (假設相等變異數)
        /// </summary>
        public static double StudentTTest(List<double> sample1, List<double> sample2)
        {
            if (sample1 == null || sample2 == null || sample1.Count < 2 || sample2.Count < 2)
                return 1.0;

            int n1 = sample1.Count;
            int n2 = sample2.Count;
            double mean1 = sample1.Average();
            double mean2 = sample2.Average();

            double ss1 = sample1.Sum(x => (x - mean1) * (x - mean1));
            double ss2 = sample2.Sum(x => (x - mean2) * (x - mean2));

            // Pooled variance
            double sp = Math.Sqrt((ss1 + ss2) / (n1 + n2 - 2));
            if (sp == 0) return 1.0;

            double t = (mean1 - mean2) / (sp * Math.Sqrt(1.0 / n1 + 1.0 / n2));
            double df = n1 + n2 - 2;

            return TDistributionPValue(Math.Abs(t), df) * 2;
        }

        #endregion

        #region Correlation

        /// <summary>
        /// Pearson 相關係數
        /// </summary>
        /// <param name="x">X 變數</param>
        /// <param name="y">Y 變數</param>
        /// <returns>(r, pValue, n)</returns>
        public static (double r, double pValue, int n) PearsonCorrelation(List<double> x, List<double> y)
        {
            if (x == null || y == null || x.Count != y.Count || x.Count < 3)
                return (0, 1.0, 0);

            int n = x.Count;
            double meanX = x.Average();
            double meanY = y.Average();

            double sumXY = 0, sumX2 = 0, sumY2 = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                sumXY += dx * dy;
                sumX2 += dx * dx;
                sumY2 += dy * dy;
            }

            if (sumX2 == 0 || sumY2 == 0)
                return (0, 1.0, n);

            double r = sumXY / Math.Sqrt(sumX2 * sumY2);

            // Fisher's z transformation for p-value
            if (Math.Abs(r) >= 1) return (r, r == 1 ? 0 : 1, n);

            double t = r * Math.Sqrt((n - 2) / (1 - r * r));
            double pValue = TDistributionPValue(Math.Abs(t), n - 2) * 2;

            return (r, pValue, n);
        }

        /// <summary>
        /// Spearman 秩相關係數
        /// </summary>
        public static (double rho, double pValue) SpearmanCorrelation(List<double> x, List<double> y)
        {
            if (x == null || y == null || x.Count != y.Count || x.Count < 3)
                return (0, 1.0);

            int n = x.Count;
            var rankX = GetRanks(x);
            var rankY = GetRanks(y);
            var result = PearsonCorrelation(rankX, rankY);
            return (result.r, result.pValue);
        }

        private static List<double> GetRanks(List<double> values)
        {
            var indexed = values.Select((v, i) => (Value: v, Index: i))
                               .OrderBy(x => x.Value)
                               .ToList();

            var ranks = new double[values.Count];
            int i = 0;
            while (i < indexed.Count)
            {
                int j = i;
                // 找出相同值的範圍
                while (j < indexed.Count && indexed[j].Value == indexed[i].Value)
                    j++;

                // 平均秩
                double avgRank = (i + j + 1) / 2.0;
                for (int k = i; k < j; k++)
                    ranks[indexed[k].Index] = avgRank;

                i = j;
            }

            return ranks.ToList();
        }

        #endregion

        #region Linear Regression

        /// <summary>
        /// 線性迴歸
        /// </summary>
        /// <returns>(slope, intercept, r2)</returns>
        public static (double slope, double intercept, double r2) LinearRegression(
            List<double> x, List<double> y)
        {
            if (x == null || y == null || x.Count != y.Count || x.Count < 3)
                return (0, 0, 0);

            int n = x.Count;
            double meanX = x.Average();
            double meanY = y.Average();

            double sumXY = 0, sumX2 = 0, sumY2 = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                sumXY += dx * dy;
                sumX2 += dx * dx;
                sumY2 += dy * dy;
            }

            if (sumX2 == 0)
                return (0, meanY, 0);

            double slope = sumXY / sumX2;
            double intercept = meanY - slope * meanX;

            double r = sumY2 == 0 ? 0 : sumXY / Math.Sqrt(sumX2 * sumY2);
            double r2 = r * r;

            return (slope, intercept, r2);
        }

        /// <summary>
        /// 線性迴歸 (完整版，含 P 值)
        /// </summary>
        /// <returns>(slope, intercept, r, r2, pValue)</returns>
        public static (double slope, double intercept, double r, double r2, double pValue) LinearRegressionFull(
            List<double> x, List<double> y)
        {
            if (x == null || y == null || x.Count != y.Count || x.Count < 3)
                return (0, 0, 0, 0, 1.0);

            int n = x.Count;
            double meanX = x.Average();
            double meanY = y.Average();

            double sumXY = 0, sumX2 = 0, sumY2 = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                sumXY += dx * dy;
                sumX2 += dx * dx;
                sumY2 += dy * dy;
            }

            if (sumX2 == 0)
                return (0, meanY, 0, 0, 1.0);

            double slope = sumXY / sumX2;
            double intercept = meanY - slope * meanX;

            double r = sumY2 == 0 ? 0 : sumXY / Math.Sqrt(sumX2 * sumY2);
            double r2 = r * r;

            // P-value for slope
            double residualSS = sumY2 - slope * sumXY;
            double se = Math.Sqrt(residualSS / (n - 2) / sumX2);
            if (se == 0) return (slope, intercept, r, r2, 0);

            double t = slope / se;
            double pValue = TDistributionPValue(Math.Abs(t), n - 2) * 2;

            return (slope, intercept, r, r2, pValue);
        }

        #endregion

        #region Multiple Testing Correction

        /// <summary>
        /// Benjamini-Hochberg FDR 校正
        /// </summary>
        /// <param name="pValues">原始 P 值列表</param>
        /// <returns>FDR 校正後的 Q 值列表</returns>
        public static List<double> BenjaminiHochbergFDR(List<double> pValues)
        {
            if (pValues == null || pValues.Count == 0)
                return new List<double>();

            int n = pValues.Count;
            var indexed = pValues.Select((p, i) => (PValue: p, Index: i))
                                .OrderBy(x => x.PValue)
                                .ToList();

            var qValues = new double[n];
            double minQ = 1.0;

            // 從最大到最小遍歷
            for (int i = n - 1; i >= 0; i--)
            {
                int rank = i + 1;
                double q = indexed[i].PValue * n / rank;
                q = Math.Min(q, minQ);
                minQ = q;
                qValues[indexed[i].Index] = Math.Min(q, 1.0);
            }

            return qValues.ToList();
        }

        /// <summary>
        /// Bonferroni 校正
        /// </summary>
        public static List<double> BonferroniCorrection(List<double> pValues)
        {
            if (pValues == null || pValues.Count == 0)
                return new List<double>();

            int n = pValues.Count;
            return pValues.Select(p => Math.Min(p * n, 1.0)).ToList();
        }

        #endregion

        #region Distribution Functions

        /// <summary>
        /// t 分佈的上尾機率 (單尾)
        /// </summary>
        private static double TDistributionPValue(double t, double df)
        {
            if (t <= 0) return 0.5;
            if (double.IsNaN(t) || double.IsInfinity(t)) return 0;
            if (df <= 0) return 1.0;

            // 使用 Beta 分佈關係
            // P(T > t) = 0.5 * I(df/(df+t²), df/2, 1/2)
            double x = df / (df + t * t);
            return 0.5 * RegularizedBeta(x, df / 2, 0.5);
        }

        /// <summary>
        /// 正則化不完全 Beta 函數
        /// </summary>
        private static double RegularizedBeta(double x, double a, double b)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;

            // 使用連分數展開
            double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) +
                                a * Math.Log(x) + b * Math.Log(1 - x));

            if (x < (a + 1) / (a + b + 2))
            {
                return bt * BetaCF(x, a, b) / a;
            }
            else
            {
                return 1.0 - bt * BetaCF(1 - x, b, a) / b;
            }
        }

        private static double BetaCF(double x, double a, double b)
        {
            const int maxIterations = 200;
            const double eps = 1e-15;

            double qab = a + b;
            double qap = a + 1;
            double qam = a - 1;
            double c = 1;
            double d = 1 - qab * x / qap;
            if (Math.Abs(d) < 1e-30) d = 1e-30;
            d = 1 / d;
            double h = d;

            for (int m = 1; m <= maxIterations; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1 + aa * d;
                if (Math.Abs(d) < 1e-30) d = 1e-30;
                c = 1 + aa / c;
                if (Math.Abs(c) < 1e-30) c = 1e-30;
                d = 1 / d;
                h *= d * c;

                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1 + aa * d;
                if (Math.Abs(d) < 1e-30) d = 1e-30;
                c = 1 + aa / c;
                if (Math.Abs(c) < 1e-30) c = 1e-30;
                d = 1 / d;
                double delta = d * c;
                h *= delta;

                if (Math.Abs(delta - 1) < eps) break;
            }

            return h;
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

        #endregion

        #region Descriptive Statistics

        /// <summary>
        /// 計算標準差
        /// </summary>
        public static double StandardDeviation(List<double> values)
        {
            if (values == null || values.Count < 2) return 0;

            double mean = values.Average();
            double sumSquares = values.Sum(x => (x - mean) * (x - mean));
            return Math.Sqrt(sumSquares / (values.Count - 1));
        }

        /// <summary>
        /// 計算標準誤差
        /// </summary>
        public static double StandardError(List<double> values)
        {
            if (values == null || values.Count < 2) return 0;
            return StandardDeviation(values) / Math.Sqrt(values.Count);
        }

        /// <summary>
        /// 計算四分位數
        /// </summary>
        public static (double Q1, double Median, double Q3) Quartiles(List<double> values)
        {
            if (values == null || values.Count == 0)
                return (0, 0, 0);

            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;

            double median = n % 2 == 0 ?
                (sorted[n / 2 - 1] + sorted[n / 2]) / 2 :
                sorted[n / 2];

            double q1 = sorted[(int)(n * 0.25)];
            double q3 = sorted[(int)(n * 0.75)];

            return (q1, median, q3);
        }

        /// <summary>
        /// 計算百分位數
        /// </summary>
        public static double Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0) return 0;
            if (percentile <= 0) return values.Min();
            if (percentile >= 100) return values.Max();

            var sorted = values.OrderBy(v => v).ToList();
            double index = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);

            if (lower == upper) return sorted[lower];

            return sorted[lower] + (index - lower) * (sorted[upper] - sorted[lower]);
        }

        #endregion
    }
}
