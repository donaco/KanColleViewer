using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 工廠で開発された装備アイテムを表します。
	/// </summary>
	public class CreatedSlotItem : RawDataWrapper<kcsapi_createitem>
	{
		public bool Succeed => this.RawData.api_create_flag == 1;

		public SlotItemInfo SlotItemInfo { get; }

		public CreatedSlotItem(kcsapi_createitem rawData)
			: base(rawData)
		{
			try
			{
				// 安全に参照を取得してから処理する
				var master = KanColleClient.Current?.Master;
				if (master == null)
				{
					this.SlotItemInfo = null;
					return;
				}

				int slotitemId = -1;

				if (this.Succeed)
				{
					// 成功時は api_slot_item から取得する（存在チェック）
					if (rawData != null && rawData.api_slot_item != null)
					{
						slotitemId = rawData.api_slot_item.api_slotitem_id;
					}
				}
				else
				{
					// 失敗時は api_fdata をパースして mst id を得る（安全に）
					if (rawData != null && !string.IsNullOrEmpty(rawData.api_fdata))
					{
						try
						{
							var parts = rawData.api_fdata.Split(',');
							if (parts.Length > 1)
							{
								int parsed;
								if (int.TryParse(parts[1], out parsed)) slotitemId = parsed;
							}
						}
						catch
						{
							// パース失敗は無視して後続のフォールバックへ
							slotitemId = -1;
						}
					}
				}

				// slotitemId が確定していれば Master から取り出す（存在チェック）
				if (slotitemId > 0)
				{
					try
					{
						if (master.SlotItems != null && master.SlotItems.ContainsKey(slotitemId))
						{
							this.SlotItemInfo = master.SlotItems[slotitemId];
							System.Diagnostics.Debug.WriteLine("createitem: {0} - {1}", this.Succeed, this.SlotItemInfo?.Name ?? "(null)");
						}
						else
						{
							this.SlotItemInfo = null;
						}
					}
					catch
					{
						this.SlotItemInfo = null;
					}
				}
				else
				{
					System.Diagnostics.Debug.WriteLine("CreatedSlotItem: cannot determine slotitem id. api_slot_item={0} api_fdata={1}",
						rawData?.api_slot_item != null ? rawData.api_slot_item.api_slotitem_id.ToString() : "null",
						rawData?.api_fdata ?? "null");
					this.SlotItemInfo = null;
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}
	}
}
