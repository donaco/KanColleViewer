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
				System.Diagnostics.Debug.WriteLine($"[AirBases.Update] airBases is null or empty, cleared AreaGroup.");
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
				EquipmentIconTypes = GetEquipmentIconTypes(x.api_plane_info)
			}).ToArray() ?? new AirBaseInfo[0];

			this.ActionKind = rawData?.FirstOrDefault()?.api_action_kind ?? 0;
		}

		#region 装備スロットからアイコンタイプの配列を解決
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
					// api_slotid が 0 以下の場合は空文字列を追加
					if (slotId <= 0)
					{
						icons.Add("");
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
					icons.Add("");
				}
			}

			return icons.ToArray();
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
				case 61:
					return "期間限定海域";
				default:
					return $"海域 {areaId}";
			}
		}
		#endregion

		public override string ToString()
		{
			return $"AreaId = {this.AreaId}, Name = \"{this.AreaName}\", Bases = {this.BaseCount}";
		}
	}
}
