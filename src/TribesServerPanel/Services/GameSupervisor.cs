using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel.Data;

namespace TribesServerPanel.Services;

/// <summary>
/// Owns the Tribes 2 (Dynamix V12 engine) dedicated server under Wine, as a hosted
/// Worker Service. The panel is PID 1, so this is where it "manages the server".
///
/// The game is a console app (PE patched GUI->CUI). It is launched on a PTY via a
/// tiny embedded Python bridge so the engine's ReadConsoleInput-based console works
/// head-less (no xvfb, no telnet): the supervisor reads the console feed from the
/// bridge's stdout and sends commands (incl. quit();) to the bridge's stdin, which
/// forwards them to the game's console input. The bridge strips terminal escapes.
///
/// Role-driven actions (enforced at the endpoint layer):
///   Admin       -> RestartAsync       (quit(); then relaunch)
///   SuperAdmin  -> ForceRestartAsync  (kill the wine tree; relaunch)  [emergency]
///   SuperAdmin  -> StopAsync          (quit(); stay down)
///   SuperAdmin  -> SendCommandAsync   (arbitrary console command)
///   Admin       -> StartAsync         (start if stopped)
/// First-run config + AutoStart gate whether the game runs (see LoadSettingsAsync).
/// Crashes auto-restart internally so the panel stays available.
/// </summary>
public sealed class GameSupervisor : BackgroundService
{
    private readonly ConsoleHub _hub;
    private readonly ILogger<GameSupervisor> _log;
    private readonly IServiceScopeFactory _scopes;

    private readonly string _gameDir, _winePrefix, _wineBin, _exePathWin, _launchParams, _ruleset;
    private volatile string? _rulesetOverride;   // null = use the SERVER_RULESET env default
    private readonly int _graceSeconds, _restartBackoff;
    private readonly bool _restartOnCrash;
    private string _bridgePath = "";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _stdinGate = new(1, 1);

    private volatile bool _configured;
    private volatile string? _launchOverride;
    private volatile string _desired = "stop";   // run | stop
    private volatile string _state = "init";     // init|unconfigured|starting|running|stopped|crashed|error
    private Process? _proc;
    private int _restarts;
    private int? _lastExit;
    private long _startedAt;
    private volatile bool _expectedExit;   // set when WE end the process (so it isn't logged as a crash)

    public GameSupervisor(ConsoleHub hub, IConfiguration cfg, ILogger<GameSupervisor> log, IServiceScopeFactory scopes)
    {
        _hub = hub; _log = log; _scopes = scopes;
        _gameDir = cfg["GAME_DIR"] ?? "/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData";
        _winePrefix = cfg["WINEPREFIX"] ?? "/opt/wineprefix";
        _wineBin = cfg["WINE_BIN"] ?? "wine";
        _exePathWin = cfg["EXE_PATH_WIN"] ?? @"C:\Dynamix\Tribes2\GameData\Tribes2.exe";
        _launchParams = cfg["LAUNCH_PARAMS"] ?? "-online -dedicated";
        _ruleset = NormalizeRuleset(cfg["SERVER_RULESET"]);   // env default ("" => base/no -mod)
        _graceSeconds = cfg.GetValue("GRACE_SECONDS", 20);
        _restartBackoff = cfg.GetValue("RESTART_BACKOFF", 5);
        _restartOnCrash = cfg.GetValue("RESTART_ON_CRASH", true);
    }

    private static string? Nonempty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ---- public lifecycle API ----------------------------------------------
    public Task StartAsync() { _desired = "run"; return Task.CompletedTask; }

    public async Task RestartAsync()
    {
        _desired = "run";
        await GracefulQuitAsync();   // monitor loop relaunches (desired==run)
    }

    public Task ForceRestartAsync()
    {
        _desired = "run";
        _expectedExit = true;        // operator-initiated kill, not a crash
        KillTree();                  // monitor loop relaunches
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _desired = "stop";
        await GracefulQuitAsync();   // stays down
    }

    public void MarkConfigured() => _configured = true;
    public void SetLaunchParams(string? p) => _launchOverride = Nonempty(p);
    public void SetRuleset(string? r) => _rulesetOverride = NormalizeRuleset(r);
    private string EffectiveLaunchParams => Nonempty(_launchOverride) ?? _launchParams;

    // "" / "base" (any case) => no mod. Anything else => the mod/ruleset folder name.
    private static string NormalizeRuleset(string? r)
    {
        r = r?.Trim();
        return string.IsNullOrEmpty(r) || r.Equals("base", StringComparison.OrdinalIgnoreCase) ? "" : r;
    }

    // Override wins once set (even "" to force base); otherwise the SERVER_RULESET env default.
    private string EffectiveRuleset => _rulesetOverride ?? _ruleset;

    // Final launch args = base params with "-mod &lt;ruleset&gt;" appended LAST.
    // The engine's console_start.cs "-mod" handler advances the argv index by 2 (while every
    // other value flag advances by 1), so it *swallows the argument that follows the mod name*.
    // Putting "-mod <ruleset>" last means the only thing it can eat is a trailing nothing --
    // any earlier -dedicated/-online is safely parsed. (The retail Classic launcher runs
    // "-dedicated -mod Classic" for exactly this reason; "-mod Classic -dedicated" eats
    // -dedicated and the server boots as a headless *client* -> Video Init -> crash.)
    private List<string> ComposeArgs()
    {
        var tokens = EffectiveLaunchParams.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var rs = EffectiveRuleset;
        if (rs.Length > 0 && !tokens.Contains("-mod"))   // respect an explicit -mod in params
        {
            tokens.Add("-mod");
            tokens.Add(rs);
        }
        return tokens;
    }

    private string FinalLaunchLine => string.Join(' ', ComposeArgs());

    /// <summary>Send a console command to the game over its stdin (PTY).</summary>
    public async Task<bool> SendCommandAsync(string cmd)
    {
        var p = _proc;
        var ok = false;
        if (p is { HasExited: false })
        {
            await _stdinGate.WaitAsync();
            try { await p.StandardInput.WriteAsync(cmd.TrimEnd('\r', '\n') + "\n"); await p.StandardInput.FlushAsync(); ok = true; }
            catch (Exception ex) { _log.LogWarning(ex, "stdin write failed"); }
            finally { _stdinGate.Release(); }
        }
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
            configured = _configured,
            running,
            pid = running ? p!.Id : (int?)null,
            @params = FinalLaunchLine,
            ruleset = EffectiveRuleset.Length == 0 ? "base" : EffectiveRuleset,
            commandsReady = running,   // stdin command channel available while running
            restarts = _restarts,
            lastExit = _lastExit,
        };
    }

    // ---- worker loop -------------------------------------------------------
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _bridgePath = await WriteBridgeAsync();
        await LoadSettingsAsync();   // gate auto-start on first-run config + AutoStart
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
                        var wasExpected = _expectedExit;
                        var startedAt = _startedAt;
                        _expectedExit = false;
                        lock (_sync) _proc = null;
                        if (!wasExpected)
                            await RecordCrashAsync(_lastExit, startedAt);  // unhandled fault -> crash report
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
                    _expectedExit = false;
                    lock (_sync) _proc = null;
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
                FileName = "python3",
                WorkingDirectory = _gameDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // python bridge runs the game on a PTY: python3 bridge.py [taskset -c N] wine EXE params...
            psi.ArgumentList.Add(_bridgePath);
            var affinity = Environment.GetEnvironmentVariable("GAME_CPU_AFFINITY");
            if (!string.IsNullOrWhiteSpace(affinity))
            {
                psi.ArgumentList.Add("taskset");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(affinity);
            }
            psi.ArgumentList.Add(_wineBin);
            psi.ArgumentList.Add(_exePathWin);
            foreach (var a in ComposeArgs())
                psi.ArgumentList.Add(a);
            psi.Environment["WINEPREFIX"] = _winePrefix;
            psi.Environment["WINEDEBUG"] = Environment.GetEnvironmentVariable("WINEDEBUG") ?? "-all";

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) _hub.Publish(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) _hub.Publish(e.Data); };

            _hub.Publish($"[panel] launching Tribes2.exe {FinalLaunchLine}");
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            lock (_sync) _proc = proc;
            _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _expectedExit = false;
            _state = "running";
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "failed to launch game");
            _hub.Publish($"[panel] failed to launch game: {ex.Message}");
            return false;
        }
    }

    private async Task GracefulQuitAsync()
    {
        var p = _proc;
        if (p is null || p.HasExited) return;
        _expectedExit = true;        // graceful/forced stop, not a crash
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

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "\n…(truncated)";

    /// <summary>Persist a crash record from the console tail + CRASHLOG.TXT for the read-only crashes page.</summary>
    private async Task RecordCrashAsync(int? exitCode, long startedAt)
    {
        try
        {
            var lines = _hub.Snapshot(80);
            string? addr = null, instr = null, module = null;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].Contains("Access Violation", StringComparison.OrdinalIgnoreCase)) continue;
                var mm = Regex.Match(lines[i], @"in\s+(\S+)");
                if (mm.Success) module = mm.Groups[1].Value.TrimEnd('.', ')');
                var detail = i + 1 < lines.Count ? lines[i + 1] : lines[i];   // "(N @ 0xADDR:0xADDR): instr"
                var am = Regex.Match(detail, @"0x[0-9A-Fa-f]+");
                if (am.Success) addr = am.Value;
                var im = Regex.Match(detail, @"\)\s*:\s*(.+)$");
                if (im.Success) instr = im.Groups[1].Value.Trim();
                break;
            }

            string? crashlog = null;
            var clp = Path.Combine(_gameDir, "CRASHLOG.TXT");
            try { if (File.Exists(clp)) crashlog = await File.ReadAllTextAsync(clp); }
            catch (Exception ex) { _log.LogWarning(ex, "could not read CRASHLOG.TXT"); }

            var details = "Console tail:\n" + string.Join("\n", lines.TakeLast(50));
            if (crashlog is not null) details += "\n\n--- CRASHLOG.TXT ---\n" + Trunc(crashlog, 8000);

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Crashes.Add(new CrashReport
            {
                StartedAt = startedAt,
                CrashedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExitCode = exitCode,
                FaultAddress = addr,
                FaultInstruction = instr,
                Module = module,
                LaunchParams = EffectiveLaunchParams,
                Details = Trunc(details, 12000),
            });
            await db.SaveChangesAsync();
            _hub.Publish($"[panel] crash recorded (exit {exitCode}{(addr is not null ? ", " + addr : "")})");
        }
        catch (Exception ex) { _log.LogError(ex, "failed to record crash"); }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.ServerSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
            _configured = s?.Configured ?? false;
            _launchOverride = Nonempty(s?.LaunchParams);
            if (s?.Ruleset != null) SetRuleset(s.Ruleset);   // persisted choice wins over the env default
            if (_configured && (s?.AutoStart ?? false))
            {
                _desired = "run"; _state = "starting";
                _log.LogInformation("Configured + AutoStart: launching game on startup.");
            }
            else
            {
                _desired = "stop"; _state = _configured ? "stopped" : "unconfigured";
                _log.LogInformation("Not auto-starting (configured={C}, autoStart={A}).", _configured, s?.AutoStart ?? false);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "failed to read server settings; staying stopped");
            _desired = "stop"; _state = "unconfigured";
        }
    }

    // Python PTY bridge: runs the game on a real TTY (headless), strips terminal
    // escapes, and bridges the game's console I/O to our stdin/stdout pipes.
    private const string BridgeScript = """
        import os, pty, select, sys, termios, fcntl, signal, re
        argv = sys.argv[1:]
        master, slave = os.openpty()
        a = termios.tcgetattr(slave); a[3] &= ~termios.ECHO
        termios.tcsetattr(slave, termios.TCSANOW, a)
        pid = os.fork()
        if pid == 0:
            os.setsid()
            for fd in (0, 1, 2): os.dup2(slave, fd)
            if master > 2: os.close(master)
            try: fcntl.ioctl(0, termios.TIOCSCTTY, 0)
            except Exception: pass
            os.execvp(argv[0], argv); os._exit(127)
        os.close(slave)
        ansi = re.compile(rb'\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b[@-Z\\-_]|[\r\x07]')
        fl = fcntl.fcntl(0, fcntl.F_GETFL); fcntl.fcntl(0, fcntl.F_SETFL, fl | os.O_NONBLOCK)
        out = sys.stdout.buffer
        while True:
            try: r, _, _ = select.select([master, 0], [], [], 1)
            except (OSError, InterruptedError): r = []
            if master in r:
                try: data = os.read(master, 8192)
                except OSError: data = b''
                if not data: break
                out.write(ansi.sub(b'', data)); out.flush()
            if 0 in r:
                try: data = os.read(0, 4096)
                except OSError: data = b''
                if data:
                    try: os.write(master, data)
                    except OSError: pass
            if os.waitpid(pid, os.WNOHANG)[0] == pid: break
        try: os.kill(pid, signal.SIGKILL)
        except Exception: pass
        """;

    private async Task<string> WriteBridgeAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "t2_ptybridge.py");
        try { await File.WriteAllTextAsync(path, BridgeScript); }
        catch (Exception ex) { _log.LogError(ex, "could not write PTY bridge"); }
        return path;
    }
}
