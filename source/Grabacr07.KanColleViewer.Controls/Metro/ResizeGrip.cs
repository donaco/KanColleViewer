using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// ウィンドウ右下のリサイズグリップコントロールです。
    /// Phase 4: MetroRadiance.UI.Controls.ResizeGrip の完全新実装です。
    /// Win32 Interop を使わず WindowChrome のヒットテストで実現します。
    /// </summary>
    public class ResizeGrip : ContentControl
    {
        static ResizeGrip()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ResizeGrip),
                new FrameworkPropertyMetadata(typeof(ResizeGrip)));
        }

        public ResizeGrip()
        {
            // WindowChrome 内でヒットテスト有効化（リサイズ操作を受け取る）
            WindowChrome.SetIsHitTestVisibleInChrome(this, true);
            WindowChrome.SetResizeGripDirection(this, ResizeGripDirection.BottomRight);
        }
    }
}
