using Grabacr07.KanColleWrapper.Internal;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json; // 追加
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO; // 追加
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 艦娘と艦隊の編成を表します。
	/// </summary>
	public class Organization : Notifier
	{
		private readonly Homeport homeport;

		private readonly List<int> evacuatedShipsIds = new List<int>();
		private readonly List<int> towShipIds = new List<int>();

		#region Ships 変更通知プロパティ

		private MemberTable<Ship> _Ships;

		/// <summary>
		/// 母港に所属する艦娘のコレクションを取得します。
		/// </summary>
		public MemberTable<Ship> Ships
		{
			get { return this._Ships; }
			private set
			{
				if (this._Ships != value)
				{
					this._Ships = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Fleets 変更通知プロパティ

		private MemberTable<Fleet> _Fleets;

		/// <summary>
		/// 編成された艦隊のコレクションを取得します。
		/// </summary>
		public MemberTable<Fleet> Fleets
		{
			get { return this._Fleets; }
			private set
			{
				if (this._Fleets != value)
				{
					this._Fleets = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Combined 変更通知プロパティ

		private bool _Combined;

		/// <summary>
		/// 第一・第二艦隊による連合艦隊が編成されているかどうかを示す値を取得または設定します。
		/// </summary>
		public bool Combined
		{
			get { return this._Combined; }
			set
			{
				if (this._Combined != value)
				{
					this._Combined = value;
					this.Combine(value);
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region CombinedFleet 変更通知プロパティ

		private CombinedFleet _CombinedFleet;

		public CombinedFleet CombinedFleet
		{
			get { return this._CombinedFleet; }
			set
			{
				if (this._CombinedFleet != value)
				{
					this._CombinedFleet = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		public Organization(Homeport parent, KanColleProxy proxy)
		{
			this.homeport = parent;

			this.Ships = new MemberTable<Ship>();
			this.Fleets = new MemberTable<Fleet>();

			proxy.api_get_member_ship.TryParse<kcsapi_ship2[]>().Subscribe(x => this.Update(x.Data));
			proxy.api_get_member_ship2.TryParse<kcsapi_ship2[]>().Subscribe(x =>
			{
				this.Update(x.Data);
				this.Update(x.Fleets);
			});
			proxy.api_get_member_ship3.TryParse<kcsapi_ship3>().Subscribe(x =>
			{
				this.Update(x.Data.api_ship_data);
				this.Update(x.Data.api_deck_data);
			});

			proxy.api_get_member_deck.TryParse<kcsapi_deck[]>().Subscribe(x => this.Update(x.Data));
			proxy.api_get_member_deck_port.TryParse<kcsapi_deck[]>().Subscribe(x => this.Update(x.Data));
			proxy.api_get_member_ship_deck.TryParse<kcsapi_ship_deck>().Subscribe(x => this.Update(x.Data));
			proxy.api_req_hensei_preset_select.TryParse<kcsapi_deck>().Subscribe(x => this.Update(x.Data));

			proxy.api_req_hensei_change.TryParse().Subscribe(this.Change);
			proxy.api_req_hokyu_charge.TryParse<kcsapi_charge>().Subscribe(x => this.Charge(x.Data));
			proxy.api_req_kaisou_powerup.TryParse<kcsapi_powerup>().Subscribe(this.Powerup);
			proxy.api_req_kaisou_slot_exchange_index.TryParse<kcsapi_slot_exchange_index>().Subscribe(this.ExchangeSlot);
			proxy.api_req_kaisou_slot_deprive.TryParse<kcsapi_slot_deprive>().Subscribe(x => this.DepriveSlotItem(x.Data));

			proxy.api_req_kousyou_getship.TryParse<kcsapi_kdock_getship>().Subscribe(x => this.GetShip(x.Data));
			proxy.api_req_kousyou_destroyship.TryParse<kcsapi_destroyship>().Subscribe(this.DestoryShip);
			proxy.api_req_member_updatedeckname.TryParse().Subscribe(this.UpdateFleetName);

			proxy.api_req_hensei_combined.TryParse<kcsapi_hensei_combined>()
				.Subscribe(x => this.Combined = x.Data.api_combined != 0);

			this.SubscribeSortieSessions(proxy);
		}


		/// <summary>
		/// 指定した ID の艦娘が所属する艦隊を取得します。
		/// </summary>
		internal Fleet GetFleet(int shipId)
		{
			return this.Fleets.Select(x => x.Value).SingleOrDefault(x => x.Ships.Any(s => s.Id == shipId));
		}

		private void UpdateFleetName(SvData data)
		{
			if (data == null || !data.IsSuccess) return;

			try
			{
				var fleet = this.Fleets[int.Parse(data.Request["api_deck_id"])];
				var name = data.Request["api_name"];

				fleet.Name = name;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("艦隊名の変更に失敗しました: {0}", ex);
			}
		}

		internal void RaiseShipsChanged()
		{
			this.RaisePropertyChanged("Ships");
		}


		#region 母港 / 艦隊編成 (Update / Change)

		/// <summary>
		/// 指定した <see cref="kcsapi_ship2"/> 型の配列を使用して、<see cref="Ships"/> プロパティ値を更新します。
		/// </summary>
		internal void Update(kcsapi_ship2[] source)
		{
			if (source.Length <= 1)
			{
				foreach (var ship in source)
				{
					var target = this.Ships[ship.api_id];
					if (target == null) continue;

					target.Update(ship);
					this.GetFleet(target.Id)?.State.Calculate();
				}
			}
			else
			{
				this.Ships = new MemberTable<Ship>(source.Select(x => new Ship(this.homeport, x)));

				if (KanColleClient.Current.IsInSortie)
				{
					foreach (var id in this.evacuatedShipsIds) this.Ships[id].Situation |= ShipSituation.Evacuation;
					foreach (var id in this.towShipIds) this.Ships[id].Situation |= ShipSituation.Tow;
				}

				foreach (var fleet in this.Fleets.Values)
				{
					fleet.State.Update();
					fleet.State.Calculate();
				}
			}
		}
		/// <summary>
		/// 戦闘結果の反映などで情報が更新された際に、重要なプロパティの通知を格好します。
		/// </summary>
		internal void NotifyUpdated()
		{
			try
			{
				// 重要なプロパティ名を指定して強制的に PropertyChanged を発行
				this.RaisePropertyChanged(nameof(this.Ships));
				this.RaisePropertyChanged(nameof(this.Fleets));
				this.RaisePropertyChanged(nameof(this.Combined));
			}
			catch
			{
			}
		}


		/// <summary>
		/// 指定した <see cref="kcsapi_deck"/> 型の配列を使用して、<see cref="Fleets"/> プロパティ値を更新します。
		/// </summary>
		internal void Update(kcsapi_deck[] source)
		{
			if (this.Fleets.Count == source.Length)
			{
				foreach (var raw in source) this.Fleets[raw.api_id]?.Update(raw);
			}
			else
			{
				foreach (var fleet in this.Fleets) fleet.Value?.Dispose();
				this.Fleets = new MemberTable<Fleet>(source.Select(x => new Fleet(this.homeport, x)));
			}
		}


		internal void Update(kcsapi_deck source)
		{
			var fleet = this.Fleets[source.api_id];
			if (fleet != null)
			{
				fleet.Update(source);
				fleet.RaiseShipsUpdated();
			}
		}


		private void Change(SvData data)
		{
			if (data == null || !data.IsSuccess) return;

			try
			{
				var fleet = this.Fleets[int.Parse(data.Request["api_id"])];
				fleet.RaiseShipsUpdated();

				var index = int.Parse(data.Request["api_ship_idx"]);
				var shipId = int.Parse(data.Request["api_ship_id"]);
				if (index == 0 && shipId == -2)
				{
					// 旗艦以外をすべて外すケース
					fleet.UnsetAll();
					return;
				}

				var ship = this.Ships[shipId];
				if (ship == null)
				{
					// 艦を外すケース
					fleet.Unset(index);
					return;
				}

				var currentFleet = this.GetFleet(ship.Id);
				if (currentFleet == null)
				{
					// ship が、現状どの艦隊にも所属していないケース
					fleet.Change(index, ship);
					return;
				}

				// ship が、現状いずれかの艦隊に所属しているケース
				var currentIndex = Array.IndexOf(currentFleet.Ships, ship);
				var old = fleet.Change(index, ship);

				// Fleet.Change(int, Ship) は、変更前の艦を返す (= old) ので、 
				// ship の移動元 (currentFleet + currentIndex) に old を書き込みにいく
				currentFleet.Change(currentIndex, old);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("編成の変更に失敗しました: {0}", ex);
			}
		}


		private void Combine(bool combine)
		{
			this.CombinedFleet?.Dispose();
			this.CombinedFleet = combine
				? new CombinedFleet(this.homeport, this.Fleets.OrderBy(x => x.Key).Select(x => x.Value).Take(2).ToArray())
				: null;
		}

		private void ExchangeSlot(SvData<kcsapi_slot_exchange_index> data)
		{
			try
			{
				var ship = this.Ships[int.Parse(data.Request["api_id"])];
				if (ship == null) return;

				ship.RawData.api_slot = data.Data.api_slot;
				ship.UpdateSlots();

				var fleet = this.Fleets.Values.FirstOrDefault(x => x.Ships.Any(y => y.Id == ship.Id));
				if (fleet == null) return;

				fleet.State.Calculate();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("装備の入れ替えに失敗しました: {0}", ex);
			}
		}

		#endregion

		#region 補給 / 近代化改修 (Charge / Powerup)

		private void Charge(kcsapi_charge source)
		{
			Fleet fleet = null; // 補給した艦が所属している艦隊。艦隊をまたいで補給はできないので、必ず 1 つに絞れる

			foreach (var ship in source.api_ship)
			{
				var target = this.Ships[ship.api_id];
				if (target == null) continue;

				target.Charge(ship.api_fuel, ship.api_bull, ship.api_onslot);

				if (fleet == null)
				{
					fleet = this.GetFleet(target.Id);
				}
			}

			if (fleet != null)
			{
				fleet.State.Update();
				fleet.State.Calculate();
			}
		}

		private void Powerup(SvData<kcsapi_powerup> svd)
		{
			try
			{
				this.Ships[svd.Data.api_ship.api_id]?.Update(svd.Data.api_ship);

				var items = svd.Request["api_id_items"]
					.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(int.Parse)
					.Where(x => this.Ships.ContainsKey(x))
					.Select(x => this.Ships[x])
					.ToArray();

				// (改修に使った艦娘のこと item って呼ぶのどうなの…)

				foreach (var x in items)
				{
					this.homeport.Itemyard.RemoveFromShip(x);
					this.Ships.Remove(x);
				}

				this.RaiseShipsChanged();
				this.Update(svd.Data.api_deck);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("近代化改修による更新に失敗しました: {0}", ex);
			}
		}

		#endregion

		#region 改装 (DepriveSlotItem)

		private void DepriveSlotItem(kcsapi_slot_deprive source)
		{
			try
			{
				if (source == null || source.api_ship_data == null) return;

				var unsetShipRaw = source.api_ship_data.api_unset_ship;
				var setShipRaw = source.api_ship_data.api_set_ship;

				// 1) 該当艦を更新（Ship.Update を使う）
				if (unsetShipRaw != null)
				{
					var target = this.Ships[unsetShipRaw.api_id];
					if (target != null)
					{
						try { target.Update(unsetShipRaw); } catch { }
					}
				}

				if (setShipRaw != null)
				{
					var target = this.Ships[setShipRaw.api_id];
					if (target != null)
					{
						try { target.Update(setShipRaw); } catch { }
					}
				}

				// 2) api_unset_list にあれば Itemyard から該当装備を削除（移動ではなく外れた装備のケース）
				try
				{
					var iy = this.homeport?.Itemyard;
					var unsetList = source.api_unset_list?.api_slot_list;
					if (iy != null && unsetList != null && unsetList.Length > 0)
					{
						foreach (var id in unsetList)
						{
							try { iy.SlotItems.Remove(id); } catch { }
						}
						try
						{
							typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
								?.Invoke(iy, null);
						}
						catch { }
					}
				}
				catch { }

				// 3) 影響を受ける艦隊を再計算・再通知
				var affectedIds = new List<int>();
				if (unsetShipRaw != null) affectedIds.Add(unsetShipRaw.api_id);
				if (setShipRaw != null) affectedIds.Add(setShipRaw.api_id);

				foreach (var id in affectedIds.Distinct())
				{
					try
					{
						var fleet = this.GetFleet(id);
						if (fleet != null)
						{
							try { fleet.State.Update(); } catch { }
							try { fleet.State.Calculate(); } catch { }
							try { fleet.RaiseShipsUpdated(); } catch { }
						}
					}
					catch { }
				}

				// 4) 組織レベルでも通知（UI 更新の確実化）
				try { this.RaiseShipsChanged(); } catch { }
				try { this.NotifyUpdated(); } catch { }
			}
			catch
			{
			}
		}

		#endregion

		#region 工廠 (Get / Destroy)

		private void GetShip(kcsapi_kdock_getship source)
		{
			this.homeport.Itemyard.AddFromDock(source);

			this.Ships.Add(new Ship(this.homeport, source.api_ship));
			this.RaiseShipsChanged();
		}

		private void DestoryShip(SvData<kcsapi_destroyship> svd)
		{
			try
			{
				var ships = svd.Request["api_ship_id"]
					.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
					.Select(x => int.Parse(x))
					.Select(x => this.Ships[x]);
                    
				foreach(var ship in ships)
				{
					this.homeport.Itemyard.RemoveFromShip(ship);

					this.Ships.Remove(ship);
				}
				this.RaiseShipsChanged();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("解体による更新に失敗しました: {0}", ex);
			}
		}

		#endregion

		#region 出撃 (Sortie / Homing / Escape)

		private void SubscribeSortieSessions(KanColleProxy proxy)
		{
#if DEBUG
			Debug.WriteLine("Organization.SubscribeSortieSessions: subscribing to sortie sessions.");
#endif
			proxy.ApiSessionSource
				.SkipUntil(proxy.api_req_map_start.TryParse().Do(this.Sortie))
				.TakeUntil(proxy.api_port)
				.Finally(this.Homing)
				.Repeat()
				.Subscribe();

			int[] evacuationOfferedShipIds = null;
			int[] towOfferedShipIds = null;

			proxy.api_req_combined_battle_battleresult
				.TryParse<kcsapi_combined_battle_battleresult>()
				.Where(x => x.Data.api_escape != null)
				.Select(x => x.Data)
				.Subscribe(x =>
				{
					if (this.CombinedFleet == null) return;
					var ships = this.CombinedFleet.Fleets.SelectMany(f => f.Ships).ToArray();
					evacuationOfferedShipIds = x.api_escape.api_escape_idx.Select(idx => ships[idx - 1].Id).ToArray();
					towOfferedShipIds = x.api_escape.api_tow_idx.Select(idx => ships[idx - 1].Id).ToArray();
				});
			proxy.api_req_combined_battle_goback_port
				.Subscribe(_ =>
				{
					if (KanColleClient.Current.IsInSortie
						&& evacuationOfferedShipIds != null
						&& evacuationOfferedShipIds.Length >= 1
						&& towOfferedShipIds != null
						&& towOfferedShipIds.Length >= 1)
					{
						this.evacuatedShipsIds.Add(evacuationOfferedShipIds[0]);
						this.towShipIds.Add(towOfferedShipIds[0]);
					}
				});
			proxy.api_get_member_ship_deck
				.Subscribe(_ =>
				{
					evacuationOfferedShipIds = null;
					towOfferedShipIds = null;
				});
		}


		private void Sortie(SvData data)
		{
			if (data == null || !data.IsSuccess)
			{
				Debug.WriteLine("Organization.Sortie: received null or unsuccessful SvData.");
				return;
			}

			try
			{
				var id = int.Parse(data.Request["api_deck_id"]);
				// Sortie の中のデバッグ行をガード
#if DEBUG
				Debug.WriteLine($"Organization.Sortie: detected sortie for deck {id} (Request keys: {string.Join(",", data.Request.Keys)})");
#endif

				var fleet = this.Fleets[id];
				if (fleet == null)
				{
					Debug.WriteLine($"Organization.Sortie: fleet {id} not found in Fleets collection.");
				}
				else
				{
					Debug.WriteLine($"Organization.Sortie: before Sortie -> Fleet.Id={fleet.Id}, IsInSortie={fleet.IsInSortie}");
					fleet.Sortie();
					Debug.WriteLine($"Organization.Sortie: after Sortie -> Fleet.Id={fleet.Id}, IsInSortie={fleet.IsInSortie}");
				}

				if (this.Combined && id == 1)
				{
					Debug.WriteLine("Organization.Sortie: combined fleet flag set, also marking fleet 2 as sortie.");
					this.Fleets[2].Sortie();
					Debug.WriteLine($"Organization.Sortie: fleet 2 IsInSortie={this.Fleets[2].IsInSortie}");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("艦隊の出撃を検知できませんでした: {0}", ex);
			}
		}

		private void Homing()
		{
			// Homing の開始ログ
#if DEBUG
			Debug.WriteLine("Organization.Homing: invoked.");
#endif

			this.evacuatedShipsIds.Clear();
			this.towShipIds.Clear();

			foreach (var ship in this.Ships.Values)
			{
				if (ship.Situation.HasFlag(ShipSituation.Evacuation)) ship.Situation &= ~ShipSituation.Evacuation;
				if (ship.Situation.HasFlag(ShipSituation.Tow)) ship.Situation &= ~ShipSituation.Tow;
			}

			foreach (var target in this.Fleets.Values)
			{
				Debug.WriteLine($"Organization.Homing: Homing fleet {target.Id} (IsInSortie before={target.IsInSortie})");
				target.Homing();
				Debug.WriteLine($"Organization.Homing: Homing fleet {target.Id} (IsInSortie after={target.IsInSortie})");
			}
		}
		/// <summary>
		/// 出撃中に艦娘情報が更新された際に、その情報を反映
		/// </summary>
		internal void Update(kcsapi_ship_deck source)
		{
			if (source == null) return;

			if (source.api_ship_data != null)
			{
				foreach (var ship in source.api_ship_data)
				{
					var target = this.Ships[ship.api_id];
					if (target == null) continue;

					target.Update(ship);

					// 出撃中マーカー等を維持
					if (this.evacuatedShipsIds.Any(x => target.Id == x)) target.Situation |= ShipSituation.Evacuation;
					if (this.towShipIds.Any(x => target.Id == x)) target.Situation |= ShipSituation.Tow;
				}
			}

			if (source.api_deck_data != null)
			{
				foreach (var deck in source.api_deck_data)
				{
					var target = this.Fleets[deck.api_id];
					if (target == null) continue;

					target.Update(deck);
				}
			}
		}

		#endregion
	}
}
