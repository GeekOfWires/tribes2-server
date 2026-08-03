import { useEffect, useState } from "react";
import { api, roleLabel, type AuditRow } from "../api";

export default function Audit() {
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
