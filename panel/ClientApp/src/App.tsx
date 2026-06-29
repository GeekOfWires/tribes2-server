import { useCallback, useEffect, useRef, useState } from "react";
import { api, RANK, ROLES, roleLabel, type Me, type Status, type UserRow, type AuditRow } from "./api";

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.me().then(setMe).catch(() => setMe(null)).finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="login-wrap"><div className="muted">Loading…</div></div>;
  if (!me) return <Login onLogin={setMe} />;
  return <Dashboard me={me} onLogout={() => setMe(null)} />;
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

type Tab = "console" | "controls" | "users" | "audit";

function Dashboard({ me, onLogout }: { me: Me; onLogout: () => void }) {
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
        {tab === "controls" && can(RANK.Admin) && <Controls me={me} />}
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
      <span className="muted">telnet: {s?.telnetConnected ? "connected" : "—"}</span>
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

function Controls({ me }: { me: Me }) {
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
