using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 課金アイテムの種類に基づく情報を表します。
	/// </summary>
	public class PayItemInfo : RawDataWrapper<kcsapi_mst_payitem>, IIdentifiable
	{
		public int Id => this.RawData.api_id;

		public string Name => this.RawData.api_name;

		public string Description => this.RawData.api_description;

		public int Price => this.RawData.api_price;

		internal PayItemInfo(kcsapi_mst_payitem rawData) : base(rawData) { }

		public override string ToString()
		{
			return $"ID = {this.Id}, Name = \"{this.Name}\"";
		}
	}
}
