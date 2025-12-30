using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BioSAK.Models;

namespace BioSAK.Services
{
    /// <summary>
    /// 限制酶切割服務
    /// </summary>
    public class RestrictionEnzymeCutter
    {
        /// <summary>
        /// 尋找所有切割位點
        /// </summary>
        /// <param name="sequence">DNA 序列</param>
        /// <param name="enzyme">限制酶</param>
        /// <param name="isCircular">是否為環狀 DNA</param>
        /// <returns>切割位點列表</returns>
        public List<CutSite> FindCutSites(string sequence, RestrictionEnzyme enzyme, bool isCircular = false)
        {
            var cutSites = new List<CutSite>();
            string cleanSequence = CleanSequence(sequence);
            
            if (string.IsNullOrEmpty(cleanSequence))
                return cutSites;

            // 搜尋正向股
            string pattern = enzyme.GetRegexPattern();
            var matches = Regex.Matches(cleanSequence, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                cutSites.Add(new CutSite
                {
                    Position = match.Index + enzyme.CutPosition5,
                    RecognitionStart = match.Index,
                    Enzyme = enzyme,
                    IsReverseStrand = false
                });
            }

            // 如果不是回文序列，還需要搜尋反向互補股
            if (!enzyme.IsPalindromic)
            {
                string revPattern = enzyme.GetReverseComplementRegexPattern();
                var revMatches = Regex.Matches(cleanSequence, revPattern, RegexOptions.IgnoreCase);

                foreach (Match match in revMatches)
                {
                    // 反向股的切割位置需要調整
                    int cutPos = match.Index + (enzyme.RecognitionSequence.Length - enzyme.CutPosition3);
                    cutSites.Add(new CutSite
                    {
                        Position = cutPos,
                        RecognitionStart = match.Index,
                        Enzyme = enzyme,
                        IsReverseStrand = true
                    });
                }
            }

            return cutSites.OrderBy(c => c.Position).ToList();
        }

        /// <summary>
        /// 使用多個酶尋找所有切割位點
        /// </summary>
        public List<CutSite> FindCutSites(string sequence, IEnumerable<RestrictionEnzyme> enzymes, bool isCircular = false)
        {
            var allCutSites = new List<CutSite>();

            foreach (var enzyme in enzymes)
            {
                var sites = FindCutSites(sequence, enzyme, isCircular);
                allCutSites.AddRange(sites);
            }

            return allCutSites.OrderBy(c => c.Position).ToList();
        }

        /// <summary>
        /// 執行切割並返回片段
        /// </summary>
        /// <param name="sequence">DNA 序列</param>
        /// <param name="enzyme">限制酶</param>
        /// <param name="isCircular">是否為環狀 DNA</param>
        /// <returns>DNA 片段列表，依大小排序</returns>
        public List<DnaFragment> DigestSequence(string sequence, RestrictionEnzyme enzyme, bool isCircular = false)
        {
            return DigestSequence(sequence, new[] { enzyme }, isCircular);
        }

        /// <summary>
        /// 使用多個酶執行切割並返回片段
        /// </summary>
        public List<DnaFragment> DigestSequence(string sequence, IEnumerable<RestrictionEnzyme> enzymes, bool isCircular = false)
        {
            var fragments = new List<DnaFragment>();
            string cleanSequence = CleanSequence(sequence);

            if (string.IsNullOrEmpty(cleanSequence))
                return fragments;

            // 取得所有切割位點
            var cutSites = FindCutSites(cleanSequence, enzymes, isCircular);

            if (cutSites.Count == 0)
            {
                // 沒有切割位點，整條序列為一個片段
                fragments.Add(new DnaFragment
                {
                    Size = cleanSequence.Length,
                    StartPosition = 0,
                    EndPosition = cleanSequence.Length - 1,
                    Sequence = cleanSequence,
                    Enzyme5End = null,
                    Enzyme3End = null
                });
                return fragments;
            }

            // 取得唯一的切割位置並排序
            var cutPositions = cutSites
                .Select(c => c.Position)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            // 建立位置到酶的對應
            var positionToEnzyme = cutSites
                .GroupBy(c => c.Position)
                .ToDictionary(g => g.Key, g => g.First().Enzyme);

            if (isCircular)
            {
                // 環狀 DNA 切割
                fragments = DigestCircular(cleanSequence, cutPositions, positionToEnzyme);
            }
            else
            {
                // 線性 DNA 切割
                fragments = DigestLinear(cleanSequence, cutPositions, positionToEnzyme);
            }

            // 依大小排序（由大到小）
            return fragments.OrderByDescending(f => f.Size).ToList();
        }

        /// <summary>
        /// 線性 DNA 切割
        /// </summary>
        private List<DnaFragment> DigestLinear(string sequence, List<int> cutPositions, 
            Dictionary<int, RestrictionEnzyme> positionToEnzyme)
        {
            var fragments = new List<DnaFragment>();
            int prevPosition = 0;

            for (int i = 0; i < cutPositions.Count; i++)
            {
                int currentPosition = cutPositions[i];
                
                if (currentPosition > prevPosition && currentPosition <= sequence.Length)
                {
                    int fragLength = currentPosition - prevPosition;
                    fragments.Add(new DnaFragment
                    {
                        Size = fragLength,
                        StartPosition = prevPosition,
                        EndPosition = currentPosition - 1,
                        Sequence = sequence.Substring(prevPosition, fragLength),
                        Enzyme5End = i == 0 ? null : positionToEnzyme.GetValueOrDefault(cutPositions[i - 1]),
                        Enzyme3End = positionToEnzyme.GetValueOrDefault(currentPosition)
                    });
                }
                prevPosition = currentPosition;
            }

            // 最後一個片段（從最後切割位到序列末端）
            if (prevPosition < sequence.Length)
            {
                int lastFragLength = sequence.Length - prevPosition;
                fragments.Add(new DnaFragment
                {
                    Size = lastFragLength,
                    StartPosition = prevPosition,
                    EndPosition = sequence.Length - 1,
                    Sequence = sequence.Substring(prevPosition),
                    Enzyme5End = positionToEnzyme.GetValueOrDefault(cutPositions.Last()),
                    Enzyme3End = null
                });
            }

            return fragments;
        }

        /// <summary>
        /// 環狀 DNA 切割
        /// </summary>
        private List<DnaFragment> DigestCircular(string sequence, List<int> cutPositions,
            Dictionary<int, RestrictionEnzyme> positionToEnzyme)
        {
            var fragments = new List<DnaFragment>();

            if (cutPositions.Count == 0)
            {
                fragments.Add(new DnaFragment
                {
                    Size = sequence.Length,
                    StartPosition = 0,
                    EndPosition = sequence.Length - 1,
                    Sequence = sequence
                });
                return fragments;
            }

            if (cutPositions.Count == 1)
            {
                // 只有一個切割位點，線性化後整條就是一個片段
                int cutPos = cutPositions[0];
                string linearized = sequence.Substring(cutPos) + sequence.Substring(0, cutPos);
                fragments.Add(new DnaFragment
                {
                    Size = sequence.Length,
                    StartPosition = cutPos,
                    EndPosition = cutPos - 1, // 環繞
                    Sequence = linearized,
                    Enzyme5End = positionToEnzyme.GetValueOrDefault(cutPos),
                    Enzyme3End = positionToEnzyme.GetValueOrDefault(cutPos)
                });
                return fragments;
            }

            // 多個切割位點
            for (int i = 0; i < cutPositions.Count; i++)
            {
                int startPos = cutPositions[i];
                int endPos = cutPositions[(i + 1) % cutPositions.Count];

                string fragSequence;
                int fragSize;

                if (endPos > startPos)
                {
                    // 正常情況：片段不跨越原點
                    fragSize = endPos - startPos;
                    fragSequence = sequence.Substring(startPos, fragSize);
                }
                else
                {
                    // 跨越原點的片段
                    fragSize = (sequence.Length - startPos) + endPos;
                    fragSequence = sequence.Substring(startPos) + sequence.Substring(0, endPos);
                }

                fragments.Add(new DnaFragment
                {
                    Size = fragSize,
                    StartPosition = startPos,
                    EndPosition = endPos == 0 ? sequence.Length - 1 : endPos - 1,
                    Sequence = fragSequence,
                    Enzyme5End = positionToEnzyme.GetValueOrDefault(startPos),
                    Enzyme3End = positionToEnzyme.GetValueOrDefault(endPos)
                });
            }

            return fragments;
        }

        /// <summary>
        /// 清理序列（移除空白和數字）
        /// </summary>
        private string CleanSequence(string sequence)
        {
            if (string.IsNullOrEmpty(sequence))
                return string.Empty;

            // 移除空白、數字和非 DNA 字元
            var cleaned = new System.Text.StringBuilder();
            foreach (char c in sequence.ToUpper())
            {
                if ("ATGCRYKMSWBDHVN".Contains(c))
                {
                    cleaned.Append(c);
                }
            }
            return cleaned.ToString();
        }

        /// <summary>
        /// 分析序列中所有酶的切割情況
        /// </summary>
        /// <param name="sequence">DNA 序列</param>
        /// <param name="enzymes">要分析的酶列表</param>
        /// <param name="isCircular">是否為環狀</param>
        /// <returns>分析結果</returns>
        public List<EnzymeAnalysisResult> AnalyzeAllEnzymes(string sequence, 
            IEnumerable<RestrictionEnzyme> enzymes, bool isCircular = false)
        {
            var results = new List<EnzymeAnalysisResult>();
            string cleanSequence = CleanSequence(sequence);

            foreach (var enzyme in enzymes)
            {
                var cutSites = FindCutSites(cleanSequence, enzyme, isCircular);
                
                results.Add(new EnzymeAnalysisResult
                {
                    Enzyme = enzyme,
                    CutCount = cutSites.Count,
                    CutPositions = cutSites.Select(c => c.Position).ToList(),
                    CutSites = cutSites
                });
            }

            return results.OrderBy(r => r.Enzyme.Name).ToList();
        }
    }

    /// <summary>
    /// 酶分析結果
    /// </summary>
    public class EnzymeAnalysisResult
    {
        public RestrictionEnzyme Enzyme { get; set; }
        public int CutCount { get; set; }
        public List<int> CutPositions { get; set; }
        public List<CutSite> CutSites { get; set; }

        /// <summary>
        /// 格式化的切割位置字串
        /// </summary>
        public string PositionsString => CutPositions.Count > 0 
            ? string.Join(", ", CutPositions) 
            : "None";

        /// <summary>
        /// 末端類型描述
        /// </summary>
        public string OverhangDescription
        {
            get
            {
                switch (Enzyme.OverhangType)
                {
                    case OverhangType.FivePrime:
                        return "5' overhang";
                    case OverhangType.ThreePrime:
                        return "3' overhang";
                    case OverhangType.Blunt:
                        return "Blunt end";
                    default:
                        return "Unknown";
                }
            }
        }
    }

    /// <summary>
    /// Dictionary 擴展方法
    /// </summary>
    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        {
            return dictionary.TryGetValue(key, out TValue value) ? value : default;
        }
    }
}
