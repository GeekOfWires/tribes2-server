import { useState } from "react";
import { Navigate } from "react-router-dom";
import { api, RANK } from "../api";
import { useAuth } from "../auth";

// /setup route: first-run setup for root, "setup required" notice for everyone else.
export default function Setup() {
  const { me, cfg, reload, signOut } = useAuth();
  if (!me) return <Navigate to="/login" replace />;
  if (cfg?.configured) return <Navigate to="/" replace />;
  return me.rank >= RANK.root
    ? <FirstRunSetup onDone={reload} onLogout={signOut} />
    : <NotConfigured onLogout={signOut} />;
}

function NotConfigured({ onLogout }: { onLogout: () => void }) {
  return (
    <div className="login-wrap">
      <div className="card login" style={{ textAlign: "center" }}>
        <h2>Setup required</h2>
        <p className="muted">This server has not been configured yet. A <strong>root</strong> administrator must complete first-time setup before it can run.</p>
        <button className="btn" onClick={onLogout}>Sign out</button>
      </div>
    </div>
  );
}

function FirstRunSetup({ onDone, onLogout }: { onDone: () => Promise<void>; onLogout: () => void }) {
  const { cfg } = useAuth();
  const [params, setParams] = useState(cfg?.launchParams || cfg?.defaultLaunchParams || "-online -dedicated");
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
          <button type="button" className="btn" onClick={onLogout}>Sign out</button>
        </div>
      </form>
    </div>
  );
}
