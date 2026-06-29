import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { RANK } from "./api";
import { AuthProvider, useAuth } from "./auth";
import DashboardLayout from "./components/DashboardLayout";
import RequireRole from "./components/RequireRole";
import RequireDev from "./components/RequireDev";
import Login from "./pages/Login";
import Setup from "./pages/Setup";
import Console from "./pages/Console";
import Controls from "./pages/Controls";
import Crashes from "./pages/Crashes";
import Users from "./pages/Users";
import Audit from "./pages/Audit";
import Files from "./pages/Files";
import Terminal from "./pages/Terminal";
import FileHistory from "./pages/FileHistory";

// Authenticated + configured shell; otherwise redirect to login / first-run setup.
function Protected() {
  const { me, cfg } = useAuth();
  if (!me) return <Navigate to="/login" replace />;
  if (cfg && !cfg.configured) return <Navigate to="/setup" replace />;
  return <DashboardLayout />;
}

function AppRoutes() {
  const { me, loading } = useAuth();
  if (loading) return <div className="login-wrap"><div className="muted">Loading…</div></div>;
  return (
    <Routes>
      <Route path="/login" element={me ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/setup" element={<Setup />} />
      <Route element={<Protected />}>
        <Route index element={<Console />} />
        <Route path="controls" element={<RequireRole rank={RANK.Admin}><Controls /></RequireRole>} />
        <Route path="crashes" element={<RequireRole rank={RANK.Admin}><Crashes /></RequireRole>} />
        <Route path="files" element={<RequireDev><Files /></RequireDev>} />
        <Route path="terminal" element={<RequireRole rank={RANK.root}><Terminal /></RequireRole>} />
        <Route path="file-history" element={<RequireRole rank={RANK.root}><FileHistory /></RequireRole>} />
        <Route path="users" element={<RequireRole rank={RANK.root}><Users /></RequireRole>} />
        <Route path="audit" element={<RequireRole rank={RANK.SuperAdmin}><Audit /></RequireRole>} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
