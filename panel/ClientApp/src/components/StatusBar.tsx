import { useEffect, useState } from "react";
import { api, type Status } from "../api";

export default function StatusBar() {
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
