using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MetroTrilithon.Mvvm
{
    // MetroTrilithon.Mvvm の内製化 (Phase 1)
    public class Notifier : INotifyPropertyChanged
    {
        private event PropertyChangedEventHandler _propertyChanged;

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add { this._propertyChanged += value; }
            remove { this._propertyChanged -= value; }
        }

        protected virtual void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            this._propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class PropertyChangedEventListener : IDisposable
    {
        private readonly INotifyPropertyChanged _source;
        private readonly Dictionary<string, List<PropertyChangedEventHandler>> _handlers
            = new Dictionary<string, List<PropertyChangedEventHandler>>();
        private readonly PropertyChangedEventHandler _globalHandler;
        private bool _isDisposed;

        public PropertyChangedEventListener(INotifyPropertyChanged source)
        {
            this._source = source;
            this._source.PropertyChanged += this.OnPropertyChanged;
        }

        public PropertyChangedEventListener(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
            : this(source)
        {
            this._globalHandler = handler;
        }

        public void Add(string propertyName, PropertyChangedEventHandler handler)
        {
            if (!this._handlers.TryGetValue(propertyName, out var list))
            {
                list = new List<PropertyChangedEventHandler>();
                this._handlers[propertyName] = list;
            }
            list.Add(handler);
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            this._globalHandler?.Invoke(sender, e);
            if (e.PropertyName != null && this._handlers.TryGetValue(e.PropertyName, out var list))
                foreach (var h in list) h(sender, e);
        }

        public void Dispose()
        {
            if (this._isDisposed) return;
            this._isDisposed = true;
            this._source.PropertyChanged -= this.OnPropertyChanged;
        }
    }

    public static class PropertyChangedExtensions
    {
        public static IDisposable Subscribe(this INotifyPropertyChanged source, PropertyChangedEventHandler handler)
            => new PropertyChangedEventListener(source, handler);

        public static IDisposable Subscribe(this INotifyPropertyChanged source, Action<string> action)
            => new PropertyChangedEventListener(source, (sender, args) => action(args.PropertyName));

        public static ListenerWrapper Subscribe(this INotifyPropertyChanged source, string propertyName, Action action, bool immediately = true)
            => new ListenerWrapper(source).Subscribe(propertyName, action, immediately);

        public sealed class ListenerWrapper : IDisposable
        {
            private readonly PropertyChangedEventListener _listener;

            internal ListenerWrapper(INotifyPropertyChanged source)
            {
                this._listener = new PropertyChangedEventListener(source);
            }

            public ListenerWrapper Subscribe(string propertyName, Action action, bool immediately = true)
            {
                if (immediately) action();
                this._listener.Add(propertyName, (sender, args) => action());
                return this;
            }

            void IDisposable.Dispose() => this._listener.Dispose();
        }
    }
}
