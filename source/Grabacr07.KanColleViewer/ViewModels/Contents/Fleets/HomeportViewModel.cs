using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleWrapper.Models;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	/// <summary>
	/// 母港で待機中の艦隊のステータスを表します。
	/// </summary>
	public class HomeportViewModel : QuickStateViewViewModel
	{
		private readonly Fleet fleet;
		private static readonly int[] NosakiShipIds = { 996, 1002 };

		// QuickStateView は ContentControl に対し型ごとの DataTemplate を適用する形で実現するので
		// 状況に応じた型がそれぞれ必要。これはその 1 つ。
	
		public ConditionViewModel Condition { get; }
		public string NosakiTimerRemaining => this.GetNosakiTimerRemaining()?.ToString(@"mm\:ss") ?? "--:--";
		public bool IsNosakiTimerActive
			=> this.fleet != null
			&& NotifyService.Current.IsNosakiTimerDisplayActive(this.fleet);

		public HomeportViewModel(FleetState state, Fleet fleet = null)
			: base(state)
		{
			this.fleet = fleet;
			this.Condition = new ConditionViewModel(state.Condition);
			this.CompositeDisposable.Add(this.Condition);

			EventHandler nosakiUpdated = (_, __) => this.InvokeOnUIDispatcher(() =>
			{
				this.RaisePropertyChanged(nameof(this.NosakiTimerRemaining));
				this.RaisePropertyChanged(nameof(this.IsNosakiTimerActive));
			});
			NotifyService.Current.NosakiTimerUpdated += nosakiUpdated;
			this.CompositeDisposable.Add(new DelegateDisposable(() => NotifyService.Current.NosakiTimerUpdated -= nosakiUpdated));
		}

		private TimeSpan? GetNosakiTimerRemaining()
		{
			if (this.fleet == null) return null;
			return NotifyService.Current.GetNosakiTimerRemaining(this.fleet);
		}
	}
}
