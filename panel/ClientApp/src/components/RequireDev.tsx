import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { RANK } from "../api";
import { useAuth } from "../auth";

// Route guard for the Files feature: Developers (any base rank) or root.
export default function RequireDev({ children }: { children: ReactNode }) {
  const { me } = useAuth();
  if (!me || !(me.isDeveloper || me.rank >= RANK.root)) return <Navigate to="/" replace />;
  return <>{children}</>;
}
