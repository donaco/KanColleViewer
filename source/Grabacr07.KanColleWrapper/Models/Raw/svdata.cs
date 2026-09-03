using System;
using System.Collections.Generic;
using System.Linq;

// API の応答仕様に合わせた小文字の型名のため、CS8981 を抑制します。
#pragma warning disable CS8981

namespace Grabacr07.KanColleWrapper.Models.Raw
{
	// ReSharper disable InconsistentNaming
	public class svdata
	{
		public int api_result { get; set; }
		public string api_result_msg { get; set; }
	}

	public class svdata<T> : svdata
	{
		public T api_data { get; set; }
		public kcsapi_deck[] api_data_deck { get; set; }
	}
	// ReSharper restore InconsistentNaming
}

#pragma warning restore CS8981
