using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// 多言語化されたリソースへのアクセスを提供します。
	/// </summary>
	public class ResourceService : ObservableObject
	{
		#region static members

		public static ResourceService Current { get; } = new ResourceService();

		#endregion

		/// <summary>
		/// サポートされているカルチャの名前。
		/// </summary>
		private readonly string[] supportedCultureNames =
		{
			"ja", // Resources.resx
			"en",
			"zh-CN",
			"ko-KR",
		};

		/// <summary>
		/// 多言語化されたリソースを取得します。
		/// </summary>
		public Resources Resources { get; }

		public string this[string key] => global::Grabacr07.KanColleViewer.Properties.Resources.ResourceManager.GetString(key, Resources.Culture) ?? key;

		/// <summary>
		/// サポートされているカルチャを取得します。
		/// </summary>
		public IReadOnlyCollection<CultureInfo> SupportedCultures { get; }

		private ResourceService()
		{
			this.Resources = new Resources();
			this.SupportedCultures = this.supportedCultureNames
				.Select(x =>
				{
					try
					{
						return CultureInfo.GetCultureInfo(x);
					}
					catch (CultureNotFoundException)
					{
						return null;
					}
				})
				.OfType<CultureInfo>()
				.ToList();
		}

		/// <summary>
		/// 指定されたカルチャ名を使用して、リソースのカルチャを変更します。
		/// </summary>
		/// <param name="name">カルチャの名前。</param>
		public void ChangeCulture(string name)
		{
			Resources.Culture = this.SupportedCultures.SingleOrDefault(x => x.Name == name) ?? CultureInfo.InvariantCulture;
			
			GeneralSettings.Culture.Value = Resources.Culture?.Name ?? string.Empty;
			this.OnPropertyChanged(nameof(this.Resources));
			this.OnPropertyChanged("Item[]");

			Controls.Globalization.ResourceService.Current.ChangeCulture(name);
			foreach (var plugin in PluginService.Current.Get<ILocalizable>()) plugin.ChangeCulture(name);
		}
	}
}
