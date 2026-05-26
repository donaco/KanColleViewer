using System;
using System.Reactive.Disposables;

namespace Grabacr07.KanColleWrapper
{
	public class DisposableNotifier : Notifier, IDisposable
	{
		protected CompositeDisposable CompositeDisposable { get; }

		public DisposableNotifier()
		{
			this.CompositeDisposable = new CompositeDisposable();
		}

		public void Dispose()
		{
			this.Dispose(true);
			this.CompositeDisposable.Dispose();

			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) { }
	}
}
