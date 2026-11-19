using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace AtkBatteryTray {
    /// <summary>
    /// 雷柏 VT7 V2（Nordic 54 系列方案）的 HID 控制通道。
    /// 命令写入 FF00/0x0E 集合的 Output Report 6（33 字节），
    /// 应答从 FF00/0x0F 集合的 Feature Report 8（33 字节）读取。
    /// 全是标准 HID 读写，不需要管理员权限，也不需要安装或启动厂商驱动。
    /// </summary>
    internal sealed class RapooHid : IDisposable {
        public const int VendorId = 0x24AE;
        const int ReportSize = 33;
        const byte OutputReportId = 6;
        const byte FeatureReportId = 8;
        const byte CmdRead = 0xA4;
        const byte CmdWrite = 0xA5;
        const int ConfigBase = 1536;
        const int ConfigStride = 1024;
        const int AddrReportRate = 640;
        const int AddrDpiXList = 648;
        const int AddrDpiEnableGear = 662;
        const int AddrDpiCurrentGear = 664;
        const int AddrDpiYList = 712;
        const int AddrConfigCurrent = 268;

        IntPtr writeHandle;
        IntPtr readHandle;
        IntPtr inputHandle;
        byte connection;
        int productId;
        int configIndex;

        public int ProductId { get { return productId; } }
        public bool Wired { get { return connection == 0xFF; } }
        public int ConfigIndex { get { return configIndex; } }

        RapooHid(IntPtr writer, IntPtr reader, IntPtr input, byte conn, int pid, int cfg) {
            writeHandle = writer; readHandle = reader; inputHandle = input; connection = conn; productId = pid; configIndex = cfg;
        }

        public void Dispose() {
            if(writeHandle != IntPtr.Zero) { NativeMethods.CloseHandle(writeHandle); writeHandle = IntPtr.Zero; }
            if(readHandle != IntPtr.Zero) { NativeMethods.CloseHandle(readHandle); readHandle = IntPtr.Zero; }
            if(inputHandle != IntPtr.Zero) { NativeMethods.CloseHandle(inputHandle); inputHandle = IntPtr.Zero; }
        }

        /// <summary>打开当前插着的雷柏鼠标；找不到返回 null。</summary>
        public static RapooHid Open() {
            var found = NativeMethods.FindVendorCollections();
            NativeMethods.Collection writer = null, reader = null, input = null;
            int pid = 0; byte conn = 0;
            foreach(var item in found) {
                if(item.ProductId != 0x1460 && item.ProductId != 0x4660) continue;
                if(item.UsagePage != 0xFF00) continue;
                if(item.Usage == 0x0E && writer == null) { writer = item; pid = item.ProductId; }
                if(item.Usage == 0x0F && reader == null) { reader = item; }
                if(item.Usage == 0x02 && input == null) { input = item; }
            }
            if(writer == null || reader == null) return null;
            conn = pid == 0x4660 ? (byte)0xFF : (byte)0xA5;
            IntPtr wh = NativeMethods.OpenForIo(writer.Path);
            IntPtr rh = NativeMethods.OpenForIo(reader.Path);
            IntPtr ih = input == null ? IntPtr.Zero : NativeMethods.OpenOverlapped(input.Path);
            if(wh == IntPtr.Zero || rh == IntPtr.Zero) {
                if(wh != IntPtr.Zero) NativeMethods.CloseHandle(wh);
                if(rh != IntPtr.Zero) NativeMethods.CloseHandle(rh);
                if(ih != IntPtr.Zero) NativeMethods.CloseHandle(ih);
                return null;
            }
            var mouse = new RapooHid(wh, rh, ih, conn, pid, 0);
            try {
                int cfg = mouse.ReadByteAt(AddrConfigCurrent);
                if(cfg >= 0 && cfg < 8) mouse.configIndex = cfg;
            } catch { }
            return mouse;
        }

        int Base { get { return ConfigBase + configIndex * ConfigStride; } }

        bool Send(byte[] payload) {
            var buffer = new byte[ReportSize];
            buffer[0] = OutputReportId;
            Array.Copy(payload, 0, buffer, 1, Math.Min(payload.Length, ReportSize - 1));
            return NativeMethods.WriteReport(writeHandle, buffer, ReportSize);
        }

        byte[] ReadRaw(int address, int length) {
            var frame = new byte[] { connection, CmdRead, (byte)length, (byte)(address & 0xFF), (byte)((address >> 8) & 0xFF), (byte)((address >> 16) & 0xFF), (byte)((address >> 24) & 0xFF) };
            if(!Send(frame)) return null;
            for(int i = 0; i < 14; i++) {
                Thread.Sleep(i == 0 ? 6 : 10);
                var reply = NativeMethods.GetFeature(readHandle, FeatureReportId, ReportSize);
                if(reply == null || reply.Length < ReportSize) continue;
                if(reply[0] == 2) continue;   // busy
                if(reply[0] != 1) return null; // 设备拒绝
                return reply;
            }
            return null;
        }

        bool WriteRaw(int address, byte[] data) {
            var frame = new byte[7 + data.Length];
            frame[0] = connection;
            frame[1] = CmdWrite;
            frame[2] = (byte)data.Length;
            frame[3] = (byte)(address & 0xFF);
            frame[4] = (byte)((address >> 8) & 0xFF);
            frame[5] = (byte)((address >> 16) & 0xFF);
            frame[6] = (byte)((address >> 24) & 0xFF);
            Array.Copy(data, 0, frame, 7, data.Length);
            if(!Send(frame)) return false;
            for(int i = 0; i < 12; i++) {
                Thread.Sleep(i == 0 ? 6 : 10);
                var reply = NativeMethods.GetFeature(readHandle, FeatureReportId, ReportSize);
                if(reply == null || reply.Length < ReportSize) continue;
                if(reply[0] == 2) continue;
                return reply[0] == 1;
            }
            return false;
        }

        int ReadByteAt(int address) {
            var reply = ReadRaw(address, 1);
            if(reply == null) return -1;
            return reply[4];
        }

        public int ReadReportRateCode() {
            var reply = ReadRaw(Base + AddrReportRate, 1);
            return reply == null ? -1 : reply[4];
        }

        /// <summary>读取鼠标主动上报的电量（输入报告 07，2.4G 下约每 3 秒一次）。</summary>
        public bool TryReadBattery(out int percent, out bool charging) {
            return TryReadBattery(out percent, out charging, 900);
        }

        public bool TryReadBattery(out int percent, out bool charging, int timeoutMilliseconds) {
            percent = -1; charging = false;
            if(inputHandle == IntPtr.Zero) return false;
            var raw = NativeMethods.ReadInput(inputHandle, 19, timeoutMilliseconds);
            if(raw == null || raw.Length < 9 || raw[0] != 0x07) return false;
            int state = raw[7];
            int value = raw[8];
            if(value < 0 || value > 100) return false;
            percent = value;
            charging = state == 2 || state == 3;
            return true;
        }

        static readonly object probeGate = new object();
        static DateTime lastProbe = DateTime.MinValue;
        static bool probedPresent;
        static int probedPercent = -1;
        static bool probedCharging;

        /// <summary>带缓存的探测：20 秒内不重复打开设备，避免面板刷新时卡顿。</summary>
        public static bool ProbeBattery(out int percent, out bool charging) {
            lock(probeGate) {
                if((DateTime.UtcNow - lastProbe).TotalSeconds >= 20) {
                    lastProbe = DateTime.UtcNow;
                    probedPresent = false;
                    try {
                        using(var mouse = Open()) {
                            if(mouse != null) {
                                probedPresent = true;
                                int value; bool state;
                                if(mouse.TryReadBattery(out value, out state, 900)) {
                                    probedPercent = value; probedCharging = state;
                                }
                            }
                        }
                    } catch { probedPresent = false; }
                    if(!probedPresent) { probedPercent = -1; probedCharging = false; }
                }
                percent = probedPercent; charging = probedCharging;
                return probedPresent;
            }
        }

        public bool WriteReportRateCode(int code) {
            return WriteRaw(Base + AddrReportRate, new byte[] { (byte)code });
        }

        public int ReadCurrentGear() {
            var reply = ReadRaw(Base + AddrDpiCurrentGear, 1);
            return reply == null ? -1 : reply[4];
        }

        public bool WriteCurrentGear(int index) {
            return WriteRaw(Base + AddrDpiCurrentGear, new byte[] { (byte)index });
        }

        public int ReadEnabledGearCount() {
            var reply = ReadRaw(Base + AddrDpiEnableGear, 1);
            return reply == null ? -1 : reply[4] + 1;
        }

        public bool WriteEnabledGearCount(int count) {
            if(count < 1) count = 1; if(count > 7) count = 7;
            return WriteRaw(Base + AddrDpiEnableGear, new byte[] { (byte)(count - 1) });
        }

        public int[] ReadGearValues(bool yAxis) {
            int address = Base + (yAxis ? AddrDpiYList : AddrDpiXList);
            var reply = ReadRaw(address, 14);
            if(reply == null) return null;
            var values = new int[7];
            for(int i = 0; i < 7; i++) values[i] = reply[4 + i * 2] | (reply[5 + i * 2] << 8);
            return values;
        }

        public bool WriteGearValue(int gearIndex, int dpi, bool yAxis) {
            int address = Base + (yAxis ? AddrDpiYList : AddrDpiXList) + gearIndex * 2;
            return WriteRaw(address, new byte[] { (byte)(dpi & 0xFF), (byte)((dpi >> 8) & 0xFF) });
        }
    }

    internal static class NativeMethods {
        internal sealed class Collection {
            public string Path;
            public int UsagePage;
            public int Usage;
            public int ProductId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OVERLAPPED {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr handle, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr handle, ref IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int length);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr parent, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteFile(IntPtr handle, byte[] buffer, int length, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr handle, byte[] buffer, int length, out int read, ref OVERLAPPED overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr handle, ref OVERLAPPED overlapped, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(IntPtr handle, ref OVERLAPPED overlapped);
        [DllImport("kernel32.dll")]
        internal static extern bool CloseHandle(IntPtr handle);

        const uint DIGCF_PRESENT = 0x02;
        const uint DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1;
        const uint FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        internal static Collection[] FindVendorCollections() {
            var list = new List<Collection>();
            Guid hidGuid; HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if(set == IntPtr.Zero || set == new IntPtr(-1)) return list.ToArray();
            try {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                for(uint index = 0; ; index++) {
                    if(!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;
                    int required = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if(required <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                        if(!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        if(string.IsNullOrEmpty(path)) continue;
                        IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if(handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                        try {
                            var attributes = new HIDD_ATTRIBUTES();
                            attributes.Size = Marshal.SizeOf(attributes);
                            if(!HidD_GetAttributes(handle, ref attributes)) continue;
                            if(attributes.VendorID != (ushort)RapooHid.VendorId) continue;
                            IntPtr preparsed = IntPtr.Zero;
                            if(!HidD_GetPreparsedData(handle, ref preparsed)) continue;
                            try {
                                var caps = new HIDP_CAPS();
                                if(HidP_GetCaps(preparsed, ref caps) < 0) continue;
                                if(caps.UsagePage != 0xFF00) continue;
                                if(caps.Usage != 0x0E && caps.Usage != 0x0F && caps.Usage != 0x02) continue;
                                list.Add(new Collection { Path = path, UsagePage = caps.UsagePage, Usage = caps.Usage, ProductId = attributes.ProductID });
                            } finally { HidD_FreePreparsedData(preparsed); }
                        } finally { CloseHandle(handle); }
                    } finally { Marshal.FreeHGlobal(detail); }
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
            return list.ToArray();
        }

        internal static IntPtr OpenForIo(string path) {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            return (handle == new IntPtr(-1)) ? IntPtr.Zero : handle;
        }

        internal static IntPtr OpenOverlapped(string path) {
            IntPtr handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            return (handle == new IntPtr(-1)) ? IntPtr.Zero : handle;
        }

        internal static byte[] ReadInput(IntPtr handle, int length, int timeoutMilliseconds) {
            var buffer = new byte[length];
            var overlapped = new OVERLAPPED();
            overlapped.hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if(overlapped.hEvent == IntPtr.Zero) return null;
            try {
                int read = 0;
                bool pending = !ReadFile(handle, buffer, length, out read, ref overlapped);
                if(!pending) return read == length ? buffer : null;
                if(WaitForSingleObject(overlapped.hEvent, (uint)timeoutMilliseconds) != 0) {
                    CancelIoEx(handle, ref overlapped);
                    return null;
                }
                int transferred = 0;
                if(!GetOverlappedResult(handle, ref overlapped, out transferred, false)) return null;
                if(transferred != length) return null;
                return buffer;
            } finally {
                CloseHandle(overlapped.hEvent);
            }
        }

        internal static bool WriteReport(IntPtr handle, byte[] buffer, int length) {
            int written = 0;
            return WriteFile(handle, buffer, length, out written, IntPtr.Zero);
        }

        internal static byte[] GetFeature(IntPtr handle, byte reportId, int length) {
            var buffer = new byte[length];
            buffer[0] = reportId;
            if(!HidD_GetFeature(handle, buffer, length)) return null;
            return buffer;
        }
    }
}
