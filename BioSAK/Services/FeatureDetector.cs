using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BioSAK.Models;

namespace BioSAK.Services
{
    /// <summary>
    /// 載體功能元件自動偵測器（精簡版）
    /// 
    /// 只偵測三類：
    /// 1. 主要修飾蛋白/報告基因（EGFP, His-tag, Myc-tag, Luciferase 等）
    /// 2. 啟動子（CMV, SV40, T7, T3, SP6, lac, CAG, EF-1α）
    /// 3. MCS（密集 unique single-cutter RE sites）
    /// </summary>
    public static class FeatureDetector
    {
        public static void AutoAnnotate(DnaConstruct construct,
            List<RestrictionEnzyme> enzymes = null, RestrictionEnzymeCutter cutter = null)
        {
            if (construct?.Sequence == null || construct.Sequence.Length < 50) return;
            string seq = construct.Sequence.ToUpper();
            var existing = construct.Features.ToList();

            // 1. 序列比對偵測
            foreach (var probe in _probes)
            {
                if (HasOverlap(existing, probe.Name, probe.Type)) continue;
                var hit = SearchBothStrands(seq, probe);
                if (hit != null)
                {
                    construct.Features.Add(new SequenceFeature
                    {
                        Name = probe.Name,
                        Type = probe.Type,
                        Start = hit.Value.s,
                        End = hit.Value.e,
                        IsReverse = hit.Value.rc
                    });
                }
            }

            // 2. MCS 偵測
            if (enzymes != null && cutter != null && !existing.Any(f => f.Type == FeatureType.MCS))
            {
                var mcs = DetectMCS(seq, enzymes, cutter, construct.IsCircular);
                if (mcs != null) construct.Features.Add(mcs);
            }
        }

        // ===============================================================
        //  探針資料庫（精簡）
        // ===============================================================

        private static readonly List<Probe> _probes = new List<Probe>
        {
            // ── Promoters ──
            P("CMV promoter", FeatureType.Promoter, 580,
              "GACATTGATTATTGACTAGTTATTAATAGTAATCAATTACGGGGTCATTAGTTCATAGCCC",
              "CATTGACGTCAATAATGACGTATGTTCCCATAGTAACGCCAATAGGGACTTTCCATTGACG",
              "CATCAAGTGTATCATATGCCAAGTACGCCCCCTATTGACGTCAATGACGG"),
            P("SV40 promoter", FeatureType.Promoter, 340,
              "ATCTCTATCACTGATAGGGAGTGGTAAACTCGACTTTAAAAGT",
              "GCCCAGTCTCTCATCTACTTTCATCCACAGTTGGCACC"),
            P("T7 promoter", FeatureType.Promoter, 25,
              "TAATACGACTCACTATAGGG"),
            P("T3 promoter", FeatureType.Promoter, 25,
              "AATTAACCCTCACTAAAGGG"),
            P("SP6 promoter", FeatureType.Promoter, 25,
              "ATTTAGGTGACACTATAGAA"),
            P("lac promoter", FeatureType.Promoter, 80,
              "TTTACACTTTATGCTTCCGGCTCGTATGTTGTGTGG",
              "AATTGTGAGCGGATAACAATTTCACACAGG"),
            P("CAG promoter", FeatureType.Promoter, 400,
              "CTCTAGAGCCTCTGCTAACCATGTTCATGCCTTCTTCTTTTTCCTACAG"),
            P("EF-1α promoter", FeatureType.Promoter, 250,
              "AACTTCTTTGGCTATGCGGGTGATGCTTTTTCCCTGT"),

            // ── Reporter Genes / Tags ──
            P("EGFP", FeatureType.Reporter, 720,
              "ATGGTGAGCAAGGGCGAGGAGCTGTTCACCGGGGTGGTGCCCATCCTGGTCGAGCTGGAC",
              "GGCAAGCTGCCCGTGCCCTGGCCCACCCTCGTGACCACCCTGACCTACGGCGTGCAGTGC"),
            P("mCherry", FeatureType.Reporter, 711,
              "ATGGTGAGCAAGGGCGAGGAGGATAACATGGCCATCATCAAGGAGTTCATGCGCTTCAAG"),
            P("tdTomato", FeatureType.Reporter, 1431,
              "ATGGTGAGCAAGGGCGAGGAGGTCATCAAAGAGTTCATGCGCTTCAAGGTGCGCATGGAG"),
            P("Firefly luciferase", FeatureType.Reporter, 1650,
              "ATGGAAGACGCCAAAAACATAAAGAAAGGCCCGGCGCCATTCTATCCTCTAGAGGATGGAA",
              "GAAGACATTCTTGGACAAATAGCTTACTACATCCTCGATATGCTGTCCCTTCTATGCCCGG"),
            P("Renilla luciferase", FeatureType.Reporter, 936,
              "ATGACTTCGAAAGTTTATGATCCAGAACAAAGGAAACGGATGATAACTGGTCCGCAGTGG"),
            P("NanoLuc", FeatureType.Reporter, 513,
              "ATGGTCTTCACACTCGAAGATTTCGTTGGGGACTGGCGACAGACAGCCG"),
            P("lacZ-alpha", FeatureType.Reporter, 400,
              "ATGACCATGATTACGCCAAGCTTGCATGCCTGCAGGTCGACTCTAGAGGATCCC"),
            P("His-tag", FeatureType.Tag, 24,
              "CACCATCACCATCACCAC", "CATCATCATCATCATCAT", "CATCACCATCACCATCAC"),
            P("FLAG-tag", FeatureType.Tag, 30,
              "GACTACAAAGACGATGACGATAAA", "GATTACAAGGATGACGACGATAAG"),
            P("Myc-tag", FeatureType.Tag, 36,
              "GAACAAAAACTCATCTCAGAAGAGGATCTG", "GAGCAGAAACTCATCTCTGAAGAGGATCTG"),
            P("HA-tag", FeatureType.Tag, 30,
              "TACCCATACGATGTTCCAGATTACGCT", "TACCCCTACGACGTGCCCGACTACGCC"),
            P("V5-tag", FeatureType.Tag, 45,
              "GGTAAGCCTATCCCTAACCCTCTCCTCGGTCTCGATTCTACG"),
            P("GST", FeatureType.Tag, 660,
              "ATGTCCCCTATACTAGGTTATTGGAAAATTAAGGGCCTTGTGCAACCCACTCGAC"),
            P("MBP", FeatureType.Tag, 1100,
              "ATGAAAATCGAAGAAGGTAAACTGGTAATCTGGATTAACGGCGATAAAGGCTATAACGGT"),
            P("Strep-tag II", FeatureType.Tag, 27,
              "TGGAGCCACCCGCAGTTCGAAAAA"),
            P("SUMO", FeatureType.Tag, 300,
              "ATGTCGGACTCAGAAGTCAATCAAGAAGCTAAGCCAGAGGTCAAGCCAG"),
        };

        private static Probe P(string name, FeatureType type, int len, params string[] seqs)
            => new Probe { Name = name, Type = type, Len = len, Seqs = seqs, MinSim = len < 30 ? 0.92 : 0.83 };

        private class Probe
        {
            public string Name; public FeatureType Type; public int Len;
            public double MinSim; public string[] Seqs;
        }

        // ===============================================================
        //  序列比對
        // ===============================================================

        private static (int s, int e, bool rc)? SearchBothStrands(string seq, Probe probe)
        {
            var fwd = SearchStrand(seq, probe);
            var rc = SearchStrand(RC(seq), probe);

            if (fwd == null && rc == null) return null;
            if (fwd != null && rc == null) return (fwd.Value.s, fwd.Value.e, false);
            if (fwd == null && rc != null)
            {
                int rs = seq.Length - rc.Value.e;
                int re = seq.Length - rc.Value.s;
                return (rs, re, true);
            }
            // 取較好的
            if (fwd.Value.sim >= rc.Value.sim)
                return (fwd.Value.s, fwd.Value.e, false);
            else
            {
                int rs = seq.Length - rc.Value.e;
                int re = seq.Length - rc.Value.s;
                return (rs, re, true);
            }
        }

        private static (int s, int e, double sim)? SearchStrand(string seq, Probe probe)
        {
            double best = 0; int bestPos = -1; string bestProbe = null;

            foreach (string p in probe.Seqs)
            {
                string pu = p.ToUpper();
                if (pu.Length > seq.Length) continue;

                // 快速：先找 8-mer seed
                string seed = pu.Substring(0, Math.Min(8, pu.Length));
                int from = 0;
                while (from <= seq.Length - pu.Length)
                {
                    int idx = seq.IndexOf(seed, from, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        // 每 probe/3 步長粗掃
                        for (int i = from; i <= seq.Length - pu.Length; i += Math.Max(1, pu.Length / 3))
                        {
                            double sim = Similarity(seq, i, pu);
                            if (sim > best) { best = sim; bestPos = i; bestProbe = pu; }
                        }
                        break;
                    }
                    // seed 命中，精搜附近
                    int lo = Math.Max(0, idx - 5);
                    int hi = Math.Min(seq.Length - pu.Length, idx + 5);
                    for (int i = lo; i <= hi; i++)
                    {
                        double sim = Similarity(seq, i, pu);
                        if (sim > best) { best = sim; bestPos = i; bestProbe = pu; }
                    }
                    from = idx + 1;
                }
            }

            if (best >= probe.MinSim && bestPos >= 0)
            {
                // 估算完整 feature 範圍
                int center = bestPos + (bestProbe?.Length ?? 0) / 2;
                int half = probe.Len / 2;
                int s = Math.Max(0, center - half);
                int e = Math.Min(seq.Length, center + half);
                return (s, e, best);
            }
            return null;
        }

        private static double Similarity(string seq, int start, string probe)
        {
            if (start + probe.Length > seq.Length) return 0;
            int m = 0;
            for (int i = 0; i < probe.Length; i++)
                if (seq[start + i] == probe[i] || seq[start + i] == 'N') m++;
            return (double)m / probe.Length;
        }

        private static string RC(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = s.Length - 1; i >= 0; i--)
                sb.Append(s[i] == 'A' ? 'T' : s[i] == 'T' ? 'A' : s[i] == 'G' ? 'C' : s[i] == 'C' ? 'G' : 'N');
            return sb.ToString();
        }

        // ===============================================================
        //  MCS 偵測
        // ===============================================================

        /// <summary>
        /// ≤200 bp 內有 ≥4 個 unique single-cut 6bp+ RE site → MCS
        /// </summary>
        public static SequenceFeature DetectMCS(string sequence, List<RestrictionEnzyme> enzymes,
            RestrictionEnzymeCutter cutter, bool isCircular)
        {
            var singles = FindUniqueSingleCutters(sequence, enzymes, cutter, isCircular);
            if (singles.Count < 4) return null;

            var sorted = singles.OrderBy(s => s.pos).ToList();
            int bestCount = 0, bestS = -1, bestE = -1;
            List<string> bestNames = null;

            for (int i = 0; i < sorted.Count; i++)
            {
                var inWin = new List<(string n, int p)>();
                for (int j = i; j < sorted.Count; j++)
                {
                    int span = sorted[j].pos + sorted[j].len - sorted[i].pos;
                    if (span > 200) break;
                    inWin.Add((sorted[j].name, sorted[j].pos));
                    if (inWin.Count >= 4 && inWin.Count > bestCount)
                    {
                        bestCount = inWin.Count;
                        bestS = sorted[i].pos;
                        bestE = sorted[j].pos + sorted[j].len;
                        bestNames = inWin.Select(w => w.n).ToList();
                    }
                }
            }

            if (bestCount >= 4)
                return new SequenceFeature
                {
                    Name = $"MCS ({string.Join(", ", bestNames.Take(6))})",
                    Type = FeatureType.MCS,
                    Start = Math.Max(0, bestS - 5),
                    End = Math.Min(sequence.Length, bestE + 5),
                    IsReverse = false
                };
            return null;
        }

        /// <summary>
        /// 找出所有 unique single-cutter（整條序列上只切 1 次的 6bp+ 酶）
        /// 公開方法，Cloning 推薦也會用到
        /// </summary>
        public static List<(string name, int pos, int len)> FindUniqueSingleCutters(
            string sequence, List<RestrictionEnzyme> enzymes, RestrictionEnzymeCutter cutter, bool isCircular)
        {
            var result = new List<(string name, int pos, int len)>();
            foreach (var enz in enzymes.Where(e => e.RecognitionSequence.Length >= 6 && !e.RecognitionSequence.Contains("N")))
            {
                try
                {
                    var sites = cutter.FindCutSites(sequence, enz, isCircular);
                    if (sites?.Count == 1)
                        result.Add((enz.Name, sites[0].Position, enz.RecognitionSequence.Length));
                }
                catch { }
            }
            return result;
        }

        private static bool HasOverlap(List<SequenceFeature> existing, string name, FeatureType type)
        {
            string nl = name.ToLower();
            return existing.Any(f =>
                f.Name.ToLower().Contains(nl.Split(' ')[0]) ||
                nl.Contains(f.Name.ToLower()));
        }
    }
}
