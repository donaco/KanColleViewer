using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleViewer.Models
{
	public class SallyArea
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024; // 1 MB
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

		public int Area { get; private set; }

		public string Name { get; private set; }

		public Color Color { get; private set; } = Colors.Transparent;

		private SallyArea() { }

		public static SallyArea Default { get; } = new SallyArea();

		public static async Task<SallyArea[]> GetAsync()
		{
			var source = Properties.Settings.Default.SallyAreaSource;
			if (string.IsNullOrWhiteSpace(source))
			{
				Debug.WriteLine("SallyArea.GetAsync: SallyAreaSource が空のためローカルを使用します。");
				return LoadFromLocalFile();
			}

			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
			{
				Debug.WriteLine("SallyArea.GetAsync: URI が無効のためローカルを使用します。 source=" + source);
				return LoadFromLocalFile();
			}

			using (var client = new HttpClient(Helper.GetProxyConfiguredHandler()))
			{
				client.Timeout = RequestTimeout;
				client.MaxResponseContentBufferSize = MaxResponseSizeBytes;

				try
				{
					var response = await client.GetAsync(uri).ConfigureAwait(false);
					if (response.IsSuccessStatusCode)
					{
						var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
						var parsed = ParseAreas(content);
						if (parsed.Length > 0) return parsed;

						Debug.WriteLine("SallyArea.GetAsync: サーバーJSON解析失敗。ローカルを使用します。");
						return LoadFromLocalFile();
					}

					Debug.WriteLine("SallyArea.GetAsync: HTTP失敗 " + (int)response.StatusCode + " " + response.ReasonPhrase);
					return LoadFromLocalFile();
				}
				catch (HttpRequestException ex)
				{
					Debug.WriteLine("SallyArea.GetAsync: HttpRequestException: " + ex);
					return LoadFromLocalFile();
				}
				catch (TaskCanceledException ex)
				{
					Debug.WriteLine("SallyArea.GetAsync: Timeout: " + ex);
					return LoadFromLocalFile();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("SallyArea.GetAsync: Unexpected: " + ex);
					return LoadFromLocalFile();
				}
			}
		}

		private static SallyArea[] LoadFromLocalFile()
		{
			try
			{
				var exePath = Assembly.GetEntryAssembly()?.Location ?? Assembly.GetExecutingAssembly().Location;
				var exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
				var localPath = Path.Combine(exeDir, "json", "EventMap.json");

				if (!File.Exists(localPath))
				{
					Debug.WriteLine("SallyArea.LoadFromLocalFile: ファイルなし: " + localPath);
					return new SallyArea[0];
				}

				var json = File.ReadAllText(localPath);
				var parsed = ParseAreas(json);
				Debug.WriteLine("SallyArea.LoadFromLocalFile: ローカル読込件数=" + parsed.Length);
				return parsed;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("SallyArea.LoadFromLocalFile: 失敗: " + ex);
				return new SallyArea[0];
			}
		}

		private static SallyArea[] ParseAreas(string content)
		{
			try
			{
				var trimmed = (content ?? string.Empty).TrimStart();
				JToken listToken = null;

				if (trimmed.StartsWith("["))
				{
					listToken = JArray.Parse(content);
				}
				else if (trimmed.StartsWith("{"))
				{
					var root = JObject.Parse(content);
					listToken = root["EventMap"];
				}

				var array = listToken as JArray;
				if (array == null) return new SallyArea[0];

				return array
					.OfType<JToken>()
					.Select(x => new SallyArea
					{
						Area = ParseArea(x["area"]),
						Name = (string)x["name"] ?? string.Empty,
						Color = Helper.StringToColor((string)x["color"])
					})
					.ToArray();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("SallyArea.ParseAreas: 解析失敗: " + ex);
				return new SallyArea[0];
			}
		}

		private static int ParseArea(JToken token)
		{
			var s = token?.Value<string>();
			if (!string.IsNullOrWhiteSpace(s))
			{
				var i = s.IndexOf('-');
				if (i > 0) s = s.Substring(0, i);

				int n;
				if (int.TryParse(s, out n)) return n;
			}

			return token?.Value<int?>() ?? 0;
		}
	}
}
