import { useEffect, useRef, useState } from "react";
import { Terminal as XTerm } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { terminalWsUrl } from "../api";

export default function Terminal() {
  const host = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState("connecting…");

  useEffect(() => {
    if (!host.current) return;
    const term = new XTerm({
      cursorBlink: true,
      fontSize: 13,
      fontFamily: 'ui-monospace, Consolas, "Cascadia Mono", monospace',
      theme: { background: "#1E1E1E", foreground: "#D4D4D4", cursor: "#AEAFAD" },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(host.current);
    fit.fit();

    const enc = new TextEncoder();
    const ws = new WebSocket(terminalWsUrl());
    ws.binaryType = "arraybuffer";

    const sendResize = () => ws.readyState === WebSocket.OPEN && ws.send(JSON.stringify({ r: [term.cols, term.rows] }));

    ws.onopen = () => { setStatus("connected"); sendResize(); term.focus(); };
    ws.onclose = () => { setStatus("disconnected"); term.write("\r\n\x1b[31m[session closed]\x1b[0m\r\n"); };
    ws.onerror = () => setStatus("error");
    ws.onmessage = (e) => term.write(new Uint8Array(e.data as ArrayBuffer));

    const dataSub = term.onData((d) => ws.readyState === WebSocket.OPEN && ws.send(enc.encode(d)));
    const resizeSub = term.onResize(sendResize);
    const ro = new ResizeObserver(() => { try { fit.fit(); } catch { /* not visible */ } });
    ro.observe(host.current);

    return () => {
      ro.disconnect(); dataSub.dispose(); resizeSub.dispose();
      try { ws.close(); } catch { /* ignore */ }
      term.dispose();
    };
  }, []);

  return (
    <div>
      <h2>Container Terminal <span className="muted" style={{ fontSize: 13 }}>({status})</span></h2>
      <p className="muted" style={{ marginTop: -6 }}>
        Interactive <code>bash</code> running as the panel user inside the container (root only). Sessions are audited.
      </p>
      <div className="card" style={{ padding: 8, background: "#1E1E1E" }}>
        <div ref={host} style={{ height: "70vh", width: "100%" }} />
      </div>
    </div>
  );
}
