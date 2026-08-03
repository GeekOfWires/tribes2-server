import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api, type Config, type Me } from "./api";

type AuthState = {
  me: Me | null;
  cfg: Config | null;
  loading: boolean;
  setMe: (m: Me | null) => void;
  reload: () => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthCtx = createContext<AuthState | null>(null);

export function useAuth(): AuthState {
  const v = useContext(AuthCtx);
  if (!v) throw new Error("useAuth must be used within <AuthProvider>");
  return v;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [cfg, setCfg] = useState<Config | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    try {
      setMe(await api.me());
      setCfg(await api.config().catch(() => null));
    } catch {
      setMe(null);
      setCfg(null);
    }
  }, []);

  useEffect(() => { reload().finally(() => setLoading(false)); }, [reload]);

  const signOut = useCallback(async () => {
    await api.logout().catch(() => {});
    setMe(null);
    setCfg(null);
  }, []);

  return (
    <AuthCtx.Provider value={{ me, cfg, loading, setMe, reload, signOut }}>
      {children}
    </AuthCtx.Provider>
  );
}
