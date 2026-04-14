using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// GitHub リポジトリの最新リリースと現在のバージョンを比較します。
	/// </summary>
	internal static class UpdateChecker
	{
		private const string GitHubApiUrl = "https://api.github.com/repos/donaco/KanColleViewer/releases/latest";

		public static async Task<UpdateCheckResult> CheckAsync()
		{
			using (var handler = Helper.GetProxyConfiguredHandler())
			using (var client = new HttpClient(handler))
			{
				client.DefaultRequestHeaders.UserAgent.ParseAdd("KanColleViewer/" + ProductInfo.Version);
				client.Timeout = TimeSpan.FromSeconds(15);

				var json = await client.GetStringAsync(GitHubApiUrl).ConfigureAwait(false);

				var tagName = ExtractJsonStringValue(json, "tag_name") ?? "";
				var htmlUrl = ExtractJsonStringValue(json, "html_url") ?? "";

				var versionText = tagName.TrimStart('v', 'V');
				var latestVersion = Version.TryParse(versionText, out var parsed)
					? parsed
					: new Version(0, 0, 0);

				// AssemblyVersion は 4 桁 (Major.Minor.Build.Revision) なので 3 桁に揃えて比較
				var currentVersion = new Version(
					ProductInfo.Version.Major,
					ProductInfo.Version.Minor,
					ProductInfo.Version.Build);

				return new UpdateCheckResult
				{
					IsUpdateAvailable = latestVersion > currentVersion,
					LatestVersion = tagName,
					ReleaseUrl = htmlUrl,
				};
			}
		}

		private static string ExtractJsonStringValue(string json, string propertyName)
		{
			var match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"([^\"]*?)\"");
			return match.Success ? match.Groups[1].Value : null;
		}
	}

	internal class UpdateCheckResult
	{
		public bool IsUpdateAvailable { get; set; }
		public string LatestVersion { get; set; }
		public string ReleaseUrl { get; set; }
	}
}
