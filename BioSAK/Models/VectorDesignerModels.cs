using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace BioSAK.Models
{
    // ========== 末端類型 ==========

    /// <summary>
    /// DNA 片段末端描述（sticky end / blunt end）
    /// </summary>
    public class DnaEnd
    {
        /// <summary>突出序列（blunt end 為空字串）</summary>
        public string OverhangSequence { get; set; } = "";

        /// <summary>突出方向：5' 或 3'（blunt 時為 None）</summary>
        public OverhangDirection Direction { get; set; } = OverhangDirection.None;

        /// <summary>產生此末端的酶（null 表示原始末端或人工末端）</summary>
        public string EnzymeName { get; set; }

        public bool IsBlunt => Direction == OverhangDirection.None || string.IsNullOrEmpty(OverhangSequence);

        /// <summary>
        /// 判斷兩個末端是否可連接
        /// </summary>
        public bool IsCompatibleWith(DnaEnd other)
        {
            if (other == null) return false;

            // 兩端都是 blunt → 可連接
            if (this.IsBlunt && other.IsBlunt) return true;

            // 一個 blunt 一個不是 → 不可連接
            if (this.IsBlunt || other.IsBlunt) return false;

            // 都是 sticky：方向相反且序列互補
            if (this.Direction == other.Direction) return false;

            return AreComplementary(this.OverhangSequence, other.OverhangSequence);
        }

        private static bool AreComplementary(string seq1, string seq2)
        {
            if (seq1.Length != seq2.Length) return false;

            for (int i = 0; i < seq1.Length; i++)
            {
                if (!IsComplement(seq1[i], seq2[seq2.Length - 1 - i]))
                    return false;
            }
            return true;
        }

        private static bool IsComplement(char a, char b)
        {
            a = char.ToUpper(a);
            b = char.ToUpper(b);
            return (a == 'A' && b == 'T') || (a == 'T' && b == 'A') ||
                   (a == 'G' && b == 'C') || (a == 'C' && b == 'G');
        }

        public override string ToString()
        {
            if (IsBlunt) return "Blunt";
            string dir = Direction == OverhangDirection.FivePrime ? "5'" : "3'";
            string enzyme = string.IsNullOrEmpty(EnzymeName) ? "" : $" ({EnzymeName})";
            return $"{dir} {OverhangSequence}{enzyme}";
        }
    }

    public enum OverhangDirection
    {
        None,       // Blunt
        FivePrime,  // 5' overhang
        ThreePrime  // 3' overhang
    }

    // ========== 序列功能區域 ==========

    /// <summary>
    /// 序列上的功能註解（promoter、resistance、MCS 等）
    /// </summary>
    public class SequenceFeature : INotifyPropertyChanged
    {
        private string _name;
        private int _start;
        private int _end;
        private bool _isReverse;
        private FeatureType _type;
        private Color _color;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>起始位置（0-based）</summary>
        public int Start
        {
            get => _start;
            set { _start = value; OnPropertyChanged(); OnPropertyChanged(nameof(Length)); }
        }

        /// <summary>結束位置（0-based, exclusive）</summary>
        public int End
        {
            get => _end;
            set { _end = value; OnPropertyChanged(); OnPropertyChanged(nameof(Length)); }
        }

        /// <summary>是否在反義股上</summary>
        public bool IsReverse
        {
            get => _isReverse;
            set { _isReverse = value; OnPropertyChanged(); }
        }

        public FeatureType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); if (_color == default) _color = GetDefaultColor(value); }
        }

        public Color Color
        {
            get => _color == default ? GetDefaultColor(_type) : _color;
            set { _color = value; OnPropertyChanged(); }
        }

        public int Length => End > Start ? End - Start : 0;

        /// <summary>格式化的位置顯示</summary>
        public string PositionDisplay => IsReverse
            ? $"complement({Start + 1}..{End})"
            : $"{Start + 1}..{End}";

        private static Color GetDefaultColor(FeatureType type)
        {
            switch (type)
            {
                case FeatureType.Promoter: return Color.FromRgb(76, 175, 80);     // 綠色
                case FeatureType.Terminator: return Color.FromRgb(244, 67, 54);    // 紅色
                case FeatureType.Resistance: return Color.FromRgb(255, 152, 0);    // 橙色
                case FeatureType.Origin: return Color.FromRgb(156, 39, 176);       // 紫色
                case FeatureType.MCS: return Color.FromRgb(33, 150, 243);          // 藍色
                case FeatureType.Gene: return Color.FromRgb(0, 150, 136);          // 青色
                case FeatureType.Reporter: return Color.FromRgb(255, 235, 59);     // 黃色
                case FeatureType.Tag: return Color.FromRgb(121, 85, 72);           // 棕色
                case FeatureType.Misc: return Color.FromRgb(158, 158, 158);        // 灰色
                default: return Color.FromRgb(158, 158, 158);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public enum FeatureType
    {
        Promoter,
        Terminator,
        Resistance,
        Origin,
        MCS,
        Gene,
        Reporter,
        Tag,
        Enhancer,
        PolyA,
        Misc
    }

    // ========== DNA 構建物 ==========

    /// <summary>
    /// 代表一個 DNA 分子（載體或片段）
    /// </summary>
    public class DnaConstruct : INotifyPropertyChanged
    {
        private static int _nextId = 1;
        private string _name;
        private string _sequence;
        private bool _isCircular;

        public int Id { get; }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Sequence
        {
            get => _sequence;
            set
            {
                _sequence = CleanSequence(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Length));
                OnPropertyChanged(nameof(LengthDisplay));
            }
        }

        public bool IsCircular
        {
            get => _isCircular;
            set { _isCircular = value; OnPropertyChanged(); OnPropertyChanged(nameof(TopologyDisplay)); }
        }

        /// <summary>序列長度 (bp)</summary>
        public int Length => Sequence?.Length ?? 0;

        /// <summary>長度顯示文字</summary>
        public string LengthDisplay => $"{Length} bp";

        /// <summary>拓撲顯示文字</summary>
        public string TopologyDisplay => IsCircular ? "Circular" : "Linear";

        /// <summary>功能註解列表</summary>
        public ObservableCollection<SequenceFeature> Features { get; set; } = new ObservableCollection<SequenceFeature>();

        /// <summary>5' 末端（僅線性分子有效）</summary>
        public DnaEnd End5 { get; set; }

        /// <summary>3' 末端（僅線性分子有效）</summary>
        public DnaEnd End3 { get; set; }

        /// <summary>此構建物源自哪個載體（用於追蹤歷史）</summary>
        public string SourceName { get; set; }

        /// <summary>顯示用摘要</summary>
        public string Summary
        {
            get
            {
                string topo = IsCircular ? "⭕" : "➡️";
                string ends = "";
                if (!IsCircular)
                {
                    string e5 = End5 != null ? End5.ToString() : "—";
                    string e3 = End3 != null ? End3.ToString() : "—";
                    ends = $" [{e5} | {e3}]";
                }
                return $"{topo} {Name} ({Length} bp){ends}";
            }
        }

        public DnaConstruct()
        {
            Id = _nextId++;
        }

        public DnaConstruct(string name, string sequence, bool isCircular) : this()
        {
            Name = name;
            Sequence = sequence;
            IsCircular = isCircular;
        }

        /// <summary>
        /// 取得子序列（支援環狀跨越原點）
        /// </summary>
        public string GetSubSequence(int start, int end)
        {
            if (string.IsNullOrEmpty(Sequence)) return "";

            if (end > start)
            {
                return Sequence.Substring(start, end - start);
            }
            else if (IsCircular && end <= start)
            {
                // 跨越原點
                return Sequence.Substring(start) + Sequence.Substring(0, end);
            }
            return "";
        }

        /// <summary>
        /// 深拷貝
        /// </summary>
        public DnaConstruct Clone()
        {
            var clone = new DnaConstruct
            {
                Name = this.Name + " (copy)",
                Sequence = this.Sequence,
                IsCircular = this.IsCircular,
                SourceName = this.SourceName,
                End5 = this.End5 != null ? new DnaEnd
                {
                    OverhangSequence = this.End5.OverhangSequence,
                    Direction = this.End5.Direction,
                    EnzymeName = this.End5.EnzymeName
                } : null,
                End3 = this.End3 != null ? new DnaEnd
                {
                    OverhangSequence = this.End3.OverhangSequence,
                    Direction = this.End3.Direction,
                    EnzymeName = this.End3.EnzymeName
                } : null
            };

            foreach (var feature in Features)
            {
                clone.Features.Add(new SequenceFeature
                {
                    Name = feature.Name,
                    Start = feature.Start,
                    End = feature.End,
                    IsReverse = feature.IsReverse,
                    Type = feature.Type,
                    Color = feature.Color
                });
            }
            return clone;
        }

        private static string CleanSequence(string seq)
        {
            if (string.IsNullOrEmpty(seq)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in seq.ToUpper())
            {
                if ("ATGCRYKMSWBDHVN".Contains(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ========== 載體模板 ==========

    /// <summary>
    /// 載體模板（用於載體庫）
    /// </summary>
    public class VectorTemplate
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }  // E.coli, Mammalian, Lentiviral, etc.
        public int Size { get; set; }
        public string Sequence { get; set; }
        public List<SequenceFeature> Features { get; set; } = new List<SequenceFeature>();

        /// <summary>建立 DnaConstruct 實例</summary>
        public DnaConstruct ToConstruct()
        {
            var construct = new DnaConstruct
            {
                Name = Name,
                Sequence = Sequence,
                IsCircular = true,
                SourceName = Name
            };

            foreach (var f in Features)
            {
                construct.Features.Add(new SequenceFeature
                {
                    Name = f.Name,
                    Start = f.Start,
                    End = f.End,
                    IsReverse = f.IsReverse,
                    Type = f.Type,
                    Color = f.Color
                });
            }

            return construct;
        }

        public override string ToString() => $"{Name} ({Size} bp) - {Description}";
    }
}
