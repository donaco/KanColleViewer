using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Composition
{
	[Serializable]
	public class PluginMetadata
	{
		public string Title { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;

		public string Version { get; set; } = string.Empty;

		public string Author { get; set; } = string.Empty;
	}
}
