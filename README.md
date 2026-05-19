提督業も忙しい！ (KanColleViewer)
--

[![Release](https://img.shields.io/github/release/donaco/KanColleViewer.svg?style=flat-square)](https://github.com/donaco/KanColleViewer/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/donaco/KanColleViewer/latest/total.svg?style=flat-square)](https://github.com/donaco/KanColleViewer/releases/latest)
[![NuGet (KanColleWrapper)](https://img.shields.io/nuget/v/KanColleWrapper.svg?style=flat-square)](https://www.nuget.org/packages/KanColleWrapper/)
[![License](https://img.shields.io/github/license/donaco/KanColleViewer.svg?style=flat-square)](https://github.com/donaco/KanColleViewer/blob/develop/LICENSE.txt)
  
提督業も忙しい！ (KanColleViewer) は、DMM.com が配信しているブラウザゲーム「艦隊これくしょん ～艦これ～」をより遊びやすくするためのツールです。
本リポジトリは [Grabacr07/KanColleViewer](https://github.com/Grabacr07/KanColleViewer) の近代改修版です。

詳しくは、[特設ページ](https://dona-co.art/?page_id=3534) [(WebArchive)](https://web.archive.org/web/20250122055413/http://grabacr.net/kancolleviewer) をご覧ください。  
ダウンロードは、[Github - 最新リリース](https://github.com/donaco/KanColleViewer/releases/latest) からどうぞ。
  

### このプロジェクトについて
Chromium ベースの内蔵 Web ブラウザー ([CefSharp.Wpf](http://cefsharp.github.io/)) 上で艦これを表示し、通信内容をキャプチャしています。
**当然ですが、通信内容の変更や、DMM/艦これのサーバーに対する情報の送信等 (マクロ・チート行為) は一切行っていません。**
  
### 主な機能
* 高速修復材や高速建造材 (ゲーム内で確認しにくいやつ) のリアルタイム表示
* 所属している艦娘の数、保有している装備の数のリアルタイム表示
* 艦隊と、艦隊に属する艦娘の一覧表示
* 装備と、それぞれを装備している艦娘の一覧表示
* 航空隊と、それぞれを配備している海域の一覧表示
* コンディションが回復し艦隊が出撃可能になったタイミングでのトースト通知
* 入渠ドック・建造ドックの使用状況と、整備・建造終了時のトースト通知
* 現在遂行中の任務の一覧表示と、残っているデイリー/ウィークリー/マンスリー任務の一覧表示
* 遠征の状況と、終了時のトースト通知
* スクリーンショット保存
* ミュート
  
  
### 動作環境
* Windows 11
* Windows 10
  
* [Microsoft Visual C++ 2015-2022 再頒布可能パッケージ](https://aka.ms/vs/17/release/vc_redist.x64.exe)

環境によってはMicrosoft Visual C++ 2015-2022 再頒布可能パッケージのインストールが必要になる場合があります。
現在、艦これ本体のセキュア化に伴い暫定対応しています。  
未検証の通信内容も多く、意図しない挙動となる可能性があることにご注意ください。


### 開発環境・言語
C# + WPF で開発しています。  
GitHub Copilot を使用しています。  
開発環境は Windows 11 Pro + Visual Studio 2026 + Adobe Creative Cloud です。

### ライセンス
* [The MIT License (MIT)](LICENSE.txt)

MIT ライセンスの下で公開する、オープンソース / フリーソフトウェアです。

### 使用ライブラリ
以下のライブラリを使用しています。

#### [Newtonsoft.Json](https://www.newtonsoft.com/json)
> Newtonsoft.Json  
> ver 13.0.4 (2025/9/16)
>
> created James Newton-King 
* **用途 :** JSON デシリアライズ
* **ライセンス :** The MIT License (MIT)
* * **ライセンス全文 :** [licenses/Newtonsoft.Json.txt](licenses/Newtonsoft.Json.txt)

#### [Livet](http://ugaya40.hateblo.jp/entry/Livet)
* **用途 :** MVVM(Model/View/ViewModel)パターン用インフラストラクチャ
* **ライセンス :** zlib/libpng

#### [StatefulModel](http://ugaya40.hateblo.jp/entry/StatefulModel)
> The MIT License (MIT)
>
> Copyright (c) 2015 Masanori Onoue
* **用途 :** M-V-Whatever の Model 向けインフラストラクチャ
* **ライセンス :** The MIT License (MIT)
* **ライセンス全文 :** [licenses/StatefulModel.txt](licenses/StatefulModel.txt)

#### [Desktop Toast](https://github.com/emoacht/DesktopToast)
> The MIT License (MIT)
>
> Copyright (c) 2014-2015 EMO
* **用途 :** トースト通知
* **ライセンス :** The MIT License (MIT)
* **ライセンス全文 :** [licenses/DesktopToast.txt](licenses/DesktopToast.txt)

#### [.NET Core Audio APIs](https://netcoreaudio.codeplex.com/)
> The MIT License (MIT)
>
> Copyright (c) 2011 Vannatech
* **用途 :** 音量操作
* **ライセンス :** The MIT License (MIT)
* **ライセンス全文 :** [licenses/NETCoreAudioAPIs.txt](licenses/NETCoreAudioAPIs.txt)

### [CefSharp.Wpf](http://cefsharp.github.io/)
* **用途 :** 内蔵 Web ブラウザー
* **ライセンス :** The 3-Clause BSD License
* **ライセンス全文 :** [licenses/CefSharp.txt](licenses/CefSharp.txt)

### [System.Reactive](https://github.com/dotnet/reactive)
* **用途 :** 非同期処理
* **ライセンス :** The MIT License (MIT)
* **ライセンス全文 :** [licenses/System.Reactive.txt](licenses/System.Reactive.txt)

