using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Serialization;

namespace Grabacr07.KanColleViewer.Models.Settings
{
	public class NetworkSettings
	{
		public class Proxy : IProxySettings
		{
			public static SerializableProperty<ProxyType> Type { get; }
				= new SerializableProperty<ProxyType>(GetKey(), Providers.Local, ProxyType.SystemProxy);

			public static SerializableProperty<string> Host { get; }
				= new SerializableProperty<string>(GetKey(), Providers.Local, null);

			public static SerializableProperty<ushort> Port { get; }
				= new SerializableProperty<ushort>(GetKey(), Providers.Local, 80);

			public static SerializableProperty<string> HttpsHost { get; }
				= new SerializableProperty<string>(GetKey(), Providers.Local, null);

			public static SerializableProperty<ushort> HttpsPort { get; }
				= new SerializableProperty<ushort>(GetKey(), Providers.Local, 443);

			public static SerializableProperty<string> FtpHost { get; }
				= new SerializableProperty<string>(GetKey(), Providers.Local, null);

			public static SerializableProperty<ushort> FtpPort { get; }
				= new SerializableProperty<ushort>(GetKey(), Providers.Local, 21);

			public static SerializableProperty<string> SocksHost { get; }
				= new SerializableProperty<string>(GetKey(), Providers.Local, null);

			public static SerializableProperty<ushort> SocksPort { get; }
				= new SerializableProperty<ushort>(GetKey(), Providers.Local, 1080);

			public static SerializableProperty<bool> IsUseHttpProxyForAllProtocols { get; }
				= new SerializableProperty<bool>(GetKey(), Providers.Local, true);

			#region IProxySettings members

			ProxyType IProxySettings.Type => Type.Value;

			string IProxySettings.HttpHost => Host.Value;

			ushort IProxySettings.HttpPort => Port.Value;

			string IProxySettings.HttpsHost => HttpsHost.Value;

			ushort IProxySettings.HttpsPort => HttpsPort.Value;

			string IProxySettings.FtpHost => FtpHost.Value;

			ushort IProxySettings.FtpPort => FtpPort.Value;

			string IProxySettings.SocksHost => SocksHost.Value;

			ushort IProxySettings.SocksPort => SocksPort.Value;

			bool IProxySettings.IsUseHttpProxyForAllProtocols => IsUseHttpProxyForAllProtocols;

			#endregion
			
			private static string GetKey([CallerMemberName] string propertyName = "")
			{
				return nameof(NetworkSettings) + "." + nameof(Proxy) + "." + propertyName;
			}
		}

		public static class LocalProxy
		{
			public static SerializableProperty<bool> IsEnabled { get; }
				= new SerializableProperty<bool>(GetKey(), Providers.Local, false);

			public static SerializableProperty<ushort> Port { get; }
				= new SerializableProperty<ushort>(GetKey(), Providers.Local, 37564);


			private static string GetKey([CallerMemberName] string propertyName = "")
			{
				return nameof(NetworkSettings) + "." + nameof(LocalProxy) + "." + propertyName;
			}
		}

		public static string LocalProxySettingsString
		{
			get
			{
				switch (Proxy.Type.Value)
				{
					case ProxyType.SystemProxy:
						{
							var port = LocalProxy.IsEnabled ? LocalProxy.Port.Value : LocalProxy.Port.Default;
							var proxyConfig = new Win32.WinHttpCurrentUserIEProxyConfig();
							Win32.WinHttp.WinHttpGetIEProxyConfigForCurrentUser(ref proxyConfig);
							var settings = IEStyleProxySettingsBuilder.Parse(proxyConfig.Proxy);
							return settings.ToIEStyleSettings("127.0.0.1", port);
						}

					case ProxyType.SpecificProxy:
						// 指定プロキシの場合、CEF の proxy-server に指定プロキシ設定を渡す
						{
							var settings = IEStyleProxySettingsBuilder.Parse(new Proxy());
							return settings.ToIEStyleSettings();
						}
					case ProxyType.DirectAccess:
						// プロキシを使用しない場合、CEF に対して空文字を返し proxy-server を設定しない
						return string.Empty;
					default:
						throw new IndexOutOfRangeException();
				}
			}
		}

		private class IEStyleProxySettingsBuilder : IProxySettings
		{
			public ProxyType Type { get; set; }

			public string HttpHost { get; set; }

			public ushort HttpPort { get; set; }

			public string HttpsHost { get; set; }

			public ushort HttpsPort { get; set; }

			public string FtpHost { get; set; }

			public ushort FtpPort { get; set; }

			public string SocksHost { get; set; }

			public ushort SocksPort { get; set; }

			public bool IsUseHttpProxyForAllProtocols { get; set; }

			private const int MaxIEProxyStringLength = 2048;
			private const int MaxHostLength = 253;

			private static readonly Regex pattern = new Regex(
				"(?<scheme>http|https|ftp|socks)=(?<host>[^:;\\s]*)(:(?<port>\\d+))?",
				RegexOptions.Singleline | RegexOptions.Compiled,
				TimeSpan.FromSeconds(1));

			private static bool IsValidHost(string host)
			{
				if (string.IsNullOrWhiteSpace(host)) return false;
				if (host.Length > MaxHostLength) return false;
				if (host.Any(char.IsControl)) return false;
				if (host.Contains(" ")) return false;
				return true;
			}

			internal static IEStyleProxySettingsBuilder Parse(string ieStyleSettings)
			{
				var value = new IEStyleProxySettingsBuilder();

				if (string.IsNullOrWhiteSpace(ieStyleSettings))
					return value;

				// 異常に長い入力は破棄
				if (ieStyleSettings.Length > MaxIEProxyStringLength)
					return value;

				ieStyleSettings = ieStyleSettings.Trim();

				if (!ieStyleSettings.Contains("="))
				{
					// すべてのプロトコルに同一設定 (host[:port])
					var setting = ieStyleSettings.Split(new[] { ':' }, 2);
					if (setting.Length > 0 && IsValidHost(setting[0]))
					{
						value = new IEStyleProxySettingsBuilder
						{
							IsUseHttpProxyForAllProtocols = true,
							HttpHost = setting[0],
						};

						if (setting.Length == 2 && ushort.TryParse(setting[1], out var httpPort) && httpPort != 0)
							value.HttpPort = httpPort;
					}
					return value;
				}

				var settings = ieStyleSettings.Split(';');
				foreach (var setting in settings)
				{
					if (string.IsNullOrWhiteSpace(setting)) continue;

					try
					{
						var groups = pattern.Match(setting).Groups;
						if (groups.Count < 1) continue;

						var host = groups["host"].Value;
						if (!IsValidHost(host)) continue;

						switch (groups["scheme"].Value)
						{
							case "http":
								value.HttpHost = host;
								if (ushort.TryParse(groups["port"].Value, out var httpPort) && httpPort != 0)
									value.HttpPort = httpPort;
								break;

							case "https":
								value.HttpsHost = host;
								if (ushort.TryParse(groups["port"].Value, out var httpsPort) && httpsPort != 0)
									value.HttpsPort = httpsPort;
								break;

							case "ftp":
								value.FtpHost = host;
								if (ushort.TryParse(groups["port"].Value, out var ftpPort) && ftpPort != 0)
									value.FtpPort = ftpPort;
								break;

							case "socks":
								value.SocksHost = host;
								if (ushort.TryParse(groups["port"].Value, out var socksPort) && socksPort != 0)
									value.SocksPort = socksPort;
								break;
						}
					}
					catch (RegexMatchTimeoutException)
					{
						// タイムアウト時は安全側で当該要素を無視
						continue;
					}
				}

				return value;
			}

			internal static IEStyleProxySettingsBuilder Parse(IProxySettings settings)
			{
				return new IEStyleProxySettingsBuilder()
				{
					Type = settings.Type,
					HttpHost = settings.HttpHost,
					HttpPort = settings.HttpPort,
					HttpsHost = settings.HttpsHost,
					HttpsPort = settings.HttpsPort,
					FtpHost = settings.FtpHost,
					FtpPort = settings.FtpPort,
					SocksHost = settings.SocksHost,
					SocksPort = settings.SocksPort,
					IsUseHttpProxyForAllProtocols = settings.IsUseHttpProxyForAllProtocols,
				};
			}

			internal string ToIEStyleSettings(string overrideHttpHost = null, ushort overrideHttpPort = 0)
			{
				var httpHost = overrideHttpHost ?? this.HttpHost;
				var httpPort = overrideHttpPort != 0 ? overrideHttpPort : this.HttpPort;
				var values = new List<string>();
				if (!string.IsNullOrWhiteSpace(httpHost))
					values.Add(httpPort == 0 ? $"http={httpHost}" : $"http={httpHost}:{httpPort}");
				if (this.IsUseHttpProxyForAllProtocols)
				{
					if (!string.IsNullOrWhiteSpace(this.HttpHost))
					{
						values.Add(this.HttpPort == 0 ? $"https={this.HttpHost}" : $"https={this.HttpHost}:{this.HttpPort}");
						values.Add(this.HttpPort == 0 ? $"ftp={this.HttpHost}" : $"ftp={this.HttpHost}:{this.HttpPort}");
					}
				}
				else
				{
					if (!string.IsNullOrWhiteSpace(this.HttpsHost))
						values.Add(this.HttpsPort == 0 ? $"https={this.HttpsHost}" : $"https={this.HttpsHost}:{this.HttpsPort}");
					if (!string.IsNullOrWhiteSpace(this.FtpHost))
						values.Add(this.FtpPort == 0 ? $"ftp={this.FtpHost}" : $"ftp={this.FtpHost}:{this.FtpPort}");
					if (!string.IsNullOrWhiteSpace(this.SocksHost))
						values.Add(this.SocksPort == 0 ? $"socks={this.SocksHost}" : $"socks={this.SocksHost}:{this.SocksPort}");
				}
				return string.Join(";", values);
			}
		}
	}
}
