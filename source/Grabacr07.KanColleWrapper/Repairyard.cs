using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.IO;
using Grabacr07.KanColleWrapper.Internal;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 複数の入渠ドックを持つ工廠を表します。
	/// </summary>
	public class Repairyard : Notifier, IDisposable
	{
		private readonly Homeport homeport;
		private readonly CompositeDisposable disposables = new CompositeDisposable();

		#region Docks 変更通知プロパティ

		private MemberTable<RepairingDock> _Docks;

		public MemberTable<RepairingDock> Docks
		{
			get { return this._Docks; }
			set
			{
				if (this._Docks != value)
				{
					this._Docks = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		internal Repairyard(Homeport parent, KanColleProxy proxy)
		{
			this.homeport = parent;
			this.Docks = new MemberTable<RepairingDock>();
		}

		public void Dispose()
		{
			this.disposables.Dispose();
		}

		internal void Update(kcsapi_ndock[] source)
		{
			if (this.Docks.Count == source.Length)
			{
				foreach (var raw in source) this.Docks[raw.api_id]?.Update(raw);
			}
			else
			{
				foreach (var dock in this.Docks) dock.Value?.Dispose();
				this.Docks = new MemberTable<RepairingDock>(source.Select(x => new RepairingDock(this.homeport, x)));
			}
		}

		private void Start(SvData data)
		{
			try
			{
				var ship = this.homeport.Organization.Ships[int.Parse(data.Request["api_ship_id"])];
				var highspeed = data.Request["api_highspeed"] == "1";

				if (highspeed)
				{
					ship.Repair();
					this.homeport.Organization.GetFleet(ship.Id)?.State.Update();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("入渠開始の解析に失敗しました: {0}", ex);
			}
		}

		private void ChangeSpeed(SvData data)
		{
			try
			{
				var dock = this.Docks[int.Parse(data.Request["api_ndock_id"])];
				var ship = dock.Ship;

				dock.Finish();
				ship.Repair();

				this.homeport.Organization.GetFleet(ship.Id)?.State.Update();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("高速修復材の解析に失敗しました: {0}", ex);
			}
		}

		/// <summary>
		/// 指定した艦 ID が現在入渠中かどうかを返します。
		/// </summary>
		internal bool CheckRepairing(int id)
		{
			try
			{
				// Docks が null になり得るケースに備え安全に扱う
				if (this.Docks == null) return false;

				// Repairing 状態のドックに対象の艦がいるか
				return this.Docks.Values.Any(d => d != null && d.ShipId == id && d.State == RepairingDockState.Repairing);
			}
			catch
			{
				// エラーがあっても false を返して処理を継続させる
				return false;
			}
		}
	}
}
