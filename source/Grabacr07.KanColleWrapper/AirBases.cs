using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 航空隊（基地航空隊）の情報を管理します
	/// </summary>
	public class AirBases : Notifier
	{
		#region AreaGroup 変更通知プロパティ

		private MemberTable<AirBase> _AreaGroup;

		/// <summary>
		/// 海域ごとにグループ化された航空隊を取得します
		/// </summary>
		public MemberTable<AirBase> AreaGroup
		{
			get { return this._AreaGroup; }
			private set
			{
				if (this._AreaGroup != value)
				{
					this._AreaGroup = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		internal AirBases()
		{
			this.AreaGroup = new MemberTable<AirBase>();
		}

		/// <summary>
		/// 航空隊情報を更新します
		/// </summary>
		internal void Update(kcsapi_air_base[] airBases, kcsapi_air_base_expanded_info[] expandedInfo)
		{
			System.Diagnostics.Debug.WriteLine($"[AirBases.Update] Called with {airBases?.Length ?? 0} airBases.");

			if (airBases == null || airBases.Length == 0)
			{
				this.AreaGroup = new MemberTable<AirBase>();
				System.Diagnostics.Debug.WriteLine($"[AirBases.Update] airbases is null or empty, cleared AreaGroup.");
				return;
			}

			// 海域IDでグループ化
			var groupedByArea = airBases.GroupBy(x => x.api_area_id).ToDictionary(g => g.Key, g => g.ToArray());

			System.Diagnostics.Debug.WriteLine($"[AirBases.Update] Grouped into {groupedByArea.Count} areas.");

			// 拡張情報をディクショナリに変換（海域IDをキーに）
			var expandedDict = (expandedInfo ?? new kcsapi_air_base_expanded_info[0])
				.ToDictionary(x => x.api_area_id);

			// 各海域の航空隊を作成
			var airBasesByArea = new Dictionary<int, AirBase>();
			foreach (var kvp in groupedByArea)
			{
				var areaId = kvp.Key;
				var basesForArea = kvp.Value;
				var expandedInfoForArea = expandedDict.ContainsKey(areaId) ? expandedDict[areaId] : null;

				airBasesByArea[areaId] = new AirBase(areaId, basesForArea, expandedInfoForArea);
				System.Diagnostics.Debug.WriteLine($"[AirBases.Update] Created AirBase for area {areaId}.");
			}

			this.AreaGroup = new MemberTable<AirBase>(airBasesByArea.Values);
			System.Diagnostics.Debug.WriteLine($"[AirBases.Update] Set AreaGroup to {this.AreaGroup.Count} items.");
		}

		#region API-driven updates (change_name / set_action)

		/// <summary>
		/// API の change_name による名称変更を反映します（UI スレッドで呼んでください）。
		/// </summary>
		internal void ApplyChangeName(int areaId, int baseId, string newName)
		{
			try
			{
				if (this.AreaGroup == null) return;
				if (!this.AreaGroup.ContainsKey(areaId)) return;

				var target = this.AreaGroup[areaId];
				target?.UpdateName(baseId, newName);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBases.ApplyChangeName] Error: {ex}");
			}
		}

		/// <summary>
		/// API の set_action による出撃状態変更を反映します（UI スレッドで呼んでください）。
		/// </summary>
		internal void ApplySetAction(int areaId, int baseId, int actionKind)
		{
			try
			{
				if (this.AreaGroup == null) return;
				if (!this.AreaGroup.ContainsKey(areaId)) return;

				var target = this.AreaGroup[areaId];
				target?.UpdateAction(baseId, actionKind);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBases.ApplySetAction] Error: {ex}");
			}
		}

		#endregion
	}

	/// <summary>
	/// 特定の海域における航空隊情報
	/// </summary>
	public class AirBase : Notifier, IIdentifiable
	{
		private readonly kcsapi_air_base[] _rawData;
		private readonly kcsapi_air_base_expanded_info _expandedInfo;
		public string[] AirBaseNames { get; private set; }

		#region AreaId 海域ID

		private int _AreaId;

		/// <summary>
		/// 海域ID を取得
		/// </summary>
		public int AreaId
		{
			get { return this._AreaId; }
			private set
			{
				if (this._AreaId != value)
				{
					this._AreaId = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AreaName 海域名

		private string _AreaName;

		/// <summary>
		/// 海域名を取得（"中部海域"、"南西海域"など）
		/// </summary>
		public string AreaName
		{
			get { return this._AreaName; }
			private set
			{
				if (this._AreaName != value)
				{
					this._AreaName = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region MaintenanceLevel 整備レベル

		private int _MaintenanceLevel;

		/// <summary>
		/// 整備レベルを取得
		/// </summary>
		public int MaintenanceLevel
		{
			get { return this._MaintenanceLevel; }
			private set
			{
				if (this._MaintenanceLevel != value)
				{
					this._MaintenanceLevel = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region BaseCount 基地航空隊の数

		private int _BaseCount;

		/// <summary>
		/// 各海域に配置されている基地航空隊の数を取得
		/// </summary>
		public int BaseCount
		{
			get { return this._BaseCount; }
			private set
			{
				if (this._BaseCount != value)
				{
					this._BaseCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region ActionKind 出撃状態

		private int _ActionKind;

		/// <summary>
		/// 出撃状態を取得（1=出撃、2=防空、3=退避、4=休息、0=待機）
		/// </summary>
		public int ActionKind
		{
			get { return this._ActionKind; }
			private set
			{
				if (this._ActionKind != value)
				{
					this._ActionKind = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AirBaseInfos 基地情報リスト

		private AirBaseInfo[] _AirBaseInfos;

		/// <summary>
		/// 各基地の名前と出撃状態を取得
		/// </summary>
		public AirBaseInfo[] AirBaseInfos
		{
			get { return this._AirBaseInfos; }
			private set
			{
				if (this._AirBaseInfos != value)
				{
					this._AirBaseInfos = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public int Id => this.AreaId;

		internal AirBase(int areaId, kcsapi_air_base[] rawData, kcsapi_air_base_expanded_info expandedInfo)
		{
			this._rawData = rawData;
			this._expandedInfo = expandedInfo;

			this.AreaId = areaId;
			this.AreaName = GetAreaNameFromId(areaId);
			this.MaintenanceLevel = expandedInfo?.api_maintenance_level ?? 0;
			this.BaseCount = rawData.Length;
			this.AirBaseNames = rawData?.Select(x => x.api_name).ToArray() ?? new string[0];

			// 各基地の情報を個別に保持（行動半径・装備スロットを計算）
			this.AirBaseInfos = rawData?.Select(x => new AirBaseInfo
			{
				Name = x.api_name,
				ActionKind = x.api_action_kind,
				Distance = (x.api_distance?.api_base ?? 0) + (x.api_distance?.api_bonus ?? 0),
				EquipmentSlotIds = x.api_plane_info?
					.Take(4)
					.Select(p => p.api_slotid)
					.ToArray() ?? new int[0],
				EquipmentSlotItemIds = GetEquipmentSlotItemIds(x.api_plane_info),
				EquipmentIconTypes = GetEquipmentIconTypes(x.api_plane_info),
				EquipmentTypes = GetEquipmentTypes(x.api_plane_info),
				EquipmentNames = GetEquipmentNames(x.api_plane_info),
				EquipmentLevels = GetEquipmentLevels(x.api_plane_info),
				EquipmentAlvs = GetEquipmentAlvs(x.api_plane_info),
				EquipmentAntiAirs = GetEquipmentAntiAirs(x.api_plane_info),
				EquipmentIntercepts = GetEquipmentIntercepts(x.api_plane_info),
				EquipmentAntibombs = GetEquipmentAntibombs(x.api_plane_info),
				EquipmentConds = GetEquipmentConds(x.api_plane_info),
				EquipmentCounts = GetEquipmentCounts(x.api_plane_info),
				EquipmentMaxCounts = GetEquipmentMaxCounts(x.api_plane_info)
			}).ToArray() ?? new AirBaseInfo[0];

			this.ActionKind = rawData?.FirstOrDefault()?.api_action_kind ?? 0;
		}

		#region 装備スロットのIconType を取得
		private static string[] GetEquipmentIconTypes(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new string[0];

			var icons = new List<string>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					// api_slotid が 0 以下の場合は "Empty" を追加
					if (slotId <= 0)
					{
						icons.Add("Empty");
						continue;
					}

					// 装備 ID から Itemyard 経由で IconType を取得
					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					if (slotItem != null && slotItem.Info != null)
					{
						icons.Add(slotItem.Info.IconType.ToString());
					}
					else
					{
						// 未登録またはマスターなし -> デフォルトアイコン（飛行機）
						icons.Add("Fighter");
					}
				}
				catch
				{
					icons.Add("Empty");
				}
			}

			return icons.ToArray();
		}
		#endregion

		#region 装備スロットの(SlotItemType)の取得
		private static int[] GetEquipmentTypes(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var types = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						types.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					// SlotItemType (api_type[2]) を取得
					types.Add((int)(slotItem?.Info?.Type ?? 0));
				}
				catch
				{
					types.Add(0);
				}
			}

			return types.ToArray();
		}
		#endregion

		#region 装備スロットの装備ID(api_slotitem_id)を取得
		private static int[] GetEquipmentSlotItemIds(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var ids = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						ids.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					// 装備マスターID (api_slotitem_id) を取得
					ids.Add(slotItem?.Info?.Id ?? 0);
				}
				catch
				{
					ids.Add(0);
				}
			}

			return ids.ToArray();
		}
		#endregion

		#region 航空隊　名前・状態の個別更新

		/// <summary>
		/// 指定基地の名前を更新して AirBaseInfos / AirBaseNames を再構築・通知します。
		/// </summary>
		internal void UpdateName(int baseId, string newName)
		{
			try
			{
				var target = this._rawData?.FirstOrDefault(x => x.api_rid == baseId);
				if (target == null) return;

				target.api_name = newName ?? target.api_name;

				// 再構築・通知
				this.RebuildAirBaseInfos();
				this.AirBaseNames = this._rawData?.Select(x => x.api_name).ToArray() ?? new string[0];
				this.RaisePropertyChanged(nameof(this.AirBaseNames));
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateName] Error: {ex}");
			}
		}

		/// <summary>
		/// 出撃状態を更新して AirBaseInfos / ActionKind を再構築・通知します。
		/// </summary>
		internal void UpdateAction(int baseId, int actionKind)
		{
			try
			{
				var target = this._rawData?.FirstOrDefault(x => x.api_rid == baseId);
				if (target == null) return;

				target.api_action_kind = actionKind;

				// 再構築・通知
				this.RebuildAirBaseInfos();
				this.ActionKind = this._rawData?.FirstOrDefault()?.api_action_kind ?? 0;
				this.RaisePropertyChanged(nameof(this.ActionKind));
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateAction] Error: {ex}");
			}
		}

		#endregion

		#region 海域IDから海域名を取得
		/// <summary>
		/// 海域IDから海域名を取得
		/// </summary>
		private static string GetAreaNameFromId(int areaId)
		{
			switch (areaId)
			{
				case 6:
					return "中部海域";
				case 7:
					return "南西海域";
				default:
					if (areaId >= 60)
					{
						return "期間限定海域";
					}
					return $"海域 {areaId}";
			}
		}
		#endregion

		#region スロット変更時、情報更新
		internal void UpdateFromSetPlane(kcsapi_plane_info[] planeInfo, ApiDistance distance, int baseId)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return;

			try
			{
				var targetBase = this._rawData?.FirstOrDefault(x => x.api_rid == baseId);
				if (targetBase == null)
				{
					System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSetPlane] Target base with baseId={baseId} not found in area {this.AreaId}");
					return;
				}

				if (distance != null)
				{
					targetBase.api_distance = distance;
				}

				foreach (var newPlane in planeInfo)
				{
					try
					{
						var existingPlane = targetBase.api_plane_info?.FirstOrDefault(p => p.api_squadron_id == newPlane.api_squadron_id);
						if (existingPlane != null)
						{
							existingPlane.api_slotid = newPlane.api_slotid;
							existingPlane.api_count = newPlane.api_count;
							existingPlane.api_max_count = newPlane.api_max_count;
							existingPlane.api_state = newPlane.api_state;

							// 追加: api_cond を反映（mapinfo が持つ場合や set_plane が送る場合に備える）
							try
							{
								existingPlane.api_cond = newPlane.api_cond;
							}
							catch
							{
								// 念のため例外吸収。フィールドが存在しない等のケースを避ける。
							}

							System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSetPlane] Updated area {this.AreaId}, baseId {baseId}, squadron {newPlane.api_squadron_id}: slotid={newPlane.api_slotid}, cond={existingPlane.api_cond}");
						}
					}
					catch (Exception exInner)
					{
						System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSetPlane] inner error: {exInner}");
					}
				}

				RebuildAirBaseInfos();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSetPlane] Error: {ex}");
			}
		}
		#endregion

		#region スロット更新時、行動半径を再計算
		private void RebuildAirBaseInfos()
		{
			try
			{
				this.AirBaseInfos = this._rawData?.Select(x => new AirBaseInfo
				{
					Name = x.api_name,
					ActionKind = x.api_action_kind,
					Distance = (x.api_distance?.api_base ?? 0) + (x.api_distance?.api_bonus ?? 0),
					EquipmentSlotIds = x.api_plane_info?
						.Take(4)
						.Select(p => p.api_slotid)
						.ToArray() ?? new int[0],
					EquipmentSlotItemIds = GetEquipmentSlotItemIds(x.api_plane_info),
					EquipmentIconTypes = GetEquipmentIconTypes(x.api_plane_info),
					EquipmentTypes = GetEquipmentTypes(x.api_plane_info),
					EquipmentNames = GetEquipmentNames(x.api_plane_info),
					EquipmentLevels = GetEquipmentLevels(x.api_plane_info),
					EquipmentAlvs = GetEquipmentAlvs(x.api_plane_info),
					EquipmentAntiAirs = GetEquipmentAntiAirs(x.api_plane_info),
					EquipmentIntercepts = GetEquipmentIntercepts(x.api_plane_info),
					EquipmentAntibombs = GetEquipmentAntibombs(x.api_plane_info),
					EquipmentCounts = GetEquipmentCounts(x.api_plane_info),
					EquipmentConds = GetEquipmentConds(x.api_plane_info),
					EquipmentMaxCounts = GetEquipmentMaxCounts(x.api_plane_info)
				}).ToArray() ?? new AirBaseInfo[0];
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBase.RebuildAirBaseInfos] Error: {ex}");
			}
		}
		#endregion

		#region 装備名の取得
		private static string[] GetEquipmentNames(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new string[0];

			var names = new System.Collections.Generic.List<string>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						names.Add("");
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					if (slotItem != null && slotItem.Info != null)
					{
						names.Add(slotItem.Info.Name);
					}
					else
					{
						names.Add("");
					}
				}
				catch
				{
					names.Add("");
				}
			}

			return names.ToArray();
		}
		#endregion

		#region 装備改修値の取得
		private static int[] GetEquipmentLevels(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var levels = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						levels.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					levels.Add(slotItem?.Level ?? 0);
				}
				catch
				{
					levels.Add(0);
				}
			}

			return levels.ToArray();
		}
		#endregion

		#region 装備熟練度の取得
		private static int[] GetEquipmentAlvs(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var alvs = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						alvs.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					alvs.Add(slotItem?.Proficiency ?? 0);
				}
				catch
				{
					alvs.Add(0);
				}
			}

			return alvs.ToArray();
		}
		#endregion

		#region 装備対空値の取得
		private static int[] GetEquipmentAntiAirs(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var antiAirs = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						antiAirs.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					antiAirs.Add(slotItem?.Info?.AA ?? 0);
				}
				catch
				{
					antiAirs.Add(0);
				}
			}

			return antiAirs.ToArray();
		}
		#endregion

		#region 装備迎撃値の取得
		private static int[] GetEquipmentIntercepts(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var intercepts = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						intercepts.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					// api_houm が迎撃値
					intercepts.Add(slotItem?.Info?.RawData?.api_houm ?? 0);
				}
				catch
				{
					intercepts.Add(0);
				}
			}

			return intercepts.ToArray();
		}
		#endregion

		#region 装備対爆値の取得
		private static int[] GetEquipmentAntibombs(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var antibombs = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					var slotId = plane.api_slotid;
					if (slotId <= 0)
					{
						antibombs.Add(0);
						continue;
					}

					var homeport = KanColleClient.Current?.Homeport;
					var slotItem = homeport?.Itemyard?.SlotItems?[slotId];

					// api_houk が対爆値
					antibombs.Add(slotItem?.Info?.RawData?.api_houk ?? 0);
				}
				catch
				{
					antibombs.Add(0);
				}
			}

			return antibombs.ToArray();
		}
		#endregion

		#region 搭載数の取得
		private static int[] GetEquipmentCounts(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var counts = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					counts.Add(plane.api_count);
				}
				catch
				{
					counts.Add(0);
				}
			}

			return counts.ToArray();
		}

		private static int[] GetEquipmentMaxCounts(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var maxCounts = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					maxCounts.Add(plane.api_max_count);
				}
				catch
				{
					maxCounts.Add(0);
				}
			}

			return maxCounts.ToArray();
		}
		#endregion

		#region 搭載数の取得(補給時)
		/// <summary>
		/// 補給 API のレスポンスから搭載数を更新します。
		/// </summary>
		internal void UpdateFromSupply(kcsapi_plane_info[] planeInfo, ApiDistance distance)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return;

			try
			{
				bool updated = false;

				// 全基地の全スロットを走査して一致する squadron_id を更新
				foreach (var baseData in this._rawData)
				{
					if (baseData.api_plane_info == null) continue;

					// 距離情報がある場合は更新
					if (distance != null)
					{
						baseData.api_distance = distance;
					}

					foreach (var newPlane in planeInfo)
					{
						var existingPlane = baseData.api_plane_info.FirstOrDefault(p => p.api_squadron_id == newPlane.api_squadron_id);
						if (existingPlane != null)
						{
							// 搭載数を更新
							existingPlane.api_count = newPlane.api_count;
							existingPlane.api_max_count = newPlane.api_max_count;
							existingPlane.api_state = newPlane.api_state;

							// Cond も更新（補給で Cond がリセットされる場合がある）
							try
							{
								existingPlane.api_cond = newPlane.api_cond;
							}
							catch { }

							updated = true;

							System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSupply] Updated area {this.AreaId}, squadron {newPlane.api_squadron_id}: count={newPlane.api_count}/{newPlane.api_max_count}, cond={newPlane.api_cond}");
						}
					}
				}

				// 更新があった場合のみ AirBaseInfos を再構築
				if (updated)
				{
					RebuildAirBaseInfos();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AirBase.UpdateFromSupply] Error: {ex}");
			}
		}
		#endregion

		#region 装備 Cond 値の取得
		private static int[] GetEquipmentConds(kcsapi_plane_info[] planeInfo)
		{
			if (planeInfo == null || planeInfo.Length == 0)
				return new int[0];

			var conds = new List<int>();
			foreach (var plane in planeInfo.Take(4))
			{
				try
				{
					conds.Add(plane?.api_cond ?? 0);
				}
				catch
				{
					conds.Add(0);
				}
			}

			return conds.ToArray();
		}
		#endregion

		public override string ToString()
		{
			return $"AreaId = {this.AreaId}, Name = \"{this.AreaName}\", Bases = {this.BaseCount}";
		}
	}
}
