using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Catalogs
{
	public class SlotItemCatalogViewModel : WindowViewModel
	{
		private readonly Subject<Unit> updateSource = new Subject<Unit>();

		public SlotItemCatalogWindowSettings Settings { get; }

		#region SlotItems 変更通知プロパティ

		private IReadOnlyCollection<SlotItemCounter> _SlotItems;

		public IReadOnlyCollection<SlotItemCounter> SlotItems
		{
			get { return this._SlotItems; }
			set
			{
				if (this._SlotItems != value)
				{
					this._SlotItems = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SlotItemTypes 変更通知プロパティ

		private IReadOnlyCollection<SlotItemTypeViewModel> _SlotItemTypes;

		public IReadOnlyCollection<SlotItemTypeViewModel> SlotItemTypes
		{
			get { return this._SlotItemTypes; }
			set
			{
				if (this._SlotItemTypes != value)
				{
					this._SlotItemTypes = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public bool CheckAllSlotItemTypes
		{
			get { return this.SlotItemTypes != null && this.SlotItemTypes.All(x => x.IsSelected); }
			set
			{
				if (this.SlotItemTypes == null) return;
				foreach (var type in this.SlotItemTypes) type.Set(value);
				this.Update();
			}
		}

		#region IsReloading 変更通知プロパティ

		private bool _IsReloading;

		public bool IsReloading
		{
			get { return this._IsReloading; }
			set
			{
				if (this._IsReloading != value)
				{
					this._IsReloading = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public SlotItemCatalogViewModel()
		{
			this.Title = "所有装備一覧";
			this.Settings = new SlotItemCatalogWindowSettings();

			// UI スレッドの SynchronizationContext を取得（コンストラクタは UI スレッドで実行される）
			var context = SynchronizationContext.Current ?? new SynchronizationContext();
			var uiScheduler = new SynchronizationContextScheduler(context);

			// 装備種類リストを初期化（マスターに存在する Type のみ）
			var master = KanColleClient.Current.Master;
			var presentTypes = master.SlotItems.Values
				.Select(x => x.Type)
				.Distinct()
				.ToList();

			// ここで表示優先順を定義します。必要な順に並べ替えてください。
			// 例: 主砲 → 副砲 → 魚雷 → 艦上戦闘機 ...（欲しい順で編集）
			var preferredOrder = new[]
			{
				SlotItemType.艦上戦闘機,
				SlotItemType.艦上爆撃機,
				SlotItemType.艦上攻撃機,
				SlotItemType.艦上偵察機,
				SlotItemType.水上偵察機,
				SlotItemType.水上爆撃機,
				SlotItemType.水上戦闘機,
				SlotItemType.対潜哨戒機,
				SlotItemType.回転翼機,

				SlotItemType.小口径主砲,
				SlotItemType.中口径主砲,
				SlotItemType.大口径主砲,

				SlotItemType.三式弾,
				SlotItemType.徹甲弾,
				SlotItemType.照明弾,

				SlotItemType.副砲,
				SlotItemType.対空機銃,
				SlotItemType.高射装置,

				SlotItemType.魚雷,
				SlotItemType.甲標的,
				SlotItemType.潜水艦魚雷,

				SlotItemType.ソナー,
				SlotItemType.大型ソナー,
				SlotItemType.爆雷,

				SlotItemType.小型電探,
				SlotItemType.大型電探,
				SlotItemType.潜水艦装備,

				SlotItemType.大発動艇,
				SlotItemType.陸戦部隊,
				SlotItemType.内火艇,
				SlotItemType.ドラム缶,

				SlotItemType.戦闘糧食,
				SlotItemType.応急修理要員,
				SlotItemType.水上艦要員,
				SlotItemType.航空要員,
				SlotItemType.洋上補給,

				SlotItemType.陸上攻撃機,
				SlotItemType.大型陸上機,
				SlotItemType.噴式戦闘爆撃機,
				SlotItemType.局地戦闘機,
				SlotItemType.大型飛行艇,
				SlotItemType.陸上偵察機,

				SlotItemType.機関部強化,
				SlotItemType.増設バルジ,
				SlotItemType.大型バルジ,

				SlotItemType.探照灯,
				SlotItemType.大型探照灯,
				SlotItemType.対地装備,
				SlotItemType.水上艦装備,
				SlotItemType.司令部施設,
				SlotItemType.艦艇修理施設,
				// 必要に応じて残りを追加...
			};

			this.SlotItemTypes = presentTypes
				.OrderBy(t =>
				{
					var idx = Array.IndexOf(preferredOrder, t);
					return idx >= 0 ? idx : int.MaxValue; // 定義にないものは末尾へ
				})
				.ThenBy(t => (int)t) // 同順位内は数値で安定ソート
				.Select(t => new SlotItemTypeViewModel(t)
				{
					IsSelected = true,
					SelectionChangedAction = () => this.Update()
				})
				.ToList();

			this.updateSource
				.Do(_ => this.IsReloading = true)
				.Throttle(TimeSpan.FromMilliseconds(100))
				.Select(_ => this.UpdateCore())
				.Do(_ => this.IsReloading = false)
				.ObserveOn(uiScheduler)             // Rx-XAML -> Rx-Core SynchronizationContextScheduler
				.Subscribe(x => this.SlotItems = x)
				.AddTo(this);

			this.Update();
		}

		public void Update()
		{
			this.RaisePropertyChanged(nameof(this.CheckAllSlotItemTypes));
			this.updateSource.OnNext(Unit.Default);
		}

		private List<SlotItemCounter> UpdateCore()
		{
			var ships = KanColleClient.Current.Homeport.Organization.Ships.Values.ToList();
			var items = KanColleClient.Current.Homeport.Itemyard.SlotItems.Values.ToList();
			var master = KanColleClient.Current.Master.SlotItems;

			// dic (Dictionary<TK,TV>)
			//  Key:   装備のマスター ID
			//  Value: Key が示す ID に該当する所有装備カウンター
			var dic = items
				.GroupBy(x => x.Info.Id)
				.ToDictionary(g => g.Key, g => new SlotItemCounter(master[g.Key], g));

			foreach (var ship in ships)
			{
				foreach (var target in ship.EquippedItems.Select(slot => new { slot, counter = dic[slot.Item.Info.Id] }))
				{
					target.counter.AddShip(ship, target.slot.Item.Level, target.slot.Item.Proficiency);
				}
			}

			// 選択された装備種類のみ表示する
			var selectedTypeIds = new HashSet<int>(this.SlotItemTypes.Where(t => t.IsSelected).Select(t => t.Id));

			return dic.Values
				.Where(x => selectedTypeIds.Contains((int)x.Target.Type))
				.OrderBy(x => x.Target.CategoryId)
				.ThenBy(x => x.Target.Id)
				.ToList();
		}

		public void SetSlotItemType(int[] ids)
		{
			foreach (var type in this.SlotItemTypes) type.Set(ids.Any(id => type.Id == id));
			this.Update();
		}
	}
}
