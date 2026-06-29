export type Me = { userName: string; role: string; rank: number };
export type Status = {
  state: string;
  desired: string;
  running: boolean;
  pid: number | null;
  params: string;
  telnetConnected: boolean;
  restarts: number;
  lastExit: number | null;
};
export type UserRow = { id: string; username: string; role: string; isActive: boolean };
export type Config = {
  configured: boolean;
  autoStart: boolean;
  launchParams: string | null;
  defaultLaunchParams: string;
};
export type AuditRow = {
  ts: number; actor: string; actorRole: string; action: string;
  target: string | null; detail: string | null; success: boolean;
};

export const RANK: Record<string, number> = { User: 10, Admin: 20, SuperAdmin: 30, root: 40 };
export const ROLES = ["User", "Admin", "SuperAdmin", "root"];
export const roleLabel = (r: string) => (r === "root" ? "root" : r === "SuperAdmin" ? "Super Admin" : r);

async function req<T>(path: string, opts: RequestInit = {}): Promise<T> {
  const res = await fetch(path, {
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    ...opts,
  });
  if (!res.ok) {
    let msg = res.statusText;
    try { const j = await res.json(); if (j?.error) msg = j.error; } catch { /* ignore */ }
    throw new Error(msg);
  }
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  me: () => req<Me>("/api/account/me"),
  login: (username: string, password: string) =>
    req<Me>("/api/account/login", { method: "POST", body: JSON.stringify({ username, password }) }),
  logout: () => req<void>("/api/account/logout", { method: "POST" }),

  status: () => req<Status>("/api/server/status"),
  restart: () => req<void>("/api/server/restart", { method: "POST" }),
  start: () => req<void>("/api/server/start", { method: "POST" }),
  forceRestart: () => req<void>("/api/server/force-restart", { method: "POST" }),
  stop: () => req<void>("/api/server/stop", { method: "POST" }),
  command: (cmd: string) => req<void>("/api/server/command", { method: "POST", body: JSON.stringify({ cmd }) }),
  shutdownPanel: () => req<void>("/api/panel/shutdown", { method: "POST" }),

  config: () => req<Config>("/api/config/"),
  completeConfig: (autoStart: boolean, launchParams: string) =>
    req<void>("/api/config/complete", { method: "POST", body: JSON.stringify({ autoStart, launchParams }) }),
  setAutoStart: (enabled: boolean) =>
    req<void>("/api/config/auto-start", { method: "POST", body: JSON.stringify({ enabled }) }),

  users: () => req<UserRow[]>("/api/users/"),
  createUser: (username: string, password: string, role: string) =>
    req<void>("/api/users/", { method: "POST", body: JSON.stringify({ username, password, role }) }),
  setRole: (id: string, role: string) =>
    req<void>(`/api/users/${id}/role`, { method: "POST", body: JSON.stringify({ role }) }),
  setActive: (id: string, active: boolean) =>
    req<void>(`/api/users/${id}/active`, { method: "POST", body: JSON.stringify({ active }) }),
  resetPassword: (id: string, password: string) =>
    req<void>(`/api/users/${id}/password`, { method: "POST", body: JSON.stringify({ password }) }),
  deleteUser: (id: string) => req<void>(`/api/users/${id}`, { method: "DELETE" }),

  audit: () => req<AuditRow[]>("/api/audit"),
};
