━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
提督業も忙しい！ (KanColleViewer)
version 4.8.5 2026/09/02
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
  
Microsoft Visual C++ 2015-2022 再頒布可能パッケージ
https://aka.ms/vs/17/release/vc_redist.x64.exe

環境によってはMicrosoft Visual C++ 2015-2022 再頒布可能パッケージのインストールが必要になる場合があります。
現在、艦これ本体のセキュア化に伴い暫定対応しています。  
未検証の通信内容も多く、意図しない挙動となる可能性があることにご注意ください。


■使用条件
オープンソース / フリーソフトウェアです。無料でご利用頂けます。  
ソースコードは、MIT ライセンスの下で GitHub にて公開しています。


■使用方法
同梱の KanColleViewer.exe を起動してください。
各画面の解説等は https://dona-co.art/?page_id=3534 を参照してください。

■WebView2移行の検証起動（段階4・開発者向け）
全ウィンドウを終了してから、
KanColleViewer.exe --webview2
で起動した場合だけ、メイン画面にWPF WebView2 CompositionControlを表示します。
CEF版と同じ単一起動制限があるため、同時起動はできません。

段階1の比較試作とは別の、本体プロジェクトをビルドしてください。
Visual Studioから起動する場合は、本体プロジェクトのプロパティにある
「デバッグ」の起動プロファイルでコマンドライン引数に --webview2 を指定します。
検証後は引数を外すとCEF版へ戻ります。

・Evergreen WebView2 Runtimeが必要です。不足時は導入先を案内して終了し、
  自動インストールやCEFへの自動切替は行いません。
・保存先はアプリのLocalAppDataフォルダー内の WebView2 です。
  CEFのChromiumおよび比較試作のWebView2Prototypeとは別で、初回は再ログインが必要です。
・HTTPSの正規ゲームサーバーにあるAPIだけを対象に、リダイレクト・204応答を除いて
  要求本文・応答本文をそれぞれ最大5 MiBまで取得し、応答順に既存の解析処理へ渡します。
  本文は画面表示・ログ出力・ファイル保存しません。
  ヘッダーには解析へ配送した件数と本文取得失敗件数を表示します。
  段階3のAPI解析・情報更新・外部中継は利用者の実機確認で問題なしとの報告を受けています。
・ユーザーCSSをメインページと正規ゲームiframeへ適用し、iframeの再生成・遷移後も適用します。
  縮小時はスクロールバーとホイールでスクロールできます。キー操作はスクロールに使わず、ゲームへ渡します。
・ゲーム部分の撮影は正規ゲームiframeを特定できた場合だけ行い、PNG/JPEGとも1200×720 pxを検証します。
  対象不明時にページ全体を保存する代替動作はありません。
・タスクバーのプレビュー範囲は、CEF版と同じ設定を使います。
  CEF版やVer4.7/4.8と同じく、環境によってはウィンドウ全体が表示されます。
・Tabキーは既存CEFと同様に抑止します。WebView2はTabをWPFへ渡さないため、
  全ページ・全iframeで最初に実行するスクリプトで抑止します。
  CEF側の入力フォーカス通知は現在参照箇所がないため、
  WebView2では不要なスクリプト公開やメッセージ受信口を設けません。
・内蔵中継プロキシとプロキシ専用モードを利用できます。
  プロキシ専用モードではゲーム画面を作らず、Relay有効時に中継プロキシだけを起動します。
  Relay有効時はCEFと中継プロキシを共有します。WebView2では上流接続失敗時に直接接続せず通知し、
  CEFでは従来どおり直接接続へフォールバックします。
  証明書エラーの例外許可はRelay有効・中継稼働中・正規HTTPSゲームサーバーに限定します。
・Relay無効時、ゲーム画面表示中は直接接続・システムプロキシ・指定プロキシから選択した設定を使用します。
  指定プロキシの設定が不正な場合や接続に失敗した場合、直接通信へ切り替えません。
・WebView2のFPS設定は別キー GeneralSettings.WebView2FrameRate へ保存します。
  初期値は既定動作です。旧CEFのLow/Medium/Highを上書き・自動変換しません。
  30/60 FPS固定は未対応、上限解除・GPU無効化の効果は未検証です。
・FPS・GPU・プロキシ・開発者向け設定の反映は再起動後です。
  ミュートは即時反映し、再起動時も保存値を適用します。
・開発者向けオプションはDevToolsの利用を許可しますが、
  外部接続用のリモートデバッグポートは開きません。
・キャッシュ削除はWebView2のディスクキャッシュのみで、ログイン情報やCEFデータは保持します。
・ダウンロードと新規ブラウザーウィンドウの要求は拒否し、理由を表示します。
  kcsapiへのメイン画面遷移も拒否します。
・検証専用の追加説明・警告は日本語です。通常起動の既存多言語表示は変更していません。

段階2の実機確認（完了、2026-11-02）:
・利用者による本体ビルド成功後、引数なしのCEF版が正常動作することを確認しました。
・Release版の --webview2 起動で検証警告とRuntimeバージョンが表示され、ログイン後のページを読み込めることを確認しました。
・URL表示、URL入力、戻る・進む・更新がページと一致し、ミュート、DevTools、未対応撮影の通知も確認しました。
・再起動後もWebView2用FPS設定（既定動作）、ミュート状態、開発者向けオプションが保持されることを確認しました。
・起動直後の初期化中にウィンドウを閉じた後も、アプリを再起動できることを確認しました。
・--webview2を外してCEFへ戻した後も、CEFのログイン状態と旧FPS設定が保持されることを確認しました。
・古いDebug出力ではRuntime欄がなく「Connection aborted」が表示されました。Runtime専用欄を追加したRelease出力ではRuntime表示とページ読込が正常でした。
・Runtime不足とProcessFailedは、Runtimeのアンインストールやプロセス強制終了を避けるため意図的に再現していません。「Connection aborted」の通知は確認済みです。

段階2のビルド・実機確認・コミットは完了しています。

段階3の実機確認（完了）:
・利用者からビルド後の動作に問題なしとの報告を受け、段階3はコミット済みです。

段階4の実機確認（完了）:
・利用者による本体ビルド成功（エラー0・警告0）後、--webview2 起動で確認しました。
・ユーザーCSSの変更がすぐ反映され、倍率変更とウィンドウ縮小時の表示・スクロールに問題がないことを確認しました。
・ゲームへのクリック・ホイール・ドラッグ、文字入力、矢印・Home・Endキー、ログイン、日本語IMEが正常に動作することを確認しました。
・Tab・Shift+Tabで入力欄が移動せず、ゲーム画面でも表示がずれないこと、再読み込み後も同じであることを確認しました。
・PNG/JPEGのゲーム撮影に問題がなく、ゲームiframeのない別ページでは撮影失敗となり、ページ全体が保存されないことを確認しました。
・艦隊・基地航空隊ウィンドウの撮影、情報エリアの分割・結合、Popup、別ウィンドウ追従、ミュートに問題がないことを確認しました。
・10分以上の連続動作に問題がなく、通常起動のCEF版にも回帰がないことを確認しました。
・タスクバーのプレビューは、CEF版・Ver4.7/4.8と同じくウィンドウ全体が表示されました（従来どおり）。
・確認中に見つかった「クリックすると文字入力できない」不具合は、焦点を持たない専用のScrollViewerで解消しました。
  https://github.com/MicrosoftEdge/WebView2Feedback/issues/5057
・同じく「Tabで入力欄が移動する」不具合は、WPF側ではなくページ側のスクリプトで抑止して解消しました。

段階4のビルドと実機確認は完了しています。


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

Desktop Toast
(https://github.com/emoacht/DesktopToast)
    The MIT License (MIT)
    Copyright (c) 2014-2015 EMO
    ・用途 : トースト通知
    ・ライセンス : The MIT License (MIT)
    ・ライセンス全文 : Licenses/DesktopToast.txt

.NET Core Audio APIs
(https://netcoreaudio.codeplex.com/)
    The MIT License (MIT)
    Copyright (c) 2011 Vannatech
    ・用途 : 音量操作
    ・ライセンス : The MIT License (MIT)
    ・ライセンス全文 : Licenses/NETCoreAudioAPIs.txt

CefSharp.Wpf
(http://cefsharp.github.io/)
    ・用途 : 内蔵 Web ブラウザー
    ・ライセンス : The 3-Clause BSD License
    ・ライセンス全文 : Licenses/CefSharp.txt

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
