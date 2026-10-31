using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace HsReconnectLite {
    public static class NativeTcp {
        [StructLayout(LayoutKind.Sequential)]
        struct OwnerRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, ProcessId; }
        [StructLayout(LayoutKind.Sequential)]
        struct DeleteRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort; }
        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
        [DllImport("iphlpapi.dll")]
        static extern uint SetTcpEntry(ref DeleteRow row);
        static int Port(uint raw) { return (ushort)IPAddress.NetworkToHostOrder(unchecked((short)(raw & 65535))); }
        public static List<Connection> Read() {
            int size = 0;
            uint code = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
            if (code != 122 && code != 0) throw new Win32Exception((int)code);
            for (int retry = 0; retry < 5; retry++) {
                int allocated = Math.Max(size, 4);
                IntPtr buffer = Marshal.AllocHGlobal(allocated);
                try {
                    code = GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0);
                    if (code == 122) continue;
                    if (code != 0) throw new Win32Exception((int)code);
                    int count = Marshal.ReadInt32(buffer);
                    int stride = Marshal.SizeOf(typeof(OwnerRow));
                    if (count < 0 || count > (allocated - 4) / stride) throw new InvalidOperationException("TCP table size mismatch.");
                    var result = new List<Connection>();
                    for (int i = 0; i < count; i++) {
                        var r = (OwnerRow)Marshal.PtrToStructure(IntPtr.Add(buffer, 4 + i * stride), typeof(OwnerRow));
                        result.Add(new Connection { State = r.State, ProcessId = r.ProcessId,
                            LocalAddress = r.LocalAddress, LocalPortRaw = r.LocalPort,
                            RemoteAddress = r.RemoteAddress, RemotePortRaw = r.RemotePort,
                            LocalIp = new IPAddress(r.LocalAddress).ToString(), LocalPort = Port(r.LocalPort),
                            RemoteIp = new IPAddress(r.RemoteAddress).ToString(), RemotePort = Port(r.RemotePort) });
                    }
                    return result;
                } finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException("TCP table changed repeatedly; please retry.");
        }
        public static void Close(Connection connection) {
            var row = new DeleteRow { State = 12, LocalAddress = connection.LocalAddress,
                LocalPort = connection.LocalPortRaw, RemoteAddress = connection.RemoteAddress,
                RemotePort = connection.RemotePortRaw };
            uint code = SetTcpEntry(ref row);
            if (code != 0) throw new Win32Exception((int)code);
        }
    }
}
