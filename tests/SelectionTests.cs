using System;
using HsReconnectLite;

class SelectionTests {
    static Connection Row(uint pid, string ip, int port, uint state) {
        return new Connection { ProcessId = pid, RemoteIp = ip, RemotePort = port, State = state };
    }
    static int failures;
    static void Check(string name, Connection expected, Connection actual) {
        if (Object.ReferenceEquals(expected, actual)) Console.WriteLine("PASS " + name);
        else { failures++; Console.WriteLine("FAIL " + name); }
    }
    static int Main() {
        Connection live = Row(42, "101.37.232.223", 3724, 5);
        Connection login = Row(42, "118.31.18.157", 1119, 5);
        Connection loopback = Row(42, "127.0.0.1", 4015, 5);
        Check("stale log must not prevent current match reconnect", live,
            ConnectionSelector.Select(new[] { login, loopback, live }, 42, "47.97.90.202", 3724));
        Check("no log required for unique live match", live,
            ConnectionSelector.Select(new[] { live }, 42, null, 0));
        Check("other process never targeted", null,
            ConnectionSelector.Select(new[] { Row(99, "1.2.3.4", 3724, 5) }, 42, null, 0));
        Check("login and internal connections never guessed", null,
            ConnectionSelector.Select(new[] { login, loopback }, 42, null, 0));
        Connection second = Row(42, "47.97.183.171", 3724, 5);
        Check("ambiguous match rejected", null,
            ConnectionSelector.Select(new[] { live, second }, 42, "old", 3724));
        Check("current log can disambiguate", second,
            ConnectionSelector.Select(new[] { live, second }, 42, second.RemoteIp, 3724));
        Check("stale logged login cannot override live match", live,
            ConnectionSelector.Select(new[] { login, live }, 42, login.RemoteIp, 1119));
        Check("not established rejected", null,
            ConnectionSelector.Select(new[] { Row(42, "1.2.3.4", 3724, 8) }, 42, "1.2.3.4", 3724));
        Connection custom = Row(42, "1.2.3.4", 5555, 5);
        Check("nonstandard endpoint requires exact log match", custom,
            ConnectionSelector.Select(new[] { custom }, 42, custom.RemoteIp, custom.RemotePort));
        Connection gameOn1119 = Row(42, "192.0.2.30", 1119, 5);
        Connection authOn1119 = Row(42, "192.0.2.31", 1119, 5);
        Check("1119 game endpoint explicitly logged by GotoGameServe is supported", gameOn1119,
            ConnectionSelector.Select(new[] { gameOn1119, authOn1119, loopback }, 42, gameOn1119.RemoteIp, 1119));
        Check("1119 endpoints never guessed without game-server evidence", null,
            ConnectionSelector.Select(new[] { gameOn1119, authOn1119 }, 42, null, 0));
        Check("stale 1119 game hint never falls back to other connection", null,
            ConnectionSelector.Select(new[] { authOn1119 }, 42, gameOn1119.RemoteIp, 1119));
        Check("duplicate exact 1119 endpoints rejected", null,
            ConnectionSelector.Select(new[] { gameOn1119, Row(42, gameOn1119.RemoteIp, 1119, 5) }, 42, gameOn1119.RemoteIp, 1119));
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
