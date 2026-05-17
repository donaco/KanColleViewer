using System;

namespace Grabacr07.KanColleWrapper
{
	public partial class KanColleProxy
	{
		#region UpstreamProxySettingsプロパティ

		private IProxySettings _UpstreamProxySettings;

		public IProxySettings UpstreamProxySettings
		{
			get { return this._UpstreamProxySettings; }
			set { this._UpstreamProxySettings = value; }
		}

		#endregion
	}
}
