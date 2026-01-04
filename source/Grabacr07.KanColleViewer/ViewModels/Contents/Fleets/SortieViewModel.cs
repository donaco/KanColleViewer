using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public class SortieViewModel : QuickStateViewViewModel
	{
		// QuickStateView は ContentControl に対し型ごとの DataTemplate を適用する形で実現するので
		// 状況に応じた型がそれぞれ必要。これはその 1 つ。

		/// <summary>
		/// 出撃中のマップ位置情報を取得します。
		/// </summary>
		public SortieInfo SortieInfo => KanColleClient.Current.SortieInfo;

		public SortieViewModel(FleetState state) : base(state) { }
	}
}
