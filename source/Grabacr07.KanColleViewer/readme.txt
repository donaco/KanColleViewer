━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
提督業も忙しい！ (KanColleViewer)
version 5.0.0.b5 2026/11/06
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━


■このソフトウェアについて
「提督業も忙しい！」は、DMM.com が配信しているブラウザゲーム
「艦隊これくしょん ～艦これ～」をより遊びやすくするためのツールです。


■主な機能
・高速修復材や高速建造材 (ゲーム内で確認しにくいやつ) のリアルタイム表示
・所属している艦娘の数、保有している装備の数のリアルタイム表示
・艦隊と、艦隊に属する艦娘の一覧表示
・装備と、それぞれを装備している艦娘の一覧表示
・コンディションが回復し艦隊が出撃可能になったタイミングでのトースト通知
・入渠ドック・建造ドックの使用状況と、整備・建造終了時のトースト通知
・現在遂行中の任務の一覧表示と、残っているデイリー/ウィークリー/マンスリー任務の一覧表示
・遠征の状況と、終了時のトースト通知
・スクリーンショット保存
・ミュート


■動作環境
Windows 11
Windows 10
  
.NET デスクトップ ランタイム 10.0.11
https://dotnet.microsoft.com/ja-jp/download/dotnet/thank-you/runtime-desktop-10.0.11-windows-x64-installer

Microsoft Edge WebView2 Evergreen Runtime
https://developer.microsoft.com/microsoft-edge/webview2/

環境によっては.NET デスクトップ ランタイム や WebView2 Runtime のインストールが必要になる場合があります。
現在、艦これ本体のセキュア化に伴い暫定対応しています。  
未検証の通信内容も多く、意図しない挙動となる可能性があることにご注意ください。


■使用条件
オープンソース / フリーソフトウェアです。無料でご利用頂けます。  
ソースコードは、MIT ライセンスの下で GitHub にて公開しています。


■使用方法
同梱の KanColleViewer.exe を起動してください。
各画面の解説等は https://dona-co.art/?page_id=3534 を参照してください。

■保存データ
・Ver5のデータは %LOCALAPPDATA%\grabacr.net\KanColleViewer\WebView2 に保存します。
・Ver4のデータは %LOCALAPPDATA%\grabacr.net\KanColleViewer\Chromium に保存され、自動では削除しません。
・「設定 > ブラウザ操作」の最下部で旧Verデータの削除を予約できます。起動時に WebView2 と Chromium の両フォルダーが存在する場合のみ選択できます。
・設定と現在のログイン状態を保つため、親フォルダーや WebView2 フォルダーは削除しないでください。

■スクリーンショット撮影
・ゲーム画面の撮影はゲーム領域を特定できた場合だけ行い、PNG/JPEG とも 1200×720 px で保存します。
  対象を特定できない場合に、ページ全体を代わりに保存することはありません。
・表示倍率や描画領域は撮影のために変更せず、取得した画像を切り抜き・拡大縮小して保存します。ブラウザーの表示倍率と画面 DPI は画像処理側で補正します。
・ゲーム領域全体がブラウザーの撮影範囲に収まらない場合は保存せず、ゲーム画面全体を表示するよう案内します。

■表示フレームレート
・FPS 設定は「自動調整」「上限解除」の 2 択です。60 FPS 固定には対応していません。
  「上限解除」の効果は環境によって異なり、負荷が上がる場合があります。

■大破時の警告
・「設定 > 動作設定 > 出撃情報」の「大破時の警告を強調する」は、初期設定では OFF です。
・ON にすると、出撃中の大破警告で赤くなるアクセント部分すべてが点滅します。OFF にすると、大破中は従来どおり赤で常時表示します。
・大破判定の解除・帰投時には点滅を停止して通常色に戻ります。設定を 

■ウィンドウの追随と最前面表示
・追随 ON かつメインウィンドウが最前面表示の間は、分割した情報ウィンドウやサブウィンドウも一時的に最前面表示し、メインの後ろに隠れないようにします。
・各ウィンドウの最前面設定は変更しません。追随またはメインの最前面表示を OFF にすると、それぞれの設定に従う表示へ戻ります。

■GPU無効化
・GPU 無効化は WebView2 の起動時に --disable-gpu を指定します。実際の描画方式や負荷への効果は保証しません。

■ブラウザーのプロセス障害
・GPU や音声などの補助プロセスの障害は通知し、WebView2 の自動復旧を待ちます。ブラウザー操作は無効にせず、ゲームの自動再読み込みも行いません。
・一時的な無応答では応答の回復を待ちます。改善しない場合や、ブラウザー・ページ描画プロセスが終了した場合はアプリを再起動してください。

■プロキシと通信
・Relay を有効にすると、外部ツールを経由して通信します。上流への接続に失敗した場合は通知し、直接接続へ切り替えません。
・Relay を無効にした場合は、直接接続・システムプロキシ・指定プロキシを選べます。
・指定プロキシの設定が不正、または接続できない場合も直接接続へは切り替えません。
・ダウンロードと新しいブラウザーウィンドウの要求は拒否し、理由を表示します。

■キャッシュ削除
・キャッシュ削除の対象は WebView2 のディスクキャッシュだけです。ログイン情報は削除しません。

■開発向けオプション
・開発者向けオプションで DevTools を利用できますが、外部接続用のリモートデバッグポートは開きません。


■開発環境・言語
C# + WPF で開発しています。
GitHub Copilot を使用しています。
開発環境は Windows 11 Pro + Visual Studio 2026 + Adobe Creative Cloud です。


■使用ライブラリ
以下のライブラリを使用しています。

Newtonsoft.Json
(https://www.newtonsoft.com/json)
    ver 13.0.4 (2025/9/16)
    created James Newton-King 
    ・用途 : JSON デシリアライズ
    ・ライセンス : The MIT License (MIT)
    ・ライセンス全文 : licenses/Newtonsoft.Json.txt

.NET Core Audio APIs (archived source)
(https://github.com/dorba/netcoreaudio)
    The MIT License (MIT)
    Copyright (c) 2011 Vannatech
    ・用途 : 音量操作
    ・ライセンス : The MIT License (MIT)
    ・ライセンス全文 : Licenses/NETCoreAudioAPIs.txt

Microsoft Edge WebView2
(https://developer.microsoft.com/microsoft-edge/webview2/)
    ・用途 : 内蔵 Web ブラウザー
    ・ライセンス : The 3-Clause BSD License
    ・ライセンス全文 : Licenses/Microsoft.Web.WebView2.txt

System.Reactive (Reactive Extensions for .NET)
(https://github.com/dotnet/reactive)
    ver 6.1.0
    Copyright (c) .NET Foundation and Contributors
    ・用途 : 非同期処理
    ・ライセンス : The MIT License (MIT)
    ・ライセンス全文 : Licenses/System.Reactive.txt



■免責事項
本ソフトウェアの使用は、すべて自己責任で行ってください。
このソフトウェアを使用した結果生じた損害について、開発者は
一切責任を負いません。


■更新履歴
2026/11/11 - version 5.0.0
2026/10/06 - version 4.8.6
2026/09/02 - version 4.8.5
2026/08/08 - version 4.8.4
2026/07/19 - version 4.8.3
2026/07/09 - version 4.8.2
2026/07/01 - version 4.8.1
2026/06/01 - version 4.8.0
2026/05/19 - version 4.7.3
2026/05/10 - version 4.7.2
2026/04/20 - version 4.7.1
2026/04/01 - version 4.7.0 
2026/03/04 - version 4.6.5
2026/02/16 - version 4.6.4
2026/01/09 - version 4.6.3
2026/01/03 - version 4.6.2
2025/12/24 - version 4.6.1
2025/12/22 - version 4.6
2025/12/19 - version 4.6 beta5
2025/12/17 - version 4.6 beta4
2025/12/14 - version 4.6 beta3
2025/12/10 - version 4.6 beta2
2025/12/07 - version 4.6 beta1
2025/11/02 - version 4.6 alpha3
2025/11/01 - version 4.6 alpha2
2025/10/26 - version 4.6 alpha1

2020/03/28 - version 4.5.2
2018/08/17 - version 4.5
2016/06/20 - version 4.2.6
2016/02/12 - version 4.2.1
2016/02/08 - version 4.2
2015/11/10 - version 4.1.6
2015/10/30 - version 4.1.5
2015/08/28 - version 4.1.3
2015/08/20 - version 4.1.2
2015/08/19 - version 4.1.1
2015/08/12 - version 4.1.0
2015/08/11 - version 4.0.1
2015/08/11 - version 4.0
2015/05/26 - version 3.8.2
2015/05/18 - version 3.8
2015/05/03 - version 3.7
2015/02/07 - version 3.5
2014/09/26 - version 3.4
2014/08/12 - version 3.3
2014/08/10 - version 3.2
2014/08/09 - version 3.1
2014/08/07 - version 3.0
2014/05/16 - version 2.6
2014/04/29 - version 2.6 beta rev.2
2014/04/23 - version 2.6 beta 
2014/03/21 - version 2.4
2014/03/04 - version 2.3
2014/03/02 - version 2.2
2014/02/19 - version 2.1
2014/02/06 - version 2.0.1
2014/02/01 - version 2.0
2014/01/20 - version 1.2.1
2013/12/29 - version 1.2
2013/12/27 - version 1.1
2013/12/22 - version 1.0

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 Product name: 提督業も忙しい！    
 Product URL:  https://dona-co.art/?page_id=3534
 Source code:  https://github.com/donaco/KanColleViewer
 License:      MIT License
 Author:       @Grabacr07 / donaco
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
