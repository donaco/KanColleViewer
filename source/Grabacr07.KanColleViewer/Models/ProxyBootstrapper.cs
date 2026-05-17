using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;

namespace Grabacr07.KanColleViewer.Models
{
	public enum ProxyBootstrapResult
	{
		None,

		Success,

		UnexpectedException,
	}

	public class ProxyBootstrapper
	{
		public ProxyBootstrapResult Result { get; private set; }

		public Exception Exception { get; private set; }

		public ProxyBootstrapper()
		{
			this.Result = ProxyBootstrapResult.None;

			KanColleClient.Current.Proxy.UpstreamProxySettings = new Settings.NetworkSettings.Proxy();
		}

		public void Try()
		{
			try
			{
				// CEF 経路に統一済み。ローカルプロキシは起動しない。
				this.Result = ProxyBootstrapResult.Success;
			}
			catch (Exception ex)
			{
				this.Result = ProxyBootstrapResult.UnexpectedException;
				this.Exception = ex;
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}

		public static void Shutdown()
		{
			// CEF 経路に統一済み。停止処理は不要。
		}
	}
}

