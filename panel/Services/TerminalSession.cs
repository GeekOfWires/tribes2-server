using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace TribesServerPanel.Services;

/// <summary>
/// Bridges a browser xterm.js terminal to an interactive bash session running on a
/// real PTY inside the container (root only). A tiny embedded Python bridge owns the
/// pseudo-terminal (so curses apps like vim/htop work); this class pumps bytes between
/// the WebSocket and the bridge's stdio.
///
/// Protocol: WebSocket BINARY frames are raw keystrokes (-> pty master). TEXT frames are
/// control JSON: {"r":[cols,rows]} -> a window-resize, forwarded to the bridge as a NUL
/// control frame it interprets out-of-band (NUL never occurs in real keyboard input).
/// </summary>
public static class TerminalSession
{
    private const string Bridge = """
import os, sys, pty, select, struct, fcntl, termios, signal

shell = sys.argv[1] if len(sys.argv) > 1 else "/bin/bash"
pid, master = pty.fork()
if pid == 0:
    os.environ.setdefault("TERM", "xterm-256color")
    os.execvp(shell, [shell, "-l"])
    os._exit(127)

def setsize(cols, rows):
    try:
        fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack("HHHH", rows, cols, 0, 0))
    except OSError:
        pass

setsize(120, 32)
MARK = b"\x00resize\x00"   # control frame: \x00resize\x00COLS\x00ROWS\x00
buf = b""
while True:
    try:
        r, _, _ = select.select([master, 0], [], [])
    except (OSError, select.error):
        break
    if master in r:
        try:
            data = os.read(master, 65536)
        except OSError:
            data = b""
        if not data:
            break
        os.write(1, data)
    if 0 in r:
        try:
            d = os.read(0, 65536)
        except OSError:
            d = b""
        if not d:
            break
        buf += d
        while True:
            i = buf.find(MARK)
            if i < 0:
                if buf:
                    os.write(master, buf)
                    buf = b""
                break
            if i > 0:
                os.write(master, buf[:i])
            rest = buf[i + len(MARK):]
            j = rest.find(b"\x00")
            if j < 0:
                buf = buf[i:]; break
            k = rest.find(b"\x00", j + 1)
            if k < 0:
                buf = buf[i:]; break
            try:
                setsize(int(rest[:j]), int(rest[j + 1:k]))
            except ValueError:
                pass
            buf = rest[k + 1:]
try:
    os.kill(pid, signal.SIGKILL)
except OSError:
    pass
""";

    private static string? _bridgePath;
    private static readonly object _gate = new();

    private static string EnsureBridge()
    {
        lock (_gate)
        {
            if (_bridgePath is not null && File.Exists(_bridgePath)) return _bridgePath;
            var p = Path.Combine(Path.GetTempPath(), "tsp_pty_shell.py");
            File.WriteAllText(p, Bridge);
            _bridgePath = p;
            return p;
        }
    }

    public static async Task RunAsync(WebSocket ws, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("python3")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(EnsureBridge());
        psi.ArgumentList.Add("/bin/bash");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start terminal bridge");

        // pty -> websocket
        var pump = Task.Run(async () =>
        {
            var buf = new byte[8192];
            var stdout = proc.StandardOutput.BaseStream;
            try
            {
                int n;
                while ((n = await stdout.ReadAsync(buf, ct)) > 0 && ws.State == WebSocketState.Open)
                    await ws.SendAsync(buf.AsMemory(0, n), WebSocketMessageType.Binary, true, ct);
            }
            catch { /* closed */ }
            finally
            {
                if (ws.State == WebSocketState.Open)
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shell exited", CancellationToken.None); } catch { }
            }
        }, ct);

        // websocket -> pty
        var stdin = proc.StandardInput.BaseStream;
        var rbuf = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var res = await ws.ReceiveAsync(rbuf, ct);
                if (res.MessageType == WebSocketMessageType.Close) break;
                if (res.Count == 0) continue;

                if (res.MessageType == WebSocketMessageType.Text)
                {
                    // {"r":[cols,rows]}
                    var txt = Encoding.UTF8.GetString(rbuf, 0, res.Count);
                    var (cols, rows) = ParseResize(txt);
                    if (cols > 0 && rows > 0)
                    {
                        var frame = Encoding.ASCII.GetBytes($"\x00resize\x00{cols}\x00{rows}\x00");
                        await stdin.WriteAsync(frame, ct);
                        await stdin.FlushAsync(ct);
                    }
                }
                else
                {
                    await stdin.WriteAsync(rbuf.AsMemory(0, res.Count), ct);
                    await stdin.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            await Task.WhenAny(pump, Task.Delay(1000, CancellationToken.None));
        }
    }

    private static (int cols, int rows) ParseResize(string json)
    {
        // tiny hand-parse of {"r":[<cols>,<rows>]} to avoid pulling in a serializer here
        try
        {
            var lb = json.IndexOf('[');
            var rb = json.IndexOf(']');
            if (lb < 0 || rb < lb) return (0, 0);
            var parts = json[(lb + 1)..rb].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2) return (0, 0);
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }
        catch { return (0, 0); }
    }
}
