import { Fragment, useEffect, useState } from "react";
import { api, type Crash } from "../api";

const fmtUtc = (unix: number) => (unix ? new Date(unix * 1000).toISOString().replace("T", " ").slice(0, 19) : "—");

export default function Crashes() {
  const [rows, setRows] = useState<Crash[]>([]);
  const [open, setOpen] = useState<number | null>(null);
  const [err, setErr] = useState("");
  useEffect(() => { api.crashes().then(setRows).catch((e) => setErr((e as Error).message)); }, []);

  return (
    <div>
      <h2>Crash Reports</h2>
      <p className="muted" style={{ marginTop: -6 }}>
        Read-only. Unexpected/unhandled game exits (access violations) for reporting against the container image.
      </p>
      {err && <p className="err">{err}</p>}
      <div className="card">
        <table>
          <thead>
            <tr><th>Server Start (UTC)</th><th>Crash (UTC)</th><th>Exit</th><th>Fault</th><th>Module</th><th>Instruction</th><th></th></tr>
          </thead>
          <tbody>
            {rows.length === 0 && <tr><td colSpan={7} className="muted">No crashes recorded.</td></tr>}
            {rows.map((c, i) => (
              <Fragment key={i}>
                <tr>
                  <td className="muted">{fmtUtc(c.startedAt)}</td>
                  <td>{fmtUtc(c.crashedAt)}</td>
                  <td>{c.exitCode ?? "—"}</td>
                  <td><code>{c.faultAddress ?? "—"}</code></td>
                  <td>{c.module ?? "—"}</td>
                  <td className="muted" style={{ maxWidth: 280, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{c.faultInstruction ?? "—"}</td>
                  <td><button className="btn" onClick={() => setOpen(open === i ? null : i)}>{open === i ? "Hide" : "Details"}</button></td>
                </tr>
                {open === i && (
                  <tr>
                    <td colSpan={7}>
                      <div className="muted" style={{ marginBottom: 6 }}>launch params: <code>{c.launchParams ?? "—"}</code></div>
                      <pre className="console" style={{ height: "auto", maxHeight: "40vh" }}>{c.details ?? "(no details)"}</pre>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
