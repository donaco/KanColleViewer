// MetroTrilithon.Mvvm (WindowViewModel / DisplayViewModel) の内製化 (Phase 1)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Shell;
using Livet;
using Livet.Messaging;
using Livet.Messaging.Windows;
using MetroTrilithon.Serialization;
using MetroTrilithon.Threading.Tasks;
using MetroTrilithon.UI.Interactivity;

namespace MetroTrilithon.Mvvm
{
    /// <summary>
    /// ウィンドウにアタッチされる ViewModel の基底クラスです。
    /// </summary>
    public class WindowViewModel : ViewModel
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
            if (this.WindowState == WindowState.Minimized) this.SendWindowAction(WindowAction.Normal);
            this.SendWindowAction(WindowAction.Active);
        }

        public virtual void Close()
        {
            if (this.IsClosed) return;
            this.SendWindowAction(WindowAction.Close);
        }

        protected override void Dispose(bool disposing)
        {
            this.IsClosed = true;
            this.IsInitialized = false;
            base.Dispose(disposing);
        }

        protected void SendWindowAction(WindowAction action)
            => this.Messenger.Raise(new WindowActionMessage(action, "Window.WindowAction"));

        protected void Transition(ViewModel viewModel, Type windowType, TransitionMode mode, bool isOwned)
        {
            var message = new TransitionMessage(windowType, viewModel, mode, isOwned ? "Window.Transition.Child" : "Window.Transition");
            this.Messenger.Raise(message);
        }

        protected void UpdateTaskbar(TaskbarItemProgressState state, double value)
        {
            var message = new TaskbarMessage("Window.UpdateTaskbar")
            {
                ProgressState = state,
                ProgressValue = value,
            };
            this.Messenger.RaiseAsync(message).Forget();
        }

        protected void InvokeOnUIDispatcher(Action action)
            => DispatcherHelper.UIDispatcher.BeginInvoke(action);
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

    public class DisplayViewModel<T> : ViewModel
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
