using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RdpManager.Data;
using RdpManager.Localization;
using RdpManager.Security;
using RdpManager.Services;
using RdpManager.ViewModels;
using RdpManager.Views;
using Serilog;

// UseWPF + UseWindowsForms birlikte oldugu icin bu iki tip belirsiz kaliyor.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class App : Application
{
    private const string MutexName = $"Global\\{AppEdition.Id}_B7E3F2A1";
    private const string ShowEventName = $"Global\\{AppEdition.Id}_Show_B7E3F2A1";

    private static Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Dil, App.xaml kaynaklari (Styles.xaml'deki menuler dahil) yuklenmeden ONCE secilmeli:
    /// {loc:T} metni yuklenirken cevriliyor. Kurucu InitializeComponent'ten once calisir.
    /// </summary>
    public App()
    {
        // Dil ayari okunmadan once: okuma yeni klasorde bos bir DB olusturur ve tasimayi engeller.
        MigrateDataDirectory();
        Localization.Loc.Initialize(ReadLanguageSetting());
    }

    private static string? ReadLanguageSetting()
    {
        try
        {
            using var connection = new Database(DataDirectory).Open();
            return Dapper.SqlMapper.QuerySingleOrDefault<string>(connection,
                "SELECT Value FROM Settings WHERE Key = @key", new { key = Localization.Loc.SettingKey });
        }
        catch
        {
            return null; // ilk calisma: tablo yok, varsayilan dil
        }
    }

    /// <summary>
    /// Uygulama gercekten kapaniyor mu? Ana pencere kapatildiginda uygulama
    /// tray'e cekilir; yalniz tray menusunden "Cikis" bunu true yapar.
    /// </summary>
    public static bool IsExiting { get; private set; }

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppEdition.Id);

    /// <summary>Ana pencereyi tray'den geri getirir.</summary>
    public static void ShowMainWindow()
    {
        if (Services?.GetService<MainWindow>() is not { } main) return;

        if (!main.IsVisible) main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
        main.Topmost = true;
        main.Topmost = false;
    }

    /// <summary>Dil degisikligi gibi ayarlar icin: kapat ve yeni bir kopya baslat.</summary>
    public static void RestartApplication() => ExitApplication(restart: true);

    /// <summary>Tray'den tam cikis. Acik oturum varsa once onaylatir.</summary>
    public static void ExitApplication(bool restart = false)
    {
        var openCount = Services?.GetService<SessionManager>()?.SessionCount ?? 0;
        if (openCount > 0)
        {
            var answer = MessageBox.Show(
                Loc.F("{0} acik oturum var. Uygulama kapatilsin mi?", openCount),
                Loc.T("Cikis"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        // Yeni kopya, bu surec tek-ornek kilidini birakana kadar bekler (--restart).
        if (restart && Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(exe, RestartArg)?.Dispose();

        IsExiting = true;
        Current.Shutdown();
    }

    private const string RestartArg = "--restart";

    /// <summary>
    /// Baska bir instance calistirilinca gonderilen sinyali dinler ve
    /// mevcut pencereyi one getirir.
    /// </summary>
    private void ShowSignalLoop()
    {
        while (true)
        {
            try
            {
                if (!_showEvent!.WaitOne()) break;
            }
            catch
            {
                break;   // dispose edildi -> cik
            }

            Dispatcher.Invoke(() =>
            {
                var target = TakeConnectRequest();
                if (target is not null) OpenConnectionByTarget(target);
                else ShowMainWindow();
            });
        }
    }

    private static string ConnectRequestPath => Path.Combine(DataDirectory, "connect-request.txt");

    /// <summary>
    /// Baglanti hedefini komut satirindan cozer. Iki bicim:
    ///   --connect &lt;deger&gt;   (Jump List)
    ///   &lt;sema&gt;://connect/&lt;deger&gt; (URI protokolu)
    /// Deger bir baglanti kimligi VEYA sunucu IP/adresi olabilir.
    /// </summary>
    private static string? ParseConnectTarget(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(AppEdition.UriScheme + "://", StringComparison.OrdinalIgnoreCase))
            {
                var rest = a[(AppEdition.UriScheme.Length + 3)..].Trim().Trim('/');
                if (rest.StartsWith("connect/", StringComparison.OrdinalIgnoreCase))
                    rest = rest["connect/".Length..];
                rest = rest.Trim('/');
                if (rest.Length > 0) return Uri.UnescapeDataString(rest);
            }
        }

        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--connect", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];

        return null;
    }

    private static string? TakeConnectRequest()
    {
        try
        {
            if (!File.Exists(ConnectRequestPath)) return null;
            var target = File.ReadAllText(ConnectRequestPath).Trim();
            File.Delete(ConnectRequestPath);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }

    /// <summary>
    /// Jump List / URI protokolu URI ile istenen baglantiyi acar. Hedef bir kimlik
    /// veya sunucu IP/adresi olabilir. Sunucuda (API'ye gore) biri bagliysa
    /// once uyarir.
    /// </summary>
    public static void OpenConnectionByTarget(string target)
    {
        var sessions = Services?.GetService<SessionManager>();
        if (sessions is null) return;

        target = target.Trim();
        var list = sessions.GetConnections();

        // Once kimlik, sonra adres/IP eslesmesi.
        var conn = list.FirstOrDefault(c => string.Equals(c.Id, target, StringComparison.Ordinal))
                   ?? list.FirstOrDefault(c => AddressMatches(c.Address, target));

        if (conn is null)
        {
            ShowMainWindow();
            MessageBox.Show(
                Loc.F("'{0}' listenizde bulunamadi.", target),
                Loc.T("Baglanti bulunamadi"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Sunucuda biri bagliysa uyar (durum API'sinin son bilinen verisi).
        var status = Services?.GetService<ServerStatusService>();
        status?.RefreshNow();
        var busyUser = status?.GetRemoteUser(conn.Address);
        if (!string.IsNullOrEmpty(busyUser))
        {
            var answer = MessageBox.Show(
                Loc.F("'{0}' ({1}) sunucusunda su an '{2}' bagli.\n\nYine de baglanilsin mi?",
                    conn.DisplayName, conn.Address, busyUser),
                Loc.T("Sunucu mesgul"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                ShowMainWindow();
                return;
            }
        }

        sessions.Open(conn);
    }

    /// <summary>Adresin host kismi (port haric) hedefle eslesiyor mu.</summary>
    private static bool AddressMatches(string? address, string target)
    {
        if (string.IsNullOrEmpty(address)) return false;
        var host = address.Split(':')[0].Trim();
        var wanted = target.Split(':')[0].Trim();
        return string.Equals(host, wanted, StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var connectTarget = ParseConnectTarget(e.Args);

        bool isNew;
        if (e.Args.Contains(RestartArg))
        {
            // Eski surec kapanirken kilidi birakir (ya da terk eder); en fazla 15 sn bekle.
            _singleInstanceMutex = new Mutex(false, MutexName);
            try { isNew = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { isNew = true; }
        }
        else
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isNew);
        }

        if (!isNew)
        {
            // Zaten calisan instance'i one getir (tray'deki "Goster" gibi) ve cik.
            try
            {
                // Jump List / URI protokolu ile bir sunucu istendiyse calisan instance'a ilet.
                if (connectTarget is not null)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(ConnectRequestPath, connectTarget);
                }

                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* sinyal gonderilemezse sessizce cik */ }

            Environment.Exit(0);
            return;
        }

        // Sonraki instance'larin "one getir" sinyalini dinle.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(ShowSignalLoop)
        {
            IsBackground = true,
            Name = "ShowListener",
        };
        listener.Start();

        Directory.CreateDirectory(DataDirectory);
        ConfigureLogging();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Yakalanmamis hata");
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI thread hatasi");
            MessageBox.Show(args.Exception.Message, Loc.T("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Services = BuildServices();

        // Pencereleri biz yonetiyoruz: ana pencere kapatilinca tray'e cekilir,
        // uygulama yalniz "Cikis" ile kapanir.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Log.Information("{App} basladi", AppEdition.Name);
        base.OnStartup(e);

        // Kisayol ONCE kaydedilmeli: ana pencere durum cubugunda hangisinin
        // tutundugunu gosteriyor.
        var launcher = Services.GetRequiredService<LauncherWindow>();
        var hotkeys = Services.GetRequiredService<HotkeyService>();
        hotkeys.Pressed += (_, _) => launcher.ShowLauncher();
        hotkeys.Register();

        var tray = Services.GetRequiredService<TrayIconService>();
        tray.ShowRequested += (_, _) => ShowMainWindow();
        tray.ExitRequested += (_, _) => ExitApplication();

        var sessions = Services.GetRequiredService<SessionManager>();
        tray.Initialize(() => sessions.GetConnections(), c => sessions.Open(c));

        var main = Services.GetRequiredService<MainWindow>();
        MainWindow = main;
        launcher.Commands = main.BuildPaletteCommands();
        main.Show();

        // Gorev cubugu Jump List'i kur ve baglanti degisince guncelle.
        JumpListService.Build(sessions.GetConnections());
        sessions.SessionCountChanged += (_, _) => JumpListService.Build(sessions.GetConnections());

        // Jump List / URI protokolu ile bir sunucu istendiyse (soguk baslangic) ac.
        if (connectTarget is not null)
            Dispatcher.BeginInvoke(() => OpenConnectionByTarget(connectTarget));
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => new Database(DataDirectory));
        services.AddSingleton<CredentialProtector>();
        services.AddSingleton<ConnectionRepository>();
        services.AddSingleton<HostDisposeQueue>();
        services.AddSingleton<MstscLauncher>();
        services.AddSingleton<WindowPlacementService>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<ServerStatusService>();
        services.AddSingleton<ReachabilityService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<LauncherWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();

        // Sema once kurulmali: ThemeService tercihi Settings tablosundan okuyor.
        provider.GetRequiredService<Database>().Initialize();
        provider.GetRequiredService<ThemeService>().Initialize();

        var vm = provider.GetRequiredService<MainViewModel>();
        provider.GetRequiredService<ServerStatusService>()
            .Initialize(() => vm.AllConnections);

        provider.GetRequiredService<ReachabilityService>().Start(() => vm.AllConnections);
        provider.GetRequiredService<UpdateService>().Start();

        return provider;
    }

    /// <summary>Eski ad(lar)la kurulmus verinin klasorunu yeni ada tasir (yeni klasor yoksa).</summary>
    private static void MigrateDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        try
        {
            MoveLegacyDataDirectory(appData, DataDirectory, AppEdition.LegacyIds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Eski surum hala acik (dosyalar kilitli). Bos veriyle baslarsak tasima bir daha denenmez.
            Loc.Initialize(null);   // ayar okunamaz; sistem dili
            MessageBox.Show(Loc.F("{0} klasoru tasinamadi. Uygulamanin eski surumunu kapatip tekrar deneyin.", appData),
                AppEdition.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            Environment.Exit(1);
        }
    }

    /// <summary>Hedef yoksa ilk bulunan eski klasoru hedefe tasir. Tasinan klasoru (yoksa null) dondurur.</summary>
    public static string? MoveLegacyDataDirectory(string appData, string target, IEnumerable<string> legacyIds)
    {
        if (Directory.Exists(target)) return null;
        foreach (var legacy in legacyIds)
        {
            var oldDir = Path.Combine(appData, legacy);
            if (!Directory.Exists(oldDir)) continue;
            Directory.Move(oldDir, target);
            return oldDir;
        }
        return null;
    }

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(DataDirectory, "logs", "rdpmanager-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (Services.GetService<LauncherWindow>() is { } launcher)
            {
                launcher.IsApplicationShuttingDown = true;
                launcher.Close();
            }

            // Cikista kucuk resimleri temizle (tercih acilsa).
            if (Services.GetService<ConnectionRepository>()?.GetSetting("thumb.clearOnExit") == "1")
                ThumbnailStore.Clear();

            Services.GetService<TrayIconService>()?.Dispose();
            Services.GetService<UpdateService>()?.Dispose();
            Services.GetService<ServerStatusService>()?.Dispose();
            Services.GetService<ReachabilityService>()?.Dispose();
            Services.GetService<HotkeyService>()?.Dispose();
            Services.GetService<HostDisposeQueue>()?.Dispose();
            (Services as IDisposable)?.Dispose();
        }
        catch
        {
            // Kapanista hata olursa bile process olsun.
        }

        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        Log.Information("{App} kapandi", AppEdition.Name);
        Log.CloseAndFlush();

        base.OnExit(e);
        Environment.Exit(0);
    }
}
