using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BioSAK.Models
{
    /// <summary>
    /// 限制酶末端類型
    /// </summary>
    public enum OverhangType
    {
        Blunt,      // 平滑末端
        FivePrime,  // 5' 突出
        ThreePrime  // 3' 突出
    }

    /// <summary>
    /// 限制酶資料模型
    /// </summary>
    public class RestrictionEnzyme
    {
        /// <summary>
        /// 酶的名稱 (例如: EcoRI)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 識別序列 (例如: GAATTC)
        /// </summary>
        public string RecognitionSequence { get; set; }

        /// <summary>
        /// 5'→3' 股的切割位置 (相對於識別序列起點，0-based)
        /// 例如: G^AATTC 的 CutPosition5 = 1
        /// </summary>
        public int CutPosition5 { get; set; }

        /// <summary>
        /// 3'→5' 股的切割位置 (相對於識別序列起點，0-based)
        /// 例如: GAATTC 的 CutPosition3 = 5 (對應 CTTAA^G)
        /// </summary>
        public int CutPosition3 { get; set; }

        /// <summary>
        /// 末端類型
        /// </summary>
        public OverhangType OverhangType
        {
            get
            {
                if (CutPosition5 == CutPosition3)
                    return OverhangType.Blunt;
                else if (CutPosition5 < CutPosition3)
                    return OverhangType.FivePrime;
                else
                    return OverhangType.ThreePrime;
            }
        }

        /// <summary>
        /// 取得突出序列
        /// </summary>
        public string GetOverhangSequence()
        {
            if (OverhangType == OverhangType.Blunt)
                return "";

            int start = Math.Min(CutPosition5, CutPosition3);
            int end = Math.Max(CutPosition5, CutPosition3);
            
            if (start >= 0 && end <= RecognitionSequence.Length)
                return RecognitionSequence.Substring(start, end - start);
            
            return "";
        }

        /// <summary>
        /// 將識別序列轉換為正則表達式 (處理 IUPAC 簡併碼)
        /// </summary>
        public string GetRegexPattern()
        {
            return ConvertToRegex(RecognitionSequence);
        }

        /// <summary>
        /// 取得反向互補序列的正則表達式
        /// </summary>
        public string GetReverseComplementRegexPattern()
        {
            string revComp = GetReverseComplement(RecognitionSequence);
            return ConvertToRegex(revComp);
        }

        /// <summary>
        /// 是否為回文序列 (palindromic)
        /// </summary>
        public bool IsPalindromic
        {
            get
            {
                return RecognitionSequence.Equals(
                    GetReverseComplement(RecognitionSequence),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// IUPAC 簡併碼對應表
        /// </summary>
        private static readonly Dictionary<char, string> IupacCodes = new Dictionary<char, string>
        {
            {'A', "A"}, {'T', "T"}, {'G', "G"}, {'C', "C"},
            {'R', "[AG]"},   // puRine
            {'Y', "[CT]"},   // pYrimidine
            {'M', "[AC]"},   // aMino
            {'K', "[GT]"},   // Keto
            {'S', "[GC]"},   // Strong
            {'W', "[AT]"},   // Weak
            {'H', "[ACT]"},  // not G
            {'B', "[CGT]"},  // not A
            {'V', "[ACG]"},  // not T
            {'D', "[AGT]"},  // not C
            {'N', "[ACGT]"}, // aNy
        };

        /// <summary>
        /// 互補鹼基對應表
        /// </summary>
        private static readonly Dictionary<char, char> ComplementMap = new Dictionary<char, char>
        {
            {'A', 'T'}, {'T', 'A'}, {'G', 'C'}, {'C', 'G'},
            {'R', 'Y'}, {'Y', 'R'}, {'M', 'K'}, {'K', 'M'},
            {'S', 'S'}, {'W', 'W'}, {'H', 'D'}, {'D', 'H'},
            {'B', 'V'}, {'V', 'B'}, {'N', 'N'}
        };

        private static string ConvertToRegex(string sequence)
        {
            var pattern = new System.Text.StringBuilder();
            foreach (char c in sequence.ToUpper())
            {
                if (IupacCodes.TryGetValue(c, out string replacement))
                    pattern.Append(replacement);
                else
                    pattern.Append(c);
            }
            return pattern.ToString();
        }

        private static string GetReverseComplement(string sequence)
        {
            var result = new char[sequence.Length];
            for (int i = 0; i < sequence.Length; i++)
            {
                char c = char.ToUpper(sequence[sequence.Length - 1 - i]);
                result[i] = ComplementMap.TryGetValue(c, out char comp) ? comp : c;
            }
            return new string(result);
        }

        public override string ToString()
        {
            return $"{Name}: {RecognitionSequence} ({OverhangType})";
        }
    }

    /// <summary>
    /// 切割位點資訊
    /// </summary>
    public class CutSite
    {
        /// <summary>
        /// 切割位置 (在原始序列中的位置，0-based)
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// 使用的限制酶
        /// </summary>
        public RestrictionEnzyme Enzyme { get; set; }

        /// <summary>
        /// 識別序列在原始序列中的起始位置
        /// </summary>
        public int RecognitionStart { get; set; }

        /// <summary>
        /// 是否在反向股上找到
        /// </summary>
        public bool IsReverseStrand { get; set; }

        /// <summary>
        /// 5' 股切割的實際位置
        /// </summary>
        public int Cut5Position => RecognitionStart + Enzyme.CutPosition5;

        /// <summary>
        /// 3' 股切割的實際位置
        /// </summary>
        public int Cut3Position => RecognitionStart + Enzyme.CutPosition3;

        public override string ToString()
        {
            return $"{Enzyme.Name} @ {Position} ({Enzyme.OverhangType})";
        }
    }

    /// <summary>
    /// DNA 片段
    /// </summary>
    public class DnaFragment
    {
        /// <summary>
        /// 片段大小 (bp)
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// 片段起始位置 (在原始序列中)
        /// </summary>
        public int StartPosition { get; set; }

        /// <summary>
        /// 片段結束位置 (在原始序列中)
        /// </summary>
        public int EndPosition { get; set; }

        /// <summary>
        /// 5' 端的酶
        /// </summary>
        public RestrictionEnzyme Enzyme5End { get; set; }

        /// <summary>
        /// 3' 端的酶
        /// </summary>
        public RestrictionEnzyme Enzyme3End { get; set; }

        /// <summary>
        /// 片段序列
        /// </summary>
        public string Sequence { get; set; }

        public override string ToString()
        {
            return $"{Size} bp ({StartPosition}-{EndPosition})";
        }
    }
}
