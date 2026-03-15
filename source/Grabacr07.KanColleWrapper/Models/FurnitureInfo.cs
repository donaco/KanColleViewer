using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 家具の種類に基づく情報を表します。
	/// </summary>
	public class FurnitureInfo : RawDataWrapper<kcsapi_mst_furniture>, IIdentifiable
	{
		public int Id => this.RawData.api_id;

		public string Name => this.RawData.api_title;

		public string Description => this.RawData.api_description;

		public int Price => this.RawData.api_price;

		public bool IsForSale => this.RawData.api_saleflg == 1;

		internal FurnitureInfo(kcsapi_mst_furniture rawData) : base(rawData) { }

		public override string ToString()
		{
			return $"ID = {this.Id}, Name = \"{this.Name}\"";
		}
	}
}
