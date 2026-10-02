using System;
using System.Collections.Generic;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.Plugins
{
	public class ExpeditionWrapper : Notifier, IDisposableHolder
	{
		private readonly CompositeDisposable compositeDisposable = new CompositeDisposable();

		public int Id { get; }

		public Expedition Source { get; }

		#region State notification property

		public ExpeditionState State
		{
			get => field;
			set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public ExpeditionWrapper(int id, Expedition expedition)
		{
			this.Id = id;
			this.Source = expedition;
			this.Source.Subscribe(nameof(Expedition.Remaining), () => this.UpdateState()).AddTo(this);
		}

		private void UpdateState()
		{
			this.State = this.Source.IsInExecution
				? this.Source.Remaining?.TotalSeconds > 0
					? ExpeditionState.InExecution
					: ExpeditionState.Returned
				: ExpeditionState.Waiting;
		}

		public void Dispose() => this.compositeDisposable.Dispose();
		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositeDisposable;
	}
}
