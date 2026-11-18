using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using Serilog;

namespace RdpManager.Services;

public sealed class UpdateService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<UpdateService>();

    private const string UpdateUrl = AppEdition.UpdateUrl;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private readonly Data.ConnectionRepository _repository;

    public UpdateService(Data.ConnectionRepository repository)
    {
        _repository = repository;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = CheckInterval,
        };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    /// <summary>Periyodik otomatik guncelleme kontrolu. Varsayilan acik.</summary>
    public bool AutoCheck
    {
        get => _repository.GetSetting("update.autocheck") != "0";
        set
        {
            _repository.SetSetting("update.autocheck", value ? "1" : "0");
            if (value) { if (!_timer.IsEnabled) { _timer.Start(); _ = CheckAsync(); } }
            else _timer.Stop();
        }
    }

    public static string CurrentVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString(3)
                      ?? "0.0.0";
            var plus = ver.IndexOf('+');
            return plus >= 0 ? ver[..plus] : ver;
        }
    }

    public string? LatestVersion { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public string? DownloadUrl { get; private set; }
    public bool IsUpdateAvailable { get; private set; }
    public bool IsDownloading { get; private set; }

    public event EventHandler? UpdateAvailable;

    public void Start()
    {
        if (!AutoCheck) return;
        _timer.Start();
        _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (string.IsNullOrEmpty(UpdateUrl)) return;

        try
        {
            var json = await _http.GetStringAsync(UpdateUrl);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var version = root.GetProperty("version").GetString();
            if (string.IsNullOrEmpty(version)) return;

            var notes = root.TryGetProperty("notes", out var notesProp) ? notesProp.GetString() : null;

            string? url = null;
            if (root.TryGetProperty("platforms", out var platforms) &&
                platforms.TryGetProperty("windows-x86_64", out var win) &&
                win.TryGetProperty("url", out var urlProp))
            {
                url = urlProp.GetString();
            }

            if (!IsNewer(version, CurrentVersion)) return;

            LatestVersion = version;
            ReleaseNotes = notes;
            DownloadUrl = url;
            IsUpdateAvailable = true;

            Logger.Information("Guncelleme mevcut: {Current} -> {Latest}", CurrentVersion, version);
            UpdateAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Guncelleme kontrolu basarisiz");
        }
    }

    public async Task DownloadAndInstallAsync()
    {
        if (string.IsNullOrEmpty(DownloadUrl)) return;

        IsDownloading = true;
        try
        {
            var tempDir = Path.Combine(App.DataDirectory, "updates");
            Directory.CreateDirectory(tempDir);

            var fileName = $"{AppEdition.Name}_{LatestVersion}_x64-setup.exe";
            var filePath = Path.Combine(tempDir, fileName);

            Logger.Information("Guncelleme indiriliyor: {Url}", DownloadUrl);

            var response = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(filePath);
                await stream.CopyToAsync(file);
            }

            Logger.Information("Guncelleme indirildi, kurulum baslatiliyor: {Path}", filePath);

            // Sessiz kurulum: sihirbaz hic gorunmesin, ek islem (kisayol/baslangic)
            // uygulanmasin, kurulum bitince uygulama installer tarafindan yeniden
            // baslatilir (installer.iss icindeki WizardSilent Run girdisi).
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=\"\"",
                UseShellExecute = true,
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Guncelleme indirme/kurulum hatasi");
            throw;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(remote, out var remoteVer)) return false;
        if (!Version.TryParse(local, out var localVer)) return false;
        return remoteVer > localVer;
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }
}
