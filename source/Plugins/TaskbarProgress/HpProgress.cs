using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Shell;
using System.Windows.Threading;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Plugins.Properties;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Linq;
using MetroTrilithon.Mvvm;
using StatefulModel;

namespace Grabacr07.KanColleViewer.Plugins
{
	[Export(typeof(IPlugin))]
	[Export(typeof(ITaskbarProgress))]
	[ExportMetadata("Guid", guid)]
	[ExportMetadata("Title", "HpProgressIndicator")]
	[ExportMetadata("Description", "艦隊内の最大損害艦 HP をタスク バー インジケーターに報告します。")]
	[ExportMetadata("Version", "1.1.2")]
	[ExportMetadata("Author", "@veigr")]
	public class HpProgress : IPlugin, ITaskbarProgress, IDisposableHolder
	{
		private const string guid = "DA0E7091-F4A6-4467-9812-3C3E0DF946EA";

		private readonly MultipleDisposable compositDisposable = new MultipleDisposable();
		private MultipleDisposable homeportDisposable = new MultipleDisposable();
		private MultipleDisposable fleetDisposable = new MultipleDisposable();
		private Dispatcher _dispatcher;

		public string Id => guid + "-1";

		public string DisplayName => "艦隊内の最大損害艦 HP";

		public TaskbarItemProgressState State { get; private set; }

		public double Value { get; private set; }

		public event EventHandler Updated;

		public void Initialize()
		{
			_dispatcher = Dispatcher.CurrentDispatcher;

			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.IsStarted), () => _dispatcher.BeginInvoke((Action)this.InitializeCore), false)
				.AddTo(this);
		}

		private void InitializeCore()
		{
			var homeport = KanColleClient.Current.Homeport;

			this.fleetDisposable.Dispose();
			this.fleetDisposable = new MultipleDisposable();

			this.homeportDisposable.Dispose();
			this.homeportDisposable = new MultipleDisposable();

			if (homeport == null) return;

			homeport.Organization
				.Subscribe(nameof(Organization.Fleets), () => _dispatcher.BeginInvoke((Action)this.UpdateFleets))
				.AddTo(this.homeportDisposable);
		}

		public void UpdateFleets()
		{
			if (KanColleClient.Current.Homeport?.Organization == null) return;

			this.fleetDisposable.Dispose();
			this.fleetDisposable = new MultipleDisposable();

			foreach (var fleet in KanColleClient.Current.Homeport.Organization.Fleets.Values)
			{
				fleet.Subscribe(nameof(Fleet.Ships), () => _dispatcher.BeginInvoke((Action)this.Update)).AddTo(this.fleetDisposable);
				fleet.Subscribe(nameof(Fleet.IsInSortie), () => _dispatcher.BeginInvoke((Action)this.Update)).AddTo(this.fleetDisposable);
			}

			this.Update();
		}

		public void Update()
		{
			var org = KanColleClient.Current.Homeport?.Organization;
			if (org == null) return;

			Ship[] ships;
			if (org.Fleets.Values.Any(x => x.IsInSortie))
			{
				ships = org.Fleets.Values
					.Where(x => x.IsInSortie)
					.SelectMany(x => x.Ships)
					.Where(x => !x.Situation.HasFlag(ShipSituation.Tow) && !x.Situation.HasFlag(ShipSituation.Evacuation))
					.ToArray();
			}
			else
			{
				ships = org.Combined && org.CombinedFleet != null
					? org.CombinedFleet.Fleets.SelectMany(x => x.Ships).ToArray()
					: org.Fleets.ContainsKey(1) ? org.Fleets[1].Ships?.ToArray() ?? Array.Empty<Ship>()
												: Array.Empty<Ship>();
			}

			if (!ships.Any())
			{
				this.Value = .0;
				this.State = TaskbarItemProgressState.None;
				this.Updated?.Invoke(this, EventArgs.Empty);
				return;
			}

			this.Value = ships.Select(x => (x.HP.Maximum == 0 ? 0.0 : x.HP.Current / (double)x.HP.Maximum)).Min();

			// 0.25 以下のとき、「大破」
			if (this.Value <= 0.25)
			{
				this.State = TaskbarItemProgressState.Error;
				this.Value = 1.0;
			}
			// 0.5 以下のとき、「中破」
			else if (this.Value <= 0.5)
			{
				this.State = TaskbarItemProgressState.Paused;
			}
			else
			{
				this.State = TaskbarItemProgressState.Normal;
			}

			this.Updated?.Invoke(this, EventArgs.Empty);
		}

		public void Dispose()
		{
			this.fleetDisposable.Dispose();
			this.homeportDisposable.Dispose();
			this.compositDisposable.Dispose();
		}
		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositDisposable;
	}
}
