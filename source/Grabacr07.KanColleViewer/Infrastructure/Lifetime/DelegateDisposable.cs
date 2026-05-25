using System;

namespace Grabacr07.KanColleViewer.Infrastructure.Lifetime
{
	/// <summary>
	/// Dispose 時にデリゲートを呼び出すシンプルな <see cref="IDisposable"/> 実装です。
	/// </summary>
	internal sealed class DelegateDisposable : IDisposable
	{
		private Action _action;

		public DelegateDisposable(Action action)
		{
			this._action = action ?? throw new ArgumentNullException(nameof(action));
		}

		public void Dispose()
		{
			this._action?.Invoke();
			this._action = null;
		}
	}
}
