using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Grabacr07.KanColleWrapper.Net
{
	/// <summary>
	/// 外部ツール中継プロキシ (<see cref="RelayHttpProxy"/>) が艦これサーバーになりすます際に使用する
	/// 自己署名証明書を生成・管理します。
	/// KancolleSniffer の CertificateManager と同じ方式（SAN = *.kancolle-server.com）を採用しますが、
	/// Windows のルート証明書ストアは汚さず、CefSharp 側の証明書エラーハンドラでのみ信頼させます。
	/// </summary>
	public static class RelayCertificateManager
	{
		private const string CertificateFileName = "relay-mitm.pfx";
		private const string CommonName = "cn=KanColleViewer Relay";
		private const string SubjectAlternativeName = "*.kancolle-server.com";

		private static X509Certificate2 cachedCertificate;
		private static readonly object syncRoot = new object();

		/// <summary>
		/// 証明書ファイルの保存先パスを取得します。
		/// </summary>
		private static string GetCertificatePath(string cacheDirectory)
		{
			return Path.Combine(cacheDirectory, CertificateFileName);
		}

		/// <summary>
		/// 証明書を取得します。存在しないか有効期限が近い場合は新規生成します。
		/// </summary>
		public static X509Certificate2 GetOrCreate(string cacheDirectory)
		{
			lock (syncRoot)
			{
				if (cachedCertificate != null) return cachedCertificate;

				var path = GetCertificatePath(cacheDirectory);

				if (File.Exists(path))
				{
					try
					{
						var existing = new X509Certificate2(path, (string)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
						if (existing.NotAfter > DateTime.Now.AddDays(30))
						{
							cachedCertificate = existing;
							return cachedCertificate;
						}
					}
					catch
					{
						// 破損している場合は再生成する
					}
				}

				cachedCertificate = CreateAndSave(path);
				return cachedCertificate;
			}
		}

		private static X509Certificate2 CreateAndSave(string path)
		{
			var request = new CertificateRequest(CommonName, RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, false));
			request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

			var san = new SubjectAlternativeNameBuilder();
			san.AddDnsName(SubjectAlternativeName);
			request.CertificateExtensions.Add(san.Build());

			var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));

			Directory.CreateDirectory(Path.GetDirectoryName(path));
			var exported = certificate.Export(X509ContentType.Pfx);
			File.WriteAllBytes(path, exported);

			// Export/Import しなおすことで秘密鍵をエクスポート可能な状態にする
			return new X509Certificate2(exported, (string)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
		}
	}
}
