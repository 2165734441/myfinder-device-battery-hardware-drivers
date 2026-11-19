using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AtkBatteryTray {
    /// <summary>
    /// Windows 指针层控制。硬件 DPI 需要厂商协议，这里提供对所有鼠标都生效的
    /// 等效灵敏度（指针速度）与指针加速开关，并负责打开系统鼠标设置。
    /// </summary>
    internal static class SystemMouse {
        const uint SPI_GETMOUSESPEED=0x0070;
        const uint SPI_SETMOUSESPEED=0x0071;
        const uint SPI_GETMOUSE=0x0003;
        const uint SPI_SETMOUSE=0x0004;
        const uint SPIF_UPDATEINIFILE=0x01;
        const uint SPIF_SENDCHANGE=0x02;

        [DllImport("user32.dll", SetLastError=true)]
        static extern bool SystemParametersInfo(uint action,uint param,ref uint value,uint winIni);
        [DllImport("user32.dll", SetLastError=true)]
        static extern bool SystemParametersInfo(uint action,uint param,[MarshalAs(UnmanagedType.LPArray)] int[] value,uint winIni);

        internal static int Speed {
            get {
                uint value=10;
                try { if(SystemParametersInfo(SPI_GETMOUSESPEED,0,ref value,0)) return (int)value; } catch { }
                return 10;
            }
        }

        internal static bool SetSpeed(int speed) {
            speed=Math.Max(1,Math.Min(20,speed));
            try {
                uint value=(uint)speed;
                return SystemParametersInfo(SPI_SETMOUSESPEED,0,ref value,SPIF_UPDATEINIFILE|SPIF_SENDCHANGE);
            } catch { return false; }
        }

        internal static bool AccelerationEnabled {
            get {
                var values=new int[3];
                try { if(SystemParametersInfo(SPI_GETMOUSE,0,values,0)) return values[0]!=0||values[1]!=0; } catch { }
                return false;
            }
        }

        internal static bool SetAcceleration(bool enabled) {
            // 1/2/10 是 Windows 默认“提高指针精确度”曲线；全 0 表示关闭加速。
            var values=enabled?new int[]{1,2,10}:new int[]{0,0,0};
            try { return SystemParametersInfo(SPI_SETMOUSE,0,values,SPIF_UPDATEINIFILE|SPIF_SENDCHANGE); }
            catch { return false; }
        }

        internal static void OpenWindowsMouseSettings() {
            try { Process.Start(new ProcessStartInfo("ms-settings:mousetouchpad") {UseShellExecute=true}); }
            catch { }
        }
    }
}
