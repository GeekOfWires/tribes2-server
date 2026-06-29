import { useEffect, useState } from "react";
import { Navigate } from "react-router-dom";
import Editor from "@monaco-editor/react";
import { api, RANK } from "../api";
import { useAuth } from "../auth";
import { DARK_PLUS, langFor, setupMonaco } from "../monaco-setup";

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
  const [ruleset, setRuleset] = useState(cfg?.ruleset || cfg?.defaultRuleset || "base");
  const [installed, setInstalled] = useState<string[]>([]);
  const [autoStart, setAutoStart] = useState(true);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");

  useEffect(() => { api.rulesets().then(setInstalled).catch(() => {}); }, []);

  // serverprefs.cs editing for the chosen ruleset
  const [prefsPath, setPrefsPath] = useState<string | null>(null);
  const [prefs, setPrefs] = useState("");
  const [prefsMsg, setPrefsMsg] = useState("");

  const openPrefs = async () => {
    setPrefsMsg("");
    try { const r = await api.serverPrefs(ruleset); setPrefsPath(r.path); setPrefs(r.content); }
    catch (e) { setPrefsMsg((e as Error).message); }
  };
  const savePrefs = async () => {
    if (!prefsPath) return;
    try { await api.saveFile(prefsPath, prefs); setPrefsMsg("Saved serverprefs.cs (audited)."); }
    catch (e) { setPrefsMsg((e as Error).message); }
  };

  const complete = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setErr("");
    try { await api.completeConfig(autoStart, params, ruleset); await onDone(); }
    catch (e2) { setErr((e2 as Error).message); setBusy(false); }
  };

  return (
    <div style={{ maxWidth: 920, margin: "32px auto", padding: "0 16px" }}>
      <h2>First-time Server Setup</h2>
      <p className="muted">Configure the dedicated server. You can change these later from Controls.</p>

      <form className="card" onSubmit={complete}>
        <label style={{ display: "block", margin: "4px 0 4px", fontSize: 13, color: "var(--muted)" }}>Launch parameters</label>
        <input style={{ width: "100%" }} value={params} onChange={(e) => setParams(e.target.value)} />
        <p className="muted" style={{ fontSize: 12 }}>Order: <code>-online</code> (or <code>-nologin</code>) first, <code>-dedicated</code> last. The ruleset's <code>-mod</code> is inserted automatically.</p>

        <label style={{ display: "block", margin: "10px 0 4px", fontSize: 13, color: "var(--muted)" }}>Ruleset / mod</label>
        <input style={{ width: 280 }} list="rulesets" value={ruleset} onChange={(e) => setRuleset(e.target.value)} placeholder="base" />
        <datalist id="rulesets">{installed.map((r) => <option key={r} value={r} />)}</datalist>
        <p className="muted" style={{ fontSize: 12 }}>
          <code>base</code> or empty = no <code>-mod</code>. Otherwise sets <code>-mod {ruleset || "…"}</code>. Installed: <code>{installed.join(", ") || "…"}</code>; type a new name to add a newer ruleset (upload its files first). Image default: <code>{cfg?.defaultRuleset || "base"}</code>.
        </p>

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

      <div className="card">
        <div className="row" style={{ justifyContent: "space-between" }}>
          <h3 style={{ margin: 0 }}>serverprefs.cs</h3>
          <span className="row">
            <button className="btn" type="button" onClick={openPrefs}>Open for ruleset "{ruleset || "base"}"</button>
            {prefsPath && <button className="btn primary" type="button" onClick={savePrefs}>Save</button>}
          </span>
        </div>
        <p className="muted" style={{ fontSize: 12 }}>
          Edit the server preferences for the selected ruleset (<code>{ruleset || "base"}/prefs/serverprefs.cs</code>). Saved independently of the form above.
        </p>
        {prefsPath && (
          <>
            <div className="muted" style={{ fontSize: 12, marginBottom: 6, wordBreak: "break-all" }}>{prefsPath}</div>
            <Editor
              height="46vh"
              theme={DARK_PLUS}
              path={prefsPath}
              language={langFor(prefsPath)}
              value={prefs}
              onChange={(v) => setPrefs(v ?? "")}
              beforeMount={setupMonaco}
              options={{ fontSize: 13, minimap: { enabled: false }, tabSize: 3, scrollBeyondLastLine: false }}
            />
          </>
        )}
        {prefsMsg && <p className={prefsMsg.startsWith("Saved") ? "ok" : "err"}>{prefsMsg}</p>}
      </div>
    </div>
  );
}
