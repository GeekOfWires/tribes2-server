import { useCallback, useEffect, useState } from "react";
import { api, type FileEditRow } from "../api";

const fmt = (unix: number) => new Date(unix * 1000).toISOString().replace("T", " ").slice(0, 19);

export default function FileHistory() {
  const [rows, setRows] = useState<FileEditRow[]>([]);
  const [msg, setMsg] = useState<{ ok: boolean; t: string } | null>(null);
  const load = useCallback(() => { api.fileEdits().then(setRows).catch((e) => setMsg({ ok: false, t: (e as Error).message })); }, []);
  useEffect(() => { load(); }, [load]);

  const revert = async (r: FileEditRow) => {
    if (!window.confirm(`Revert ${r.action} of ${r.path} (by ${r.actor})?`)) return;
    try { await api.revertEdit(r.id); setMsg({ ok: true, t: `Reverted #${r.id}.` }); load(); }
    catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
  };

  return (
    <div>
      <h2>File Change History</h2>
      <p className="muted" style={{ marginTop: -6 }}>Every file change made through the panel. Root can revert to the pre-change state.</p>
      {msg && <p className={msg.ok ? "ok" : "err"}>{msg.t}</p>}
      <div className="card">
        <table>
          <thead><tr><th>Time (UTC)</th><th>Actor</th><th>Action</th><th>Path</th><th>Size</th><th></th></tr></thead>
          <tbody>
            {rows.length === 0 && <tr><td colSpan={6} className="muted">No file changes recorded.</td></tr>}
            {rows.map((r) => (
              <tr key={r.id} style={r.reverted ? { opacity: 0.55 } : undefined}>
                <td className="muted">{fmt(r.ts)}</td>
                <td>{r.actor}</td>
                <td>{r.action}{r.isDirectory ? " (dir)" : ""}</td>
                <td style={{ wordBreak: "break-all" }}>{r.path}</td>
                <td className="muted">{r.action === "delete" ? "—" : r.newSize}</td>
                <td>
                  {r.reverted ? <span className="muted">reverted</span>
                    : r.canRevert ? <button className="btn" onClick={() => revert(r)}>Revert</button>
                    : <span className="muted" title="snapshot unavailable">—</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
