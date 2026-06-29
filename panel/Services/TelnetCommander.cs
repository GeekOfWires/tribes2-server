using System.Net.Sockets;
using System.Text;

namespace TribesServerPanel.Services;

/// <summary>
/// Persistent authenticated telnet client to the in-game V12-engine remote console
/// (the reliable command-injection path under headless Wine). Write-only here;
/// console output is captured from the game's stdout by the supervisor.
/// </summary>
public sealed class TelnetCommander
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;

    public TelnetCommander(string host, int port, string password)
    {
        _host = host; _port = port; _password = password;
    }

    public bool IsConnected => _client?.Connected == true;

    public void Reset()
    {
        try { _client?.Close(); } catch { /* ignore */ }
        _client = null;
    }

    public async Task<bool> SendAsync(string command, CancellationToken ct = default)
    {
        var payload = Encoding.Latin1.GetBytes(command.TrimEnd('\r', '\n') + "\r\n");
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (_client is not { Connected: true } && !await ConnectAsync(ct)) return false;
                try
                {
                    await _client!.GetStream().WriteAsync(payload, ct);
                    return true;
                }
                catch (Exception)
                {
                    Reset(); // reconnect on the next attempt
                }
            }
            return false;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> ConnectAsync(CancellationToken ct)
    {
        try
        {
            var c = new TcpClient();
            await c.ConnectAsync(_host, _port, ct);
            // Torque/V12 telnet console: the password is the first line sent.
            await c.GetStream().WriteAsync(Encoding.Latin1.GetBytes(_password + "\r\n"), ct);
            _client = c;
            return true;
        }
        catch
        {
            _client = null;
            return false;
        }
    }
}
