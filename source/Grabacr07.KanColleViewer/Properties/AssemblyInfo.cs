using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;

[assembly: AssemblyTitle("提督業も忙しい！")]
[assembly: AssemblyDescription("提督業も忙しい！")]
[assembly: AssemblyCompany("grabacr.net")]
[assembly: AssemblyProduct("KanColleViewer")]
[assembly: AssemblyCopyright("Copyright © 2013 - 2018 Grabacr07")]

[assembly: ComVisible(false)]
[assembly: Guid("101B49A6-7E7B-422D-95FF-500F9EF483A8")]

[assembly: ThemeInfo(
	ResourceDictionaryLocation.None,
	ResourceDictionaryLocation.SourceAssembly)]

// MetroTrilithon.UI.Controls を内製化 XAML 名前空間を再定義
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/personal/controls", "MetroTrilithon.UI.Controls")]

// 内製コントロール (PromptTextBox, PromptComboBox, ExpanderButton, Int32Rule 等) を kcvc に登録
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/kancolleviewer/controls", "Grabacr07.KanColleViewer.Views.Controls")]

[assembly: AssemblyVersion("4.8.3")]
