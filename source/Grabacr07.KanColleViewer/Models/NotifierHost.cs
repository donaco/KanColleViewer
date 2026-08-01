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
		private readonly Dictionary<int, DateTimeOffset> nosakiNextNotifyAt = new Dictionary<int, DateTimeOffset>();
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

			this.StartNosakiTimer(client.Homeport.Organization, client.Homeport.Repairyard);

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

			foreach (var fleet in organization.Fleets.Values)
			{
				fleet.Expedition.Returned += this.HandleExpeditionReturned;
				this.organizationDisposables.Add(new DelegateDisposable(() => fleet.Expedition.Returned -= this.HandleExpeditionReturned));

				fleet.State.Condition.Rejuvenated += this.HandleConditionRejuvenated;
				this.organizationDisposables.Add(new DelegateDisposable(() => fleet.State.Condition.Rejuvenated -= this.HandleConditionRejuvenated));
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

		private void StartNosakiTimer(Organization organization, Repairyard repairyard)
		{
			this.nosakiTimerSubscription?.Dispose();
			this.nosakiNextNotifyAt.Clear();

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

				// 対象の野崎がいない艦隊は対象外
				if (!nosakiInTop2.Any())
				{
					continue;
				}
 
				// 同じ艦隊で停止条件成立ならタイマー停止（通知はしない）
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
						// 条件成立から15分後に初回通知
						this.nosakiNextNotifyAt[ship.Id] = now.Add(NosakiNotifyInterval);
						continue;
					}

					if (now < nextNotifyAt) continue;

					var notification = Notification.Create(
						Notification.Types.FleetRejuvenated,
						"野崎タイマー",
						$"「{fleet.Name}」はタイマーを継続中です。",
						() => WindowService.Current.MainWindow.Activate());

					if (Settings.KanColleSettings.NotifyNosakiTimer)
					{
						this.Notify(notification);
					}
					this.nosakiNextNotifyAt[ship.Id] = now.Add(NosakiNotifyInterval);
				}
			}

			var staleIds = this.nosakiNextNotifyAt.Keys.Where(id => !validShipIds.Contains(id)).ToArray();
			foreach (var shipId in staleIds)
			{
				this.nosakiNextNotifyAt.Remove(shipId);
			}

			this.NosakiTimerUpdated?.Invoke(this, EventArgs.Empty);
		}

		public TimeSpan? GetNosakiTimerRemaining(Fleet fleet)
		{
			if (fleet?.Ships == null) return null;

			var now = DateTimeOffset.Now;
			var remains = fleet.Ships
				.Take(2)
				.Where(s => s != null)
				.Where(s => NosakiShipIds.Contains(s.Info?.Id ?? -1))
				.Select(s =>
				{
					if (!this.nosakiNextNotifyAt.TryGetValue(s.Id, out var next)) return (TimeSpan?)null;
					var r = next - now;
					return r < TimeSpan.Zero ? TimeSpan.Zero : r;
				})
				.Where(x => x.HasValue)
				.Select(x => x.Value)
				.ToArray();

			if (remains.Length == 0) return null;
			return remains.Min();
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
