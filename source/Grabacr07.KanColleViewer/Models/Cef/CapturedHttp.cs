using System.Collections.Generic;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public class CapturedHttp
	{
		public string Url { get; set; } = string.Empty;
		public string Method { get; set; } = string.Empty;
		public int StatusCode { get; set; }
		public string RequestBody { get; set; } = string.Empty;
		public string ResponseBody { get; set; } = string.Empty;
		public IDictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>();
	}
}
