using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Shell;
using System.Windows.Threading;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Plugins.Properties;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Linq;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.Plugins
{
	[Export(typeof(IPlugin))]
	[Export(typeof(ITaskbarProgress))]
	[Export(typeof(ISettings))]
	[ExportMetadata("Guid", guid)]
	[ExportMetadata("Title", "タスク バー遠征モニター")]
	[ExportMetadata("Description", "遠征の状況をタスク バー インジケーターに報告します。")]
	[ExportMetadata("Version", "2.1.0")]
	[ExportMetadata("Author", "@Grabacr07")]
	public class ExpeditionProgress : IPlugin, ITaskbarProgress, ISettings, IDisposableHolder
	{
		private const string guid = "C8BF00A6-9FD4-4CC4-8FC5-ECCC5675CDEB";

		private readonly CompositeDisposable compositDisposable = new CompositeDisposable();
		private CompositeDisposable homeportDisposable = new CompositeDisposable();
		private CompositeDisposable wrapperDisposable = new CompositeDisposable();
		private ExpeditionWrapper[] wrappers = Array.Empty<ExpeditionWrapper>();
		private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

		public string Id => guid + "-1";

		public string DisplayName => "遠征状況";

		public TaskbarItemProgressState State { get; private set; }

		public double Value { get; private set; }

		public bool ErrorIfAllWaiting
		{
			get { return Settings.Default.ErrorIfAllWaiting; }
			set
			{
				Settings.Default.ErrorIfAllWaiting = value;
				Settings.Default.Save();
				this.Update();
			}
		}

		object ISettings.View => new ExpeditionProgressSettings { DataContext = this, };

		public event EventHandler Updated;

		public void Initialize()
		{
			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.IsStarted), () => _dispatcher.BeginInvoke((Action)this.InitializeCore), false)
				.AddTo(this);

			var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(Settings.Default.Interval), };
			EventHandler tickHandler = (sender, e) => this.Update();
			timer.Tick += tickHandler;
			timer.Start();

			Disposable.Create(() =>
			{
				timer.Stop();
				timer.Tick -= tickHandler;
			}).AddTo(this);
			Disposable.Create(() => Settings.Default.Save()).AddTo(this);
		}

		private void InitializeCore()
		{
			var homeport = KanColleClient.Current.Homeport;

			this.wrapperDisposable.Dispose();
			this.wrapperDisposable = new CompositeDisposable();
			this.wrappers = Array.Empty<ExpeditionWrapper>();

			this.homeportDisposable.Dispose();
			this.homeportDisposable = new CompositeDisposable();

			if (homeport == null) return;

			homeport.Organization
				.Subscribe(nameof(Organization.Fleets), () => _dispatcher.BeginInvoke((Action)this.UpdateExpeditions))
				.AddTo(this.homeportDisposable);
		}

		public void UpdateExpeditions()
		{
			if (KanColleClient.Current.Homeport?.Organization == null) return;

			this.wrapperDisposable.Dispose();
			this.wrapperDisposable = new CompositeDisposable();

			this.wrappers = KanColleClient.Current.Homeport.Organization.Fleets
				.Skip(1)
				.Select(x => new { x.Value.Id, x.Value.Expedition, })
				.Where(a => a.Expedition != null)
				.Select(a =>
				{
					var w = new ExpeditionWrapper(a.Id, a.Expedition);
						w.Subscribe(nameof(ExpeditionWrapper.State), () => _dispatcher.BeginInvoke((Action)this.Update)).AddTo(w);
						w.AddTo(this.wrapperDisposable);
					return w;
				})
				.ToArray();

			this.Update();
		}

		public void Update()
		{
			if (KanColleClient.Current.Homeport?.Organization == null) return;

			if (this.wrappers.Length == 0)
			{
				this.State = TaskbarItemProgressState.None;
				this.Value = .0;
			}
			else if (this.wrappers.Any(x => x.State == ExpeditionState.Returned))
			{
				this.State = TaskbarItemProgressState.Indeterminate;
				this.Value = 1.0;
			}
			else
			{
				var target = this.wrappers.Aggregate(Early);
				if (target.Source.Remaining.HasValue && target.Source.ReturnTime.HasValue)
				{
					var state = this.wrappers.Any(x => x.State == ExpeditionState.Waiting)
						? TaskbarItemProgressState.Paused
						: TaskbarItemProgressState.Normal;
					var start = target.Source.ReturnTime.Value.Subtract(TimeSpan.FromMinutes(target.Source.Mission.RawData.api_time)); // 開始時間
					var value = DateTimeOffset.Now.Subtract(start).TotalMinutes / target.Source.Mission.RawData.api_time;

					this.State = state;
					this.Value = value;
				}
				else
				{
					this.State = TaskbarItemProgressState.Error;
					this.Value = this.ErrorIfAllWaiting ? 1.0 : .0;
				}
			}

			this.Updated?.Invoke(this, EventArgs.Empty);
		}

		private static ExpeditionWrapper Early(ExpeditionWrapper wrapper1, ExpeditionWrapper wrapper2)
		{
			// 2 つの遠征を比較して早く帰ってくるほうを返すやつ

			return wrapper1.Source.IsInExecution
				? wrapper2.Source.IsInExecution
					? wrapper1.Source.ReturnTime < wrapper2.Source.ReturnTime
						? wrapper1
						: wrapper2
					: wrapper1
				: wrapper2;
		}

		public void Dispose()
		{
			this.wrapperDisposable.Dispose();
			this.homeportDisposable.Dispose();
			this.compositDisposable.Dispose();
		}
		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositDisposable;
	}
}
