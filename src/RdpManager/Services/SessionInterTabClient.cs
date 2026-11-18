using System.Windows;
using Dragablz;
using RdpManager.Views;

namespace RdpManager.Services;

/// <summary>
/// Bir sekme baska bir pencereye suruklendiginde Dragablz'in cagirdigi fabrika.
/// Yeni bir <see cref="TabWindowView"/> uretir.
/// </summary>
internal sealed class SessionInterTabClient : IInterTabClient
{
    private readonly SessionManager _manager;

    public SessionInterTabClient(SessionManager manager) => _manager = manager;

    public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
    {
        var window = _manager.CreateSessionWindow();
        return new NewTabHost<Window>(window, window.TabControl);
    }

    public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
        // Bosalan oturum penceresi kapansin; sunucu listesi ayri pencerede, etkilenmez.
        => TabEmptiedResponse.CloseWindowOrLayoutBranch;
}
