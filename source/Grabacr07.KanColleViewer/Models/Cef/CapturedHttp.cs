using System.Collections.Generic;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public class CapturedHttp
	{
		public string Url { get; set; }
		public string Method { get; set; }
		public int StatusCode { get; set; }
		public string RequestBody { get; set; }
		public string ResponseBody { get; set; }
		public IDictionary<string, string> ResponseHeaders { get; set; }
	}
}
