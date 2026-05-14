using System;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 艦これサーバーのオリジンを検証するユーティリティクラス。
	/// 正規サーバーのパターン: https://w*.kancolle-server.com
	/// </summary>
	public static class KanColleServerOrigin
	{
		/// <summary>許可するホストのサフィックス</summary>
		private const string AllowedHostSuffix = "kancolle-server.com";

		/// <summary>
		/// 指定した URL が艦これの正規サーバーから来たものかを検証します。
		/// スキームは https のみ許可します。
		/// </summary>
		/// <param name="url">検証対象の URL 文字列</param>
		/// <returns>正規オリジンであれば true</returns>
		public static bool IsValid(string url)
		{
			if (string.IsNullOrEmpty(url)) return false;

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
				return false;

			// HTTPS のみ許可（HTTP は拒否）
			if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
				return false;

			return IsAllowedHost(uri.Host);
		}

		/// <summary>
		/// ホスト名のみを検証します。
		/// </summary>
		/// <param name="host">検証対象のホスト名（例: w14.kancolle-server.com）</param>
		/// <returns>正規ホストであれば true</returns>
		public static bool IsAllowedHost(string host)
		{
			if (string.IsNullOrEmpty(host)) return false;

			// 完全一致 または サブドメイン一致のみ許可
			// 例: kancolle-server.com         → OK
			//     w14.kancolle-server.com     → OK
			//     evil.com                    → NG
			//     evil.kancolle-server.com.evil.com → NG（末尾一致のため安全）
			return string.Equals(host, AllowedHostSuffix, StringComparison.OrdinalIgnoreCase)
				|| host.EndsWith("." + AllowedHostSuffix, StringComparison.OrdinalIgnoreCase);
		}
	}
}
