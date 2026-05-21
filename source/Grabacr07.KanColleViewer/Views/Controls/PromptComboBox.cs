using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grabacr07.KanColleViewer.Views.Controls
{
    /// <summary>
    /// 未選択時にプロンプト（ヒント）テキストを表示できる ComboBox です。
    /// Phase 4: MetroRadiance.UI.Controls.PromptComboBox の代替実装です。
    /// </summary>
    public class PromptComboBox : ComboBox
    {
        static PromptComboBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PromptComboBox),
                new FrameworkPropertyMetadata(typeof(PromptComboBox)));
        }

        // ── Prompt ────────────────────────────────────────────────────

        public static readonly DependencyProperty PromptProperty =
            DependencyProperty.Register(
                nameof(Prompt), typeof(string), typeof(PromptComboBox),
                new UIPropertyMetadata(string.Empty));

        public string Prompt
        {
            get => (string)this.GetValue(PromptProperty);
            set => this.SetValue(PromptProperty, value);
        }

        // ── PromptBrush ───────────────────────────────────────────────

        public static readonly DependencyProperty PromptBrushProperty =
            DependencyProperty.Register(
                nameof(PromptBrush), typeof(Brush), typeof(PromptComboBox),
                new UIPropertyMetadata(Brushes.Gray));

        public Brush PromptBrush
        {
            get => (Brush)this.GetValue(PromptBrushProperty);
            set => this.SetValue(PromptBrushProperty, value);
        }
    }
}
