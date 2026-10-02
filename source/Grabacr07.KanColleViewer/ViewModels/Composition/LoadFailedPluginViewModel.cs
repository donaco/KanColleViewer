using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Composition;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Composition
{
	public class LoadFailedPluginViewModel : ViewModelBase
	{
		#region Message 変更通知プロパティ

		public string Message
		{
			get { return field; }
			set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Exception 変更通知プロパティ

		public string Exception
		{
			get { return field; }
			set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Metadata 変更通知プロパティ

		public object Metadata
		{
			get { return field; }
			set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public LoadFailedPluginViewModel(LoadFailedPluginData data)
		{
			var metadata = data.Metadata;
			if (metadata is null)
			{
				this.Metadata = new BlacklistedAssembly { Name = Path.GetFileName(data.FilePath) ?? string.Empty };
			}
			else
			{
				this.Metadata = metadata;
			}

			using (var reader = new StringReader(data.Message))
			{
				this.Message = reader.ReadLine() ?? string.Empty;
				this.Exception = reader.ReadToEnd();
			}
		}
	}

	public class BlacklistedAssembly
	{
		public string Name { get; set; } = string.Empty;
	}
}
