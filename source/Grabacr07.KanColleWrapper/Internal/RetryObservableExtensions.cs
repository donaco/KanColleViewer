using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Internal
{
	public static class RetryObservableExtensions
	{
		// svdata 正規化ロジック（Extensions.NormalizeSvDataString と互換）
		public static string NormalizeSvDataString(string s)
		{
			if (string.IsNullOrEmpty(s)) return null;

			// svdata= を取り除く（存在すれば）
			var t = s.Replace("svdata=", "");

			// 最初の '{' を探す（throw 1; プレフィックス等を排除）
			var first = t.IndexOf('{');
			if (first < 0) return null;

			// 波括弧でバランスをとって切り出す（文字列リテラル内の '{' '}' を無視、エスケープも考慮）
			int depth = 0;
			bool inString = false;
			bool escape = false;
			int i;
			for (i = first; i < t.Length; i++)
			{
				char ch = t[i];

				if (escape)
				{
					// 直前がバックスラッシュによるエスケープ → 文字列内の特殊文字は無視して継続
					escape = false;
					continue;
				}

				if (ch == '\\')
				{
					// 次の文字はエスケープされる
					escape = true;
					continue;
				}

				if (ch == '"')
				{
					// 文字列リテラルの開始/終了をトグル
					inString = !inString;
					continue;
				}

				if (inString)
				{
					// 文字列内にある波括弧は無視
					continue;
				}

				if (ch == '{')
				{
					depth++;
				}
				else if (ch == '}')
				{
					depth--;
					if (depth == 0)
					{
						i++; // include this closing brace
						break;
					}
				}
			}

			if (depth != 0) return null;

			return t.Substring(first, i - first).Trim();
		}

		/// <summary>
		/// When catched exception, do onError action and repeat observable sequence.
		/// </summary>
		public static IObservable<TSource> OnErrorRetry<TSource, TException>(
			this IObservable<TSource> source, Action<TException> onError)
			where TException : Exception
		{
			return source.OnErrorRetry(onError, TimeSpan.Zero);
		}

		/// <summary>
		/// When catched exception, do onError action and repeat observable sequence after delay time.
		/// </summary>
		public static IObservable<TSource> OnErrorRetry<TSource, TException>(
			this IObservable<TSource> source, Action<TException> onError, TimeSpan delay)
			where TException : Exception
		{
			return source.OnErrorRetry(onError, int.MaxValue, delay);
		}

		/// <summary>
		/// When catched exception, do onError action and repeat observable sequence during within retryCount.
		/// </summary>
		public static IObservable<TSource> OnErrorRetry<TSource, TException>(
			this IObservable<TSource> source, Action<TException> onError, int retryCount)
			where TException : Exception
		{
			return source.OnErrorRetry(onError, retryCount, TimeSpan.Zero);
		}

		/// <summary>
		/// When catched exception, do onError action and repeat observable sequence after delay time during within retryCount.
		/// </summary>
		public static IObservable<TSource> OnErrorRetry<TSource, TException>(
			this IObservable<TSource> source, Action<TException> onError, int retryCount, TimeSpan delay)
			where TException : Exception
		{
			return source.OnErrorRetry(onError, retryCount, delay, Scheduler.Default);
		}

		/// <summary>
		/// When catched exception, do onError action and repeat observable sequence after delay time(work on delayScheduler) during within retryCount.
		/// </summary>
		public static IObservable<TSource> OnErrorRetry<TSource, TException>(
			this IObservable<TSource> source, Action<TException> onError, int retryCount, TimeSpan delay, IScheduler delayScheduler)
			where TException : Exception
		{
			var result = Observable.Defer(() =>
			{
				var dueTime = (delay.Ticks < 0) ? TimeSpan.Zero : delay;
				var empty = Observable.Empty<TSource>();
				var count = 0;

				IObservable<TSource> self = null;
				self = source.Catch((TException ex) =>
				{
					onError(ex);

					return (++count < retryCount)
						? (dueTime == TimeSpan.Zero)
							? self.SubscribeOn(Scheduler.CurrentThread)
							: empty.Delay(dueTime, delayScheduler).Concat(self).SubscribeOn(Scheduler.CurrentThread)
						: Observable.Throw<TSource>(ex);
				});
				return self;
			});

			return result;
		}
	}
}
