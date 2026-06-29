import { useCallback, useEffect, useState } from "react";
import { api, ROLES, roleLabel, type UserRow } from "../api";

export default function Users() {
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
