using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;

[assembly: AssemblyTitle("KanColleViewer.Controls")]
[assembly: AssemblyCompany("grabacr.net")]
[assembly: AssemblyProduct("KanColleViewer.Controls")]
[assembly: AssemblyDescription("UI controls for KanColleViewer and plugins.")]
[assembly: AssemblyCopyright("Copyright © 2015 Grabacr07")]

[assembly: ComVisible(false)]
[assembly: Guid("7FFFD746-AE00-4701-82B5-373C603D8D21")]

[assembly: ThemeInfo(
	ResourceDictionaryLocation.None,
	ResourceDictionaryLocation.SourceAssembly)]

[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/kancolleviewer/controls", "Grabacr07.KanColleViewer.Controls")]
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/kancolleviewer/converters", "Grabacr07.KanColleViewer.Converters")]
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/kancolleviewer/interactivity", "Grabacr07.KanColleViewer.Interactivity")]
// Phase 1: MetroTrilithon.UI.Controls を KanColleViewer.Controls に内製化
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/personal/controls", "MetroTrilithon.UI.Controls")]
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/personal/converters", "MetroTrilithon.UI.Converters")]
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2015/personal/interactivity", "MetroTrilithon.UI.Interactivity")]
// Phase 4: MetroWindow / CaptionButton 等を内製化し metro: namespace に登録
[assembly: XmlnsDefinition("http://schemes.grabacr.net/winfx/2014/controls", "Grabacr07.KanColleViewer.Controls.Metro")]

[assembly: AssemblyVersion("1.3.2")]
[assembly: AssemblyInformationalVersion("1.3.2")]
