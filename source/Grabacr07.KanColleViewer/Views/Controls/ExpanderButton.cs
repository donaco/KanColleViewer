using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Grabacr07.KanColleViewer.Views.Controls
{
    /// <summary>
    /// コンテンツの展開・折りたたみを切り替える ToggleButton です。
    /// Phase 4: MetroRadiance.UI.Controls.ExpanderButton の代替実装です。
    /// </summary>
    public class ExpanderButton : ToggleButton
    {
        static ExpanderButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ExpanderButton),
                new FrameworkPropertyMetadata(typeof(ExpanderButton)));
        }

        // ── Direction ─────────────────────────────────────────────────

        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register(
                nameof(Direction), typeof(ExpandDirection), typeof(ExpanderButton),
                new UIPropertyMetadata(ExpandDirection.Down));

        public ExpandDirection Direction
        {
            get => (ExpandDirection)this.GetValue(DirectionProperty);
            set => this.SetValue(DirectionProperty, value);
        }
    }
}
