using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HsReconnectLite;

class NativeTests {
    static int Main() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var client = new TcpClient()) {
                client.Connect(IPAddress.Loopback, port);
                using (var server = listener.AcceptTcpClient()) {
                    uint pid = (uint)Process.GetCurrentProcess().Id;
                    int clientPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;
                    var rows = NativeTcp.Read();
                    var outgoing = rows.SingleOrDefault(r => r.ProcessId == pid && r.State == 5 && r.LocalIp == "127.0.0.1" && r.LocalPort == clientPort && r.RemoteIp == "127.0.0.1" && r.RemotePort == port);
                    var incoming = rows.SingleOrDefault(r => r.ProcessId == pid && r.State == 5 && r.LocalPort == port && r.RemotePort == clientPort);
                    if (outgoing == null || incoming == null) throw new Exception("Loopback endpoints or PID/state decoded incorrectly.");
                    Console.WriteLine("PASS native TCP table: PID, state, addresses and ports match real sockets.");
                    // Optional local-only destructive test; never touches a game or external connection.
                    if (Environment.GetCommandLineArgs().Contains("--close-test")) {
                        NativeTcp.Close(outgoing);
                        Thread.Sleep(100);
                        if (NativeTcp.Read().Any(r => r.ProcessId == pid && r.State == 5 && r.LocalPort == clientPort && r.RemotePort == port))
                            throw new Exception("Test socket remained established after SetTcpEntry.");
                        Console.WriteLine("PASS SetTcpEntry closes only the test's own loopback socket.");
                    }
                }
            }
        } finally { listener.Stop(); }
        return 0;
    }
}
