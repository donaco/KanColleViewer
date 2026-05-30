━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
提督業も忙しい！ (KanColleViewer)
version 4.8.0  2026/05/19
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
