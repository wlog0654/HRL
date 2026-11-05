using System;
using System.Collections.Generic;
using System.Linq;

namespace HsReconnectLite {
    public sealed class Connection {
        public uint State, LocalAddress, LocalPortRaw, RemoteAddress, RemotePortRaw, ProcessId;
        public string RemoteIp;
        public int RemotePort;
        public string LocalIp;
        public int LocalPort;
        public override string ToString() { return "PID " + ProcessId + " " + LocalIp + ":" + LocalPort + " -> " + RemoteIp + ":" + RemotePort; }
    }
    public static class ConnectionSelector {
        public static Connection Select(IEnumerable<Connection> rows, uint pid, string logIp, int logPort) {
            var active = rows.Where(r => r.ProcessId == pid && r.State == 5).ToList();
            var matches = active.Where(r => r.RemotePort == 3724).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) {
                var exact = matches.Where(r => r.RemoteIp == logIp && r.RemotePort == logPort).ToList();
                return exact.Count == 1 ? exact[0] : null;
            }
            // The hint comes specifically from Network.GotoGameServe, not login logs.
            // Game servers can also use 1119. Require an exact, unique live match;
            // never pick another 1119 connection when the hint is stale or absent.
            var logged = active.Where(r => r.RemoteIp == logIp && r.RemotePort == logPort).ToList();
            return logged.Count == 1 ? logged[0] : null;
        }
    }
}
