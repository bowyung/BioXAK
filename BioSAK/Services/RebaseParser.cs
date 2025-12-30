using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BioSAK.Models;

namespace BioSAK.Services
{
    /// <summary>
    /// 限制酶資料解析器
    /// </summary>
    public static class RebaseParser
    {
        private static List<RestrictionEnzyme> _cachedEnzymes = null;
        private static readonly object _lock = new object();

        /// <summary>
        /// 從內建資料檔載入所有限制酶
        /// </summary>
        public static List<RestrictionEnzyme> LoadEnzymes()
        {
            lock (_lock)
            {
                if (_cachedEnzymes != null)
                    return _cachedEnzymes;

                _cachedEnzymes = new List<RestrictionEnzyme>();

                // 嘗試從嵌入資源或檔案載入
                string dataPath = GetDataFilePath();
                
                if (File.Exists(dataPath))
                {
                    _cachedEnzymes = ParseEnzymeFile(dataPath);
                }
                else
                {
                    // 使用內建的基本酶列表
                    _cachedEnzymes = GetBuiltInEnzymes();
                }

                return _cachedEnzymes;
            }
        }

        /// <summary>
        /// 從檔案解析限制酶資料
        /// </summary>
        public static List<RestrictionEnzyme> ParseEnzymeFile(string filePath)
        {
            var enzymes = new List<RestrictionEnzyme>();

            foreach (var line in File.ReadAllLines(filePath))
            {
                // 跳過註解和空行
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var enzyme = ParseEnzymeLine(line);
                if (enzyme != null)
                {
                    // 檢查是否已存在同名酶（避免重複）
                    if (!enzymes.Any(e => e.Name.Equals(enzyme.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        enzymes.Add(enzyme);
                    }
                }
            }

            return enzymes.OrderBy(e => e.Name).ToList();
        }

        /// <summary>
        /// 解析單行酶資料
        /// Format: Name;RecognitionSequence;CutPosition5;CutPosition3
        /// </summary>
        private static RestrictionEnzyme ParseEnzymeLine(string line)
        {
            try
            {
                var parts = line.Split(';');
                if (parts.Length < 4)
                    return null;

                return new RestrictionEnzyme
                {
                    Name = parts[0].Trim(),
                    RecognitionSequence = parts[1].Trim().ToUpper(),
                    CutPosition5 = int.Parse(parts[2].Trim()),
                    CutPosition3 = int.Parse(parts[3].Trim())
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 取得資料檔路徑
        /// </summary>
        private static string GetDataFilePath()
        {
            // 嘗試多個可能的路徑
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "common_enzymes.txt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "common_enzymes.txt"),
                Path.Combine(Environment.CurrentDirectory, "Data", "common_enzymes.txt"),
                "Data/common_enzymes.txt"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return possiblePaths[0]; // 返回預設路徑
        }

        /// <summary>
        /// 內建的基本限制酶列表
        /// </summary>
        private static List<RestrictionEnzyme> GetBuiltInEnzymes()
        {
            return new List<RestrictionEnzyme>
            {
                // 常用 6-cutters (5' overhang)
                new RestrictionEnzyme { Name = "EcoRI", RecognitionSequence = "GAATTC", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "BamHI", RecognitionSequence = "GGATCC", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "HindIII", RecognitionSequence = "AAGCTT", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "XhoI", RecognitionSequence = "CTCGAG", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "SalI", RecognitionSequence = "GTCGAC", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "XbaI", RecognitionSequence = "TCTAGA", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "NcoI", RecognitionSequence = "CCATGG", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "NheI", RecognitionSequence = "GCTAGC", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "NdeI", RecognitionSequence = "CATATG", CutPosition5 = 2, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "BglII", RecognitionSequence = "AGATCT", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "ClaI", RecognitionSequence = "ATCGAT", CutPosition5 = 2, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "MfeI", RecognitionSequence = "CAATTG", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "AgeI", RecognitionSequence = "ACCGGT", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "SpeI", RecognitionSequence = "ACTAGT", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "AflII", RecognitionSequence = "CTTAAG", CutPosition5 = 1, CutPosition3 = 5 },
                new RestrictionEnzyme { Name = "MluI", RecognitionSequence = "ACGCGT", CutPosition5 = 1, CutPosition3 = 5 },

                // 常用 6-cutters (3' overhang)
                new RestrictionEnzyme { Name = "SacI", RecognitionSequence = "GAGCTC", CutPosition5 = 5, CutPosition3 = 1 },
                new RestrictionEnzyme { Name = "KpnI", RecognitionSequence = "GGTACC", CutPosition5 = 5, CutPosition3 = 1 },
                new RestrictionEnzyme { Name = "SphI", RecognitionSequence = "GCATGC", CutPosition5 = 5, CutPosition3 = 1 },
                new RestrictionEnzyme { Name = "PstI", RecognitionSequence = "CTGCAG", CutPosition5 = 5, CutPosition3 = 1 },
                new RestrictionEnzyme { Name = "ApaI", RecognitionSequence = "GGGCCC", CutPosition5 = 5, CutPosition3 = 1 },
                new RestrictionEnzyme { Name = "NsiI", RecognitionSequence = "ATGCAT", CutPosition5 = 5, CutPosition3 = 1 },

                // 常用 6-cutters (blunt)
                new RestrictionEnzyme { Name = "EcoRV", RecognitionSequence = "GATATC", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "SmaI", RecognitionSequence = "CCCGGG", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "StuI", RecognitionSequence = "AGGCCT", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "NruI", RecognitionSequence = "TCGCGA", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "PvuII", RecognitionSequence = "CAGCTG", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "HpaI", RecognitionSequence = "GTTAAC", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "ScaI", RecognitionSequence = "AGTACT", CutPosition5 = 3, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "DraI", RecognitionSequence = "TTTAAA", CutPosition5 = 3, CutPosition3 = 3 },

                // 常用 4-cutters
                new RestrictionEnzyme { Name = "MboI", RecognitionSequence = "GATC", CutPosition5 = 0, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "Sau3AI", RecognitionSequence = "GATC", CutPosition5 = 0, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "DpnI", RecognitionSequence = "GATC", CutPosition5 = 2, CutPosition3 = 2 },
                new RestrictionEnzyme { Name = "DpnII", RecognitionSequence = "GATC", CutPosition5 = 0, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "HaeIII", RecognitionSequence = "GGCC", CutPosition5 = 2, CutPosition3 = 2 },
                new RestrictionEnzyme { Name = "AluI", RecognitionSequence = "AGCT", CutPosition5 = 2, CutPosition3 = 2 },
                new RestrictionEnzyme { Name = "RsaI", RecognitionSequence = "GTAC", CutPosition5 = 2, CutPosition3 = 2 },
                new RestrictionEnzyme { Name = "TaqI", RecognitionSequence = "TCGA", CutPosition5 = 1, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "MspI", RecognitionSequence = "CCGG", CutPosition5 = 1, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "HpaII", RecognitionSequence = "CCGG", CutPosition5 = 1, CutPosition3 = 3 },

                // 常用 8-cutters
                new RestrictionEnzyme { Name = "NotI", RecognitionSequence = "GCGGCCGC", CutPosition5 = 2, CutPosition3 = 6 },
                new RestrictionEnzyme { Name = "PacI", RecognitionSequence = "TTAATTAA", CutPosition5 = 5, CutPosition3 = 3 },
                new RestrictionEnzyme { Name = "AscI", RecognitionSequence = "GGCGCGCC", CutPosition5 = 2, CutPosition3 = 6 },
                new RestrictionEnzyme { Name = "FseI", RecognitionSequence = "GGCCGGCC", CutPosition5 = 6, CutPosition3 = 2 },
                new RestrictionEnzyme { Name = "SwaI", RecognitionSequence = "ATTTAAAT", CutPosition5 = 4, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "PmeI", RecognitionSequence = "GTTTAAAC", CutPosition5 = 4, CutPosition3 = 4 },
                new RestrictionEnzyme { Name = "SbfI", RecognitionSequence = "CCTGCAGG", CutPosition5 = 6, CutPosition3 = 2 },

                // Type IIS enzymes
                new RestrictionEnzyme { Name = "BsaI", RecognitionSequence = "GGTCTC", CutPosition5 = 7, CutPosition3 = 11 },
                new RestrictionEnzyme { Name = "BsmBI", RecognitionSequence = "CGTCTC", CutPosition5 = 7, CutPosition3 = 11 },
                new RestrictionEnzyme { Name = "BbsI", RecognitionSequence = "GAAGAC", CutPosition5 = 8, CutPosition3 = 12 },
                new RestrictionEnzyme { Name = "SapI", RecognitionSequence = "GCTCTTC", CutPosition5 = 8, CutPosition3 = 11 },
            };
        }

        /// <summary>
        /// 根據名稱搜尋酶
        /// </summary>
        public static List<RestrictionEnzyme> SearchByName(string searchText)
        {
            var enzymes = LoadEnzymes();
            if (string.IsNullOrWhiteSpace(searchText))
                return enzymes;

            return enzymes
                .Where(e => e.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>
        /// 根據識別序列長度篩選
        /// </summary>
        public static List<RestrictionEnzyme> FilterByCutterType(int length)
        {
            var enzymes = LoadEnzymes();
            return enzymes
                .Where(e => e.RecognitionSequence.Length == length)
                .ToList();
        }

        /// <summary>
        /// 根據末端類型篩選
        /// </summary>
        public static List<RestrictionEnzyme> FilterByOverhangType(OverhangType type)
        {
            var enzymes = LoadEnzymes();
            return enzymes
                .Where(e => e.OverhangType == type)
                .ToList();
        }

        /// <summary>
        /// 取得所有回文酶
        /// </summary>
        public static List<RestrictionEnzyme> GetPalindromicEnzymes()
        {
            var enzymes = LoadEnzymes();
            return enzymes
                .Where(e => e.IsPalindromic)
                .ToList();
        }
    }
}
