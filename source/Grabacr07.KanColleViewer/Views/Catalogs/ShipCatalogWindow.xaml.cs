using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Views.Catalogs
{
    /// <summary>
    /// 艦娘一覧ウィンドウを表します。
    /// </summary>
    partial class ShipCatalogWindow
    {
        public ShipCatalogWindow()
        {
            this.InitializeComponent();

            if (Application.Instance is { MainWindow: { } mainWindow })
            {
                mainWindow.Closed += (sender, args) => this.Close();
            }
        }
    }
}
