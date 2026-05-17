using System;
using System.Collections.Specialized;
using System.Web;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper.Models
{
	public class SvData<T> : RawDataWrapper<svdata<T>>
	{
		public NameValueCollection Request { get; private set; }

		public bool IsSuccess => this.RawData.api_result == 1;

		public T Data => this.RawData.api_data;

		public kcsapi_deck[] Fleets => this.RawData.api_data_deck;

		public SvData(svdata<T> rawData, string reqBody)
			: base(rawData)
		{
			this.Request = HttpUtility.ParseQueryString(reqBody);
		}
	}

	public class SvData : RawDataWrapper<svdata>
	{
		public NameValueCollection Request { get; private set; }

		public bool IsSuccess => this.RawData.api_result == 1;

		public SvData(svdata rawData, string reqBody)
			: base(rawData)
		{
			this.Request = HttpUtility.ParseQueryString(reqBody);
		}


		#region Parse methods (generic)

		#endregion

		#region Parse methods (non generic)

		#endregion
	}
}
