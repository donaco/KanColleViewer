using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// BGM の情報を表します。
	/// </summary>
	public class BGMInfo : RawDataWrapper<kcsapi_mst_bgm>, IIdentifiable
	{
		public int Id => this.RawData.api_id;

		public string Name => this.RawData.api_name;

		public string Detail => this.RawData.api_detail;

		internal BGMInfo(kcsapi_mst_bgm rawData) : base(rawData) { }

		public override string ToString()
		{
			return $"ID = {this.Id}, Name = \"{this.Name}\"";
		}
	}
}
