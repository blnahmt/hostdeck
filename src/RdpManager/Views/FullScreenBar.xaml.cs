using System.Windows;
using RdpManager.Services;
using RdpManager.ViewModels;

namespace RdpManager.Views;

/// <summary>
/// Tam ekran oturumun ustunde beliren arac cubugu.
///
/// AYRI BIR PENCERE olmasi sart: RDP kontrolu bir HWND ve WPF ogeleri
/// bir HWND'nin uzerine cizilemez (airspace). Ayni pencerede duran her sey
/// oturumun ALTINDA kalir ve gorunmez.
/// </summary>
public partial class FullScreenBar : Window
{
    private readonly SessionTabItem _tab;
    private readonly SessionManager _manager;

    public FullScreenBar(SessionTabItem tab, SessionManager manager)
    {
        _tab = tab;
        _manager = manager;
        InitializeComponent();

        TitleText.Text = tab.Title;
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => _manager.ExitFullScreen(_tab);

    private void CtrlAltDel_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _tab.Host.SendCtrlAltDelete();
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) => _tab.Host.Reconnect();

    private void Mstsc_Click(object sender, RoutedEventArgs e) => _manager.OpenInMstsc(_tab);
}
