using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleViewer.Models
{
	public class SallyArea
	{
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
				return new SallyArea[0];
			}

			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
			{
				System.Diagnostics.Debug.WriteLine("SallyArea.GetAsync: invalid URI: " + source);
				return new SallyArea[0];
			}

			using (var client = new HttpClient(Helper.GetProxyConfiguredHandler()))
			{
				try
				{
					var response = await client.GetAsync(uri).ConfigureAwait(false);
					if (response.IsSuccessStatusCode)
					{
						var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

						// Newtonsoft.Json によるパースへ変更
						JArray array;
						try
						{
							array = JArray.Parse(content);
						}
						catch (Exception)
						{
							return new SallyArea[0];
						}

						var result = array
							.OfType<JToken>()
							.Select(x =>
								new SallyArea
								{
									Area = (int?)(x["area"]) ?? 0,
									Name = (string)(x["name"]),
									Color = Helper.StringToColor((string)(x["color"]))
								})
							.ToArray();

						return result;
					}
				}
				catch (HttpRequestException hrex)
				{
					// DNS 解決や接続エラーなどのネットワーク系例外
					System.Diagnostics.Debug.WriteLine("SallyArea.GetAsync: HttpRequestException: " + hrex);
					StatusService.Current.Notify("出撃海域の取得に失敗しました（ネットワークエラー）。");
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine(ex);
					StatusService.Current.Notify("出撃海域の取得に失敗しました: " + ex.Message);
				}
			}

			return new SallyArea[0];
		}
	}
}
