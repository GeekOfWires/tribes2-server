import { NavLink, Outlet } from "react-router-dom";
import { RANK, roleLabel } from "../api";
import { useAuth } from "../auth";

const cls = ({ isActive }: { isActive: boolean }) => "navbtn" + (isActive ? " active" : "");

export default function DashboardLayout() {
  const { me, signOut } = useAuth();
  if (!me) return null;
  const can = (r: number) => me.rank >= r;
  return (
    <div className="shell">
      <nav className="sidebar">
        <div className="brand">Tribes <span>2</span></div>
        <NavLink to="/" end className={cls}>Console</NavLink>
        {can(RANK.Admin) && <NavLink to="/controls" className={cls}>Controls</NavLink>}
        {can(RANK.Admin) && <NavLink to="/crashes" className={cls}>Crashes</NavLink>}
        {can(RANK.root) && <NavLink to="/users" className={cls}>Users</NavLink>}
        {can(RANK.SuperAdmin) && <NavLink to="/audit" className={cls}>Audit Log</NavLink>}
        <div className="spacer" />
        <div className="muted" style={{ fontSize: 13 }}>{me.userName}</div>
        <div className="badge" style={{ marginBottom: 8 }}>{roleLabel(me.role)}</div>
        <button className="btn" onClick={signOut}>Sign out</button>
      </nav>
      <main className="main"><Outlet /></main>
    </div>
  );
}
