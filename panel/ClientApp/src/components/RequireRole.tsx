import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "../auth";

// Route guard: redirect to the console if the user's rank is below `rank`.
export default function RequireRole({ rank, children }: { rank: number; children: ReactNode }) {
  const { me } = useAuth();
  if (!me || me.rank < rank) return <Navigate to="/" replace />;
  return <>{children}</>;
}
