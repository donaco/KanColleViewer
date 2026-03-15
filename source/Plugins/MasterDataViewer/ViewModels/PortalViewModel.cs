using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Livet;

namespace Grabacr07.KanColleViewer.Plugins.ViewModels
{
	public class PortalViewModel : ViewModel
	{
		#region Categories 変更通知プロパティ

		private string[] _Categories;

		public string[] Categories
		{
			get { return this._Categories; }
			set
			{
				if (this._Categories != value)
				{
					this._Categories = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SelectedCategory 変更通知プロパティ

		private string _SelectedCategory;

		public string SelectedCategory
		{
			get { return this._SelectedCategory; }
			set
			{
				if (this._SelectedCategory != value)
				{
					this._SelectedCategory = value;
					this.RaisePropertyChanged();
					this.UpdateItems();
				}
			}
		}

		#endregion

		#region Items 変更通知プロパティ

		private IReadOnlyList<MasterDataItemViewModel> _Items;

		public IReadOnlyList<MasterDataItemViewModel> Items
		{
			get { return this._Items; }
			set
			{
				if (this._Items != value)
				{
					this._Items = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsLoaded 変更通知プロパティ

		private bool _IsLoaded;

		public bool IsLoaded
		{
			get { return this._IsLoaded; }
			set
			{
				if (this._IsLoaded != value)
				{
					this._IsLoaded = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region カテゴリ
		private const int DeepSeaThreshold = 1500;

		public PortalViewModel()
		{
			this.Categories = new[]
			{
				"艦娘",
				"深海棲艦",
				"艦種",
				"装備",
				"深海装備",
				"装備タイプ",
				"消費アイテム",
				"課金アイテム",
				"任務 (遠征)",
				"海域",
				"マップ",
				"家具",
				"BGM",
			};
			this.SelectedCategory = this.Categories[0];
		}
		#endregion

		/// <summary>
		/// マスターデータを読み込みます。CallMethodButton から呼ばれます。
		/// </summary>
		public void Load()
		{
			this.UpdateItems();
		}

		#region 読み仮名を表示
		private string FormatShipName(ShipInfo ship)
		{
			// 読み仮名が空、またはハイフンのみの場合は非表示
			if (string.IsNullOrEmpty(ship.Kana) || ship.Kana.Trim() == "-")
			{
				return ship.Name;
			}
			return $"{ship.Name} ({ship.Kana})";
		}
		#endregion

		#region brタグ消去
		private string RemoveHtmlBreaks(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}
			return Regex.Replace(text, @"<\s*br\s*/?\s*>", "", RegexOptions.IgnoreCase);
		}
		#endregion

		private void UpdateItems()
		{
			var master = KanColleClient.Current.Master;
			if (master == null)
			{
				this.Items = null;
				this.IsLoaded = false;
				return;
			}

			this.IsLoaded = true;

			switch (this.SelectedCategory)
			{
				case "艦娘":
					this.Items = master.Ships.Values
						.Where(x => x.Id <= DeepSeaThreshold)
						.OrderBy(x => x.SortId)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							this.FormatShipName(x),
							$"{x.ShipType.Name} / 耐久: {x.HP} / 装甲: {x.MaxArmer} / 火力: {x.MaxFirepower} / 雷装: {x.MaxTorpedo} / 対空: {x.MaxAA}"))
						.ToList();
					break;

				case "深海棲艦":
					this.Items = master.Ships.Values
						.Where(x => x.Id > DeepSeaThreshold)
						.OrderBy(x => x.SortId)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							this.FormatShipName(x),
							$"{x.ShipType.Name} / 耐久: {x.HP} / 装甲: {x.MaxArmer} / 火力: {x.MaxFirepower} / 雷装: {x.MaxTorpedo} / 対空: {x.MaxAA}"))
						.ToList();
					break;

				case "艦種":
					this.Items = master.ShipTypes.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"ソート順: {x.SortNumber}"))
						.ToList();
					break;

				case "装備":
					this.Items = master.SlotItems.Values
						.Where(x => x.Id <= DeepSeaThreshold)
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"{x.EquipType.Name} / 火力: {x.Firepower} / 雷装: {x.Torpedo} / 対空: {x.AA} / 装甲: {x.Armer} / 命中: {x.Hit} / 回避: {x.Evade} / 索敵: {x.ViewRange}"))
						.ToList();
					break;

				case "深海装備":
					this.Items = master.SlotItems.Values
						.Where(x => x.Id > DeepSeaThreshold)
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"{x.EquipType.Name} / 火力: {x.Firepower} / 雷装: {x.Torpedo} / 対空: {x.AA} / 装甲: {x.Armer} / 命中: {x.Hit} / 回避: {x.Evade} / 索敵: {x.ViewRange}"))
						.ToList();
					break;

				case "装備タイプ":
					this.Items = master.SlotItemEquipTypes.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							""))
						.ToList();
					break;

				case "消費アイテム":
					this.Items = master.UseItems.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							""))
						.ToList();
					break;

				case "課金アイテム":
					this.Items = master.PayItems.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"{x.Price} 円 / {this.RemoveHtmlBreaks(x.Description)}"))
						.ToList();
					break;

				case "任務 (遠征)":
					this.Items = master.Missions.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Title,
							this.RemoveHtmlBreaks(x.Detail ?? "")))
						.ToList();
					break;

				case "海域":
					this.Items = master.MapAreas.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							""))
						.ToList();
					break;

				case "マップ":
					this.Items = master.MapInfos.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"{x.MapArea?.Name ?? "?"} / {x.OperationName} / {this.RemoveHtmlBreaks(x.OperationSummary)}"))
						.ToList();
					break;

				case "家具":
					this.Items = master.Furnitures.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							$"コイン：{x.Price} / 職人: {(x.IsForSale ? "○" : "×")} / {this.RemoveHtmlBreaks(x.Description)}"))
						.ToList();
					break;

				case "BGM":
					this.Items = master.BGMs.Values
						.OrderBy(x => x.Id)
						.Select(x => new MasterDataItemViewModel(
							x.Id,
							x.Name,
							this.RemoveHtmlBreaks(x.Detail ?? "")))
						.ToList();
					break;

				default:
					this.Items = null;
					break;
			}
		}
	}

	/// <summary>
	/// マスターデータの 1 件分を表す ViewModel です。
	/// </summary>
	public class MasterDataItemViewModel
	{
		public int Id { get; }
		public string Name { get; }
		public string Detail { get; }

		public MasterDataItemViewModel(int id, string name, string detail)
		{
			this.Id = id;
			this.Name = name;
			this.Detail = detail;
		}
	}
}
