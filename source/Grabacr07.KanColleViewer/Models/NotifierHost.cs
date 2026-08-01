using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// <see cref="KanColleClient"/> や関連するオブジェクトからのイベント、プラグインからのイベントを受信し、<see cref="INotifier"/>
	/// を実装する各通知機能へイベントを配信します。
	/// </summary>
	public class NotifyService : ObservableObject, INotifier, IDisposableHolder
	{
		#region singleton members

		public static NotifyService Current { get; } = new NotifyService();

		#endregion

		private INotifier notifier;
		private bool isRegistered;

		private readonly CompositeDisposable compositeDisposable = new CompositeDisposable();
		private CompositeDisposable dockyardDisposables;
		private CompositeDisposable repairyardDisposables;
		private CompositeDisposable organizationDisposables;
		private readonly Dictionary<int, string> fleetCompositionSignatures = new Dictionary<int, string>();
		private readonly object nosakiTimerSync = new object();
		private readonly Dictionary<int, DateTimeOffset> nosakiNextNotifyAt = new Dictionary<int, DateTimeOffset>();
		private DateTimeOffset? nosakiSharedNextNotifyAt;
		private IDisposable nosakiTimerSubscription;
		public event EventHandler NosakiTimerUpdated;

		// ﾉｻｷﾁｬﾝのID
		private static readonly int[] NosakiShipIds = { 996, 1002 };
		private static readonly TimeSpan NosakiNotifyInterval = TimeSpan.FromMinutes(15);

		private NotifyService() { }

		public void Initialize()
		{
			foreach (var source in PluginService.Current.Get<IRequestNotify>())
			{
				source.NotifyRequested += this.HandleNotifyRequested;
				Disposable.Create(() => source.NotifyRequested -= this.HandleNotifyRequested).AddTo(this);
			}

			// IsStarted が true に変わる最初にして唯一タイミングで購読登録するのよ、司令官！
			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.IsStarted), this.RegisterHomeportListener, false)
				.AddTo(this);
		}

		public void Notify(INotification notify)
		{
			if (this.notifier == null)
			{
				this.notifier = PluginService.Current.GetNotifier();
			}

			this.notifier.Notify(notify);
		}

		public INotification CreateTest(string header = "テスト通知", string body = "これは「提督業も忙しい！」のテスト通知です。", Action activated = null, Action<Exception> failed = null)
		{
			return Notification.Create(Notification.Types.Test, header, body, activated ?? WindowService.Current.MainWindow.Activate, failed);
		}

		#region Initialize() method parts

		private void HandleNotifyRequested(object sender, NotifyEventArgs e)
		{
			this.Notify(e);
		}

		private void RegisterHomeportListener()
		{
			if (this.isRegistered) return;

			var client = KanColleClient.Current;

			client.Homeport.Repairyard
				.Subscribe(nameof(Repairyard.Docks), () => this.UpdateRepairyard(client.Homeport.Repairyard))
				.AddTo(this);

			client.Homeport.Dockyard
				.Subscribe(nameof(Dockyard.Docks), () => this.UpdateDockyard(client.Homeport.Dockyard))
				.AddTo(this);

			client.Homeport.Organization
				.Subscribe(nameof(Organization.Fleets), () => this.UpdateFleets(client.Homeport.Organization))
				.AddTo(this);

			this.StartNosakiTimer(client.Homeport.Organization);

			this.isRegistered = true;
		}

		#endregion

		#region Dockyard

		private void UpdateDockyard(Dockyard dockyard)
		{
			this.dockyardDisposables?.Dispose();
			this.dockyardDisposables = new CompositeDisposable();

			foreach (var dock in dockyard.Docks.Values)
			{
				dock.Completed += this.HandleDockyardCompleted;
				this.dockyardDisposables.Add(new DelegateDisposable(() => dock.Completed -= this.HandleDockyardCompleted));
			}
		}

		private void HandleDockyardCompleted(object sender, BuildingCompletedEventArgs args)
		{
			if (!Settings.KanColleSettings.NotifyBuildingCompleted) return;

			var shipName = Settings.KanColleSettings.CanDisplayBuildingShipName
				? args.Ship.Name
				: Resources.Common_ShipGirl;

			var notification = Notification.Create(
				Notification.Types.BuildingCompleted,
				Resources.Dockyard_NotificationMessage_Title,
				string.Format(Resources.Dockyard_NotificationMessage, args.DockId, shipName),
				() => WindowService.Current.MainWindow.Activate());

			this.Notify(notification);
		}

		#endregion

		#region Repairyard

		private void UpdateRepairyard(Repairyard repairyard)
		{
			this.repairyardDisposables?.Dispose();
			this.repairyardDisposables = new CompositeDisposable();

			foreach (var dock in repairyard.Docks.Values)
			{
				dock.Completed += this.HandleRepairyardCompleted;
				this.repairyardDisposables.Add(new DelegateDisposable(() => dock.Completed -= this.HandleRepairyardCompleted));
			}
		}

		private void HandleRepairyardCompleted(object sender, RepairingCompletedEventArgs args)
		{
			if (!Settings.KanColleSettings.NotifyRepairingCompleted) return;

			var notification = Notification.Create(
				Notification.Types.RepairingCompleted,
				Resources.Repairyard_NotificationMessage_Title,
				string.Format(Resources.Repairyard_NotificationMessage, args.DockId, args.Ship.Info.Name),
				() => WindowService.Current.MainWindow.Activate());

			this.Notify(notification);
		}

		#endregion

		#region Fleet

		private void UpdateFleets(Organization organization)
		{
			this.organizationDisposables?.Dispose();
			this.organizationDisposables = new CompositeDisposable();

			var aliveFleetIds = new HashSet<int>();

			foreach (var fleet in organization.Fleets.Values)
			{
				if (fleet == null) continue;
				aliveFleetIds.Add(fleet.Id);

				// 現在の編成シグネチャを初期化（初回イベントで誤リセットしない）
				this.UpdateFleetCompositionSignature(fleet);

				fleet.Expedition.Returned += this.HandleExpeditionReturned;
				this.organizationDisposables.Add(new DelegateDisposable(() => fleet.Expedition.Returned -= this.HandleExpeditionReturned));

				fleet.State.Condition.Rejuvenated += this.HandleConditionRejuvenated;
				this.organizationDisposables.Add(new DelegateDisposable(() => fleet.State.Condition.Rejuvenated -= this.HandleConditionRejuvenated));

				// api_req_hensei/change などの編成変更を検知してタイマーをリセット
				System.ComponentModel.PropertyChangedEventHandler fleetChanged = (s, e) =>
				{
					if (e.PropertyName == nameof(Fleet.ShipsUpdated))
					{
						// ShipsUpdated が来ても、編成実体が変わらない（api_port等）ならリセットしない
						if (this.UpdateFleetCompositionSignature(fleet))
						{
							this.ResetNosakiTimerByFleetChange();
						}
					}
				};
				fleet.PropertyChanged += fleetChanged;
				this.organizationDisposables.Add(new DelegateDisposable(() => fleet.PropertyChanged -= fleetChanged));
			}

			// 消えた艦隊IDのシグネチャを掃除
			var stale = this.fleetCompositionSignatures.Keys.Where(id => !aliveFleetIds.Contains(id)).ToArray();
			foreach (var id in stale)
			{
				this.fleetCompositionSignatures.Remove(id);
			}
		}

		private bool UpdateFleetCompositionSignature(Fleet fleet)
		{
			if (fleet == null) return false;

			// 並び順込みで編成をシグネチャ化
			var signature = fleet.Ships == null
				? string.Empty
				: string.Join(",", fleet.Ships.Where(s => s != null).Select(s => s.Id));

			if (this.fleetCompositionSignatures.TryGetValue(fleet.Id, out var current)
				&& string.Equals(current, signature, StringComparison.Ordinal))
			{
				return false;
			}

			this.fleetCompositionSignatures[fleet.Id] = signature;
			return true;
		}

		private void ResetNosakiTimerByFleetChange()
		{
			var shouldRaise = false;

			lock (this.nosakiTimerSync)
			{
				if (this.nosakiNextNotifyAt.Count == 0)
				{
					this.nosakiSharedNextNotifyAt = null;
					shouldRaise = true;
				}
				else
				{
					var next = DateTimeOffset.Now.Add(NosakiNotifyInterval);
					var keys = this.nosakiNextNotifyAt.Keys.ToArray();
					foreach (var key in keys)
					{
						this.nosakiNextNotifyAt[key] = next;
					}

					// 共有タイマー時刻を即時更新
					this.nosakiSharedNextNotifyAt = next;
					shouldRaise = true;
				}
			}

			if (shouldRaise)
			{
				this.NosakiTimerUpdated?.Invoke(this, EventArgs.Empty);
			}
		}

		private void HandleExpeditionReturned(object sender, ExpeditionReturnedEventArgs args)
		{
			if (!Settings.KanColleSettings.NotifyExpeditionReturned) return;

			var notify = Notification.Create(
				Notification.Types.ExpeditionReturned,
				Resources.Expedition_NotificationMessage_Title,
				string.Format(Resources.Expedition_NotificationMessage, args.FleetName),
				() => WindowService.Current.MainWindow.Activate());

			this.Notify(notify);
		}

		private void HandleConditionRejuvenated(object sender, ConditionRejuvenatedEventArgs args)
		{
			if (!Settings.KanColleSettings.NotifyFleetRejuvenated) return;

			// 野崎タイマー停止通知と重複させない
			if (Settings.KanColleSettings.NotifyNosakiTimer)
			{
				var organization = KanColleClient.Current?.Homeport?.Organization;
				var fleet = organization?.Fleets?.Values?
					.FirstOrDefault(x => x != null && x.Name == args.FleetName);

				if (fleet != null && this.IsNosakiTimerBlockedByOtherHighCondition(fleet))
				{
					return;
				}
			}

			var notification = Notification.Create(
				Notification.Types.FleetRejuvenated,
				"疲労回復完了",
				$"「{args.FleetName}」に編成されている艦娘の疲労が回復しました。",
				() => WindowService.Current.MainWindow.Activate());

			this.Notify(notification);
		}

		private void StartNosakiTimer(Organization organization)
		{
			this.nosakiTimerSubscription?.Dispose();
			lock (this.nosakiTimerSync)
			{
				this.nosakiNextNotifyAt.Clear();
				this.nosakiSharedNextNotifyAt = null;
			}

			this.nosakiTimerSubscription = Observable
				.Interval(TimeSpan.FromSeconds(1))
				.StartWith(0L)
				.Subscribe(_ => this.CheckNosakiTimer(organization));


			this.nosakiTimerSubscription.AddTo(this);
		}

		private void CheckNosakiTimer(Organization organization)
		{
			if (organization?.Fleets?.Values == null) return;
 			var now = DateTimeOffset.Now;
			var shouldNotify = false;

			lock (this.nosakiTimerSync)
			{
				var validShipIds = new HashSet<int>();

				foreach (var fleet in organization.Fleets.Values.Where(f => f != null))
				{
					if (fleet.IsInSortie) continue;
					if (fleet.Expedition?.IsInExecution == true) continue;
				
					var nosakiInTop2 = fleet.Ships
						.Take(2)
						.Where(s => s != null)
						.Where(s => NosakiShipIds.Contains(s.Info?.Id ?? -1))
						.ToArray();

					if (!nosakiInTop2.Any()) continue;

					if (this.IsNosakiTimerBlockedByOtherHighCondition(fleet))
					{
						foreach (var nosaki in nosakiInTop2)
						{
							this.nosakiNextNotifyAt.Remove(nosaki.Id);
						}
						continue;
					}

					foreach (var ship in nosakiInTop2)
					{
						if (!this.IsNosakiConditionSatisfied(ship))
						{
							this.nosakiNextNotifyAt.Remove(ship.Id);
							continue;
						}

						validShipIds.Add(ship.Id);

						if (!this.nosakiNextNotifyAt.TryGetValue(ship.Id, out var nextNotifyAt))
						{
							this.nosakiNextNotifyAt[ship.Id] = now.Add(NosakiNotifyInterval);
							continue;
						}
					}
				}

				var staleIds = this.nosakiNextNotifyAt.Keys.Where(id => !validShipIds.Contains(id)).ToArray();
				foreach (var shipId in staleIds)
				{
					this.nosakiNextNotifyAt.Remove(shipId);
				}

				this.nosakiSharedNextNotifyAt = this.nosakiNextNotifyAt.Count > 0
					? this.nosakiNextNotifyAt.Values.Max()
					: (DateTimeOffset?)null;

				if (this.nosakiSharedNextNotifyAt.HasValue && now >= this.nosakiSharedNextNotifyAt.Value)
				{
					shouldNotify = Settings.KanColleSettings.NotifyNosakiTimer;

					var next = now.Add(NosakiNotifyInterval);
					var keys = this.nosakiNextNotifyAt.Keys.ToArray();
					foreach (var key in keys)
					{
						this.nosakiNextNotifyAt[key] = next;
					}
					this.nosakiSharedNextNotifyAt = this.nosakiNextNotifyAt.Count > 0 ? next : (DateTimeOffset?)null;
				}
 			}

			if (shouldNotify)
			{
				var notification = Notification.Create(
					Notification.Types.FleetRejuvenated,
					"野崎タイマー",
					"15分経過しました。",
					() => WindowService.Current.MainWindow.Activate());
				this.Notify(notification);
			}

			this.NosakiTimerUpdated?.Invoke(this, EventArgs.Empty);
		}

		public TimeSpan? GetNosakiTimerRemaining(Fleet fleet)
		{
			lock (this.nosakiTimerSync)
			{
				if (!this.nosakiSharedNextNotifyAt.HasValue) return null;
				var remain = this.nosakiSharedNextNotifyAt.Value - DateTimeOffset.Now;
				return remain < TimeSpan.Zero ? TimeSpan.Zero : remain;
			}
 		}

		private bool IsNosakiConditionSatisfied(Ship ship)
		{
			if (ship?.Info == null) return false;

			var isSupplied = ship.Fuel.Current >= ship.Fuel.Maximum
				&& ship.Bull.Current >= ship.Bull.Maximum;

			var hpRate = ship.HP.Maximum <= 0 ? 0.0 : ship.HP.Current / (double)ship.HP.Maximum;
			var isHpAtLeast75Percent = hpRate >= 0.75; // 小破判定: HP75%以上

			var isConditionEnough = ship.Condition >= 30;

			var isNotRepairing = ship.TimeToRepair == TimeSpan.Zero; // 入渠判定

			return isSupplied
				&& isHpAtLeast75Percent
				&& isConditionEnough
				&& isNotRepairing;
		}

		private bool IsNosakiTimerBlockedByOtherHighCondition(Fleet fleet)
		{
			if (fleet?.Ships == null) return false;

			var otherShips = fleet.Ships
				.Where(s => s != null)
				.Where(s => !NosakiShipIds.Contains(s.Info?.Id ?? -1))
				// 野崎以外の入渠中艦娘は判定対象から除外
				.Where(s => s.TimeToRepair == TimeSpan.Zero)
				.ToArray();

			// 「野崎以外（入渠中を除く）の艦娘が全員 cond 54 以上」のときのみ停止
			return otherShips.Length > 0
				&& otherShips.All(s => s.Condition >= 54);
		}

		#endregion

		#region IDisposable members

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositeDisposable;

		public void Dispose()
		{
			this.compositeDisposable.Dispose();
			this.dockyardDisposables?.Dispose();
			this.repairyardDisposables?.Dispose();
			this.organizationDisposables?.Dispose();
		}

		#endregion
	}
}
