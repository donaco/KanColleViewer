using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// Metro スタイルのウィンドウです。
    /// Phase 4: MetroRadiance.UI.Controls.MetroWindow の完全新実装です。
    /// WindowChrome (System.Windows.Shell) でカスタムタイトルバーを実現します。
    /// </summary>
    [TemplatePart(Name = PartResizeGrip, Type = typeof(FrameworkElement))]
    public class MetroWindow : Window
    {
        private const string PartResizeGrip = "PART_ResizeGrip";

        static MetroWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MetroWindow),
                new FrameworkPropertyMetadata(typeof(MetroWindow)));
        }

        private FrameworkElement _resizeGrip;
        private FrameworkElement _captionBar;

        // ── IsCaptionBar 添付プロパティ ────────────────────────────────

        public static readonly DependencyProperty IsCaptionBarProperty =
            DependencyProperty.RegisterAttached(
                "IsCaptionBar", typeof(bool), typeof(MetroWindow),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsCaptionBarChanged));

        public static void SetIsCaptionBar(FrameworkElement element, bool value)
            => element.SetValue(IsCaptionBarProperty, value);

        public static bool GetIsCaptionBar(FrameworkElement element)
            => (bool)element.GetValue(IsCaptionBarProperty);

        private static void OnIsCaptionBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var element = d as FrameworkElement;
            if (element == null) return;
            var window = Window.GetWindow(element) as MetroWindow;
            if (window == null) return;

            if ((bool)e.NewValue)
            {
                window._captionBar = element;
                element.Loaded += (_, __) => window.UpdateCaptionHeight();
                element.SizeChanged += (_, __) => window.UpdateCaptionHeight();
            }
            else if (window._captionBar == element)
            {
                window._captionBar = null;
            }
        }

        // ── IsRestoringWindowPlacement 依存関係プロパティ ─────────────

        public static readonly DependencyProperty IsRestoringWindowPlacementProperty =
            DependencyProperty.Register(
                nameof(IsRestoringWindowPlacement), typeof(bool), typeof(MetroWindow),
                new UIPropertyMetadata(false));

        public bool IsRestoringWindowPlacement
        {
            get => (bool)this.GetValue(IsRestoringWindowPlacementProperty);
            set => this.SetValue(IsRestoringWindowPlacementProperty, value);
        }

        // ── コンストラクタ ─────────────────────────────────────────────

        public MetroWindow()
        {
            // WindowChrome でカスタムタイトルバーを有効化
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight        = 0,
                ResizeBorderThickness = new Thickness(4),
                GlassFrameThickness  = new Thickness(0),
                CornerRadius         = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }

        // ── テンプレート適用 ───────────────────────────────────────────

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            this._resizeGrip = this.GetTemplateChild(PartResizeGrip) as FrameworkElement;
            if (this._resizeGrip != null)
            {
                this._resizeGrip.Visibility = this.ResizeMode == ResizeMode.CanResizeWithGrip
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                WindowChrome.SetIsHitTestVisibleInChrome(this._resizeGrip, true);
            }
        }

        // ── ウィンドウ状態 ─────────────────────────────────────────────

        protected override void OnActivated(System.EventArgs e)
        {
            base.OnActivated(e);
            if (this._captionBar != null) this._captionBar.Opacity = 1.0;
        }

        protected override void OnDeactivated(System.EventArgs e)
        {
            base.OnDeactivated(e);
            if (this._captionBar != null) this._captionBar.Opacity = 0.5;
        }

        // ── ウィンドウ位置の保存・復元 ────────────────────────────────

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (this.IsRestoringWindowPlacement)
            {
                WindowPlacementHelper.Restore(this);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!e.Cancel && this.IsRestoringWindowPlacement)
            {
                WindowPlacementHelper.Save(this);
            }
        }

        // ── 内部ユーティリティ ─────────────────────────────────────────

        private void UpdateCaptionHeight()
        {
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null && this._captionBar != null)
            {
                chrome.CaptionHeight = this._captionBar.ActualHeight;
            }
        }
    }
}
