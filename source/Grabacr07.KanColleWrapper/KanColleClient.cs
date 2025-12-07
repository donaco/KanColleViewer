using Grabacr07.KanColleWrapper.Models.Raw;
using Nekoxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Windows; // 追加

namespace Grabacr07.KanColleWrapper
{
	public class KanColleClient : Notifier
	{
		#region singleton

		public static KanColleClient Current { get; } = new KanColleClient();

		#endregion

		public IKanColleClientSettings Settings { get; set; }

		/// <summary>
		/// 艦これの通信をフックするプロキシを取得します。
		/// </summary>
		public KanColleProxy Proxy { get; private set; }

		/// <summary>
		/// ユーザーに依存しないマスター情報を取得します。
		/// </summary>
		public Master Master { get; private set; }

		/// <summary>
		/// 母港の情報を取得します。
		/// </summary>
		public Homeport Homeport { get; private set; }

		#region IsStarted 変更通知プロパティ

		private bool _IsStarted;

		/// <summary>
		/// 艦これが開始されているかどうかを示す値を取得します。
		/// </summary>
		public bool IsStarted
		{
			get { return this._IsStarted; }
			set
			{
				if (this._IsStarted != value)
				{
					this._IsStarted = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsInSortie 変更通知プロパティ

		private bool _IsInSortie;

		/// <summary>
		/// 艦隊が出撃中かどうかを示す値を取得します。
		/// </summary>
		public bool IsInSortie
		{
			get { return this._IsInSortie; }
			private set
			{
				if (this._IsInSortie != value)
				{
					this._IsInSortie = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		// Captured 処理を委譲するコンポーネント
		private readonly CapturedProcessor capturedProcessor;

		private KanColleClient()
		{
			this.Initialieze();

			// CapturedProcessor を初期化
			this.capturedProcessor = new CapturedProcessor(
				// getProxy
				() => this.Proxy ?? (this.Proxy = new KanColleProxy()),
				// isStartedProvider
				() => this.IsStarted,
				// onInitialized
				(start2, requireInfo) =>
				{
					try
					{
						// 診断ログ: onInitialized をファイルに残す
						try
						{
							var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
							Directory.CreateDirectory(logDir);
							var path = Path.Combine(logDir, "client_updates.log");
							File.AppendAllText(path, $"{DateTime.Now:O} onInitialized invoked (CapturedProcessor)\nstart2 length: { (start2?.ToString().Length ?? 0) } requireInfo length: { (requireInfo?.ToString().Length ?? 0) }\n\n");
						}
						catch { }

						// UI スレッドで Master/Homeport/SetRequireInfo/IsStarted を設定する
						if (Application.Current != null)
						{
							Application.Current.Dispatcher.Invoke(() =>
							{
								this.Master = new Master(start2);
								this.Homeport = new Homeport(this.Proxy);
								this.SetRequireInfo(requireInfo);
								this.IsStarted = true;
							});
						}
						else
						{
							// UI が存在しない（テスト等）の場合は通常実行
							this.Master = new Master(start2);
							this.Homeport = new Homeport(this.Proxy);
							this.SetRequireInfo(requireInfo);
							this.IsStarted = true;
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine("onInitialized handler failed: " + ex);
					}
				});

			var start = this.Proxy.api_req_map_start;
			var end = this.Proxy.api_port;

			this.Proxy.ApiSessionSource
				.SkipUntil(start.Do(_ => this.IsInSortie = true))
				.TakeUntil(end)
				.Finally(() => this.IsInSortie = false)
				.Repeat()
				.Subscribe();


			// Initialieze() の proxy 取得直後に追加
			// 診断: 取得できない API のパース状況をログ出力する
			void AddDebugSubscriptions(KanColleProxy proxy)
			{
				// raw Session を見る（来ているか）
				proxy.api_get_member_ship.Subscribe(s =>
				{
					try
					{
						var json = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(s.Response?.BodyAsString);
						System.Diagnostics.Debug.WriteLine($"DBG: api_get_member/ship session captured. Mime={s.Response?.MimeType} rawLen={(s.Response?.BodyAsString?.Length ?? 0)} normLen={(json?.Length ?? 0)}");
						if (!string.IsNullOrEmpty(json))
						{
							try
							{
								var root = JObject.Parse(json);
								var apiData = root["api_data"];
								if (apiData != null)
								{
									if (apiData.Type == JTokenType.Array)
									{
										System.Diagnostics.Debug.WriteLine($"DBG: api_get_member/ship api_data is array length={(apiData as JArray)?.Count}");
									}
									else
									{
										System.Diagnostics.Debug.WriteLine($"DBG: api_get_member/ship api_data is object type={apiData.Type}");
									}
								}
							}
							catch (Exception ex)
							{
								System.Diagnostics.Debug.WriteLine("DBG: api_get_member/ship parse failed: " + ex);
							}
						}

						// 診断ファイル出力（この行はスコープ内の変数を使うため安全）
						try
						{
							var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
							Directory.CreateDirectory(logDir);
							var preview = json ?? "(no norm)";
							File.AppendAllText(Path.Combine(logDir, "dbg_endpoints.log"), $"{DateTime.Now:O} DBG: api_get_member/ship rawLen={(s.Response?.BodyAsString?.Length ?? 0)} normLen={(preview.Length)}\n");
						}
						catch { }
					}
					catch { }
				});

				// 共通ヘルパーでその他のエンドポイントも同様に観察
				Action<IObservable<Session>, string> watch = (obs, name) =>
				{
					obs.Subscribe(s =>
					{
						try
						{
							var json = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(s.Response?.BodyAsString);
							System.Diagnostics.Debug.WriteLine($"DBG: {name} session captured. Mime={s.Response?.MimeType} rawLen={(s.Response?.BodyAsString?.Length ?? 0)} normLen={(json?.Length ?? 0)}");
							if (!string.IsNullOrEmpty(json))
							{
								try
								{
									var root = JObject.Parse(json);
									var apiData = root["api_data"];
									if (apiData != null)
									{
										if (apiData.Type == JTokenType.Array)
										{
											System.Diagnostics.Debug.WriteLine($"DBG: {name} api_data is array length={(apiData as JArray)?.Count}");
										}
										else if (apiData.Type == JTokenType.Object)
										{
											// オブジェクト内のプロパティ数をログ
											System.Diagnostics.Debug.WriteLine($"DBG: {name} api_data is object properties={(apiData as JObject)?.Count}");
										}
										else
										{
											System.Diagnostics.Debug.WriteLine($"DBG: {name} api_data is {apiData.Type}");
										}
									}
								}
								catch (Exception ex)
								{
									System.Diagnostics.Debug.WriteLine($"DBG: {name} parse failed: " + ex);
								}
							}

							// 診断ファイル出力（ローカル変数 name, json を使用）
							try
							{
								var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
								Directory.CreateDirectory(logDir);
								var preview = json ?? "(no norm)";
								File.AppendAllText(Path.Combine(logDir, "dbg_endpoints.log"), $"{DateTime.Now:O} DBG: {name} rawLen={(s.Response?.BodyAsString?.Length ?? 0)} normLen={(preview.Length)}\n");
							}
							catch { }
						}
						catch { }
					});
				};

				watch(proxy.api_get_member_ndock, "api_get_member/ndock");
				watch(proxy.api_get_member_material, "api_get_member/material");
				watch(proxy.api_get_member_mission, "api_get_member/mission");
				watch(proxy.api_get_member_questlist, "api_get_member/questlist");
				watch(proxy.api_get_member_basic, "api_get_member/basic");
				watch(proxy.api_get_member_ship3, "api_get_member/ship3");
				watch(proxy.api_get_member_slot_item, "api_get_member/slot_item");
			}
			// 追加: 定義した診断購読を有効化
			try
			{
				AddDebugSubscriptions(this.Proxy);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("AddDebugSubscriptions failed: " + ex);
			}

			// 追加2：グローバルに api_port が捕まっているかを確実にログ
			try
			{
				// グローバル診断: api_port の到着を記録し、現在の Homeport インスタンス hash を出す
				this.Proxy.api_port.Subscribe(s =>
				{
					try
					{
						var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
						Directory.CreateDirectory(logDir);
						var path = Path.Combine(logDir, "client_updates.log");

						var homeHash = this.Homeport != null ? this.Homeport.GetHashCode().ToString() : "(no homeport)";
						var respLen = s.Response?.BodyAsString?.Length ?? 0;
						File.AppendAllText(path, $"{DateTime.Now:O} Global.api_port captured. HomeportHash={homeHash} respLen={respLen}\n");
					}
					catch { }
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Global api_port subscribe failed: " + ex);
			}

			//追加3
			try
			{
				// 生セッション（MimeType フィルタを通さない）から api_port を捕捉してログする（診断用）
				this.Proxy.SessionSource
					.Where(s => s?.Request?.PathAndQuery == "/kcsapi/api_port/port")
					.Subscribe(s =>
					{
						try
						{
							var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
							Directory.CreateDirectory(logDir);
							var path = Path.Combine(logDir, "client_updates.log");

							var homeHash = this.Homeport != null ? this.Homeport.GetHashCode().ToString() : "(no homeport)";
							var respLen = s.Response?.BodyAsString?.Length ?? 0;
							var mime = s.Response?.MimeType ?? "(no mime)";
							File.AppendAllText(path, $"{DateTime.Now:O} Global.SessionSource api_port captured. HomeportHash={homeHash} respLen={respLen} mime={mime}\n");
						}
						catch { }
					});
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Global SessionSource api_port subscribe failed: " + ex);
			}

		}
			//ログ診断用の購読はここまで

		public void Initialieze()
		{
			var proxy = this.Proxy ?? (this.Proxy = new KanColleProxy());

			var start2Source = proxy.api_start2_getData.TryParse<kcsapi_start2>();
			var requireInfoSource = proxy.api_get_member_require_info.TryParse<kcsapi_require_info>();
			var firstTime = start2Source
				.CombineLatest(requireInfoSource, (start2, requireInfo) => new { start2, requireInfo, })
				.FirstAsync();

			// Homeport の初期化と require_info の適用に Master のインスタンスが必要なため、初回のみ足並み揃えて実行
			// 2 回目以降は受信したタイミングでそれぞれ更新すればよい

			firstTime.Subscribe(x =>
			{
				this.Master = new Master(x.start2.Data);
				this.Homeport = new Homeport(proxy);
				this.SetRequireInfo(x.requireInfo.Data);
				this.IsStarted = true;
			});

			start2Source
				.SkipUntil(firstTime)
				.Subscribe(x => this.Master = new Master(x.Data));

			requireInfoSource
				.SkipUntil(firstTime)
				.Subscribe(x => this.SetRequireInfo(x.Data));
		}

		// SetRequireInfo の先頭に診断ログを追加（既存メソッドを置き換え）
		private void SetRequireInfo(kcsapi_require_info data)
		{
			try
			{
				var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
				Directory.CreateDirectory(logDir);
				var path = Path.Combine(logDir, "client_updates.log");

				File.AppendAllText(path, $"{DateTime.Now:O} SetRequireInfo invoked\n");
				if (data == null)
				{
					File.AppendAllText(path, "  data is null\n\n");
					return;
				}
				else
				{
					File.AppendAllText(path, $"  api_basic present: {(data.api_basic != null)}\n");
					File.AppendAllText(path, $"  api_slot_item count: {(data.api_slot_item != null ? data.api_slot_item.Length.ToString() : "null")}\n");
					File.AppendAllText(path, $"  api_kdock count: {(data.api_kdock != null ? data.api_kdock.Length.ToString() : "null")}\n");
				}
			}
			catch { /* swallow */ }

			// Homeport の更新は UI スレッドで行う（バインディング更新を確実にするため）
			if (Application.Current != null)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					if (data.api_basic != null)
					{
						this.Homeport.UpdateAdmiral(data.api_basic);
					}
					this.Homeport.Itemyard.Update(data.api_slot_item);
					this.Homeport.Dockyard.Update(data.api_kdock);
				});
			}
			else
			{
				if (data.api_basic != null)
				{
					this.Homeport.UpdateAdmiral(data.api_basic);
				}
				this.Homeport.Itemyard.Update(data.api_slot_item);
				this.Homeport.Dockyard.Update(data.api_kdock);
			}
		}

		/// <summary>
		/// CefSharp によって捕捉した HTTP を外部から受け取るエントリ（従来の公開 API を維持）
		/// </summary>
		public void ProcessCaptured(string url, string responseBody)
		{
			// 診断用ログ（必ず調査後に削除してください）
			try
			{
				var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
				Directory.CreateDirectory(logDir);
				var path = Path.Combine(logDir, "dbg_processcaptured.log");

				var normalized = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(responseBody);
				var preview = responseBody?.Length > 2000 ? responseBody.Substring(0, 2000) + "..." : responseBody;
				var normLen = normalized?.Length ?? 0;

				var entry = $"{DateTime.Now:O} URL={url}\nrawLen={(responseBody?.Length ?? 0)} normLen={normLen}\nPreview:\n{preview}\n\n";
				File.AppendAllText(path, entry, System.Text.Encoding.UTF8);
			}
			catch { /* swallow */ }

			// 実処理は CapturedProcessor に委譲（初期化判定はこれで行う）
			try
			{
				this.capturedProcessor.Process(url, responseBody);
			}
			catch { /* swallow */ }

			// 追加: 起動済み/未起動に関わらず、CEF で捕まえた /kcsapi/api_port/port (および一部の重要エンドポイント)
			// を直接 Homeport に流す（プロキシを使わない環境向けのフォールバック）。
			try
			{
				if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(responseBody)) return;

				// 正規化済み JSON が既に渡される想定だが念のため正規化
				var normalized = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(responseBody);
				if (string.IsNullOrEmpty(normalized)) normalized = responseBody;

				// /kcsapi/api_port/port をパースして Homeport に反映
				if (url.Contains("/kcsapi/api_port/port"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_port>(normalized, out var port))
					{
						try
						{
							// UI スレッドで安全に反映する
							if (Application.Current != null)
							{
								Application.Current.Dispatcher.BeginInvoke(new Action(() =>
								{
									try
									{
										if (port.api_basic != null) this.Homeport.UpdateAdmiral(port.api_basic);
										if (port.api_ship != null) this.Homeport.Organization.Update(port.api_ship);
										if (port.api_ndock != null) this.Homeport.Repairyard.Update(port.api_ndock);
										if (port.api_deck_port != null) this.Homeport.Organization.Update(port.api_deck_port);
										if (port.api_material != null) this.Homeport.Materials.Update(port.api_material);
									}
									catch (Exception ex)
									{
										// 更新失敗はログ（調査用）
										try
										{
											var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
											var path = Path.Combine(logDir, "client_updates.log");
											File.AppendAllText(path, $"{DateTime.Now:O} ProcessCaptured -> port apply failed: {ex}\n");
										}
										catch { }
									}
								}));
							}
							else
							{
								// 非 UI 環境の場合は直接呼ぶ
								if (port.api_basic != null) this.Homeport.UpdateAdmiral(port.api_basic);
								if (port.api_ship != null) this.Homeport.Organization.Update(port.api_ship);
								if (port.api_ndock != null) this.Homeport.Repairyard.Update(port.api_ndock);
								if (port.api_deck_port != null) this.Homeport.Organization.Update(port.api_deck_port);
								if (port.api_material != null) this.Homeport.Materials.Update(port.api_material);
							}

							// 診断ログ
							try
							{
								var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
								Directory.CreateDirectory(logDir);
								var path = Path.Combine(logDir, "client_updates.log");
								File.AppendAllText(path, $"{DateTime.Now:O} ProcessCaptured: applied api_port to Homeport. portShips={(port.api_ship?.Length ?? 0)} materials={(port.api_material?.Length ?? 0)} ndocks={(port.api_ndock?.Length ?? 0)}\n");
							}
							catch { }
						}
						catch { /* swallow */ }
					}
					else
					{
						// パース失敗はログ
						try
						{
							var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
							Directory.CreateDirectory(logDir);
							var path = Path.Combine(logDir, "client_updates.log");
							File.AppendAllText(path, $"{DateTime.Now:O} ProcessCaptured: api_port parse failed. url={url}\n");
						}
						catch { }
					}

					return;
				}
			}
			catch { /* swallow */ }
		}
	}
}
