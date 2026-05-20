using System;
using System.Collections.Generic;
using Livet;

namespace MetroTrilithon.Lifetime
{
    // MetroTrilithon.Lifetime の内製化 (Phase 1)
    public static class DisposableExtensions
    {
        /// <summary>
        /// <see cref="IDisposable"/> オブジェクトを、指定した <see cref="IDisposableHolder.CompositeDisposable"/> に追加します。
        /// </summary>
        public static T AddTo<T>(this T disposable, IDisposableHolder holder) where T : IDisposable
        {
            if (holder == null)
            {
                disposable.Dispose();
            }
            else
            {
                holder.CompositeDisposable.Add(disposable);
            }
            return disposable;
        }

        /// <summary>
        /// <see cref="IDisposable"/> オブジェクトを <see cref="LivetCompositeDisposable"/> に追加します。
        /// </summary>
        public static T AddTo<T>(this T disposable, LivetCompositeDisposable compositeDisposable) where T : IDisposable
        {
            compositeDisposable?.Add(disposable);
            return disposable;
        }
    }
}
