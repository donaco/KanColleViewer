using System.Windows;
using System.Windows.Controls;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// 最小化・最大化・閉じるボタンをまとめたコントロールです。
    /// Phase 4: MetroRadiance.UI.Controls.SystemButtons の完全新実装です。
    /// </summary>
    public class SystemButtons : Control
    {
        static SystemButtons()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SystemButtons),
                new FrameworkPropertyMetadata(typeof(SystemButtons)));
        }
    }
}
