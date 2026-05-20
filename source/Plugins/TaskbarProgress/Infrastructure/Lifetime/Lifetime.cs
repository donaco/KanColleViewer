using System;
using System.Collections.Generic;
using StatefulModel;

namespace MetroTrilithon.Lifetime
{
    // MetroTrilithon.Lifetime の内製化 (Phase 1)
    public interface IDisposableHolder : IDisposable
    {
        ICollection<IDisposable> CompositeDisposable { get; }
    }

    public static class Disposable
    {
        public static IDisposable Create(Action dispose)
        {
            return new AnonymousDisposable(dispose);
        }

        private sealed class AnonymousDisposable : IDisposable
        {
            private bool _isDisposed;
            private readonly Action _dispose;

            public AnonymousDisposable(Action dispose) { this._dispose = dispose; }

            public void Dispose()
            {
                if (this._isDisposed) return;
                this._isDisposed = true;
                this._dispose();
            }
        }
    }

    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, IDisposableHolder holder) where T : IDisposable
        {
            if (holder == null) disposable.Dispose();
            else holder.CompositeDisposable.Add(disposable);
            return disposable;
        }

        public static T AddTo<T>(this T disposable, MultipleDisposable obj) where T : IDisposable
        {
            if (obj == null) disposable.Dispose();
            else obj.Add(disposable);
            return disposable;
        }
    }
}
