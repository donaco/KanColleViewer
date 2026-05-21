using System;
using System.Windows.Input;

namespace Grabacr07.KanColleViewer.Infrastructure.Mvvm
{
    /// <summary>
    /// <see cref="ICommand"/> の汎用実装です。
    /// Livet の ViewModelCommand の代替として使用します。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute) : this(execute, null) { }

        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            this._execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this._canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => this._canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => this._execute();

        /// <summary>
        /// <see cref="CanExecuteChanged"/> を手動で発火します。
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// パラメーターを受け取る <see cref="ICommand"/> の汎用実装です。
    /// Livet の ListenerCommand&lt;T&gt; の代替として使用します。
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<T> execute) : this(execute, null) { }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute)
        {
            this._execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this._canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (parameter is T t) return this._canExecute?.Invoke(t) ?? true;
            if (parameter == null && !typeof(T).IsValueType) return this._canExecute?.Invoke(default) ?? true;
            return false;
        }

        public void Execute(object parameter)
        {
            if (parameter is T t) this._execute(t);
            else if (parameter == null && !typeof(T).IsValueType) this._execute(default);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
