using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Net
{
	/// <summary>
	/// CEF または WebView2 からの HTTPS 通信を、外部ツール（例: 公開日誌拡張版）へ
	/// 透過的に中継するローカルプロキシです。
	///
	/// ブラウザー側で TLS を終端するため、このプロキシ自体は TLS を一切復号せず、
	/// CONNECT トンネルのバイト列をそのまま転送するだけです。
	/// 外部ツールが本来の「上流プロキシ」として振る舞い、自分自身の MITM 証明書で
	/// なりすましを行う前提を壊さないようにするため、KancolleSniffer と同じ「盲目的な中継」方式を採用します。
	/// </summary>
	public sealed class RelayHttpProxy : IDisposable
	{
		private static readonly Regex ConnectRequestRegex = new(@"^CONNECT\s+([^\s:]+):(\d+)\s+HTTP", RegexOptions.IgnoreCase);
		private static readonly byte[] ConnectionEstablishedResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
		private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
		private TcpListener? listener;
		private bool running;

		/// <summary>
		/// このプロキシがローカルで待ち受けるポート番号です。
		/// </summary>
		public int ListeningPort { get; private set; }

		/// <summary>
		/// 転送先の外部ツール（上流プロキシ）のホスト名です。未設定の場合の動作は AllowDirectFallback に従います。
		/// </summary>
		public string? UpstreamHost { get; set; }

		/// <summary>
		/// 転送先の外部ツール（上流プロキシ）のポート番号です。
		/// </summary>
		public int UpstreamPort { get; set; }

		/// <summary>
		/// 上流プロキシに接続できない場合、ゲームサーバーへの直接接続を許可するかどうかです。
		/// </summary>
		public bool AllowDirectFallback { get; set; } = true;

		public event EventHandler? UpstreamConnectionFailed;

		public RelayHttpProxy()
		{
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
						client = await this.listener!.AcceptTcpClientAsync().ConfigureAwait(false);
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
			string? targetHost = null;
			try
			{
				using (client)
				using (var clientStream = client.GetStream())
				{
					var requestLine = await ReadLineAsync(clientStream).ConfigureAwait(false);
					if (string.IsNullOrEmpty(requestLine)) return;

					// CONNECT 以外のヘッダーも読み飛ばす（CONNECT 以外は艦これ通信では通常発生しない）
					await ReadHeadersAsync(clientStream).ConfigureAwait(false);

					var match = ConnectRequestRegex.Match(requestLine);
					if (!match.Success)
					{
						return;
					}

					targetHost = match.Groups[1].Value;
					var targetPort = int.Parse(match.Groups[2].Value);

					var isGameServer = KanColleServerOrigin.IsAllowedHost(targetHost);
					if (isGameServer && string.IsNullOrWhiteSpace(this.UpstreamHost))
					{
						if (this.AllowDirectFallback)
							await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
						else
							this.ReportUpstreamConnectionFailure("上流プロキシが未設定です");
						return;
					}

					if (isGameServer)
						await RelayViaUpstreamAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
					else
						await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (!this.AllowDirectFallback && targetHost is not null && KanColleServerOrigin.IsAllowedHost(targetHost))
					this.ReportUpstreamConnectionFailure($"上流プロキシ通信に失敗しました ({ex.GetType().Name})");
			}
		}

		/// <summary>
		/// 指定されたホストへ直接 TCP トンネルを張ります。
		/// TLS は一切解かず、バイト列をそのまま双方向に転送します。
		/// </summary>
		private async Task RelayDirectAsync(NetworkStream clientStream, string targetHost, int targetPort)
		{
			using (var tcp = new TcpClient())
			{
				try
				{
					using var timeout = new CancellationTokenSource(ConnectTimeout);
					await tcp.ConnectAsync(targetHost, targetPort, timeout.Token).ConfigureAwait(false);
				}
				catch
				{
					return;
				}

				await clientStream.WriteAsync(ConnectionEstablishedResponse, 0, ConnectionEstablishedResponse.Length).ConfigureAwait(false);

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
					using var timeout = new CancellationTokenSource(ConnectTimeout);
					await upstreamTcp.ConnectAsync(this.UpstreamHost!, this.UpstreamPort, timeout.Token).ConfigureAwait(false);
				}
				catch
				{
					if (this.AllowDirectFallback)
						await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
					else
						this.ReportUpstreamConnectionFailure("上流プロキシへ接続できません");
					return;
				}

				using (var upstreamStream = upstreamTcp.GetStream())
				{
					string? upstreamStatusLine;
					using (var handshakeTimeout = new CancellationTokenSource(ConnectTimeout))
					{
						// 上流プロキシへ改めて CONNECT を発行する
						var connectRequest = Encoding.ASCII.GetBytes($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\n\r\n");
						await upstreamStream.WriteAsync(connectRequest, 0, connectRequest.Length, handshakeTimeout.Token).ConfigureAwait(false);
						upstreamStatusLine = await ReadLineAsync(upstreamStream, handshakeTimeout.Token).ConfigureAwait(false);
						await ReadHeadersAsync(upstreamStream, handshakeTimeout.Token).ConfigureAwait(false);
					}

					if (upstreamStatusLine == null || !upstreamStatusLine.Contains(" 200"))
					{
						if (this.AllowDirectFallback)
							await RelayDirectAsync(clientStream, targetHost, targetPort).ConfigureAwait(false);
						else
							this.ReportUpstreamConnectionFailure("上流プロキシが CONNECT を拒否しました");
						return;
					}

					await clientStream.WriteAsync(ConnectionEstablishedResponse, 0, ConnectionEstablishedResponse.Length).ConfigureAwait(false);

					await PipeBothWaysAsync(clientStream, upstreamStream).ConfigureAwait(false);
				}
			}
		}

		private void ReportUpstreamConnectionFailure(string reason)
		{
			if (this.AllowDirectFallback) return;
			System.Diagnostics.Debug.WriteLine($"RelayHttpProxy: {reason}; direct fallback is disabled.");
			try
			{
				this.UpstreamConnectionFailed?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"RelayHttpProxy: failure notification failed ({ex.GetType().Name}).");
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
			var buffer = ArrayPool<byte>.Shared.Rent(8192);
			try
			{
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
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken = default)
		{
			var sb = new StringBuilder();
			var prevWasCr = false;
			var buf = new byte[1];

			while (true)
			{
				var read = await stream.ReadAsync(buf, 0, 1, cancellationToken).ConfigureAwait(false);
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

		private static async Task ReadHeadersAsync(Stream stream, CancellationToken cancellationToken = default)
		{
			while (true)
			{
				var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
				if (line == null || line.Length == 0) break;
			}
		}
	}
}
