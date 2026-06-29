import { useCallback, useEffect, useRef, useState } from "react";
import { api, RANK, ROLES, roleLabel, type Me, type Status, type UserRow, type AuditRow, type Config } from "./api";

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [cfg, setCfg] = useState<Config | null>(null);
  const [loading, setLoading] = useState(true);

  const loadCfg = useCallback(() => api.config().then(setCfg).catch(() => setCfg(null)), []);

  useEffect(() => {
    (async () => {
      try {
        setMe(await api.me());
        await loadCfg();
      } catch {
        setMe(null);
      } finally {
        setLoading(false);
      }
    })();
  }, [loadCfg]);

  const onLogin = async (m: Me) => { setMe(m); await loadCfg(); };
  const onLogout = () => { setMe(null); setCfg(null); };

  if (loading) return <div className="login-wrap"><div className="muted">Loading…</div></div>;
  if (!me) return <Login onLogin={onLogin} />;

  if (cfg && !cfg.configured) {
    return me.rank >= RANK.root
      ? <FirstRunSetup cfg={cfg} onDone={loadCfg} onLogout={onLogout} />
      : <NotConfigured onLogout={onLogout} />;
  }
  return <Dashboard me={me} cfg={cfg} onLogout={onLogout} refreshCfg={loadCfg} />;
}

function Login({ onLogin }: { onLogin: (m: Me) => void }) {
  const [u, setU] = useState("");
  const [p, setP] = useState("");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setErr("");
    try { onLogin(await api.login(u, p)); }
    catch { setErr("Invalid username or password"); }
    finally { setBusy(false); }
  };
  return (
    <div className="login-wrap">
      <form className="card login" onSubmit={submit}>
        <h2>Tribes<span style={{ color: "var(--primary)" }}>NEXT</span> Panel</h2>
        <label>Username</label>
        <input value={u} onChange={(e) => setU(e.target.value)} autoComplete="username" />
        <label>Password</label>
        <input type="password" value={p} onChange={(e) => setP(e.target.value)} autoComplete="current-password" />
        {err && <p className="err">{err}</p>}
        <button className="btn primary" style={{ width: "100%", marginTop: 14 }} disabled={busy}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}

function SignOut({ onLogout }: { onLogout: () => void }) {
  return <button className="btn" onClick={async () => { await api.logout().catch(() => {}); onLogout(); }}>Sign out</button>;
}

function NotConfigured({ onLogout }: { onLogout: () => void }) {
  return (
    <div className="login-wrap">
      <div className="card login" style={{ textAlign: "center" }}>
        <h2>Setup required</h2>
        <p className="muted">This server has not been configured yet. A <strong>root</strong> administrator must complete first-time setup before it can run.</p>
        <SignOut onLogout={onLogout} />
      </div>
    </div>
  );
}

function FirstRunSetup({ cfg, onDone, onLogout }: { cfg: Config; onDone: () => void; onLogout: () => void }) {
  const [params, setParams] = useState(cfg.launchParams || cfg.defaultLaunchParams);
  const [autoStart, setAutoStart] = useState(true);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");

  const complete = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setErr("");
    try { await api.completeConfig(autoStart, params); await onDone(); }
    catch (e2) { setErr((e2 as Error).message); setBusy(false); }
  };

  return (
    <div className="login-wrap">
      <form className="card" style={{ width: 460 }} onSubmit={complete}>
        <h2>First-time Server Setup</h2>
        <p className="muted">Configure the dedicated server. After this the server can run, and you can change Auto-Start anytime.</p>

        <label style={{ display: "block", margin: "10px 0 4px", fontSize: 13, color: "var(--muted)" }}>Launch parameters</label>
        <input style={{ width: "100%" }} value={params} onChange={(e) => setParams(e.target.value)} />
        <p className="muted" style={{ fontSize: 12 }}>Order: <code>-online</code> (or <code>-nologin</code>) first, <code>-dedicated</code> last, mods in between.</p>

        <label style={{ display: "flex", gap: 8, alignItems: "center", marginTop: 10 }}>
          <input type="checkbox" checked={autoStart} onChange={(e) => setAutoStart(e.target.checked)} style={{ width: "auto" }} />
          <span>Auto-Start the server when the panel starts</span>
        </label>

        {err && <p className="err">{err}</p>}
        <div className="row" style={{ marginTop: 16 }}>
          <button className="btn primary" disabled={busy}>{busy ? "Configuring…" : "Complete setup & start"}</button>
          <SignOut onLogout={onLogout} />
        </div>
      </form>
    </div>
  );
}

type Tab = "console" | "controls" | "users" | "audit";

function Dashboard({ me, cfg, onLogout, refreshCfg }: { me: Me; cfg: Config | null; onLogout: () => void; refreshCfg: () => void }) {
  const [tab, setTab] = useState<Tab>("console");
  const logout = async () => { await api.logout().catch(() => {}); onLogout(); };
  const can = (rank: number) => me.rank >= rank;

  return (
    <div className="shell">
      <nav className="sidebar">
        <div className="brand">Tribes <span>2</span></div>
        <button className={`navbtn ${tab === "console" ? "active" : ""}`} onClick={() => setTab("console")}>Console</button>
        {can(RANK.Admin) && <button className={`navbtn ${tab === "controls" ? "active" : ""}`} onClick={() => setTab("controls")}>Controls</button>}
        {can(RANK.root) && <button className={`navbtn ${tab === "users" ? "active" : ""}`} onClick={() => setTab("users")}>Users</button>}
        {can(RANK.SuperAdmin) && <button className={`navbtn ${tab === "audit" ? "active" : ""}`} onClick={() => setTab("audit")}>Audit Log</button>}
        <div className="spacer" />
        <div className="muted" style={{ fontSize: 13 }}>{me.userName}</div>
        <div className="badge" style={{ marginBottom: 8 }}>{roleLabel(me.role)}</div>
        <button className="btn" onClick={logout}>Sign out</button>
      </nav>
      <main className="main">
        {tab === "console" && <ConsoleView />}
        {tab === "controls" && can(RANK.Admin) && <Controls me={me} cfg={cfg} refreshCfg={refreshCfg} />}
        {tab === "users" && can(RANK.root) && <Users />}
        {tab === "audit" && can(RANK.SuperAdmin) && <Audit />}
      </main>
    </div>
  );
}

function StatusBar() {
  const [s, setS] = useState<Status | null>(null);
  useEffect(() => {
    let on = true;
    const poll = () => api.status().then((x) => on && setS(x)).catch(() => {});
    poll();
    const id = setInterval(poll, 5000);
    return () => { on = false; clearInterval(id); };
  }, []);
  const color = s?.running ? "var(--primary)" : "var(--danger)";
  return (
    <div className="row" style={{ marginBottom: 12 }}>
      <span className="badge" style={{ color, borderColor: color }}>● {s?.running ? "running" : s?.state ?? "…"}</span>
      {s?.pid && <span className="muted">pid {s.pid}</span>}
      {s?.params && <span className="muted">params: <code>{s.params}</code></span>}
      <span className="muted">commands: {s?.commandsReady ? "ready" : "—"}</span>
      {s && <span className="muted">restarts: {s.restarts}</span>}
    </div>
  );
}

function ConsoleView() {
  const [lines, setLines] = useState<string[]>([]);
  const [live, setLive] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const stick = useRef(true);

  useEffect(() => {
    const es = new EventSource("/api/console/stream");
    es.onopen = () => setLive(true);
    es.onerror = () => setLive(false);
    es.onmessage = (e) => setLines((prev) => (prev.length > 2000 ? prev.slice(-1500) : prev).concat(e.data));
    return () => es.close();
  }, []);
  useEffect(() => { if (box.current && stick.current) box.current.scrollTop = box.current.scrollHeight; }, [lines]);
  const onScroll = () => {
    const b = box.current; if (!b) return;
    stick.current = b.scrollHeight - b.scrollTop - b.clientHeight < 40;
  };

  return (
    <div>
      <h2>Server Console</h2>
      <StatusBar />
      <div className="row" style={{ marginBottom: 8 }}>
        <span className={live ? "ok" : "muted"}>{live ? "● live" : "○ connecting…"}</span>
        <button className="btn" onClick={() => setLines([])}>Clear view</button>
      </div>
      <div className="console" ref={box} onScroll={onScroll}>
        {lines.length ? lines.join("\n") : <span className="muted">Waiting for console output…</span>}
      </div>
    </div>
  );
}

function Controls({ me, cfg, refreshCfg }: { me: Me; cfg: Config | null; refreshCfg: () => void }) {
  const [msg, setMsg] = useState<{ ok: boolean; t: string } | null>(null);
  const [cmd, setCmd] = useState("");
  const [busy, setBusy] = useState(false);
  const can = (rank: number) => me.rank >= rank;

  const run = async (fn: () => Promise<unknown>, label: string, confirm?: string) => {
    if (confirm && !window.confirm(confirm)) return;
    setBusy(true);
    try { await fn(); setMsg({ ok: true, t: `${label}: ok` }); }
    catch (e) { setMsg({ ok: false, t: `${label}: ${(e as Error).message}` }); }
    finally { setBusy(false); }
  };
  const sendCmd = async (e: React.FormEvent) => {
    e.preventDefault();
    const c = cmd.trim(); if (!c) return;
    await run(() => api.command(c), `command "${c}"`);
    setCmd("");
  };
  const toggleAutoStart = async (enabled: boolean) => {
    await run(() => api.setAutoStart(enabled), `auto-start ${enabled ? "on" : "off"}`);
    refreshCfg();
  };

  return (
    <div>
      <h2>Server Controls</h2>
      <StatusBar />
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Lifecycle</h3>
        <div className="row">
          <button className="btn primary" disabled={busy} onClick={() => run(api.restart, "restart", "Gracefully restart the server?")}>Restart</button>
          <button className="btn" disabled={busy} onClick={() => run(api.start, "start")}>Start</button>
          {can(RANK.SuperAdmin) && <button className="btn warn" disabled={busy} onClick={() => run(api.forceRestart, "force restart", "Emergency FORCE restart (kill + relaunch the game)?")}>Force Restart</button>}
          {can(RANK.SuperAdmin) && <button className="btn warn" disabled={busy} onClick={() => run(api.stop, "stop", "Gracefully stop the server and keep it down?")}>Stop</button>}
        </div>
      </div>

      {can(RANK.root) && cfg && (
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Auto-Start</h3>
          <label style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input type="checkbox" disabled={busy} checked={cfg.autoStart} onChange={(e) => toggleAutoStart(e.target.checked)} style={{ width: "auto" }} />
            <span>Launch the Tribes 2 server automatically when the panel starts</span>
          </label>
        </div>
      )}

      {can(RANK.SuperAdmin) && (
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Console Command</h3>
          <form className="row" onSubmit={sendCmd}>
            <input style={{ flex: 1, minWidth: 240 }} placeholder='e.g. echo("hi"); or kick(...);' value={cmd} onChange={(e) => setCmd(e.target.value)} disabled={busy} />
            <button className="btn primary" disabled={busy}>Send</button>
          </form>
        </div>
      )}

      {can(RANK.root) && (
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Danger Zone</h3>
          <p className="muted" style={{ marginTop: 0 }}>Force-shutting down the panel stops the container (it will auto-restart if the restart policy is enabled).</p>
          <button className="btn danger" disabled={busy} onClick={() => run(api.shutdownPanel, "panel shutdown", "FORCE SHUTDOWN the web panel and container?")}>Force Shutdown Panel</button>
        </div>
      )}

      {msg && <p className={msg.ok ? "ok" : "err"}>{msg.t}</p>}
    </div>
  );
}

function Users() {
  const [rows, setRows] = useState<UserRow[]>([]);
  const [msg, setMsg] = useState<{ ok: boolean; t: string } | null>(null);
  const [nu, setNu] = useState(""); const [np, setNp] = useState(""); const [nr, setNr] = useState("User");
  const load = useCallback(() => { api.users().then(setRows).catch((e) => setMsg({ ok: false, t: (e as Error).message })); }, []);
  useEffect(() => { load(); }, [load]);

  const act = async (fn: () => Promise<unknown>, label: string, confirm?: string) => {
    if (confirm && !window.confirm(confirm)) return;
    try { await fn(); setMsg({ ok: true, t: `${label}: ok` }); load(); }
    catch (e) { setMsg({ ok: false, t: `${label}: ${(e as Error).message}` }); }
  };
  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    await act(() => api.createUser(nu, np, nr), "create user");
    setNu(""); setNp(""); setNr("User");
  };
  const reset = (u: UserRow) => {
    const pw = window.prompt(`New password for ${u.username} (min 8):`);
    if (pw) act(() => api.resetPassword(u.id, pw), "reset password");
  };

  return (
    <div>
      <h2>User Management</h2>
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Create User</h3>
        <form className="row" onSubmit={create}>
          <input placeholder="username" value={nu} onChange={(e) => setNu(e.target.value)} />
          <input type="password" placeholder="password" value={np} onChange={(e) => setNp(e.target.value)} />
          <select value={nr} onChange={(e) => setNr(e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}</select>
          <button className="btn primary">Create</button>
        </form>
      </div>
      <div className="card">
        <table>
          <thead><tr><th>Username</th><th>Role</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>
            {rows.map((u) => (
              <tr key={u.id}>
                <td>{u.username}</td>
                <td>
                  <select value={u.role} onChange={(e) => act(() => api.setRole(u.id, e.target.value), "set role")}>
                    {ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
                  </select>
                </td>
                <td>{u.isActive ? <span className="ok">active</span> : <span className="muted">disabled</span>}</td>
                <td className="row">
                  <button className="btn" onClick={() => act(() => api.setActive(u.id, !u.isActive), u.isActive ? "deactivate" : "activate")}>{u.isActive ? "Deactivate" : "Activate"}</button>
                  <button className="btn" onClick={() => reset(u)}>Reset PW</button>
                  <button className="btn danger" onClick={() => act(() => api.deleteUser(u.id), "delete", `Delete ${u.username}?`)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {msg && <p className={msg.ok ? "ok" : "err"}>{msg.t}</p>}
    </div>
  );
}

function Audit() {
  const [rows, setRows] = useState<AuditRow[]>([]);
  useEffect(() => { api.audit().then(setRows).catch(() => {}); }, []);
  return (
    <div>
      <h2>Audit Log</h2>
      <div className="card">
        <table>
          <thead><tr><th>Time (UTC)</th><th>Actor</th><th>Role</th><th>Action</th><th>Target</th><th>Detail</th><th>OK</th></tr></thead>
          <tbody>
            {rows.map((a, i) => (
              <tr key={i}>
                <td className="muted">{new Date(a.ts * 1000).toISOString().replace("T", " ").slice(0, 19)}</td>
                <td>{a.actor}</td><td>{roleLabel(a.actorRole)}</td><td>{a.action}</td>
                <td>{a.target}</td><td className="muted">{a.detail}</td>
                <td className={a.success ? "ok" : "err"}>{a.success ? "✓" : "✗"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
