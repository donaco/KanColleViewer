using System.Diagnostics;
using CefSharp;

namespace Grabacr07.KanColleViewer.Models.Cef
{
    /// <summary>
    /// ファイルダウンロードを全てキャンセルする <see cref="IDownloadHandler"/> の実装です。
    /// 艦これのゲームプレイに正規のダウンロードは不要なため、
    /// 悪意ある Content-Disposition によるファイル保存を防止します。
    /// </summary>
    public class BlockDownloadHandler : IDownloadHandler
    {
        public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
        {
            // 全ダウンロードを拒否
            Debug.WriteLine($"[DownloadHandler] ダウンロードをブロックしました: {url}");
            return false;
        }

        public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            // CanDownload で false を返した場合はここに来ないが、念のため何もしない
            return true;
        }

        public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
        {
            // ダウンロードが進行してしまった場合はキャンセルする
            callback?.Cancel();
        }
    }
}
