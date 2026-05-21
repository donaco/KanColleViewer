using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// ウィンドウ操作を示す識別子を定義します。
    /// Phase 4: MetroRadiance.UI.Controls.WindowAction の代替実装です。
    /// </summary>
    public enum WindowAction
    {
        None,
        Active,
        Close,
        Normalize,
        Maximize,
        Minimize,
        OpenSystemMenu,
    }

    /// <summary>
    /// キャプションボタンのモードを示す識別子を定義します。
    /// Phase 4: MetroRadiance.UI.Controls.CaptionButtonMode の代替実装です。
    /// </summary>
    public enum CaptionButtonMode
    {
        Normal,
        Toggle,
    }

    /// <summary>
    /// ウィンドウのキャプション部分で使用するボタンコントロールを表します。
    /// Phase 4: MetroRadiance.UI.Controls.CaptionButton の完全新実装です。
    /// </summary>
    public class CaptionButton : ButtonBase
    {
        static CaptionButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(CaptionButton),
                new FrameworkPropertyMetadata(typeof(CaptionButton)));
        }

        private Window _owner;

        // ── WindowAction ───────────────────────────────────────────────

        public static readonly DependencyProperty WindowActionProperty =
            DependencyProperty.Register(
                nameof(WindowAction), typeof(WindowAction), typeof(CaptionButton),
                new UIPropertyMetadata(WindowAction.None));

        public WindowAction WindowAction
        {
            get => (WindowAction)this.GetValue(WindowActionProperty);
            set => this.SetValue(WindowActionProperty, value);
        }

        // ── Mode ───────────────────────────────────────────────────────

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode), typeof(CaptionButtonMode), typeof(CaptionButton),
                new UIPropertyMetadata(CaptionButtonMode.Normal));

        public CaptionButtonMode Mode
        {
            get => (CaptionButtonMode)this.GetValue(ModeProperty);
            set => this.SetValue(ModeProperty, value);
        }

        // ── IsChecked ─────────────────────────────────────────────────

        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked), typeof(bool), typeof(CaptionButton),
                new UIPropertyMetadata(false));

        public bool IsChecked
        {
            get => (bool)this.GetValue(IsCheckedProperty);
            set => this.SetValue(IsCheckedProperty, value);
        }

        // ── 初期化・クリック ───────────────────────────────────────────

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // WindowChrome 内でヒットテスト有効化
            WindowChrome.SetIsHitTestVisibleInChrome(this, true);

            this._owner = Window.GetWindow(this);
            if (this._owner != null)
            {
                this._owner.StateChanged += (_, __) => this.UpdateVisibility();
                this.UpdateVisibility();
            }
        }

        protected override void OnClick()
        {
            this.InvokeWindowAction();

            if (this.Mode == CaptionButtonMode.Toggle)
                this.IsChecked = !this.IsChecked;

            base.OnClick();
        }

        private void InvokeWindowAction()
        {
            var window = this._owner ?? Window.GetWindow(this);
            if (window == null) return;

            switch (this.WindowAction)
            {
                case WindowAction.Close:     window.Close();                     break;
                case WindowAction.Maximize:  window.WindowState = WindowState.Maximized; break;
                case WindowAction.Minimize:  window.WindowState = WindowState.Minimized; break;
                case WindowAction.Normalize: window.WindowState = WindowState.Normal;    break;
                case WindowAction.Active:    window.Activate();                   break;
            }
        }

        private void UpdateVisibility()
        {
            if (this._owner == null) return;

            switch (this.WindowAction)
            {
                case WindowAction.Maximize:
                    this.Visibility = this._owner.WindowState != WindowState.Maximized
                        ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case WindowAction.Normalize:
                    this.Visibility = this._owner.WindowState != WindowState.Normal
                        ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case WindowAction.Minimize:
                    this.Visibility = this._owner.WindowState != WindowState.Minimized
                        ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }
    }
}
