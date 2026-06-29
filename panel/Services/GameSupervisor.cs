using System.Diagnostics;
using System.Security.Cryptography;

namespace TribesServerPanel.Services;

/// <summary>
/// Owns the Tribes 2 (Dynamix V12 engine) dedicated server process under Wine.
/// Runs as a hosted Worker Service: captures stdout into the ConsoleHub, injects
/// the telnet remote-console bootstrap, and drives the lifecycle. The web panel is
/// PID 1, so this is where "the panel manages the container lifecycle" lives.
///
/// Role-driven actions (enforced at the endpoint layer):
///   Admin       -> RestartAsync       (graceful quit(); then relaunch)
///   SuperAdmin  -> ForceRestartAsync  (kill the wine tree; relaunch)  [emergency]
///   SuperAdmin  -> StopAsync          (graceful quit(); stay down)
///   SuperAdmin  -> SendCommandAsync   (arbitrary console command)
///   Admin       -> StartAsync         (start if stopped)
/// Crashes auto-restart internally so the panel stays available.
/// </summary>
public sealed class GameSupervisor : BackgroundService
{
    private readonly ConsoleHub _hub;
    private readonly ILogger<GameSupervisor> _log;

    private readonly string _gameDir, _winePrefix, _wineBin, _exePathWin, _launchParams;
    private readonly int _telnetPort, _graceSeconds, _restartBackoff;
    private readonly string _consolePass, _listenPass;
    private readonly bool _restartOnCrash;

    private readonly TelnetCommander _telnet;
    private readonly object _sync = new();

    private volatile string _desired = "run";   // run | stop
    private volatile string _state = "init";     // init|starting|running|stopped|crashed|error
    private Process? _proc;
    private int _restarts;
    private int? _lastExit;

    public GameSupervisor(ConsoleHub hub, IConfiguration cfg, ILogger<GameSupervisor> log)
    {
        _hub = hub; _log = log;
        _gameDir = cfg["GAME_DIR"] ?? "/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData";
        _winePrefix = cfg["WINEPREFIX"] ?? "/opt/wineprefix";
        _wineBin = cfg["WINE_BIN"] ?? "wine";
        _exePathWin = cfg["EXE_PATH_WIN"] ?? @"C:\Dynamix\Tribes2\GameData\Tribes2.exe";
        _launchParams = cfg["LAUNCH_PARAMS"] ?? "-online -dedicated";
        _telnetPort = cfg.GetValue("TELNET_PORT", 23000);
        _graceSeconds = cfg.GetValue("GRACE_SECONDS", 20);
        _restartBackoff = cfg.GetValue("RESTART_BACKOFF", 5);
        _restartOnCrash = cfg.GetValue("RESTART_ON_CRASH", true);
        _consolePass = Nonempty(cfg["TELNET_CONSOLE_PASS"]) ?? RandomHex();
        _listenPass = Nonempty(cfg["TELNET_LISTEN_PASS"]) ?? RandomHex();
        _telnet = new TelnetCommander("127.0.0.1", _telnetPort, _consolePass);
    }

    private static string? Nonempty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string RandomHex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // ---- public lifecycle API ---------------------------------------------
    public Task StartAsync() { _desired = "run"; return Task.CompletedTask; }

    public async Task RestartAsync()
    {
        _desired = "run";
        await GracefulQuitAsync(); // monitor loop relaunches because desired==run
    }

    public Task ForceRestartAsync()
    {
        _desired = "run";
        KillTree();                // monitor loop relaunches
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _desired = "stop";
        await GracefulQuitAsync();  // stays down
    }

    public async Task<bool> SendCommandAsync(string cmd)
    {
        var ok = await _telnet.SendAsync(cmd);
        _hub.Publish($"[panel] >>> {cmd}" + (ok ? "" : "  (SEND FAILED)"));
        return ok;
    }

    public object Status()
    {
        var p = _proc;
        var running = p is { HasExited: false };
        return new
        {
            state = _state,
            desired = _desired,
            running,
            pid = running ? p!.Id : (int?)null,
            @params = _launchParams,
            telnetConnected = _telnet.IsConnected,
            restarts = _restarts,
            lastExit = _lastExit,
        };
    }

    // ---- worker loop -------------------------------------------------------
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RenderAutoexec();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var p = _proc;
                if (_desired == "run" && (p is null || p.HasExited))
                {
                    if (p is { HasExited: true })
                    {
                        _lastExit = SafeExitCode(p);
                        _telnet.Reset();
                        lock (_sync) _proc = null;
                        if (!_restartOnCrash)
                        {
                            _state = "stopped"; _desired = "stop";
                            _log.LogWarning("Game exited ({Code}); auto-restart disabled", _lastExit);
                            continue;
                        }
                        _restarts++;
                        _state = "crashed";
                        _hub.Publish($"[panel] game exited ({_lastExit}); restarting in {_restartBackoff}s");
                        await Task.Delay(TimeSpan.FromSeconds(_restartBackoff), stoppingToken);
                        if (stoppingToken.IsCancellationRequested || _desired != "run") continue;
                    }

                    _state = "starting";
                    if (!TrySpawn())
                    {
                        _state = "error";
                        await Task.Delay(TimeSpan.FromSeconds(_restartBackoff), stoppingToken);
                    }
                }
                else if (_desired == "stop" && p is { HasExited: true })
                {
                    _state = "stopped";
                    lock (_sync) _proc = null;
                    _telnet.Reset();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogError(ex, "supervisor loop error"); }

            await Task.Delay(500, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _desired = "stop";
        try { await GracefulQuitAsync(); } catch { /* best effort */ }
        await base.StopAsync(cancellationToken);
    }

    // ---- internals ---------------------------------------------------------
    private bool TrySpawn()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _wineBin,
                WorkingDirectory = _gameDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(_exePathWin);
            foreach (var a in _launchParams.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(a);
            psi.Environment["WINEPREFIX"] = _winePrefix;
            psi.Environment["WINEDEBUG"] = Environment.GetEnvironmentVariable("WINEDEBUG") ?? "-all";

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) _hub.Publish(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) _hub.Publish(e.Data); };

            _hub.Publish($"[panel] launching Tribes2.exe {_launchParams}");
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            lock (_sync) _proc = proc;
            _state = "running";
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "failed to launch game (is wine present?)");
            _hub.Publish($"[panel] failed to launch game: {ex.Message}");
            return false;
        }
    }

    private async Task GracefulQuitAsync()
    {
        var p = _proc;
        if (p is null || p.HasExited) return;
        _hub.Publish("[panel] graceful quit -> quit();");
        await SendCommandAsync("quit();");
        var deadline = DateTime.UtcNow.AddSeconds(_graceSeconds);
        while (DateTime.UtcNow < deadline && !p.HasExited)
            await Task.Delay(250);
        if (!p.HasExited)
        {
            _hub.Publish("[panel] grace expired; killing process tree");
            KillTree();
        }
    }

    private void KillTree()
    {
        lock (_sync)
        {
            try { if (_proc is { HasExited: false } p) p.Kill(entireProcessTree: true); }
            catch (Exception ex) { _log.LogWarning(ex, "kill failed"); }
        }
    }

    private static int? SafeExitCode(Process p) { try { return p.ExitCode; } catch { return null; } }

    private void RenderAutoexec()
    {
        // console_start.cs exec()s autoexec.cs after arg parsing; enable the telnet
        // remote console there (the stock -telnetParams handler has an empty-listen-pass bug).
        var content = $$"""
            if ($LaunchMode $= "DedicatedServer")
            {
               telnetSetParameters({{_telnetPort}}, "{{_consolePass}}", "{{_listenPass}}");
               echo("[panel] telnet remote console enabled on port {{_telnetPort}}");
            }
            """;
        try
        {
            if (Directory.Exists(_gameDir))
                File.WriteAllText(Path.Combine(_gameDir, "autoexec.cs"), content);
            else
                _log.LogWarning("GAME_DIR {Dir} not found; skipping autoexec render", _gameDir);
        }
        catch (Exception ex) { _log.LogWarning(ex, "could not write autoexec.cs"); }
    }
}
