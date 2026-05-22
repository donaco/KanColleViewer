using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Grabacr07.KanColleViewer.Composition;
using Livet;

namespace Grabacr07.KanColleViewer.ViewModels.Composition
{
	public class ToolViewModel : ViewModel
	{
		private readonly ITool tool;
		private object view;
		private bool viewCreated;

		public string Name => this.tool.Name;

		public object View
		{
			get
			{
				if (this.viewCreated)
				{
					return this.view;
				}

				try
				{
					this.view = this.tool.View;
				}
				catch (Exception ex)
				{
					Application.ReportRecoverableException($"ToolView:{this.Name}", this.tool, ex);
					this.view = CreateFallbackView(ex);
				}

				this.viewCreated = true;
				return this.view;
			}
		}

		public ToolViewModel(ITool tool)
		{
			this.tool = tool;
		}

		private object CreateFallbackView(Exception ex)
		{
			return new Border
			{
				Padding = new Thickness(12),
				Child = new TextBlock
				{
					Text = $"{this.Name} の表示中にエラーが発生しました。詳細は ErrorReports と cef.log を確認してください。\r\n\r\n{ex.GetType().Name}: {ex.Message}",
					TextWrapping = TextWrapping.Wrap,
				}
			};
		}
	}
}
