using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Microsoft.Win32;

namespace Grabacr07.KanColleViewer.Plugins.ViewModels
{
	internal class PortalViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		#region IsTopMost 変更通知プロパティ

		private bool _IsTopMost;

		/// <summary>
		/// ポップアップウィンドウを常に最前面に表示するかどうかを示す値を取得または設定します。
		/// </summary>
		public bool IsTopMost
		{
			get { return this._IsTopMost; }
			set
			{
				if (this._IsTopMost != value)
				{
					this._IsTopMost = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		private const int DeepSeaThreshold = 1500;

		/// <summary>
		/// ポップアップウィンドウのインスタンスを保持します（多重起動防止用）。
		/// </summary>
		private Views.MasterDataWindow _popupWindow;

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

		/// <summary>
		/// マスターデータを読み込みます。CallMethodButton から呼ばれます。
		/// </summary>
		public void Load()
		{
			this.UpdateItems();
		}

		/// <summary>
		/// 別ウィンドウでマスターデータを表示します。
		/// </summary>
		public void ShowPopupWindow()
		{
			try
			{
				if (this._popupWindow != null && this._popupWindow.IsLoaded)
				{
					this._popupWindow.Activate();
					return;
				}

				this._popupWindow = new Views.MasterDataWindow
				{
					DataContext = this,
				};

				this._popupWindow.Closed += (s, e) =>
				{
					this._popupWindow = null;
				};

				this._popupWindow.Show();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MasterDataViewer] ポップアップウィンドウの表示に失敗: {ex.Message}");
				this._popupWindow = null;
			}
		}

		/// <summary>
		/// 現在表示中のマスターデータを CSV でエクスポートします。
		/// </summary>
		public void ExportCsv()
		{
			if (this.Items == null || this.Items.Count == 0) return;

			var dialog = new SaveFileDialog
			{
				Title = "CSV エクスポート",
				Filter = "CSV ファイル (*.csv)|*.csv",
				FileName = $"MasterData_{this.SelectedCategory}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
			};

			if (dialog.ShowDialog() != true) return;

			// パス検証（無効文字・拡張子チェック）
			if (!IsValidExportPath(dialog.FileName, ".csv")) return;

			try
			{
				using (var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8))
				{
					// BOM 付き UTF-8 で出力（Excel 対応）
					writer.WriteLine("Id,Name,Detail");

					foreach (var item in this.Items)
					{
						writer.WriteLine($"{item.Id},{EscapeCsvField(item.Name)},{EscapeCsvField(item.Detail)}");
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MasterDataViewer] CSV エクスポートに失敗: {ex.Message}");
			}
		}

		/// <summary>
		/// 現在表示中のマスターデータを XML でエクスポートします。
		/// </summary>
		public void ExportXml()
		{
			if (this.Items == null || this.Items.Count == 0) return;

			var dialog = new SaveFileDialog
			{
				Title = "XML エクスポート",
				Filter = "XML ファイル (*.xml)|*.xml",
				FileName = $"MasterData_{this.SelectedCategory}_{DateTime.Now:yyyyMMdd_HHmmss}.xml",
			};

			if (dialog.ShowDialog() != true) return;

			// パス検証（無効文字・拡張子チェック）
			if (!IsValidExportPath(dialog.FileName, ".xml")) return;

			try
			{
				var document = new XDocument(
					new XDeclaration("1.0", "utf-8", "yes"),
					new XElement("MasterData",
						new XAttribute("Category", this.SelectedCategory ?? ""),
						new XAttribute("ExportedAt", DateTime.Now.ToString("o")),
						this.Items.Select(item =>
							new XElement("Item",
								new XElement("Id", item.Id),
								new XElement("Name", item.Name ?? ""),
								new XElement("Detail", item.Detail ?? "")
							)
						)
					)
				);

				document.Save(dialog.FileName);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MasterDataViewer] XML エクスポートに失敗: {ex.Message}");
			}
		}

		/// <summary>
		/// エクスポート先パスの妥当性を検証します。
		/// </summary>
		/// <param name="path">検証するファイルパス。</param>
		/// <param name="expectedExtension">期待する拡張子（例: ".csv"）。</param>
		/// <returns>パスが有効な場合 true。</returns>
		private static bool IsValidExportPath(string path, string expectedExtension)
		{
			if (string.IsNullOrWhiteSpace(path)) return false;

			// 無効文字チェック
			if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

			// 拡張子チェック（大文字小文字を区別しない）
			if (!Path.GetExtension(path).Equals(expectedExtension, StringComparison.OrdinalIgnoreCase)) return false;

			return true;
		}

		/// <summary>
		/// CSV フィールドをエスケープします。カンマ・改行・ダブルクォートを含む場合はダブルクォートで囲みます。
		/// </summary>
		private static string EscapeCsvField(string field)
		{
			if (string.IsNullOrEmpty(field)) return "";

			if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
			{
				return "\"" + field.Replace("\"", "\"\"") + "\"";
			}
			return field;
		}

		private string FormatShipName(ShipInfo ship)
		{
			// 読み仮名が空、またはハイフンのみの場合は非表示
			if (string.IsNullOrEmpty(ship.Kana) || ship.Kana.Trim() == "-")
			{
				return ship.Name;
			}
			return $"{ship.Name} ({ship.Kana})";
		}

		private string RemoveHtmlBreaks(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}
			return Regex.Replace(text, @"<\s*br\s*/?\s*>", "", RegexOptions.IgnoreCase);
		}

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
