export type Me = { userName: string; role: string; rank: number; isDeveloper: boolean };
export type Status = {
  state: string;
  desired: string;
  running: boolean;
  pid: number | null;
  params: string;
  commandsReady: boolean;
  restarts: number;
  lastExit: number | null;
  ruleset: string;
};
export type UserRow = { id: string; username: string; role: string; isActive: boolean; isDeveloper: boolean };
export type DirEntry = { name: string; isDir: boolean; size: number; mtime: number };
export type DirListing = { path: string; parent: string | null; gameDataRoot: string; entries: DirEntry[] };
export type FileRead = { path: string; content?: string; isBinary?: boolean; tooLarge?: boolean; size: number };
export type FileEditRow = {
  id: number; ts: number; actor: string; actorRole: string; path: string; action: string;
  isDirectory: boolean; previousExisted: boolean; newSize: number; reverted: boolean; canRevert: boolean;
};
export type Config = {
  configured: boolean;
  autoStart: boolean;
  launchParams: string | null;
  defaultLaunchParams: string;
  ruleset: string | null;
  defaultRuleset: string;
};
export type ServerPrefs = { path: string; content: string; exists: boolean };
export type AuditRow = {
  ts: number; actor: string; actorRole: string; action: string;
  target: string | null; detail: string | null; success: boolean;
};
export type Crash = {
  startedAt: number; crashedAt: number; exitCode: number | null;
  faultAddress: string | null; faultInstruction: string | null;
  module: string | null; launchParams: string | null; details: string | null;
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
  completeConfig: (autoStart: boolean, launchParams: string, ruleset: string) =>
    req<void>("/api/config/complete", { method: "POST", body: JSON.stringify({ autoStart, launchParams, ruleset }) }),
  setAutoStart: (enabled: boolean) =>
    req<void>("/api/config/auto-start", { method: "POST", body: JSON.stringify({ enabled }) }),
  setRuleset: (ruleset: string) =>
    req<{ ruleset: string }>("/api/config/ruleset", { method: "POST", body: JSON.stringify({ ruleset }) }),
  serverPrefs: (ruleset: string) =>
    req<ServerPrefs>(`/api/config/serverprefs?ruleset=${encodeURIComponent(ruleset)}`),

  users: () => req<UserRow[]>("/api/users/"),
  createUser: (username: string, password: string, role: string) =>
    req<void>("/api/users/", { method: "POST", body: JSON.stringify({ username, password, role }) }),
  setRole: (id: string, role: string) =>
    req<void>(`/api/users/${id}/role`, { method: "POST", body: JSON.stringify({ role }) }),
  setActive: (id: string, active: boolean) =>
    req<void>(`/api/users/${id}/active`, { method: "POST", body: JSON.stringify({ active }) }),
  resetPassword: (id: string, password: string) =>
    req<void>(`/api/users/${id}/password`, { method: "POST", body: JSON.stringify({ password }) }),
  setDeveloper: (id: string, enabled: boolean) =>
    req<void>(`/api/users/${id}/developer`, { method: "POST", body: JSON.stringify({ enabled }) }),
  deleteUser: (id: string) => req<void>(`/api/users/${id}`, { method: "DELETE" }),

  audit: () => req<AuditRow[]>("/api/audit"),
  crashes: () => req<Crash[]>("/api/crashes"),

  // ---- file browser / editor ----
  listDir: (path?: string) => req<DirListing>(`/api/files/list${path ? `?path=${encodeURIComponent(path)}` : ""}`),
  readFile: (path: string) => req<FileRead>(`/api/files/read?path=${encodeURIComponent(path)}`),
  saveFile: (path: string, content: string) =>
    req<{ saved: boolean; size: number }>("/api/files/save", { method: "POST", body: JSON.stringify({ path, content }) }),
  createPath: (path: string, isDir: boolean) =>
    req<void>("/api/files/create", { method: "POST", body: JSON.stringify({ path, isDir }) }),
  deletePath: (path: string) =>
    req<void>("/api/files/delete", { method: "POST", body: JSON.stringify({ path }) }),
  fileEdits: () => req<FileEditRow[]>("/api/files/edits/"),
  revertEdit: (id: number) => req<void>(`/api/files/edits/${id}/revert`, { method: "POST" }),
  uploadFiles: async (dir: string, files: FileList | File[]) => {
    const fd = new FormData();
    fd.append("path", dir);
    for (const f of Array.from(files)) fd.append("files", f);
    const res = await fetch("/api/files/upload", { method: "POST", credentials: "include", body: fd });
    if (!res.ok) {
      let msg = res.statusText;
      try { const j = await res.json(); if (j?.error) msg = j.error; } catch { /* ignore */ }
      throw new Error(msg);
    }
    return res.json() as Promise<{ uploaded: { name: string; size: number }[] }>;
  },
};

// WebSocket URL for the root container terminal (same-origin; cookie auth).
export const terminalWsUrl = () => {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  return `${proto}//${location.host}/api/terminal/ws`;
};
