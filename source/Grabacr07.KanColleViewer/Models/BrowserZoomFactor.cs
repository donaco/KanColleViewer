using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Grabacr07.KanColleViewer.Models
{
	public class BrowserZoomFactor : ObservableObject, IZoomFactor
	{
		private const double neutral = 1.0;

		private static readonly double?[] zoomTable =
		{
			0.25, 0.50, 2.0 / 3.0, 0.75,
			1.00, 1.25, 1.50, 1.75,
			2.00, 2.50,
			3.00,
			4.00,
		};

		private double[]? supportedValuesCache;

		public double[] SupportedValues
		{
		get { return this.supportedValuesCache ?? (this.supportedValuesCache = zoomTable.Where(x => x.HasValue).Select(x => x.GetValueOrDefault()).ToArray()); }
		}

		#region Current 変更通知プロパティ

		public double Current
		{
			get { return field; }
			set
			{
				if (field.Equals(value)) return;

				field = value;
				this.CurrentParcentage = (int)(value * 100);
				this.CanZoomDown = (zoomTable.FirstOrDefault() ?? neutral) < value;
				this.CanZoomUp = value < (zoomTable.LastOrDefault() ?? neutral);
				this.OnPropertyChanged(string.Empty);
			}
		}

		#endregion

		#region CurrentParcentage 変更通知プロパティ

		public int CurrentParcentage
		{
			get { return field; }
			private set
			{
				if (field != value)
				{
					field = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion

		#region CanZoomUp 変更通知プロパティ

		public bool CanZoomUp
		{
			get { return field; }
			private set
			{
				if (field != value)
				{
					field = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion

		#region CanZoomDown 変更通知プロパティ

		public bool CanZoomDown
		{
			get { return field; }
			private set
			{
				if (field != value)
				{
					field = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion


		public void ZoomUp()
		{
			this.Current = zoomTable.FirstOrDefault(x => this.Current < x) ?? zoomTable.LastOrDefault() ?? neutral;
		}

		public void ZoomDown()
		{
			this.Current = zoomTable.LastOrDefault(x => x < this.Current) ?? zoomTable.FirstOrDefault() ?? neutral;
		}
	}
}
