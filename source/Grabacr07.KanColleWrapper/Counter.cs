using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper
{
	public abstract class CounterBase : Notifier, IDisposable
	{
		// サブクラスが Subscribe() の戻り値をここへ登録する
		protected readonly CompositeDisposable Disposables = new CompositeDisposable();

		#region Count 変更通知プロパティ

		private int _Count;

		public int Count
		{
			get { return this._Count; }
			set
			{
				if (this._Count != value)
				{
					this._Count = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public void Reset()
		{
			this.Count = 0;
		}

		public void Dispose()
		{
			this.Disposables.Dispose();
		}
	}

	public class ItemDestroyCounter : CounterBase
	{
		public ItemDestroyCounter(KanColleClient client)
		{
			EventHandler handler = (_, __) => this.Count++;
			client.ItemDestroyed += handler;
			this.Disposables.Add(System.Reactive.Disposables.Disposable.Create(() => client.ItemDestroyed -= handler));
		}
	}

	public class SupplyCounter : CounterBase
	{
		public SupplyCounter(KanColleClient client)
		{
			EventHandler handler = (_, __) => this.Count++;
			client.SupplyCompleted += handler;
			this.Disposables.Add(System.Reactive.Disposables.Disposable.Create(() => client.SupplyCompleted -= handler));
		}
	}

	public class MissionCounter : CounterBase
	{
		public MissionCounter(KanColleClient client)
		{
			EventHandler handler = (_, __) => this.Count++;
			client.MissionSucceeded += handler;
			this.Disposables.Add(System.Reactive.Disposables.Disposable.Create(() => client.MissionSucceeded -= handler));
		}
	}
}

