// MetroTrilithon.Mvvm (WindowViewModel / DisplayViewModel) の内製化 (Phase 1)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Shell;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using MetroTrilithon.Serialization;

namespace MetroTrilithon.Mvvm
{
    /// <summary>
    /// ウィンドウにアタッチされる ViewModel の基底クラスです。
    /// </summary>
    public class WindowViewModel : ViewModelBase
    {
        #region Title

        private string _Title;
        public string Title
        {
            get { return this._Title; }
            set { if (this._Title != value) { this._Title = value; this.RaisePropertyChanged(); } }
        }

        #endregion

        #region CanClose

        private bool _CanClose = true;
        public virtual bool CanClose
        {
            get { return this._CanClose; }
            set { if (this._CanClose != value) { this._CanClose = value; this.RaisePropertyChanged(); } }
        }

        #endregion

        #region IsClosed

        private bool _IsClosed;
        public bool IsClosed
        {
            get { return this._IsClosed; }
            private set { if (this._IsClosed != value) { this._IsClosed = value; this.RaisePropertyChanged(); } }
        }

        #endregion

        public bool IsInitialized { get; private set; }
        public bool DialogResult { get; protected set; }
        public WindowState WindowState { get; set; }

        /// <summary>ウィンドウを閉じるよう View に要求するイベントです。</summary>
        public event EventHandler CloseRequested;

        /// <summary>ウィンドウをアクティブ化するよう View に要求するイベントです。</summary>
        public event EventHandler ActivateRequested;

        /// <summary>新しいウィンドウへの遷移を View に要求するイベントです。</summary>
        public event EventHandler<TransitionRequestedEventArgs> TransitionRequested;

        /// <summary>タスクバーの状態更新を View に要求するイベントです。</summary>
        public event EventHandler<TaskbarUpdateEventArgs> TaskbarUpdateRequested;

        /// <summary>スクリーンショット保存を View に要求するイベントです。</summary>
        public event EventHandler<ScreenshotRequestedEventArgs> ScreenshotRequested;

        /// <summary>WebBrowser のズームリセットを View に要求するイベントです。</summary>
        public event EventHandler ZoomRequested;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Initialize()
        {
            if (this.IsClosed) return;
            this.DialogResult = false;
            this.InitializeCore();
            this.IsInitialized = true;
        }

        protected virtual void InitializeCore() { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void CloseCanceledCallback() => this.CloseCanceledCallbackCore();

        protected virtual void CloseCanceledCallbackCore() { }

        public virtual void Activate()
        {
            if (this.WindowState == WindowState.Minimized) this.RequestNormal();
            this.ActivateRequested?.Invoke(this, EventArgs.Empty);
        }

        public virtual void Close()
        {
            if (this.IsClosed) return;
            this.CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            this.IsClosed = true;
            this.IsInitialized = false;
            base.Dispose(disposing);
        }

        protected void RaiseCanCloseChanged()
        {
            this.RaisePropertyChanged(nameof(this.CanClose));
        }

        private void RequestNormal()
        {
            // 最小化からの復元は WindowState を変更することで対応
            this.WindowState = WindowState.Normal;
            this.RaisePropertyChanged(nameof(this.WindowState));
        }

        protected void SendTransition(object viewModel, Type windowType, bool isOwned)
        {
            this.TransitionRequested?.Invoke(this, new TransitionRequestedEventArgs(viewModel, windowType, isOwned));
        }

        protected void UpdateTaskbar(TaskbarItemProgressState state, double value)
        {
            this.TaskbarUpdateRequested?.Invoke(this, new TaskbarUpdateEventArgs(state, value));
        }

        protected void RaiseScreenshotRequested(string path, object format)
        {
            this.ScreenshotRequested?.Invoke(this, new ScreenshotRequestedEventArgs(path, format));
        }

        public void RaiseZoomRequested()
        {
            this.ZoomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>ウィンドウ遷移要求のイベント引数です。</summary>
    public class TransitionRequestedEventArgs : EventArgs
    {
        public object ViewModel { get; }
        public Type WindowType { get; }
        public bool IsOwned { get; }

        public TransitionRequestedEventArgs(object viewModel, Type windowType, bool isOwned)
        {
            this.ViewModel = viewModel;
            this.WindowType = windowType;
            this.IsOwned = isOwned;
        }
    }

    /// <summary>タスクバー更新要求のイベント引数です。</summary>
    public class TaskbarUpdateEventArgs : EventArgs
    {
        public TaskbarItemProgressState State { get; }
        public double Value { get; }

        public TaskbarUpdateEventArgs(TaskbarItemProgressState state, double value)
        {
            this.State = state;
            this.Value = value;
        }
    }

    /// <summary>スクリーンショット要求のイベント引数です。</summary>
    public class ScreenshotRequestedEventArgs : EventArgs
    {
        public string Path { get; }
        public object Format { get; }

        public ScreenshotRequestedEventArgs(string path, object format)
        {
            this.Path = path;
            this.Format = format;
        }
    }

    /// <summary>
    /// 値と表示文字列のペアを保持する ViewModel です。
    /// </summary>
    public static class DisplayViewModel
    {
        public static DisplayViewModel<T> Create<T>(T value, string display)
            => new DisplayViewModel<T> { Value = value, Display = display };

        public static DisplayViewModel<T> ToDefaultDisplay<T>(this SerializableProperty<T> property, string display)
            => new DisplayViewModel<T> { Value = property.Default, Display = display };

        public static IEnumerable<DisplayViewModel<TResult>> ToDisplay<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, TResult> valueSelector,
            Func<TSource, string> displaySelector)
        {
            foreach (var item in source)
                yield return new DisplayViewModel<TResult> { Value = valueSelector(item), Display = displaySelector(item) };
        }
    }

    public class DisplayViewModel<T> : ViewModelBase
    {
        #region Value

        private T _Value;
        public T Value
        {
            get { return this._Value; }
            set { if (!Equals(this._Value, value)) { this._Value = value; this.RaisePropertyChanged(); } }
        }

        #endregion

        #region Display

        private string _Display;
        public string Display
        {
            get { return this._Display; }
            set { if (this._Display != value) { this._Display = value; this.RaisePropertyChanged(); } }
        }

        #endregion

        public static implicit operator T(DisplayViewModel<T> dvm) => dvm.Value;
        public override string ToString() => this.Display;
    }
}
