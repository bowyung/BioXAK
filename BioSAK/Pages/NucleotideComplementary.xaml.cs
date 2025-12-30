using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BioSAK
{
    public partial class NucleotideComplementary : Page
    {
        private bool isDNA = true; // 追蹤目前模式

        public NucleotideComplementary()
        {
            InitializeComponent();
        }

        // 當點擊 DNA RadioButton
        private void DNA_Click(object sender, RoutedEventArgs e)
        {
            isDNA = true;
            UpdateCalculation(); // 切換模式時立即重新計算
        }

        // 當點擊 RNA RadioButton
        private void RNA_Click(object sender, RoutedEventArgs e)
        {
            isDNA = false;
            UpdateCalculation(); // 切換模式時立即重新計算
        }

        private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 驗證輸入：允許 DNA(ATGCN) 或 RNA(AUCGN)
            
            string allowed = "ATGCNUatgcnu";

            // 如果要嚴格限制當下模式：
            // string allowed = isDNA ? "ATGCNatgcn" : "AUCGNaucgn";

            foreach (char c in e.Text)
            {
                if (!allowed.Contains(c))
                {
                    e.Handled = true; // 阻止輸入
                    return;
                }
            }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 控制提示文字顯示
            Hint.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Hidden;

            // 執行計算
            UpdateCalculation();
        }

        // 將計算邏輯獨立出來，方便重複呼叫
        private void UpdateCalculation()
        {
            if (Input == null) return; // 避免初始化時報錯

            string inputSeq = Input.Text.ToUpper().Trim();

            // 如果沒輸入，清空結果
            if (string.IsNullOrEmpty(inputSeq))
            {
                Reverse.Text = "";
                Complement.Text = "";
                ReverseComplement.Text = "";
                return;
            }

            // 1. Reverse
            Reverse.Text = GetReverse(inputSeq);

            // 2. Complement & Reverse Complement
            if (isDNA)
            {
                Complement.Text = GetComplement(inputSeq);
                ReverseComplement.Text = GetReverseComplement(inputSeq);
            }
            else
            {
                Complement.Text = GetRNAComplement(inputSeq);
                ReverseComplement.Text = GetRNAReverseComplement(inputSeq);
            }
        }


        private static string GetReverse(string input)
        {
            char[] reversed = input.ToCharArray();
            Array.Reverse(reversed);
            return new string(reversed);
        }

        private static string GetComplement(string input)
        {
            StringBuilder complement = new StringBuilder();
            foreach (char baseChar in input)
            {
                switch (baseChar)
                {
                    case 'A': complement.Append('T'); break;
                    case 'T': complement.Append('A'); break;
                    case 'C': complement.Append('G'); break;
                    case 'G': complement.Append('C'); break;
                    case 'N': complement.Append('N'); break;
                    case 'U': complement.Append('A'); break; // 容錯處理
                    default: complement.Append('?'); break;
                }
            }
            return complement.ToString();
        }

        private static string GetRNAComplement(string input)
        {
            StringBuilder complement = new StringBuilder();
            foreach (char baseChar in input)
            {
                switch (baseChar)
                {
                    case 'A': complement.Append('U'); break;
                    case 'U': complement.Append('A'); break;
                    case 'C': complement.Append('G'); break;
                    case 'G': complement.Append('C'); break;
                    case 'N': complement.Append('N'); break;
                    case 'T': complement.Append('A'); break; // 容錯處理
                    default: complement.Append('?'); break;
                }
            }
            return complement.ToString();
        }

        private static string GetReverseComplement(string input)
        {
            // 先反轉
            char[] arr = input.ToCharArray();
            Array.Reverse(arr);
            string reversed = new string(arr);

            // 再取互補 (直接呼叫上面的 GetComplement 邏輯，避免重複 switch case)
            // 但因為您原本的方法是寫在一個大方法裡，維持您的寫法也可以：
            StringBuilder sb = new StringBuilder();
            foreach (char c in reversed)
            {
                switch (c)
                {
                    case 'A': sb.Append('T'); break;
                    case 'T': sb.Append('A'); break;
                    case 'C': sb.Append('G'); break;
                    case 'G': sb.Append('C'); break;
                    case 'N': sb.Append('N'); break;
                    default: sb.Append('?'); break;
                }
            }
            return sb.ToString();
        }

        private static string GetRNAReverseComplement(string input)
        {
            char[] arr = input.ToCharArray();
            Array.Reverse(arr);
            string reversed = new string(arr);

            StringBuilder sb = new StringBuilder();
            foreach (char c in reversed)
            {
                switch (c)
                {
                    case 'A': sb.Append('U'); break;
                    case 'U': sb.Append('A'); break;
                    case 'C': sb.Append('G'); break;
                    case 'G': sb.Append('C'); break;
                    case 'N': sb.Append('N'); break;
                    default: sb.Append('?'); break;
                }
            }
            return sb.ToString();
        }
    }
}