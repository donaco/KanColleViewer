using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// 旧 CEF 版のブラウザー フレームレート モードです。現在は使用しません。
	/// 旧 CEF 版のローカル設定ファイルには、この型名付きで値が保存されている場合があります。
	/// 型を削除すると設定ファイルを読み込めず、<see cref="Settings.SettingsHost.Load"/> がローカル設定ファイルごと削除するため、
	/// 読み込み用に型だけを残しています。名前空間・型名・メンバー名を変更しないでください。
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
