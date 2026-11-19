using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AtkBattery;

namespace AtkBatteryTray {
    /// <summary>
    /// 设备电量卡片的右键控制菜单。按设备类型给出可用控制：
    /// 鼠标 -> 指针灵敏度/加速、硬件 DPI 档位（协议受支持时）、回报率说明；
    /// 耳机 -> Windows Sonic 7.1 虚拟环绕、变声器、麦克风降噪。
    /// 所有不可用的硬件项都会说明原因，不会发送未经验证的厂商报文。
    /// </summary>
    internal static class DeviceControlMenu {
        static ContextMenuStrip openMenu;

        internal static void Show(Form form,TrayContext owner,WirelessCard card,Point screenPoint,Action closed) {
            CloseOpen();
            var theme=new DialogTheme(true);
            var menu=new ContextMenuStrip();
            menu.ShowImageMargin=false;menu.ShowCheckMargin=true;menu.ShowItemToolTips=true;
            menu.BackColor=theme.Card;menu.ForeColor=theme.Foreground;
            menu.Font=new Font("Microsoft YaHei UI",VisualDesign.BodyFontSize,FontStyle.Regular,GraphicsUnit.Pixel);
            menu.Padding=new Padding(4,6,4,6);
            menu.Renderer=new MenuRenderer(theme);
            menu.Opened+=delegate{ClampMenu(menu,screenPoint);};
            menu.Closed+=delegate{openMenu=null;if(closed!=null)closed();};

            AddHeader(menu,card!=null?card.Name:"设备控制",theme);
            bool isMouse=card!=null&&(card.Kind=="mouse"||card.NativeMouse);
            bool isHeadset=card!=null&&(card.Kind=="headset"||card.NativeHeadset||card.AudioOnly);
            if(card==null) {
                BuildMouse(menu,owner,new WirelessCard {Id="synthetic:mouse",Name="鼠标",Kind="mouse",NativeMouse=owner.NativeMousePresent,Dpi=owner.Dpi},theme,form);
                AddSeparator(menu,theme);
                BuildHeadset(menu,owner,new WirelessCard {Id="synthetic:headset",Name="耳机",Kind="headset",AudioOnly=true},theme,form,closed);
            }
            else if(isMouse)BuildMouse(menu,owner,card,theme,form);
            else if(isHeadset)BuildHeadset(menu,owner,card,theme,form,closed);
            else BuildGeneric(menu,owner,card,theme,form,closed);

            AddSeparator(menu,theme);
            var hub=new ToolStripMenuItem("外设驱动中心（键盘 / 灯光 / 手柄 / 鼠标）…");
            hub.Click+=delegate{PeripheralHubForm.ShowHub(form);};
            MenuColors(hub,theme);menu.Items.Add(hub);

            var openrgb=PeripheralCatalog.Find("openrgb");
            var lighting=new ToolStripMenuItem("ARGB 灯光（OpenRGB）…");
            lighting.Enabled=openrgb!=null&&(openrgb.Installed||!String.IsNullOrEmpty(openrgb.Url));
            lighting.ToolTipText=openrgb==null?"":(openrgb.Installed?"打开 OpenRGB 修改键盘、鼠标、主板与灯带的颜色。":"本机未安装，点击打开 OpenRGB 官网。");
            lighting.Click+=delegate{if(!PeripheralCatalog.Open(openrgb))ShowNotice(form,"ARGB 灯光","未能打开 OpenRGB，请先安装或从官网下载。");};
            MenuColors(lighting,theme);menu.Items.Add(lighting);

            AddSeparator(menu,theme);
            var refresh=new ToolStripMenuItem("刷新读数");
            refresh.Click+=delegate{owner.RefreshDevice(card==null?"all":card.NativeMouse?"mouse":card.NativeHeadset?"headset":"all");};
            MenuColors(refresh,theme);menu.Items.Add(refresh);

            if(card!=null&&card.Id!="empty"&&card.Id!="native:audio"&&!card.AudioOnly) {
                var hide=new ToolStripMenuItem("隐藏此设备");
                hide.Click+=delegate{HideDevice(owner,card.Id);if(closed!=null)closed();};
                MenuColors(hide,theme);menu.Items.Add(hide);
            }

            openMenu=menu;
            menu.Show(form,screenPoint);
        }

        static void BuildMouse(ContextMenuStrip menu,TrayContext owner,WirelessCard card,DialogTheme theme,Form form) {
            var group=new ToolStripMenuItem("指针灵敏度（等效 DPI）");
            MenuColors(group,theme);menu.Items.Add(group);

            int speed=SystemMouse.Speed;
            var presetGroup=new ToolStripMenuItem("档位");
            MenuColors(presetGroup,theme);group.DropDownItems.Add(presetGroup);
            int[] values={2,4,6,8,10,12,14,17,20};
            string[] labels={"very slow","","","","默认（10）","","","","very fast"};
            foreach(int value in values) {
                int target=value;
                var item=new ToolStripMenuItem("第 "+value+" 级"+(SpeedLabel(target)));
                item.Checked=speed==target;
                item.Click+=delegate{SystemMouse.SetSpeed(target);};
                MenuColors(item,theme);presetGroup.DropDownItems.Add(item);
            }

            var acceleration=new ToolStripMenuItem("提高指针精确度（鼠标加速）");
            acceleration.Checked=SystemMouse.AccelerationEnabled;
            acceleration.Click+=delegate{SystemMouse.SetAcceleration(!SystemMouse.AccelerationEnabled);};
            MenuColors(acceleration,theme);group.DropDownItems.Add(acceleration);

            var openSettings=new ToolStripMenuItem("打开 Windows 鼠标设置");
            openSettings.Click+=delegate{SystemMouse.OpenWindowsMouseSettings();};
            MenuColors(openSettings,theme);group.DropDownItems.Add(openSettings);

            // 硬件 DPI / 回报率：直接走雷柏 HID 协议，不经过厂商驱动界面。
            int rate=-1,gear=-1,enabled=-1;int[] gearX=null,gearY=null;
            using(var probe=RapooHid.Open()) {
                if(probe!=null) {
                    rate=probe.ReadReportRateCode();
                    gear=probe.ReadCurrentGear();
                    enabled=probe.ReadEnabledGearCount();
                    gearX=probe.ReadGearValues(false);
                    gearY=probe.ReadGearValues(true);
                }
            }
            bool hardware=gearX!=null&&gear>=0;

            var status=new ToolStripMenuItem(hardware
                ? "雷柏鼠标：当前 "+gearX[Math.Min(gear,gearX.Length-1)]+" DPI · "+RateLabel(rate)
                : "雷柏鼠标：未检测到（VT7 V2 请插好接收器或数据线）");
            status.Enabled=false;MenuColors(status,theme);menu.Items.Add(status);

            var stages=new ToolStripMenuItem("硬件 DPI 档位");
            MenuColors(stages,theme);menu.Items.Add(stages);
            if(hardware) {
                int total=enabled>0?Math.Min(enabled,7):7;
                for(int i=0;i<total;i++) {
                    int index=i;int dpi=gearX[Math.Min(i,gearX.Length-1)];
                    var item=new ToolStripMenuItem("第 "+(i+1)+" 档 · "+dpi+" DPI"+(i==gear?"（当前）":""));
                    item.Checked=i==gear;
                    item.Click+=delegate{RapooApply(delegate(RapooHid m){m.WriteCurrentGear(index);}, "切换 DPI 档位失败");};
                    MenuColors(item,theme);stages.DropDownItems.Add(item);
                }
                var edit=new ToolStripMenuItem("修改当前档位 DPI");
                MenuColors(edit,theme);stages.DropDownItems.Add(edit);
                int[] presets={400,800,1200,1600,2000,2400,3200,4000,6400,8000,12000,16000,24000,30000};
                foreach(int dpi in presets) {
                    int value=dpi;
                    var item=new ToolStripMenuItem(value+" DPI");
                    item.Click+=delegate{RapooApply(delegate(RapooHid m) {
                        int current=m.ReadCurrentGear();
                        if(current<0) current=0;
                        m.WriteGearValue(current,value,false);
                        m.WriteGearValue(current,value,true);
                    }, "写入 DPI 失败");};
                    MenuColors(item,theme);edit.DropDownItems.Add(item);
                }
            } else {
                var info=new ToolStripMenuItem("请连接雷柏 VT7 V2 后重试");
                info.Enabled=false;
                info.ToolTipText="本程序通过标准 HID 报文直接读写鼠标参数，不需要管理员权限，也不需要打开雷柏 A HUB。若仍检测不到，请重新插拔 2.4G 接收器。";
                MenuColors(info,theme);stages.DropDownItems.Add(info);
            }

            var dpiEditor=new ToolStripMenuItem("编辑 DPI 档位（自定义数值）…");
            dpiEditor.Enabled=hardware;
            dpiEditor.Click+=delegate{OpenDpiEditor();};
            MenuColors(dpiEditor,theme);menu.Items.Add(dpiEditor);

            var polling=new ToolStripMenuItem("回报率（轮询率）");
            MenuColors(polling,theme);menu.Items.Add(polling);
            if(hardware) {
                int[] codes={129,130,132,1,2,4,8};
                string[] rateLabels={"8000 Hz","4000 Hz","2000 Hz","1000 Hz","500 Hz","250 Hz","125 Hz"};
                for(int i=0;i<codes.Length;i++) {
                    int code=codes[i];
                    var item=new ToolStripMenuItem(rateLabels[i]);
                    item.Checked=rate==code;
                    item.Click+=delegate{RapooApply(delegate(RapooHid m){m.WriteReportRateCode(code);}, "写入回报率失败");};
                    MenuColors(item,theme);polling.DropDownItems.Add(item);
                }
                var note=new ToolStripMenuItem("修改后立即生效，无需打开驱动");
                note.Enabled=false;MenuColors(note,theme);polling.DropDownItems.Add(note);
            } else {
                var info=new ToolStripMenuItem("连接鼠标后可调 8000/4000/2000/1000/500/250/125 Hz");
                info.Enabled=false;MenuColors(info,theme);polling.DropDownItems.Add(info);
            }

            var macroGroup=new ToolStripMenuItem("鼠标宏 / 按键替换");
            MenuColors(macroGroup,theme);menu.Items.Add(macroGroup);

            var manageMacros=new ToolStripMenuItem("管理宏与按键替换…");
            manageMacros.Click+=delegate{OpenMacroManager();};
            MenuColors(manageMacros,theme);macroGroup.DropDownItems.Add(manageMacros);

            int enabledMacros=MacroStore.EnabledCount();
            var macroStatus=new ToolStripMenuItem("宏引擎："+(MacroEngine.Running?"运行中":"未运行")+" · 已启用 "+enabledMacros+" 个");
            macroStatus.Enabled=false;
            MenuColors(macroStatus,theme);macroGroup.DropDownItems.Add(macroStatus);

            var macros=MacroStore.All;
            if(macros.Count==0) {
                var empty=new ToolStripMenuItem("还没有宏，点“管理宏…”录制一个");
                empty.Enabled=false;MenuColors(empty,theme);macroGroup.DropDownItems.Add(empty);
            } else {
                foreach(var macro in macros) {
                    var entry=macro;
                    var item=new ToolStripMenuItem((String.IsNullOrEmpty(entry.Name)?"未命名":entry.Name)+"  →  "+MacroText.TriggerLabel(entry.Trigger));
                    item.Checked=entry.Enabled;
                    item.ToolTipText=entry.Steps!=null?("共 "+entry.Steps.Count+" 步。点击切换启用状态。"):"";
                    item.Click+=delegate{entry.Enabled=!entry.Enabled;MacroStore.Save();};
                    MenuColors(item,theme);macroGroup.DropDownItems.Add(item);
                }
            }

            var vendor=new ToolStripMenuItem(VendorApps.Rapoo!=null?"打开雷柏 A HUB（仅备用）":"雷柏 A HUB 未安装");
            vendor.Enabled=VendorApps.Rapoo!=null;
            vendor.Click+=delegate{OpenVendor(VendorApps.Rapoo,"https://hub.rapoo.cn/");};
            MenuColors(vendor,theme);menu.Items.Add(vendor);
        }

        static void OpenDpiEditor() {
            MacroEngine.Suspend();
            try {
                using(var dialog=new DpiEditorForm()) {
                    dialog.TopMost=true;
                    dialog.ShowDialog();
                }
            } finally {
                MacroEngine.Resume();
            }
        }

        static void OpenMacroManager() {
            MacroEngine.Suspend();
            try {
                using(var dialog=new MacroManagerForm()) {
                    dialog.TopMost=true;
                    dialog.ShowDialog();
                }
            } finally {
                MacroEngine.Resume();
            }
        }

        static string RateLabel(int code) {
            switch(code) {
                case 129:return"8000 Hz";
                case 130:return"4000 Hz";
                case 132:return"2000 Hz";
                case 1:return"1000 Hz";
                case 2:return"500 Hz";
                case 4:return"250 Hz";
                case 8:return"125 Hz";
                default:return code<0?"未知回报率":"回报率代码 "+code;
            }
        }

        static void RapooApply(Action<RapooHid> action,string failure) {
            try {
                using(var mouse=RapooHid.Open()) {
                    if(mouse==null) {
                        MessageBox.Show("未检测到雷柏鼠标。请确认 2.4G 接收器已插好，或改用数据线连接。","设备电量卡片",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        return;
                    }
                    action(mouse);
                }
            } catch(Exception ex) {
                MessageBox.Show(failure+"："+ex.Message,"设备电量卡片",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }

        static void BuildHeadset(ContextMenuStrip menu,TrayContext owner,WirelessCard card,DialogTheme theme,Form form,Action closed) {
            string endpoint=HeadsetEndpoint(owner);

            var surround=new ToolStripMenuItem("7.1 虚拟环绕（Windows Sonic）");
            var state=SpatialAudio.Query(endpoint);
            surround.Checked=state.Ok&&state.Enabled;
            surround.ToolTipText=state.Ok?"使用 Windows Sonic 为所有耳机提供虚拟 7.1 环绕，无需厂商驱动。":state.Error;
            surround.Click+=delegate{
                var result=SpatialAudio.Set(endpoint,!surround.Checked);
                if(!result.Ok) {
                    ShowNotice((Form)form,"7.1 虚拟环绕",result.Error);return;
                }
                surround.Checked=result.Enabled;
            };
            MenuColors(surround,theme);menu.Items.Add(surround);

            var voice=new ToolStripMenuItem("变声器");
            MenuColors(voice,theme);menu.Items.Add(voice);
            var engine=VoiceEffectEngine.Shared;
            bool running=engine.Running;
            foreach(VoiceEffectEngine.Preset preset in new[]{VoiceEffectEngine.Preset.Off,VoiceEffectEngine.Preset.Deep,VoiceEffectEngine.Preset.High,VoiceEffectEngine.Preset.Cartoon}) {
                var item=new ToolStripMenuItem(VoiceEffectEngine.PresetName(preset)+(preset==VoiceEffectEngine.Preset.Off?"（关闭变声，只做降噪）":""));
                item.Checked=running&&engine.CurrentPreset==preset;
                item.Click+=delegate{
                    bool ok=engine.Start(preset,engine.GateEnabled,engine.GateDb);
                    if(!ok){ShowNotice(form,"变声器",engine.Error);}
                };
                MenuColors(item,theme);voice.DropDownItems.Add(item);
            }
            var stop=new ToolStripMenuItem("关闭语音处理");
            stop.Enabled=running;
            stop.Click+=delegate{engine.Stop();};
            MenuColors(stop,theme);voice.DropDownItems.Add(stop);

            var noise=new ToolStripMenuItem("降噪（麦克风噪声门）");
            noise.Checked=engine.Running&&engine.GateEnabled;
            noise.Click+=delegate{
                bool next=!(engine.Running&&engine.GateEnabled);
                var preset=engine.Running?engine.CurrentPreset:VoiceEffectEngine.Preset.Off;
                bool ok=engine.Start(preset,next,engine.GateDb);
                if(!ok){ShowNotice(form,"降噪",engine.Error);return;}
                noise.Checked=next;
            };
            MenuColors(noise,theme);menu.Items.Add(noise);

            var status=new ToolStripMenuItem(VoiceStatus(engine));
            status.Enabled=false;
            MenuColors(status,theme);menu.Items.Add(status);

            var routing=new ToolStripMenuItem("语音处理输出设置…");
            routing.Click+=delegate{ShowRoutingInfo(owner,engine,form);};
            MenuColors(routing,theme);menu.Items.Add(routing);

            var vendorHead=new ToolStripMenuItem(VendorApps.Xiberia!=null?"打开 XIBERIA Audio Center（耳机驱动）":"未找到 XIBERIA Audio Center");
            vendorHead.Enabled=VendorApps.Xiberia!=null;
            vendorHead.Click+=delegate{OpenVendor(VendorApps.Xiberia,null);};
            MenuColors(vendorHead,theme);menu.Items.Add(vendorHead);

            var sound=new ToolStripMenuItem("打开 Windows 声音设置");
            sound.Click+=delegate{OpenUrl("ms-settings:sound");};
            MenuColors(sound,theme);menu.Items.Add(sound);
        }

        static void BuildGeneric(ContextMenuStrip menu,TrayContext owner,WirelessCard card,DialogTheme theme,Form form,Action closed) {
            var info=new ToolStripMenuItem("该设备暂无可调项");
            info.Enabled=false;MenuColors(info,theme);menu.Items.Add(info);
            if(card==null||card.Kind!="keyboard"&&card.Kind!="gamepad") return;
            string category=card.Kind=="keyboard"?"键盘":"手柄";
            var group=new ToolStripMenuItem(category=="键盘"?"键盘厂商驱动":"手柄厂商驱动");
            MenuColors(group,theme);menu.Items.Add(group);
            int installed=0;
            foreach(var app in PeripheralCatalog.ByCategory(category)) {
                if(!app.Installed) continue;
                var item=new ToolStripMenuItem(app.Name+"（已安装）");
                item.ToolTipText=app.Note;
                item.Click+=delegate{PeripheralCatalog.Open(app);};
                MenuColors(item,theme);group.DropDownItems.Add(item);installed++;
            }
            if(installed==0) {
                var none=new ToolStripMenuItem("未检测到已安装的"+category+"厂商驱动");
                none.Enabled=false;MenuColors(none,theme);group.DropDownItems.Add(none);
            }
            var more=new ToolStripMenuItem("更多"+category+"驱动 / 官网下载…");
            more.Click+=delegate{PeripheralHubForm.ShowHub(form);};
            MenuColors(more,theme);group.DropDownItems.Add(more);
        }

        static string VoiceStatus(VoiceEffectEngine engine) {
            if(!engine.Running)return "变声器/降噪：未运行";
            return "语音处理运行中 · "+(engine.GateEnabled?"降噪开":"降噪关")+" · "+engine.Destination;
        }

        static void ShowRoutingInfo(TrayContext owner,VoiceEffectEngine engine,Form form) {
            var lines=new List<string>();
            lines.Add("变声器与降噪会处理默认麦克风，并输出到虚拟声卡：");
            lines.Add("  "+(engine.Running?engine.Destination:"（未运行，启动后自动选择 Voicemeeter Input / CABLE Input）"));
            lines.Add("");
            lines.Add("让其他程序用上处理后的声音：");
            lines.Add("1. 在 Voicemeeter 中确认 Virtual Input 的 B1 已点亮；");
            lines.Add("2. 把游戏/语音软件的麦克风选为 Voicemeeter Out B1；");
            lines.Add("3. 或在系统声音设置里把默认麦克风改为 Voicemeeter Out B1。");
            lines.Add("");
            lines.Add("提示：延迟约 60–120 ms；关闭本程序会停止语音处理。");
            ShowNotice(null,"语音处理输出",String.Join("\r\n",lines.ToArray()));
        }

        static string SpeedLabel(int value) {
            switch(value){case 2:return "（很慢）";case 20:return "（很快）";case 10:return "（默认）";default:return "";}
        }

        static string DeviceName(WirelessCard card) {
            if(card.Device!=null&&!String.IsNullOrEmpty(card.Device.Name))return card.Device.Name;
            return String.IsNullOrEmpty(card.Name)?"当前设备":card.Name;
        }

        static string HeadsetEndpoint(TrayContext owner) {
            var audio=owner.Audio;
            if(audio!=null) {
                if(audio.ActiveOutput=="headset"&&!String.IsNullOrEmpty(audio.HeadsetId))return audio.HeadsetId;
                if(!String.IsNullOrEmpty(audio.HeadsetId))return audio.HeadsetId;
                if(!String.IsNullOrEmpty(audio.SpeakerId))return audio.SpeakerId;
            }
            return null;
        }

        static void HideDevice(TrayContext owner,string id) {
            var pref=DeviceSettings.Current;
            DeviceSettings.SetDeviceHidden(pref,id,true);
            try{DeviceSettings.SaveConfiguration(pref);}catch{}
            owner.ConfigurationChanged(false);
        }

        static void AddHeader(ContextMenuStrip menu,string name,DialogTheme theme) {
            var header=new ToolStripMenuItem(String.IsNullOrEmpty(name)?"设备":name);
            header.Enabled=false;
            var font=header.Font;
            header.Font=new Font(font.FontFamily,font.Size,FontStyle.Bold);
            header.ForeColor=theme.Foreground;
            menu.Items.Add(header);
        }

        static void AddSeparator(ContextMenuStrip menu,DialogTheme theme) {
            var separator=new ToolStripSeparator();
            separator.BackColor=theme.Border;
            menu.Items.Add(separator);
        }

        static void MenuColors(ToolStripMenuItem item,DialogTheme theme) {
            item.BackColor=theme.Card;
            item.ForeColor=theme.Foreground;
        }

        static void ShowNotice(Form owner,string title,string text) {
            ThemedMessage.Show(owner,title,text,MessageBoxButtons.OK);
        }

        static void OpenUrl(string url) {
            try{Process.Start(new ProcessStartInfo(url) {UseShellExecute=true});}catch{}
        }

        static void OpenVendor(string path,string fallbackUrl) {
            if(!String.IsNullOrEmpty(path)) {
                try{Process.Start(new ProcessStartInfo(path) {UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)});return;}catch{}
            }
            if(!String.IsNullOrEmpty(fallbackUrl))OpenUrl(fallbackUrl);
        }

        /// <summary>本机已安装的厂商驱动入口：先按已知路径找，再在桌面 / 开始菜单按名称匹配。</summary>
        internal static class VendorApps {
            static bool scanned;static string rapoo,xiberia;

            internal static string Rapoo{get{Scan();return rapoo;}}
            internal static string Xiberia{get{Scan();return xiberia;}}

            static void Scan() {
                if(scanned)return;scanned=true;
                rapoo=Resolve(new[]{"RAPOO AHUB","RAPOO A HUB","AHUB"},new[]{@"D:\sbqd\2\RAPOO AHUB 2.0\RAPOO AHUB 2.0.exe"});
                xiberia=Resolve(new[]{"XIBERIA Audio Center","XIBERIA"},new[]{@"D:\XIBERIA Audio Center X\CPL\XIBERIA Audio Center X.exe"});
            }

            static string Resolve(string[] keywords,string[] knownExe) {
                foreach(var exe in knownExe) {try{if(File.Exists(exe))return exe;}catch{}}
                foreach(var dir in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}) {
                    try{foreach(var file in Directory.GetFiles(dir,"*.lnk"))if(Matches(Path.GetFileNameWithoutExtension(file),keywords))return file;}catch{}
                }
                foreach(var dir in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)}) {
                    try{foreach(var file in Directory.GetFiles(dir,"*.lnk",SearchOption.AllDirectories))if(Matches(Path.GetFileNameWithoutExtension(file),keywords))return file;}catch{}
                }
                return null;
            }

            static bool Matches(string name,string[] keywords) {
                foreach(var keyword in keywords)if(name.IndexOf(keyword,StringComparison.OrdinalIgnoreCase)>=0)return true;
                return false;
            }
        }

        static void CloseOpen() {
            if(openMenu!=null) {
                try{openMenu.Close();}catch{}
                openMenu=null;
            }
        }

        static void ClampMenu(ContextMenuStrip menu,Point point) {
            Rectangle screen=Screen.FromPoint(point).WorkingArea;
            Size size=menu.Size;
            int x=point.X,y=point.Y;
            if(x+size.Width>screen.Right)x=Math.Max(screen.Left,screen.Right-size.Width);
            if(y+size.Height>screen.Bottom)y=Math.Max(screen.Top,point.Y-size.Height);
            menu.SetBounds(x,y,size.Width,size.Height);
        }

        sealed class MenuRenderer : ToolStripProfessionalRenderer {
            readonly DialogTheme Theme;
            internal MenuRenderer(DialogTheme theme):base(new MenuColorTable(theme)){Theme=theme;}
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Theme.Card);}
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
                using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
                if(!e.Item.Selected||!e.Item.Enabled){base.OnRenderMenuItemBackground(e);return;}
                var rect=new Rectangle(2,0,e.Item.Width-4,e.Item.Height);
                using(var brush=new SolidBrush(Theme.Selected))e.Graphics.FillRectangle(brush,rect);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){
                if(!e.Item.Enabled)e.TextColor=Theme.Muted;
                else if(e.Item.Selected)e.TextColor=Theme.SelectedForeground;
                else e.TextColor=Theme.Foreground;
                base.OnRenderItemText(e);
            }
        }

        sealed class MenuColorTable : ProfessionalColorTable {
            readonly DialogTheme Theme;
            internal MenuColorTable(DialogTheme theme){Theme=theme;}
            public override Color ToolStripDropDownBackground{get{return Theme.Card;}}
            public override Color MenuBorder{get{return Theme.Border;}}
            public override Color MenuItemBorder{get{return Theme.Selected;}}
            public override Color MenuItemSelected{get{return Theme.Selected;}}
            public override Color MenuItemSelectedGradientBegin{get{return Theme.Selected;}}
            public override Color MenuItemSelectedGradientEnd{get{return Theme.Selected;}}
            public override Color SeparatorDark{get{return Theme.Border;}}
            public override Color SeparatorLight{get{return Theme.Border;}}
            public override Color CheckBackground{get{return Theme.Accent;}}
            public override Color CheckPressedBackground{get{return Theme.Accent;}}
            public override Color CheckSelectedBackground{get{return Theme.Accent;}}
        }
    }
}

