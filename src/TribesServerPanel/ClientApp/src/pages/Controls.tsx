import { useEffect, useState } from "react";
import { api, RANK } from "../api";
import { useAuth } from "../auth";
import StatusBar from "../components/StatusBar";

export default function Controls() {
  const { me, cfg, reload } = useAuth();
  const [msg, setMsg] = useState<{ ok: boolean; t: string } | null>(null);
  const [cmd, setCmd] = useState("");
  const [ruleset, setRuleset] = useState(cfg?.ruleset ?? cfg?.defaultRuleset ?? "base");
  const [installed, setInstalled] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => { api.rulesets().then(setInstalled).catch(() => {}); }, []);
  if (!me) return null;
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
    await reload();
  };
  const applyRuleset = async () => {
    await run(() => api.setRuleset(ruleset), `ruleset "${ruleset || "base"}"`);
    await reload();
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

      {can(RANK.root) && cfg && (
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Ruleset / Mod</h3>
          <p className="muted" style={{ marginTop: 0, fontSize: 13 }}>Sets <code>-mod</code> (empty or <code>base</code> = none). Takes effect on the next restart.</p>
          <div className="row">
            <input style={{ width: 240 }} list="rulesets-c" value={ruleset} onChange={(e) => setRuleset(e.target.value)} placeholder="base" disabled={busy} />
            <datalist id="rulesets-c">{installed.map((r) => <option key={r} value={r} />)}</datalist>
            <button className="btn primary" disabled={busy} onClick={applyRuleset}>Apply</button>
          </div>
          <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>Installed: <code>{installed.join(", ") || "…"}</code> — or type a newer ruleset you've uploaded.</p>
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
