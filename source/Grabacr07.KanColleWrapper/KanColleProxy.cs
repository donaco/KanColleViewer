using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;　//診断用
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Nekoxy;
using StatefulModel;

namespace Grabacr07.KanColleWrapper
{
	public partial class KanColleProxy
	{
		private readonly IConnectableObservable<Session> connectableSessionSource;
		private readonly IConnectableObservable<Session> apiSource;
		private readonly MultipleDisposable compositeDisposable;

		public IObservable<Session> SessionSource => this.connectableSessionSource.AsObservable();

		public IObservable<Session> ApiSessionSource => this.apiSource.AsObservable();

		#region UpstreamProxySettingsプロパティ

		private IProxySettings _UpstreamProxySettings;

		public IProxySettings UpstreamProxySettings
		{
			get { return this._UpstreamProxySettings; }
			set
			{
				this._UpstreamProxySettings = value;
				this.ApplyUpstreamProxySettings();
			}
		}

		#endregion

		public int ListeningPort { get; private set; } = 37564;

		public KanColleProxy()
		{
			this.compositeDisposable = new MultipleDisposable();

			this.connectableSessionSource = Observable
				.FromEvent<Action<Session>, Session>(
					action => action,
					h => HttpProxy.AfterSessionComplete += h,
					h => HttpProxy.AfterSessionComplete -= h)
				.Publish();

			// --- 診断: 特定 API のセッションをログに残す購読を追加 ---
			this.connectableSessionSource
				.Where(s => s?.Request?.PathAndQuery != null)
				.Subscribe(session =>
				{
					try
					{
						var path = session.Request.PathAndQuery;
						// 監視対象のパス（必要なら追加）
						var watchList = new[]
						{
							"/api_get_member/ship",
							"/api_get_member/mission",
							"/api_get_member/material",
							"/api_get_member/basic",
							"/api_get_member/ndock",
							"/api_get_member/questlist"
						};

						if (watchList.Any(w => path.Contains(w)))
						{
							var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
							Directory.CreateDirectory(logDir);
							var logPath = Path.Combine(logDir, "proxy_sessions.log");

							// 正規化済み JSON プレビューを取得（安全に）
							var raw = session.Response?.BodyAsString;

							// StatusCode 等が存在するか不明なため、リフレクションで取得して文字列化（コンパイル時の型差に対応）
							string statusText = null;
							try
							{
								if (session.Response != null)
								{
									var resp = session.Response;
									var t = resp.GetType();
									var prop = t.GetProperty("StatusCode") ?? t.GetProperty("Status") ?? t.GetProperty("StatusLine");
									if (prop != null)
									{
										var val = prop.GetValue(resp);
										if (val != null) statusText = val.ToString();
									}
								}
							}
							catch { /* ignore reflection errors */ }

							var normalized = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(raw);
							var preview = normalized ?? (raw?.Length > 2000 ? raw.Substring(0, 2000) + "..." : raw);

							var entry = $"{DateTime.Now:O} Path={path} Mime={session.Response?.MimeType} Status={statusText}\n{preview}\n\n";
							File.AppendAllText(logPath, entry);
						}
					}
					catch
					{
						// ログ失敗は診断中は無視
					}
				});
			// --- 診断ログ購読 ここまで ---

			this.apiSource = this.connectableSessionSource
				.Where(s => s.Request.PathAndQuery.StartsWith("/kcsapi"))
				.Where(s => s.Response.MimeType.Equals("text/plain"))
				#region .Do(debug)
#if DEBUG
.Do(session =>
				{
					Debug.WriteLine("==================================================");
					Debug.WriteLine("Nekoxy session: ");
					Debug.WriteLine(session);
					Debug.WriteLine("");
				})
#endif
			#endregion
				.Publish();
		}


		public void Startup(int proxy = 37564)
		{
			this.ListeningPort = proxy;
			
			HttpProxy.Startup(proxy, false, false);
			this.ApplyUpstreamProxySettings();

			this.compositeDisposable.Add(this.connectableSessionSource.Connect());
			this.compositeDisposable.Add(this.apiSource.Connect());
		}

		public void Shutdown()
		{
			this.compositeDisposable.Dispose();
			HttpProxy.Shutdown();
		}

		/// <summary>
		/// 上流プロキシを設定
		/// </summary>
		private void ApplyUpstreamProxySettings()
		{
			switch (this.UpstreamProxySettings?.Type)
			{
				case ProxyType.DirectAccess:
					HttpProxy.UpstreamProxyConfig = new ProxyConfig(ProxyConfigType.DirectAccess);
					break;
				case ProxyType.SystemProxy:
					HttpProxy.UpstreamProxyConfig = new ProxyConfig(ProxyConfigType.SystemProxy);
					break;
				case ProxyType.SpecificProxy:
					HttpProxy.UpstreamProxyConfig = new ProxyConfig(ProxyConfigType.SpecificProxy, this.UpstreamProxySettings.HttpHost, this.UpstreamProxySettings.HttpPort);
					break;
				default:
					//UpstreamProxySettings == null は SystemProxy使用とみなす
					HttpProxy.UpstreamProxyConfig = new ProxyConfig(ProxyConfigType.SystemProxy);
					break;
			}
		}
	}
}
