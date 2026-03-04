using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	public class FocusAction : TriggerAction<UIElement>
	{
		protected override void Invoke(object parameter)
		{
			if (this.AssociatedObject.Focusable)
			{
				this.AssociatedObject.Focus();
			}
		}
	}
}
