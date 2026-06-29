import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";

export default function Login() {
  const { setMe, reload } = useAuth();
  const navigate = useNavigate();
  const [u, setU] = useState("");
  const [p, setP] = useState("");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setErr("");
    try {
      setMe(await api.login(u, p));
      await reload();
      navigate("/", { replace: true });
    } catch {
      setErr("Invalid username or password");
      setBusy(false);
    }
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
