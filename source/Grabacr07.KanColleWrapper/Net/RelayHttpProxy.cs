using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Net
{
	/// <summary>
	/// CEF (Chromium) からの HTTPS 通信を、外部ツール（例: 公開日誌拡張版）へ
	/// 透過的に中継するローカルプロキシです。
	///
	/// CEF は自身で TLS を終端して復号するため（CustomRequestHandler 参照）、
	/// このプロキシ自体は TLS を一切復号せず、CONNECT トンネルのバイト列をそのまま転送するだけです。
	/// 外部ツールが本来の「上流プロキシ」として振る舞い、自分自身の MITM 証明書で
	/// なりすましを行う前提を壊さないようにするため、KancolleSniffer と同じ「盲目的な中継」方式を採用します。
	/// </summary>
	public sealed class RelayHttpProxy : IDisposable
	{
		private TcpListener listener;
		private bool running;

		/// <summary>
		/// このプロキシがローカルで待ち受けるポート番号です。
		/// </summary>
		public int ListeningPort { get; private set; }

		/// <summary>
		/// 転送先の外部ツール（上流プロキシ）のホスト名です。未設定 (null/空文字) の場合は
		/// 実際の艦これサーバーへ直接転送します。
		/// </summary>
		public string UpstreamHost { get; set; }

		/// <summary>
		/// 転送先の外部ツール（上流プロキシ）のポート番号です。
		/// </summary>
		public int UpstreamPort { get; set; }

		public RelayHttpProxy(X509Certificate2 unusedServerCertificate = null)
		{
			// 証明書は使用しません（このプロキシは TLS を復号しないため）。
			// 既存呼び出し元との互換性のため引数だけ残しています。
		}

		public void Start(int port)
		{
			if (this.running) return;

			this.listener = new TcpListener(IPAddress.Loopback, port);
			this.listener.Start();
			this.ListeningPort = ((IPEndPoint)this.listener.LocalEndpoint).Port;
			this.running = true;

			Task.Run(AcceptLoopAsync);
		}

		public void Stop()
		{
			this.running = false;
			try { this.listener?.Stop(); } catch { /* ignore */ }
		}

		public void Dispose() => Stop();

		private async Task AcceptLoopAsync()
		{
			try
			{
				while (this.running)
				{
					TcpClient client;
					try
					{
						client = await this.listener.AcceptTcpClientAsync().ConfigureAwait(false);
					}
					catch (Exception) when (!this.running)
					{
						break;
					}

					_ = Task.Run(() => HandleClientAsync(client));
				}
			}
			catch
			{
				// リスナー終了時の例外は無視する
			}
		}

		private async Task HandleClientAsync(TcpClient client)
		{
			try
			{
				using (client)
				using (var clientStream = client.GetStream())
				{
					var requestLine = await ReadLineAsync(clientStream).ConfigureAwait(false);
					if (string.IsNullOrEmpty(requestLine)) return;

					// CONNECT 以外のヘッダーも読み飛ばす（CONNECT 以外は艦これ通信では通常発生しない）
					await ReadHeadersAsync(clientStream).ConfigureAwait(false);

					var match = Regex.Match(requestLine, @"^CONNECT\s+([^\s:]+):(\d+)\s+HTTP", RegexOptions.IgnoreCase);
					if (!match.Success)
					{
						return;
					}

					var targetHost = match.Groups[1].Value;
					var targetPort = int.Parse(match.Groups[2].Value);

					var hasExternalRelay = !string.IsNullOrEmpty(this.UpstreamHost)
						&& KanColleServerOrigin.IsAllowedHost(targetHost);

					if (hasExternalRelay)
					{
						await RelayViaUpstreamAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
					}
					else
					{
						await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
					}
				}
			}
			catch
			{
				// 個別接続の例外はゲーム全体を止めないよう握りつぶす
			}
		}

		/// <summary>
		/// 艦これサーバー以外、または中継先が未設定の場合、実サーバーへ直接 TCP トンネルを張ります。
		/// TLS は一切解かず、バイト列をそのまま双方向に転送します。
		/// </summary>
		private async Task RelayDirectAsync(NetworkStream clientStream, string targetHost, int targetPort)
		{
			using (var tcp = new TcpClient())
			{
				try
				{
					await tcp.ConnectAsync(targetHost, targetPort).ConfigureAwait(false);
				}
				catch
				{
					return;
				}

				var established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
				await clientStream.WriteAsync(established, 0, established.Length).ConfigureAwait(false);

				using (var serverStream = tcp.GetStream())
				{
					await PipeBothWaysAsync(clientStream, serverStream).ConfigureAwait(false);
				}
			}
		}

		/// <summary>
		/// 艦これサーバー宛の通信を、外部ツール（上流プロキシ）へ CONNECT トンネルとして中継します。
		/// 外部ツール自身が MITM 復号（自己署名証明書によるなりすまし）を行う前提のため、
		/// このプロキシは TLS に一切関与せず、バイト列をそのまま転送するだけです。
		/// </summary>
		private async Task RelayViaUpstreamAsync(NetworkStream clientStream, string targetHost, int targetPort)
		{
			using (var upstreamTcp = new TcpClient())
			{
				try
				{
					await upstreamTcp.ConnectAsync(this.UpstreamHost, this.UpstreamPort).ConfigureAwait(false);
				}
				catch
				{
					// 上流（外部ツール）に接続できない場合は実サーバーへ直接フォールバックする
					await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
					return;
				}

				using (var upstreamStream = upstreamTcp.GetStream())
				{
					// 上流プロキシへ改めて CONNECT を発行する
					var connectRequest = Encoding.ASCII.GetBytes($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\n\r\n");
					await upstreamStream.WriteAsync(connectRequest, 0, connectRequest.Length).ConfigureAwait(false);

					var upstreamStatusLine = await ReadLineAsync(upstreamStream).ConfigureAwait(false);
					await ReadHeadersAsync(upstreamStream).ConfigureAwait(false);

					if (upstreamStatusLine == null || !upstreamStatusLine.Contains(" 200"))
					{
						// 上流プロキシが CONNECT を拒否した場合は実サーバーへ直接フォールバックする
						await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
						return;
					}

					var established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
					await clientStream.WriteAsync(established, 0, established.Length).ConfigureAwait(false);

					await PipeBothWaysAsync(clientStream, upstreamStream).ConfigureAwait(false);
				}
			}
		}

		private static async Task PipeBothWaysAsync(Stream a, Stream b)
		{
			var aToB = CopyStreamAsync(a, b);
			var bToA = CopyStreamAsync(b, a);
			await Task.WhenAny(aToB, bToA).ConfigureAwait(false);
		}

		private static async Task CopyStreamAsync(Stream from, Stream to)
		{
			try
			{
				var buffer = new byte[8192];
				int read;
				while ((read = await from.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
				{
					await to.WriteAsync(buffer, 0, read).ConfigureAwait(false);
				}
			}
			catch
			{
				// 接続断は正常終了として扱う
			}
		}

		private static async Task<string> ReadLineAsync(Stream stream)
		{
			var sb = new StringBuilder();
			var prevWasCr = false;
			var buf = new byte[1];

			while (true)
			{
				var read = await stream.ReadAsync(buf, 0, 1).ConfigureAwait(false);
				if (read == 0) return sb.Length > 0 ? sb.ToString() : null;

				var c = (char)buf[0];
				if (c == '\n')
				{
					if (prevWasCr && sb.Length > 0) sb.Length--;
					return sb.ToString();
				}

				prevWasCr = c == '\r';
				sb.Append(c);
			}
		}

		private static async Task ReadHeadersAsync(Stream stream)
		{
			while (true)
			{
				var line = await ReadLineAsync(stream).ConfigureAwait(false);
				if (line == null || line.Length == 0) break;
			}
		}
	}
}
