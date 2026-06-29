import { useEffect, useRef, useState } from "react";
import StatusBar from "../components/StatusBar";

export default function Console() {
  const [lines, setLines] = useState<string[]>([]);
  const [live, setLive] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const stick = useRef(true);

  useEffect(() => {
    const es = new EventSource("/api/console/stream");
    es.onopen = () => setLive(true);
    es.onerror = () => setLive(false);
    es.onmessage = (e) => setLines((prev) => (prev.length > 2000 ? prev.slice(-1500) : prev).concat(e.data));
    return () => es.close();
  }, []);
  useEffect(() => { if (box.current && stick.current) box.current.scrollTop = box.current.scrollHeight; }, [lines]);
  const onScroll = () => {
    const b = box.current; if (!b) return;
    stick.current = b.scrollHeight - b.scrollTop - b.clientHeight < 40;
  };

  return (
    <div>
      <h2>Server Console</h2>
      <StatusBar />
      <div className="row" style={{ marginBottom: 8 }}>
        <span className={live ? "ok" : "muted"}>{live ? "● live" : "○ connecting…"}</span>
        <button className="btn" onClick={() => setLines([])}>Clear view</button>
      </div>
      <div className="console" ref={box} onScroll={onScroll}>
        {lines.length ? lines.join("\n") : <span className="muted">Waiting for console output…</span>}
      </div>
    </div>
  );
}
