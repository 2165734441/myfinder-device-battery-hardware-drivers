using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AtkBattery;

namespace AtkBatteryTray {
    [DataContract]
    internal sealed class MacroStep {
        [DataMember(Name="kind")] public string Kind;
        [DataMember(Name="value")] public int Value;
        [DataMember(Name="delay")] public int DelayMs;
        [DataMember(Name="text")] public string Text;
    }

    [DataContract]
    internal sealed class MacroDefinition {
        [DataMember(Name="id")] public string Id;
        [DataMember(Name="name")] public string Name;
        [DataMember(Name="trigger")] public string Trigger;
        [DataMember(Name="enabled")] public bool Enabled;
        [DataMember(Name="steps")] public List<MacroStep> Steps;
    }

    [DataContract]
    internal sealed class MacroFile {
        [DataMember(Name="schema")] public int Schema;
        [DataMember(Name="macros")] public List<MacroDefinition> Macros;
    }

    internal static class MacroStore {
        static readonly object Gate=new object();
        static List<MacroDefinition> macros;
        static string PathFile {
            get {return System.IO.Path.Combine(DeviceSettings.DataDirectory,"mouse-macros.json");}
        }

        internal static List<MacroDefinition> All {
            get {
                lock(Gate) {
                    if(macros==null) macros=Load();
                    return macros;
                }
            }
        }

        internal static List<MacroDefinition> Snapshot() {
            lock(Gate) {
                if(macros==null) macros=Load();
                var copy=new List<MacroDefinition>();
                foreach(var macro in macros) copy.Add(Clone(macro));
                return copy;
            }
        }

        static MacroDefinition Clone(MacroDefinition macro) {
            var copy=new MacroDefinition {Id=macro.Id,Name=macro.Name,Trigger=macro.Trigger,Enabled=macro.Enabled,Steps=new List<MacroStep>()};
            if(macro.Steps!=null) foreach(var step in macro.Steps) copy.Steps.Add(new MacroStep {Kind=step.Kind,Value=step.Value,DelayMs=step.DelayMs,Text=step.Text});
            return copy;
        }

        internal static MacroDefinition Add(string name) {
            lock(Gate) {
                if(macros==null) macros=Load();
                var macro=new MacroDefinition {Id=Guid.NewGuid().ToString("N"),Name=name,Trigger=null,Enabled=false,Steps=new List<MacroStep>()};
                macros.Add(macro);
                return macro;
            }
        }

        internal static void Remove(MacroDefinition macro) {
            lock(Gate) {
                if(macros==null) macros=Load();
                macros.Remove(macro);
            }
        }

        internal static void Reload() {
            lock(Gate) macros=null;
        }

        internal static void Save() {
            lock(Gate) {
                if(macros==null) macros=Load();
                try {
                    Directory.CreateDirectory(DeviceSettings.DataDirectory);
                    var file=new MacroFile {Schema=1,Macros=macros};
                    var serializer=new DataContractJsonSerializer(typeof(MacroFile));
                    string temporary=PathFile+".tmp";
                    using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)) serializer.WriteObject(stream,file);
                    if(File.Exists(PathFile)) File.Delete(PathFile);
                    File.Move(temporary,PathFile);
                } catch(Exception) {
                    SafeDiagnostic.Transition("mouse_macros","save_failed");
                }
            }
        }

        static List<MacroDefinition> Load() {
            var list=new List<MacroDefinition>();
            try {
                if(!File.Exists(PathFile)) return list;
                var serializer=new DataContractJsonSerializer(typeof(MacroFile));
                using(var stream=new FileStream(PathFile,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    var file=(MacroFile)serializer.ReadObject(stream);
                    if(file!=null&&file.Macros!=null) foreach(var macro in file.Macros) {
                        if(macro==null||String.IsNullOrEmpty(macro.Id)) continue;
                        if(macro.Steps==null) macro.Steps=new List<MacroStep>();
                        list.Add(macro);
                    }
                }
            } catch(Exception) {
                SafeDiagnostic.Transition("mouse_macros","load_failed");
            }
            return list;
        }

        internal static MacroDefinition FindByTrigger(string trigger) {
            if(String.IsNullOrEmpty(trigger)) return null;
            lock(Gate) {
                if(macros==null) macros=Load();
                foreach(var macro in macros) if(macro.Enabled&&String.Equals(macro.Trigger,trigger,StringComparison.Ordinal)) return macro;
            }
            return null;
        }

        internal static MacroDefinition FirstEnabled() {
            lock(Gate) {
                if(macros==null) macros=Load();
                foreach(var macro in macros) if(macro.Enabled&&!String.IsNullOrEmpty(macro.Trigger)) return macro;
            }
            return null;
        }

        internal static int EnabledCount() {
            int count=0;
            lock(Gate) {
                if(macros==null) macros=Load();
                foreach(var macro in macros) if(macro!=null&&macro.Enabled) count++;
            }
            return count;
        }
    }

    internal static class MacroText {
        internal static string KeyName(int vk) {
            if(vk>=0x30&&vk<=0x39) return ((char)vk).ToString();
            if(vk>=0x41&&vk<=0x5A) return ((char)vk).ToString();
            try {
                string name=((Keys)vk).ToString();
                if(name=="Return") return "Enter";
                if(name=="Escape") return "Esc";
                if(name=="Space") return "空格";
                if(name=="Back") return "退格";
                return name;
            } catch(Exception) { return "0x"+vk.ToString("X2"); }
        }

        internal static string TriggerLabel(string trigger) {
            if(String.IsNullOrEmpty(trigger)) return "未设置";
            if(trigger=="mouse:x1") return "鼠标侧键 1（后退）";
            if(trigger=="mouse:x2") return "鼠标侧键 2（前进）";
            if(trigger=="mouse:middle") return "鼠标中键";
            if(trigger=="mouse:left") return "鼠标左键";
            if(trigger=="mouse:right") return "鼠标右键";
            if(trigger.StartsWith("key:")) {
                int vk;
                if(Int32.TryParse(trigger.Substring(4),System.Globalization.NumberStyles.HexNumber,null,out vk)) return "按键 "+KeyName(vk);
                return trigger;
            }
            return trigger;
        }

        internal static string StepLabel(MacroStep step) {
            if(step==null) return "";
            if(step.Kind=="delay") return "等待 "+(step.DelayMs>0?step.DelayMs:0)+" ms";
            if(step.Kind=="keydown") return "按下 "+KeyName(step.Value);
            if(step.Kind=="keyup") return "松开 "+KeyName(step.Value);
            if(step.Kind=="mouseDown") return "鼠标 "+MouseName(step.Value)+" 按下";
            if(step.Kind=="mouseUp") return "鼠标 "+MouseName(step.Value)+" 松开";
            return step.Kind;
        }

        internal static string MouseName(int button) {
            switch(button) {
                case 1:return "左键";
                case 2:return "右键";
                case 3:return "中键";
                case 4:return "侧键 1";
                case 5:return "侧键 2";
                default:return "按键 "+button;
            }
        }
    }

    internal static class MacroPlayer {
        internal const long PlaybackMark=0x4D414352;   // "MACR"：只忽略本程序自己注入的按键，避免宏递归
        static readonly object Gate=new object();
        static bool playing;

        internal static bool IsPlaying {get{lock(Gate)return playing;}}

        internal static void Play(MacroDefinition macro) {
            if(macro==null) return;
            List<MacroStep> steps;
            lock(Gate) {
                if(playing) return;
                playing=true;
                steps=new List<MacroStep>();
                if(macro.Steps!=null) foreach(var step in macro.Steps) steps.Add(step);
            }
            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    foreach(var step in steps) {
                        if(step!=null) {
                            if(step.DelayMs>0) Thread.Sleep(Math.Min(step.DelayMs,3000));
                            Send(step);
                        }
                    }
                    Thread.Sleep(20);
                } catch(Exception) {
                    SafeDiagnostic.Transition("mouse_macros","play_failed");
                } finally {
                    lock(Gate) playing=false;
                }
            });
        }

        static void Send(MacroStep step) {
            if(step.Kind=="keydown"||step.Kind=="keyup") {
                var input=new INPUT();
                input.type=1;
                input.U.ki.wVk=(ushort)step.Value;
                input.U.ki.wScan=0;
                input.U.ki.dwFlags=step.Kind=="keyup"?2u:0u;
                input.U.ki.time=0;
                input.U.ki.dwExtraInfo=new IntPtr(PlaybackMark);
                SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)));
                return;
            }
            uint flag=0;uint data=0;
            switch(step.Kind) {
                case "mouseDown":
                    if(step.Value==1){flag=0x0002;}
                    else if(step.Value==2){flag=0x0008;}
                    else if(step.Value==3){flag=0x0020;}
                    else {flag=0x0080;data=(uint)(step.Value==5?2:1);}
                    break;
                case "mouseUp":
                    if(step.Value==1){flag=0x0004;}
                    else if(step.Value==2){flag=0x0010;}
                    else if(step.Value==3){flag=0x0040;}
                    else {flag=0x0100;data=(uint)(step.Value==5?2:1);}
                    break;
                default:return;
            }
            var mouse=new INPUT();
            mouse.type=0;
            mouse.U.mi.dx=0;mouse.U.mi.dy=0;mouse.U.mi.mouseData=data;
            mouse.U.mi.dwFlags=flag;mouse.U.mi.time=0;mouse.U.mi.dwExtraInfo=new IntPtr(PlaybackMark);
            SendInput(1,new[]{mouse},Marshal.SizeOf(typeof(INPUT)));
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll",SetLastError=true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }

    internal static class MacroEngine {
        const int WH_KEYBOARD_LL=13;
        const int WH_MOUSE_LL=14;
        const int WM_KEYDOWN=0x0100;
        const int WM_KEYUP=0x0101;
        const int WM_SYSKEYDOWN=0x0104;
        const int WM_SYSKEYUP=0x0105;
        const int WM_MBUTTONDOWN=0x0207;
        const int WM_MBUTTONUP=0x0208;
        const int WM_XBUTTONDOWN=0x020B;
        const int WM_XBUTTONUP=0x020C;
        const int WM_LBUTTONDOWN=0x0201;
        const int WM_LBUTTONUP=0x0202;
        const int WM_RBUTTONDOWN=0x0204;
        const int WM_RBUTTONUP=0x0205;

        static IntPtr keyboardHook=IntPtr.Zero;        static IntPtr mouseHook=IntPtr.Zero;
        static HookProc keyboardProc;
        static HookProc mouseProc;
        static readonly object Gate=new object();
        static volatile bool active;
        static volatile bool suspended;
        static volatile bool recording;
        static volatile bool capturing;
        static Action<MacroEvent> recordSink;
        static Action<string> captureSink;
        static Action recordingStopped;
        static DateTime recordingStarted;
        static Thread pollThread;
        static volatile bool pollStop;
        static List<MacroDefinition> pollMacros;
        static DateTime pollReload;
        static readonly Dictionary<string,DateTime> lastTriggered=new Dictionary<string,DateTime>();

        internal static bool Running {get{return active;}}
        internal static bool RecordingActive {get{return recording;}}
        internal static string LastError {get;private set;}

        internal static void Start() {
            lock(Gate) {
                if(active) return;
                try {
                    keyboardProc=KeyboardHook;
                    mouseProc=MouseHook;
                    keyboardHook=SetWindowsHookEx(WH_KEYBOARD_LL,keyboardProc,GetModuleHandle(null),0);
                    mouseHook=SetWindowsHookEx(WH_MOUSE_LL,mouseProc,GetModuleHandle(null),0);
                    if(keyboardHook==IntPtr.Zero||mouseHook==IntPtr.Zero) {
                        LastError="键盘/鼠标钩子安装失败";
                        StopUnlocked();
                        return;
                    }
                    active=true;
                    pollStop=false;pollMacros=null;pollReload=DateTime.MinValue;
                    pollThread=new Thread(PollLoop) {IsBackground=true,Name="MacroTriggerPoll"};
                    pollThread.Start();
                } catch(Exception error) {
                    LastError=error.Message;
                    StopUnlocked();
                }
            }
        }

        internal static void Stop() {
            lock(Gate) StopUnlocked();
        }

        static void StopUnlocked() {
            pollStop=true;
            if(pollThread!=null&&pollThread.IsAlive){try{pollThread.Join(200);}catch(Exception){}}
            pollThread=null;pollMacros=null;
            if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
            if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
            recording=false;capturing=false;recordSink=null;captureSink=null;recordingStopped=null;
            active=false;
        }

        internal static void Suspend() {suspended=true;}
        internal static void Resume() {suspended=false;}

        internal static void BeginRecording(Action<MacroEvent> sink,Action stopped) {
            lock(Gate) {
                if(!active) Start();
                recordSink=sink;recordingStopped=stopped;recordingStarted=DateTime.UtcNow;
                capturing=false;captureSink=null;recording=true;
            }
        }

        internal static void EndRecording() {
            bool wasRecording;
            Action stopped;
            lock(Gate) {
                wasRecording=recording;recording=false;recordSink=null;
                stopped=recordingStopped;recordingStopped=null;
            }
            if(wasRecording&&stopped!=null) stopped();
        }

        internal static void CaptureNext(Action<string> sink) {
            lock(Gate) {if(!active) Start();captureSink=sink;capturing=true;}
        }

        internal static void CancelCapture() {
            lock(Gate){capturing=false;captureSink=null;}
        }

        static IntPtr KeyboardHook(int code,IntPtr wParam,IntPtr lParam) {
            try {
                if(code>=0) {
                    int message=wParam.ToInt32();
                    bool down=message==WM_KEYDOWN||message==WM_SYSKEYDOWN;
                    if(message==WM_KEYDOWN||message==WM_KEYUP||message==WM_SYSKEYDOWN||message==WM_SYSKEYUP) {
                        var info=(KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam,typeof(KBDLLHOOKSTRUCT));
                        if(info.dwExtraInfo.ToInt64()==MacroPlayer.PlaybackMark) return CallNextHookEx(keyboardHook,code,wParam,lParam);
                        int vk=(int)info.vkCode;
                        if(recording) {
                            if(vk==0x79&&IsDown(0x11)&&IsDown(0x12)) {  // Ctrl+Alt+F10
                                EndRecording();
                                return (IntPtr)1;
                            }
                            var sink=recordSink;
                            if(sink!=null) sink(new MacroEvent {Kind=down?"keydown":"keyup",Value=vk,At=DateTime.UtcNow});
                            return CallNextHookEx(keyboardHook,code,wParam,lParam);
                        }
                        if(capturing) {
                            if(IsModifier(vk)) return CallNextHookEx(keyboardHook,code,wParam,lParam);
                            var sink=captureSink;
                            capturing=false;captureSink=null;
                            if(sink!=null) sink("key:0x"+vk.ToString("X2"));
                            return (IntPtr)1;
                        }
                        if(!suspended) {
                            var macro=MacroStore.FindByTrigger("key:0x"+vk.ToString("X2"));
                            if(macro!=null) {if(down)TryTrigger(macro,macro.Trigger);return (IntPtr)1;}
                        }
                    }
                }
            } catch(Exception) {}
            return CallNextHookEx(keyboardHook,code,wParam,lParam);
        }

        static IntPtr MouseHook(int code,IntPtr wParam,IntPtr lParam) {
            try {
                if(code>=0) {
                    int message=wParam.ToInt32();
                    string trigger=null;string kind=null;int button=0;
                    if(message==WM_XBUTTONDOWN||message==WM_XBUTTONUP) {
                        var info=(MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam,typeof(MSLLHOOKSTRUCT));
                        int index=(int)((info.mouseData>>16)&0xFFFF)==1?1:2;
                        button=index==1?4:5;
                        trigger="mouse:x"+index;
                        kind=message==WM_XBUTTONDOWN?"mouseDown":"mouseUp";
                    } else if(message==WM_MBUTTONDOWN||message==WM_MBUTTONUP) {
                        trigger="mouse:middle";button=3;
                        kind=message==WM_MBUTTONDOWN?"mouseDown":"mouseUp";
                    } else if(message==WM_LBUTTONDOWN||message==WM_LBUTTONUP) {
                        trigger="mouse:left";button=1;
                        kind=message==WM_LBUTTONDOWN?"mouseDown":"mouseUp";
                    } else if(message==WM_RBUTTONDOWN||message==WM_RBUTTONUP) {
                        trigger="mouse:right";button=2;
                        kind=message==WM_RBUTTONDOWN?"mouseDown":"mouseUp";
                    }
                    if(trigger==null) return CallNextHookEx(mouseHook,code,wParam,lParam);
                    var info2=(MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam,typeof(MSLLHOOKSTRUCT));
                    if(info2.dwExtraInfo.ToInt64()==MacroPlayer.PlaybackMark) return CallNextHookEx(mouseHook,code,wParam,lParam);
                    if(recording) {
                        var sink=recordSink;
                        if(sink!=null) sink(new MacroEvent {Kind=kind,Value=button,At=DateTime.UtcNow});
                        return CallNextHookEx(mouseHook,code,wParam,lParam);
                    }
                    if(capturing) {
                        var sink=captureSink;
                        capturing=false;captureSink=null;
                        if(sink!=null) sink(trigger);
                        return (IntPtr)1;
                    }
                    if(!suspended) {
                        var macro=MacroStore.FindByTrigger(trigger);
                        if(macro!=null) {if(kind=="mouseDown")TryTrigger(macro,trigger);return (IntPtr)1;}
                    }
                }
            } catch(Exception) {}
            return CallNextHookEx(mouseHook,code,wParam,lParam);
        }

        static int TriggerVk(string trigger) {
            if(String.IsNullOrEmpty(trigger)) return 0;
            if(trigger=="mouse:x1") return 0x05;
            if(trigger=="mouse:x2") return 0x06;
            if(trigger=="mouse:middle") return 0x04;
            if(trigger=="mouse:left") return 0x01;
            if(trigger=="mouse:right") return 0x02;
            if(trigger.StartsWith("key:")) {
                int vk;
                if(Int32.TryParse(trigger.Substring(4),System.Globalization.NumberStyles.HexNumber,null,out vk)) return vk;
            }
            return 0;
        }

        static void PollLoop() {
            var previous=new Dictionary<int,bool>();
            while(!pollStop) {
                try {
                    if(pollMacros==null||DateTime.UtcNow>=pollReload) {
                        pollMacros=MacroStore.Snapshot();
                        pollReload=DateTime.UtcNow.AddSeconds(1);
                    }
                    if(suspended||recording||capturing) {previous.Clear();Thread.Sleep(50);continue;}
                    foreach(var macro in pollMacros) {
                        if(macro==null||!macro.Enabled) continue;
                        int vk=TriggerVk(macro.Trigger);
                        if(vk<=0) continue;
                        bool down=(GetAsyncKeyState(vk)&0x8000)!=0;
                        bool was=false;previous.TryGetValue(vk,out was);
                        if(down&&!was) TryTrigger(macro,macro.Trigger);
                        previous[vk]=down;
                    }
                } catch(Exception) {}
                Thread.Sleep(15);
            }
        }

        internal static void TryTrigger(MacroDefinition macro,string trigger) {
            if(macro==null||!macro.Enabled||suspended||recording||capturing) return;
            lock(Gate) {
                DateTime last;
                if(lastTriggered.TryGetValue(trigger,out last)&&(DateTime.UtcNow-last).TotalMilliseconds<400) return;
                lastTriggered[trigger]=DateTime.UtcNow;
            }
            MacroPlayer.Play(macro);
        }

        static bool IsModifier(int vk) {
            return vk==0x10||vk==0x11||vk==0x12||vk==0x5B||vk==0x5C||vk==0xA0||vk==0xA1||vk==0xA2||vk==0xA3||vk==0xA4||vk==0xA5;
        }

        static bool IsDown(int vk) {return (GetAsyncKeyState(vk)&0x8000)!=0;}

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);

        [DllImport("user32.dll",SetLastError=true)]
        static extern IntPtr SetWindowsHookEx(int idHook,HookProc callback,IntPtr module,uint threadId);

        [DllImport("user32.dll",SetLastError=true)]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);

        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);
    }

    internal sealed class MacroEvent {
        internal string Kind;
        internal int Value;
        internal DateTime At;
    }
}
