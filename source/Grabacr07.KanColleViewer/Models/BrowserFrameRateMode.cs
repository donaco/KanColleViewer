using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// ブラウザーのフレームレート モード（GPU 負荷との兼ね合い）を示す識別子を定義します。
	/// </summary>
	public enum BrowserFrameRateMode
	{
		/// <summary>
		/// 低負荷：30FPS
		/// </summary>
		Low = 0,

		/// <summary>
		/// 中負荷：60FPS
		/// </summary>
		Medium = 1,

		/// <summary>
		/// 高負荷：モニタの FPS に自動調整
		/// </summary>
		High = 2,
	}
}
