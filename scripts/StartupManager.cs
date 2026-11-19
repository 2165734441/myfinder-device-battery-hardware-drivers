using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AtkBatteryTray {
    internal static class StartupManager {
        internal const string ShortcutName="MyFinder 设备电量.lnk";
        internal static string CurrentShortcutPath {get{return ShortcutPath;}}
        static string StartupFolder {get{return Environment.GetFolderPath(Environment.SpecialFolder.Startup);}}
        static string ShortcutPath {get{return Path.Combine(StartupFolder,ShortcutName);}}
        static string Executable {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AtkMouseBattery.exe");}}
        const string ApprovalKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        internal static bool IsEnabled() {
            try {
                if(!File.Exists(ShortcutPath)) return false;
                object shell=null,link=null;
                try {
                    var type=Type.GetTypeFromProgID("WScript.Shell");
                    if(type==null) return false;
                    shell=Activator.CreateInstance(type);
                    link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{ShortcutPath});
                    string target=Convert.ToString(link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));
                    string args=Convert.ToString(link.GetType().InvokeMember("Arguments",BindingFlags.GetProperty,null,link,null));
                    if(String.IsNullOrWhiteSpace(target)||!File.Exists(target)) return false;
                    if(!String.Equals(Path.GetFullPath(target),Path.GetFullPath(Executable),StringComparison.OrdinalIgnoreCase)) return false;
                    if(!String.IsNullOrEmpty(args)) return false;
                } finally {
                    if(link!=null&&Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                    if(shell!=null&&Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                }
                return !IsDisabledByWindows();
            } catch(Exception) {return false;}
        }

        internal static bool SetEnabled(bool enabled) {
            try {
                if(enabled) CreateShortcut(); else DeleteShortcut();
                ClearApproval();
                return enabled==IsEnabled();
            } catch(Exception) {return false;}
        }

        static void CreateShortcut() {
            Directory.CreateDirectory(StartupFolder);
            object shell=null,link=null;
            try {
                var type=Type.GetTypeFromProgID("WScript.Shell");
                if(type==null) throw new InvalidOperationException("系统不支持创建快捷方式。");
                shell=Activator.CreateInstance(type);
                link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{ShortcutPath});
                var t=link.GetType();
                t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Executable});
                t.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{""});
                t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(Executable)});
                t.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"MyFinder Device Battery - start quietly at login."});
                t.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{Executable+",0"});
                t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
            } finally {
                if(link!=null&&Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if(shell!=null&&Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        static void DeleteShortcut() {
            if(File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        }

        static bool IsDisabledByWindows() {
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(ApprovalKey)) {
                    if(key==null) return false;
                    object raw=key.GetValue(ShortcutName);
                    if(!(raw is byte[])) return false;
                    var bytes=(byte[])raw;
                    return bytes.Length>0&&(bytes[0]==3||bytes[0]==7);
                }
            } catch(Exception) {return false;}
        }

        static void ClearApproval() {
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(ApprovalKey,true)) {
                    if(key!=null) key.DeleteValue(ShortcutName,false);
                }
            } catch(Exception) {}
        }
    }
}
