using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AtkBatteryTray {
    /// <summary>
    /// 本机外设厂商软件的目录与入口。只负责发现/启动厂商自己的程序，
    /// 不冒充厂商驱动，也不向未经验证的硬件发送报文。
    /// </summary>
    internal sealed class PeripheralApp {
        internal string Id,Name,Category,Note,Url;
        internal string[] Keywords,KnownPaths;
        internal bool WebOnly;
        internal string Path;

        internal bool Installed {get{return !String.IsNullOrEmpty(Path);}}
        internal string Status {get{return WebOnly?"在线工具":Installed?"已安装":"未安装";}}
    }

    internal static class PeripheralCatalog {
        static List<PeripheralApp> apps;
        static List<string> shortcuts;
        static DateTime scannedAt=DateTime.MinValue;

        internal static List<PeripheralApp> All {get{Ensure();return apps;}}

        internal static void Refresh() {
            apps=null;shortcuts=null;scannedAt=DateTime.MinValue;Ensure();
        }

        internal static PeripheralApp Find(string id) {
            Ensure();
            foreach(var app in apps) if(String.Equals(app.Id,id,StringComparison.OrdinalIgnoreCase)) return app;
            return null;
        }

        internal static List<PeripheralApp> ByCategory(string category) {
            Ensure();
            var result=new List<PeripheralApp>();
            foreach(var app in apps) if(String.Equals(app.Category,category,StringComparison.OrdinalIgnoreCase)) result.Add(app);
            return result;
        }

        internal static bool Open(PeripheralApp app) {
            if(app==null) return false;
            if(app.Installed) return Start(app.Path);
            if(!String.IsNullOrEmpty(app.Url)) return Start(app.Url);
            return false;
        }

        static bool Start(string target) {
            try {
                var info=new ProcessStartInfo(target) {UseShellExecute=true};
                if(!target.StartsWith("http",StringComparison.OrdinalIgnoreCase)&&!target.StartsWith("ms-",StringComparison.OrdinalIgnoreCase)) {
                    string dir=System.IO.Path.GetDirectoryName(target);
                    if(!String.IsNullOrEmpty(dir)&&Directory.Exists(dir)) info.WorkingDirectory=dir;
                }
                Process.Start(info);
                return true;
            } catch(Exception) {return false;}
        }

        static void Ensure() {
            if(apps!=null&&(DateTime.UtcNow-scannedAt).TotalSeconds<8) return;
            var list=new List<PeripheralApp>();
            Add(list,"xinmeng","XINMENG Driver（新盟键盘）","键盘","新盟键盘的按键、灯效与宏设置。",
                null,new[]{"XINMENG","新盟"},new[]{"%USERPROFILE%\\Desktop\\XINMENG Driver.lnk","%PUBLIC%\\Desktop\\XINMENG Driver.lnk"},false);
            Add(list,"openrgb","OpenRGB（ARGB 灯光）","灯光","统一修改主板、内存、键盘、鼠标、机箱灯带的 ARGB 颜色与灯效。",
                "https://openrgb.org/",new[]{"OpenRGB"},new[]{"%USERPROFILE%\\Desktop\\OpenRGB - 快捷方式.lnk","%PUBLIC%\\Desktop\\OpenRGB.lnk","%ProgramFiles%\\OpenRGB\\OpenRGB.exe","%ProgramFiles(x86)%\\OpenRGB\\OpenRGB.exe"},false);
            Add(list,"betop","北通智控（手柄）","手柄","北通手柄的按键映射、连发、摇杆与体感设置。",
                "https://www.betop.com/",new[]{"北通智控","北通","BETOP"},new[]{"%PUBLIC%\\Desktop\\北通智控.lnk"},false);
            Add(list,"rapoo","雷柏 A HUB（鼠标）","鼠标","雷柏鼠标/键盘的 DPI、回报率、宏与灯效。",
                "https://hub.rapoo.cn/",new[]{"RAPOO AHUB","RAPOO A HUB","AHUB","雷柏"},new[]{"D:\\sbqd\\2\\RAPOO AHUB 2.0\\RAPOO AHUB 2.0.exe"},false);
            Add(list,"xiberia","XIBERIA Audio Center（耳机）","耳机","西伯利亚耳机的 7.1、均衡器与麦克风增强。",
                "https://www.xiberia.com/",new[]{"XIBERIA","西伯利亚"},new[]{"D:\\XIBERIA Audio Center X\\CPL\\XIBERIA Audio Center X.exe"},false);

            Add(list,"ghub","罗技 G HUB（鼠标/键盘）","鼠标","罗技 G 系列鼠标、键盘、耳机的 DPI、宏与灯效。",
                "https://www.logitechg.com/innovation/g-hub.html",new[]{"G HUB","LGHUB","Logitech G HUB"},
                new[]{"%ProgramFiles%\\LGHUB\\lghub.exe","%ProgramFiles%\\Logitech\\G HUB\\lghub.exe","%ProgramFiles(x86)%\\Logitech\\G HUB\\lghub.exe"},false);
            Add(list,"synapse","雷蛇 Synapse（鼠标/键盘）","鼠标","雷蛇设备的按键、宏、灯效与校准。",
                "https://www.razer.com/synapse-4",new[]{"Razer Synapse","雷蛇云"},
                new[]{"%ProgramFiles%\\Razer\\Synapse4\\Razer Synapse 4.exe","%ProgramFiles%\\Razer\\Synapse3\\WPFUI\\Razer Synapse 3.exe","%ProgramFiles(x86)%\\Razer\\Synapse3\\WPFUI\\Razer Synapse 3.exe"},false);
            Add(list,"steelseries","SteelSeries GG（耳机/鼠标）","耳机","SteelSeries 耳机、鼠标的 EQ、降噪、灯效与宏。",
                "https://steelseries.com/gg",new[]{"SteelSeries GG","SteelSeries"},
                new[]{"%ProgramFiles%\\SteelSeries\\SteelSeries GG\\SteelSeriesGG.exe","%ProgramFiles(x86)%\\SteelSeries\\SteelSeries GG\\SteelSeriesGG.exe"},false);
            Add(list,"icue","Corsair iCUE（键盘/鼠标/灯光）","键盘","海盗船键盘、鼠标、水冷与灯带的灯效和宏。",
                "https://www.corsair.com/icue",new[]{"iCUE","Corsair iCUE"},
                new[]{"%ProgramFiles%\\Corsair\\CORSAIR iCUE5 Software\\iCUE.exe","%ProgramFiles%\\Corsair\\CORSAIR iCUE4 Software\\iCUE.exe"},false);
            Add(list,"ngenuity","HyperX NGENUITY（耳机/键鼠）","耳机","HyperX 耳机、键盘、鼠标的 EQ、灯效与按键设置。",
                "https://hyperx.com/pages/ngenuity",new[]{"NGENUITY","HyperX"},null,false);

            Add(list,"armoury","ASUS Armoury Crate（主板/整机）","主板/整机","华硕主板、显卡、笔记本的灯效、风扇与性能模式。",
                "https://www.asus.com/campaign/armoury-crate/",new[]{"Armoury Crate","Armoury"},
                new[]{"%ProgramFiles%\\ASUS\\ArmouryDevice\\ArmouryCrate.exe","%ProgramFiles(x86)%\\ASUS\\ArmouryDevice\\ArmouryCrate.exe"},false);
            Add(list,"msicenter","MSI Center（主板/整机）","主板/整机","微星主板、显卡、笔记本的灯效、风扇与性能模式。",
                "https://www.msi.com/Landing/msi-center",new[]{"MSI Center"},
                new[]{"%ProgramFiles%\\MSI\\MSI Center\\MSI Center.exe","%ProgramFiles(x86)%\\MSI\\MSI Center\\MSI Center.exe"},false);
            Add(list,"gcc","GIGABYTE Control Center（主板/整机）","主板/整机","技嘉主板、显卡的灯效、风扇与性能设置。",
                "https://www.gigabyte.com/Consumer/Software/GIGABYTE-Control-Center/",new[]{"GIGABYTE Control Center","RGB Fusion","GIGABYTE"},
                new[]{"%ProgramFiles%\\GIGABYTE\\Control Center\\GCC.exe","%ProgramFiles(x86)%\\GIGABYTE\\Control Center\\GCC.exe"},false);
            Add(list,"polychrome","ASRock Polychrome RGB（主板）","主板/整机","华擎主板与灯带的 ARGB 灯效同步。",
                "https://www.asrock.com/mb/feature/polychrome/",new[]{"Polychrome","ASRock"},
                new[]{"%ProgramFiles%\\ASRock\\Polychrome RGB\\PolychromeRGB.exe","%ProgramFiles(x86)%\\ASRock\\Polychrome RGB\\PolychromeRGB.exe"},false);

            Add(list,"signalrgb","SignalRGB（ARGB 灯光）","灯光","跨品牌灯效同步、游戏联动与灯效模板。",
                "https://www.signalrgb.com/",new[]{"SignalRGB"},new[]{"%ProgramFiles%\\SignalRGB\\SignalRgb.exe","%ProgramFiles(x86)%\\SignalRGB\\SignalRgb.exe"},false);
            Add(list,"via","VIA（键盘改键）","键盘","浏览器里给 QMK/VIA 键盘改键、分层与旋钮功能。",
                "https://usevia.app/",new[]{"VIA"},null,true);
            Add(list,"qmk","QMK Toolbox（键盘刷机）","键盘","QMK 键盘固件刷写与调试。",
                "https://qmk.fm/toolbox/",new[]{"QMK Toolbox","QMK"},new[]{"%ProgramFiles%\\QMK Toolbox\\qmk_toolbox.exe"},false);
            Add(list,"keychron","Keychron Launcher（键盘）","键盘","Keychron 键盘的按键、分层与灯效网页配置。",
                "https://launcher.keychron.com/",new[]{"Keychron"},null,true);
            Add(list,"wootility","Wootility（Wooting 键盘）","键盘","Wooting 模拟轴、死区与 RGB 网页配置。",
                "https://wootility.io/",new[]{"Wootility","Wooting"},null,true);
            Add(list,"atk","ATK HUB（ATK/VXE 键鼠）","键盘","ATK / VXE 鼠标键盘的 DPI、回报率、宏与灯效。",
                "https://www.atk.pro/",new[]{"ATK HUB","ATK V HUB"},new[]{"%ProgramFiles%\\ATK\\ATK HUB\\ATK HUB.exe"},false);
            Add(list,"vgn","VGN / VXE HUB（键鼠）","键盘","VGN / VXE 键鼠的 DPI、回报率与灯效。",
                "https://www.vgnlab.com/",new[]{"VGN HUB","VXE HUB","VGN","VXE"},null,false);
            Add(list,"monsgeek","MonsGeek Driver（键盘）","键盘","魔极客键盘的按键、灯效与宏。",
                "https://www.monsgeek.com/pages/download",new[]{"MonsGeek","魔极客"},null,false);
            Add(list,"akko","Akko Cloud Driver（键盘）","键盘","Akko 键盘的按键、灯效与宏。",
                "https://www.akkogear.com/pages/download",new[]{"Akko"},null,false);
            Add(list,"dareu","达尔优驱动（键鼠）","键盘","达尔优键盘、鼠标的按键、DPI 与灯效。",
                "https://www.dareu.com/",new[]{"达尔优","Dareu"},null,false);
            Add(list,"ajazz","黑爵驱动（键鼠）","键盘","黑爵键盘、鼠标的按键、灯效与宏。",
                "https://www.ajazz.com/",new[]{"黑爵","Ajazz"},null,false);
            Add(list,"aula","狼蛛驱动（键鼠）","键盘","狼蛛键盘、鼠标的灯效、宏与按键。",
                "https://www.aulastar.com/",new[]{"狼蛛","AULA"},null,false);
            Add(list,"mchose","迈从驱动（键鼠）","键盘","迈从键盘、鼠标的按键、灯效与宏。",
                "https://www.mchose.com/",new[]{"迈从","MCHOSE"},null,false);

            Add(list,"flydigi","飞智空间站（手柄）","手柄","飞智手柄的按键映射、连发、体感与摇杆曲线。",
                "https://www.flydigi.com/",new[]{"飞智空间站","飞智","Flydigi"},null,false);
            Add(list,"8bitdo","8BitDo Ultimate Software（手柄）","手柄","八位堂手柄的按键、摇杆、震动与宏。",
                "https://www.8bitdo.com/software/",new[]{"8BitDo","八位堂"},null,false);
            Add(list,"gamesir","GameSir Nexus（手柄）","手柄","盖世小鸡手柄的按键、摇杆、震动与配置。",
                "https://www.gamesir.hk/",new[]{"GameSir Nexus","GameSir","盖世小鸡"},null,false);
            Add(list,"xbox","Xbox Accessories（Xbox 手柄）","手柄","微软官方 Xbox 手柄按键映射与固件更新。",
                "ms-windows-store://pdp/?productid=9NBLGGH30XJ3",new[]{"Xbox Accessories","Xbox 配件"},null,true);
            Add(list,"ds4windows","DS4Windows（PS 手柄）","手柄","把 PS4/PS5 手柄映射成 Xbox 手柄或键鼠。",
                "https://ds4-windows.com/",new[]{"DS4Windows","DS4"},new[]{"%ProgramFiles%\\DS4Windows\\DS4Windows.exe"},false);
            Add(list,"rewasd","reWASD（手柄映射）","手柄","手柄按键重映射、宏、体感与虚拟手柄。",
                "https://www.rewasd.com/",new[]{"reWASD"},new[]{"%ProgramFiles%\\reWASD\\reWASD.exe"},false);
            Add(list,"joytokey","JoyToKey（手柄转键鼠）","手柄","把游戏手柄按键映射成键盘和鼠标。",
                "https://joytokey.net/en/",new[]{"JoyToKey"},null,false);

            scannedAt=DateTime.UtcNow;
            BuildShortcutIndex();
            foreach(var app in list) if(!app.WebOnly) app.Path=Resolve(app);
            apps=list;
        }

        static void Add(List<PeripheralApp> list,string id,string name,string category,string note,string url,string[] keywords,string[] knownPaths,bool webOnly) {
            list.Add(new PeripheralApp {Id=id,Name=name,Category=category,Note=note,Url=url,Keywords=keywords,KnownPaths=knownPaths,WebOnly=webOnly});
        }

        static void BuildShortcutIndex() {
            shortcuts=new List<string>();
            var dirs=new[]{
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };
            foreach(var dir in dirs) {
                if(String.IsNullOrEmpty(dir)||!Directory.Exists(dir)) continue;
                bool recursive=String.Equals(dir,Environment.GetFolderPath(Environment.SpecialFolder.Programs),StringComparison.OrdinalIgnoreCase)||
                    String.Equals(dir,Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),StringComparison.OrdinalIgnoreCase);
                try {
                    foreach(var file in Directory.GetFiles(dir,"*.lnk",recursive?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly)) shortcuts.Add(file);
                } catch(Exception) {}
            }
        }

        static string Resolve(PeripheralApp app) {
            if(app.KnownPaths!=null) {
                foreach(var raw in app.KnownPaths) {
                    try {string path=Environment.ExpandEnvironmentVariables(raw);if(File.Exists(path))return path;} catch(Exception) {}
                }
            }
            if(app.Keywords==null||shortcuts==null) return null;
            foreach(var file in shortcuts) {
                string name=System.IO.Path.GetFileNameWithoutExtension(file);
                foreach(var keyword in app.Keywords) if(name.IndexOf(keyword,StringComparison.OrdinalIgnoreCase)>=0) return file;
            }
            return null;
        }
    }
}
