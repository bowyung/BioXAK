using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BioSAK.Models;

namespace BioSAK.Services
{
    /// <summary>
    /// GenBank flat file (.gb / .gbk) 解析器 — 針對 NCBI 真實檔案格式最佳化
    /// 
    /// 解決的常見問題：
    /// - NCBI LOCUS 行使用 accession number 而非載體名 → 從 DEFINITION 提取
    /// - Feature 續行格式不一致 → 用寬鬆的空格偵測
    /// - Qualifier 值跨多行（如 /translation）→ 正確串接
    /// </summary>
    public static class GenBankParser
    {
        // ===============================================================
        //  公開 API
        // ===============================================================

        public static VectorTemplate ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"GenBank file not found: {filePath}");
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            return ParseText(text, Path.GetFileNameWithoutExtension(filePath));
        }

        public static VectorTemplate ParseText(string gbText, string fallbackName = "Unknown")
        {
            if (string.IsNullOrWhiteSpace(gbText))
                throw new ArgumentException("GenBank text is empty.");

            var lines = gbText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var template = new VectorTemplate();

            // 1) 基本資訊
            bool isLinear = ParseLocus(lines, template);
            ParseDefinition(lines, template);

            // 2) 如果 DEFINITION 裡找到載體名，優先使用
            string defName = ExtractVectorName(template.Description, template.Name);
            if (!string.IsNullOrEmpty(defName))
                template.Name = defName;
            if (string.IsNullOrEmpty(template.Name))
                template.Name = fallbackName;

            // 3) Features（也嘗試從 source /organism 再抓名字）
            string organismName = null;
            template.Features = ParseFeatures(lines, out organismName);
            if (!string.IsNullOrEmpty(organismName))
            {
                string orgVec = ExtractVectorName(organismName, null);
                if (!string.IsNullOrEmpty(orgVec) && IsLikelyVectorName(orgVec))
                    template.Name = orgVec;
            }

            // 4) Sequence
            template.Sequence = ParseOrigin(lines);
            template.Size = template.Sequence?.Length ?? 0;

            // 5) 拓撲：如果 LOCUS 說 linear，記錄下來
            if (isLinear && string.IsNullOrEmpty(template.Description))
                template.Description = "(linear)";

            // 6) 分類
            if (string.IsNullOrEmpty(template.Category))
                template.Category = InferCategory(template);

            if (string.IsNullOrEmpty(template.Description))
                template.Description = template.Name;

            return template;
        }

        public static List<VectorTemplate> ParseAllFromDirectory(string directoryPath)
        {
            var templates = new List<VectorTemplate>();
            if (!Directory.Exists(directoryPath)) return templates;

            foreach (var file in Directory.GetFiles(directoryPath, "*.gb")
                         .Concat(Directory.GetFiles(directoryPath, "*.gbk")))
            {
                try
                {
                    var t = ParseFile(file);
                    if (t?.Sequence?.Length > 0) templates.Add(t);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse {file}: {ex.Message}");
                }
            }
            return templates.OrderBy(t => t.Name).ToList();
        }

        public static List<VectorTemplate> ParseAllFromEmbeddedResources(
            string resourcePrefix = "BioSAK.Data.Vectors.")
        {
            var templates = new List<VectorTemplate>();
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames()
                .Where(n => n.StartsWith(resourcePrefix) && (n.EndsWith(".gb") || n.EndsWith(".gbk")));

            foreach (var resName in names)
            {
                try
                {
                    using (var stream = asm.GetManifestResourceStream(resName))
                    {
                        if (stream == null) continue;
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string fallback = resName.Replace(resourcePrefix, "")
                                .Replace(".gb", "").Replace(".gbk", "");
                            var t = ParseText(reader.ReadToEnd(), fallback);
                            if (t?.Sequence?.Length > 0) templates.Add(t);
                        }
                    }
                }
                catch { }
            }
            return templates.OrderBy(t => t.Name).ToList();
        }

        public static bool IsValidGenBankFile(string path)
        {
            try
            {
                using (var r = new StreamReader(path))
                    return r.ReadLine()?.StartsWith("LOCUS") == true;
            }
            catch { return false; }
        }

        // ===============================================================
        //  LOCUS
        // ===============================================================

        /// <summary>回傳 true 如果是 linear</summary>
        private static bool ParseLocus(string[] lines, VectorTemplate template)
        {
            var locus = lines.FirstOrDefault(l => l.StartsWith("LOCUS"));
            if (locus == null) return false;

            var parts = locus.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                template.Name = parts[1]; // 暫存，後面可能被 DEFINITION 覆蓋

            return locus.ToUpper().Contains("LINEAR");
        }

        // ===============================================================
        //  DEFINITION — 提取載體名稱
        // ===============================================================

        private static void ParseDefinition(string[] lines, VectorTemplate template)
        {
            var sb = new StringBuilder();
            bool active = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("DEFINITION"))
                {
                    active = true;
                    sb.Append(line.Substring("DEFINITION".Length).Trim());
                }
                else if (active)
                {
                    if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                        sb.Append(" " + line.Trim());
                    else break;
                }
            }

            string def = sb.ToString().TrimEnd('.');
            if (!string.IsNullOrWhiteSpace(def))
                template.Description = def;
        }

        /// <summary>
        /// 從 DEFINITION 文字中提取載體名稱
        /// 
        /// 範例：
        ///   "Cloning vector pEGFP-N1, complete sequence"     → "pEGFP-N1"
        ///   "Expression vector pcDNA3.1(+), complete sequence" → "pcDNA3.1(+)"
        ///   "pGL3-Basic vector DNA, complete sequence"        → "pGL3-Basic"
        ///   "Lentiviral vector pLKO.1-puro"                  → "pLKO.1-puro"
        ///   "Cloning vector pMD2.G, complete sequence"       → "pMD2.G"
        /// </summary>
        private static string ExtractVectorName(string definition, string locusName)
        {
            if (string.IsNullOrWhiteSpace(definition)) return null;

            // 策略 1：找 p 開頭的載體名（最常見格式）
            // 匹配 pXXX 模式，允許 - . + ( ) 數字
            var match = Regex.Match(definition, @"\b(p[A-Z][A-Za-z0-9_.+()-]{1,30})\b", RegexOptions.None);
            if (match.Success)
            {
                string name = match.Groups[1].Value.TrimEnd('.', ',', ';');
                // 去除尾端的描述性文字
                name = Regex.Replace(name, @"(complete|sequence|vector|plasmid|DNA)$", "",
                    RegexOptions.IgnoreCase).TrimEnd(' ', ',', '.');
                if (name.Length >= 3) return name;
            }

            // 策略 2：尋找常見關鍵字後的名稱
            var patterns = new[]
            {
                @"(?:vector|plasmid)\s+([A-Za-z][A-Za-z0-9_.+()-]{2,30})",
                @"([A-Za-z][A-Za-z0-9_.+-]*(?:\([+\-]\))?)\s+(?:vector|plasmid|DNA)",
            };
            foreach (var pat in patterns)
            {
                var m = Regex.Match(definition, pat, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string name = m.Groups[1].Value.TrimEnd('.', ',');
                    if (name.Length >= 3 && name != "complete" && name != "sequence")
                        return name;
                }
            }

            return null;
        }

        /// <summary>判斷字串是否像載體名</summary>
        private static bool IsLikelyVectorName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return false;
            // 以 p 開頭且含英文字母和數字
            if (s.StartsWith("p", StringComparison.OrdinalIgnoreCase) && s.Length >= 3) return true;
            // 常見非 p 開頭的載體
            string[] known = { "pcDNA", "pET", "pGL", "pRL", "pLKO", "pMD", "psPAX", "pCAGGS" };
            return known.Any(k => s.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        }

        // ===============================================================
        //  FEATURES 段解析 — 重寫為更穩健的版本
        // ===============================================================

        private static List<SequenceFeature> ParseFeatures(string[] lines, out string organismName)
        {
            organismName = null;
            var features = new List<SequenceFeature>();

            // 找 FEATURES 段範圍
            int start = -1, end = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("FEATURES")) start = i + 1;
                else if (start >= 0 && !string.IsNullOrEmpty(lines[i]) &&
                         lines[i].Length > 0 && char.IsLetter(lines[i][0]))
                { end = i; break; }
            }
            if (start < 0) return features;
            if (end < 0) end = lines.Length;

            // 將 FEATURES 段切分為各個 feature 區塊
            var featureBlocks = new List<FeatureBlock>();
            FeatureBlock current = null;

            for (int i = start; i < end; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 新 feature 行偵測：
                // GenBank 標準格式：col 0-4 = 空格, col 5+ = feature key, col 21+ = location
                // 寬鬆判斷：行以 1~8 個空格開頭，接著非空格字元（非 / 開頭）
                if (IsFeatureKeyLine(line))
                {
                    if (current != null) featureBlocks.Add(current);
                    current = new FeatureBlock();

                    string trimmed = line.TrimStart();
                    int sp = trimmed.IndexOf(' ');
                    if (sp > 0)
                    {
                        current.Type = trimmed.Substring(0, sp);
                        current.Location = trimmed.Substring(sp).Trim();
                    }
                    else
                    {
                        current.Type = trimmed;
                        current.Location = "";
                    }
                }
                else if (current != null)
                {
                    // Qualifier 或續行
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("/"))
                    {
                        // 新的 qualifier
                        current.QualifierLines.Add(trimmed);
                    }
                    else if (current.QualifierLines.Count > 0)
                    {
                        // 上一個 qualifier 的續行（/translation, /note 等可以很長）
                        current.QualifierLines[current.QualifierLines.Count - 1] += " " + trimmed;
                    }
                    else if (!string.IsNullOrEmpty(current.Location) &&
                             (current.Location.Contains("(") && !current.Location.Contains(")")))
                    {
                        // Location 續行（如 complement(join(1..100,\n200..300))）
                        current.Location += trimmed;
                    }
                }
            }
            if (current != null) featureBlocks.Add(current);

            // 轉換每個 block 為 SequenceFeature
            foreach (var block in featureBlocks)
            {
                var quals = ParseQualifiers(block.QualifierLines);

                // 從 source feature 提取 organism 名稱
                if (block.Type.Equals("source", StringComparison.OrdinalIgnoreCase))
                {
                    organismName = GetQual(quals, "organism");
                    continue; // 不加入 feature 列表
                }

                var location = ParseLocation(block.Location);
                if (location == null) continue;

                // 名稱優先順序：/label > /gene > /product > /note > type
                string name = GetQual(quals, "label")
                           ?? GetQual(quals, "gene")
                           ?? GetQual(quals, "product")
                           ?? GetQual(quals, "note");

                // 如果 name 太長，截斷
                if (name != null && name.Length > 50)
                    name = name.Substring(0, 47) + "...";
                if (string.IsNullOrEmpty(name))
                    name = block.Type;

                var featureType = MapFeatureType(block.Type, name, quals);

                features.Add(new SequenceFeature
                {
                    Name = name,
                    Start = location.Start,
                    End = location.End,
                    IsReverse = location.IsComplement,
                    Type = featureType
                });
            }

            return features;
        }

        /// <summary>
        /// 判斷是否為 feature key 行
        /// GenBank 標準：5 個空格 + feature key（非空格，非 /）
        /// 寬鬆模式：1~8 個前導空格 + 英文字母開頭
        /// </summary>
        private static bool IsFeatureKeyLine(string line)
        {
            if (line.Length < 6) return false;

            // 必須以空格開頭
            if (line[0] != ' ') return false;

            // 找到第一個非空格字元
            int firstNonSpace = 0;
            while (firstNonSpace < line.Length && line[firstNonSpace] == ' ') firstNonSpace++;

            // 前導空格在 1~8 個之間（標準是 5 個，但允許偏差）
            if (firstNonSpace < 1 || firstNonSpace > 10) return false;

            // 第一個非空格字元必須是英文字母（feature key 如 CDS, gene, promoter）
            if (firstNonSpace >= line.Length) return false;
            char first = line[firstNonSpace];
            if (!char.IsLetter(first)) return false;

            // 不能是 qualifier（以 / 開頭）
            if (first == '/') return false;

            // feature key 後面要有空格 + location，或者至少 key 長度 > 2
            string trimmed = line.TrimStart();
            int spaceAfterKey = trimmed.IndexOf(' ');

            // 如果整行就是一個單詞且很短，可能是 feature key + location 在下一行
            // 但更常見的是：key + 空格 + location 在同一行
            if (spaceAfterKey > 0 && spaceAfterKey < 20)
            {
                string potentialKey = trimmed.Substring(0, spaceAfterKey);
                // Feature key 只含字母、數字、底線、引號
                if (Regex.IsMatch(potentialKey, @"^[A-Za-z_'][A-Za-z0-9_']{0,20}$"))
                    return true;
            }

            return false;
        }

        private class FeatureBlock
        {
            public string Type = "";
            public string Location = "";
            public List<string> QualifierLines = new List<string>();
        }

        // ===============================================================
        //  Qualifier 解析
        // ===============================================================

        private static Dictionary<string, string> ParseQualifiers(List<string> qualLines)
        {
            var quals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in qualLines)
            {
                string line = rawLine.TrimStart('/');
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    quals[line.Trim()] = "true";
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim().Trim('"');

                // 只保留第一次出現的值（避免 CDS 的 /gene 覆蓋 gene feature 的 /gene）
                // 但 /label 永遠覆蓋（因為最重要）
                if (key.Equals("label", StringComparison.OrdinalIgnoreCase) || !quals.ContainsKey(key))
                    quals[key] = val;
            }

            return quals;
        }

        private static string GetQual(Dictionary<string, string> q, string key)
        {
            return q.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v.Trim('"') : null;
        }

        // ===============================================================
        //  Location 解析
        // ===============================================================

        private static FeatureLocation ParseLocation(string locStr)
        {
            if (string.IsNullOrWhiteSpace(locStr)) return null;

            try
            {
                bool isComplement = false;
                string w = locStr.Trim();

                // complement(...)
                if (w.StartsWith("complement(", StringComparison.OrdinalIgnoreCase))
                {
                    isComplement = true;
                    w = w.Substring("complement(".Length);
                    // 移除尾端的 )，但要注意巢狀括號
                    if (w.EndsWith(")")) w = w.Substring(0, w.Length - 1);
                }

                // join(...) / order(...)
                if (w.StartsWith("join(", StringComparison.OrdinalIgnoreCase) ||
                    w.StartsWith("order(", StringComparison.OrdinalIgnoreCase))
                {
                    int paren = w.IndexOf('(');
                    string inner = w.Substring(paren + 1).TrimEnd(')');
                    int minS = int.MaxValue, maxE = int.MinValue;
                    foreach (var part in inner.Split(','))
                    {
                        var sub = ParseSimpleRange(part.Trim());
                        if (sub != null) { minS = Math.Min(minS, sub.Start); maxE = Math.Max(maxE, sub.End); }
                    }
                    if (minS == int.MaxValue) return null;
                    return new FeatureLocation { Start = minS, End = maxE, IsComplement = isComplement };
                }

                var simple = ParseSimpleRange(w);
                if (simple != null) { simple.IsComplement = isComplement; return simple; }
                return null;
            }
            catch { return null; }
        }

        private static FeatureLocation ParseSimpleRange(string s)
        {
            s = s.Replace("<", "").Replace(">", "").Trim();

            if (s.Contains(".."))
            {
                var p = s.Split(new[] { ".." }, StringSplitOptions.None);
                if (p.Length == 2 && int.TryParse(p[0].Trim(), out int a) && int.TryParse(p[1].Trim(), out int b))
                    return new FeatureLocation { Start = a - 1, End = b }; // 1-based → 0-based
            }
            else if (int.TryParse(s, out int pt))
                return new FeatureLocation { Start = pt - 1, End = pt };

            return null;
        }

        private class FeatureLocation
        {
            public int Start, End;
            public bool IsComplement;
        }

        // ===============================================================
        //  ORIGIN（序列）
        // ===============================================================

        private static string ParseOrigin(string[] lines)
        {
            var sb = new StringBuilder();
            bool active = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("ORIGIN")) { active = true; continue; }
                if (active)
                {
                    if (line.StartsWith("//")) break;
                    foreach (char c in line)
                        if (char.IsLetter(c)) sb.Append(char.ToUpper(c));
                }
            }
            return sb.ToString();
        }

        // ===============================================================
        //  Feature Type 映射
        // ===============================================================

        private static FeatureType MapFeatureType(string gbType, string name,
            Dictionary<string, string> quals)
        {
            string tl = gbType.ToLower();
            string nl = (name ?? "").ToLower();

            // --- 精確 type ---
            switch (tl)
            {
                case "promoter":
                case "caat_signal":
                case "tata_signal":
                    return FeatureType.Promoter;
                case "terminator":
                    return FeatureType.Terminator;
                case "rep_origin":
                    return FeatureType.Origin;
                case "polya_signal":
                case "polya_site":
                    return FeatureType.PolyA;
                case "enhancer":
                    return FeatureType.Enhancer;
                case "primer_bind":
                    return FeatureType.Misc;
            }

            // --- 根據名稱推測 ---
            if (IsResistance(nl, quals)) return FeatureType.Resistance;
            if (IsReporter(nl)) return FeatureType.Reporter;
            if (IsTag(nl)) return FeatureType.Tag;

            if (nl.Contains("promoter") || nl.Contains("prm")) return FeatureType.Promoter;
            if (nl.Contains("terminator") || nl.Contains("term ")) return FeatureType.Terminator;
            if (nl.Contains("polya") || nl.Contains("poly(a)") || nl.Contains("poly a")) return FeatureType.PolyA;
            if (nl.Contains("enhancer")) return FeatureType.Enhancer;
            if (nl.Contains("mcs") || nl.Contains("multiple cloning") || nl.Contains("polylinker"))
                return FeatureType.MCS;

            // ori
            if (tl == "rep_origin" || (nl.Contains("ori") &&
                (nl.Contains("cole1") || nl.Contains("f1") || nl.Contains("pmb1") ||
                 nl.Contains("p15a") || nl.Contains("sv40") || nl.Contains("puc"))))
                return FeatureType.Origin;

            // CDS / gene
            if (tl == "cds" || tl == "gene") return FeatureType.Gene;

            // regulatory
            if (tl == "regulatory")
            {
                string rc = GetQual(quals, "regulatory_class") ?? "";
                if (rc.Contains("promoter")) return FeatureType.Promoter;
                if (rc.Contains("enhancer")) return FeatureType.Enhancer;
                if (rc.Contains("terminator")) return FeatureType.Terminator;
                if (rc.Contains("polyA") || rc.Contains("poly_A")) return FeatureType.PolyA;
            }

            // misc_feature — 再用名稱判斷一次
            if (tl == "misc_feature")
            {
                if (nl.Contains("ori")) return FeatureType.Origin;
                if (nl.Contains("ltr")) return FeatureType.Misc;
            }

            return FeatureType.Misc;
        }

        private static bool IsResistance(string nl, Dictionary<string, string> quals)
        {
            string[] kw = {
                "ampr", "amp(r)", "bla", "beta-lactamase", "ampicillin",
                "kanr", "kan(r)", "kanamycin", "aph(3", "nptii", "neor", "neo(r)", "neomycin",
                "cmr", "cat", "chloramphenicol", "tetr", "tet(r)", "tetracycline",
                "genr", "gentamicin", "hygr", "hygromycin", "zeor", "zeocin", "bler",
                "puror", "puromycin", "pac", "specr", "spectinomycin", "aada",
                "blasticidin", "bsr", "nourseothricin", "nat", "resistance"
            };
            if (kw.Any(k => nl.Contains(k))) return true;
            string prod = GetQual(quals, "product") ?? "";
            return prod.ToLower().Contains("resistance") || prod.ToLower().Contains("lactamase");
        }

        private static bool IsReporter(string nl)
        {
            string[] kw = {
                "gfp", "egfp", "eyfp", "ecfp", "mcherry", "rfp", "dsred", "tdtomato",
                "venus", "cerulean", "citrine", "luciferase", "luc2", "fluc", "firefly",
                "renilla", "rluc", "nanoluc", "nluc",
                "lacz", "beta-gal", "β-gal", "seap", "reporter"
            };
            return kw.Any(k => nl.Contains(k));
        }

        private static bool IsTag(string nl)
        {
            string[] kw = {
                "his-tag", "his tag", "6xhis", "his6", "histag", "polyhistidine",
                "flag", "ha-tag", "ha tag", "myc-tag", "myc tag", "c-myc",
                "v5-tag", "v5 tag", "gst", "glutathione", "mbp", "maltose",
                "strep-tag", "strep tag", "sumo", "thioredoxin", "trx",
                "thrombin", "tev site", "prescission", "linker"
            };
            return kw.Any(k => nl.Contains(k));
        }

        // ===============================================================
        //  分類推測
        // ===============================================================

        private static string InferCategory(VectorTemplate t)
        {
            string nl = (t.Name ?? "").ToLower();
            string dl = (t.Description ?? "").ToLower();
            var fnames = t.Features.Select(f => (f.Name ?? "").ToLower()).ToList();

            // Lentiviral
            if (fnames.Any(f => f.Contains("ltr") || f.Contains("psi") || f.Contains("wpre")) ||
                nl.Contains("plko") || nl.Contains("plenti") || nl.Contains("pcdh"))
                return "Lentiviral";

            // Mammalian
            if (fnames.Any(f => f.Contains("cmv") || f.Contains("ef1") || f.Contains("cag") ||
                                 f.Contains("sv40") || f.Contains("bgh")))
                return "Mammalian Expression";

            // Luciferase
            if (nl.Contains("pgl") || nl.Contains("prl") || nl.Contains("pnl") ||
                fnames.Any(f => f.Contains("luciferase") || f.Contains("luc2")))
                return "Luciferase Reporter";

            // E. coli expression
            if (nl.Contains("pet") || nl.Contains("pgex") || nl.Contains("pmal") || nl.Contains("pcold") ||
                fnames.Any(f => f.Contains("t7") && f.Contains("promoter")))
                return "E. coli Expression";

            // TA / TOPO cloning
            if (nl.Contains("pgem") || nl.Contains("pcr2") || nl.Contains("pcr-blunt") ||
                nl.Contains("topo") || nl.Contains("pjet") || nl.Contains("pta"))
                return "TA / TOPO Cloning";

            // E. coli cloning
            if (nl.Contains("puc") || nl.Contains("pbr") || nl.Contains("pbluescript"))
                return "E. coli Cloning";

            // Packaging
            if (nl.Contains("pmd2") || nl.Contains("pspax") || nl.Contains("pax2") ||
                dl.Contains("packaging") || dl.Contains("envelope"))
                return "Packaging";

            return "General";
        }
    }
}
