using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AtkBatteryTray {
    internal sealed class MacroManagerForm : Form {
        readonly ListBox list=new ListBox();
        readonly TextBox nameBox=new TextBox();
        readonly TextBox triggerBox=new TextBox();
        readonly CheckBox enabledBox=new CheckBox();
        readonly ListBox stepsBox=new ListBox();
        readonly Label status=new Label();
        readonly Timer countdown=new Timer();
        readonly Timer autoStop=new Timer();
        readonly List<MacroEvent> recorded=new List<MacroEvent>();
        MacroDefinition selected;
        bool loading;
        int countdownLeft;
        Form overlay;
        Label overlayLabel;

        internal MacroManagerForm() {
            Text="鼠标宏与按键替换";
            Font=new Font("Microsoft YaHei UI",9f,FontStyle.Regular,GraphicsUnit.Point);
            StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(780,540);
            MinimumSize=new Size(780,540);
            BackColor=Color.FromArgb(247,248,250);
            ShowInTaskbar=true;

            var title=new Label {Text="在这里录制宏，然后把鼠标侧键 / 中键 / 键盘键替换成这个宏。",AutoSize=true,Location=new Point(18,14),ForeColor=Color.FromArgb(60,62,68)};
            Controls.Add(title);

            list.Location=new Point(18,44);list.Size=new Size(230,420);
            list.IntegralHeight=false;
            list.SelectedIndexChanged+=delegate{SelectMacro();};
            Controls.Add(list);

            var nameLabel=new Label {Text="宏名称",AutoSize=true,Location=new Point(270,46)};
            Controls.Add(nameLabel);
            nameBox.Location=new Point(270,68);nameBox.Width=300;
            nameBox.TextChanged+=delegate{if(!loading&&selected!=null)selected.Name=nameBox.Text.Trim();};
            Controls.Add(nameBox);

            enabledBox.Text="启用此宏（按键替换生效）";enabledBox.AutoSize=true;enabledBox.Location=new Point(590,70);
            enabledBox.CheckedChanged+=delegate{if(!loading&&selected!=null)selected.Enabled=enabledBox.Checked;};
            Controls.Add(enabledBox);

            var triggerLabel=new Label {Text="触发键（按下它就会播放宏）",AutoSize=true,Location=new Point(270,104)};
            Controls.Add(triggerLabel);
            triggerBox.Location=new Point(270,126);triggerBox.Width=300;triggerBox.ReadOnly=true;
            Controls.Add(triggerBox);

            var capture=new Button {Text="捕获触发键…",Location=new Point(580,124),Width=110,Height=28};
            capture.Click+=delegate{CaptureTrigger();};
            Controls.Add(capture);

            var clearTrigger=new Button {Text="清除",Location=new Point(696,124),Width=60,Height=28};
            clearTrigger.Click+=delegate{if(selected!=null){selected.Trigger=null;triggerBox.Text="";RefreshList();}};
            Controls.Add(clearTrigger);

            var stepsLabel=new Label {Text="宏内容（按键顺序）",AutoSize=true,Location=new Point(270,164)};
            Controls.Add(stepsLabel);
            stepsBox.Location=new Point(270,186);stepsBox.Size=new Size(486,250);
            stepsBox.IntegralHeight=false;
            Controls.Add(stepsBox);

            var record=new Button {Text="录制宏（3 秒后开始）",Location=new Point(270,448),Width=170,Height=32};
            record.Click+=delegate{StartRecording();};
            Controls.Add(record);

            var test=new Button {Text="试运行",Location=new Point(450,448),Width=90,Height=32};
            test.Click+=delegate{if(selected!=null&&selected.Steps!=null&&selected.Steps.Count>0)MacroPlayer.Play(selected);else status.Text="这个宏还没有内容，请先录制。";};
            Controls.Add(test);

            var clearSteps=new Button {Text="清空内容",Location=new Point(550,448),Width=90,Height=32};
            clearSteps.Click+=delegate{if(selected!=null){selected.Steps.Clear();RefreshSteps();}};
            Controls.Add(clearSteps);

            var add=new Button {Text="新建宏",Location=new Point(18,474),Width=100,Height=30};
            add.Click+=delegate{AddMacro();};
            Controls.Add(add);

            var remove=new Button {Text="删除",Location=new Point(126,474),Width=80,Height=30};
            remove.Click+=delegate{DeleteMacro();};
            Controls.Add(remove);

            var save=new Button {Text="保存",Location=new Point(214,474),Width=80,Height=30};
            save.Click+=delegate{SaveCurrent();MacroStore.Save();status.Text="已保存。";status.ForeColor=Color.FromArgb(60,120,70);};
            Controls.Add(save);

            var close=new Button {Text="关闭",Location=new Point(302,474),Width=80,Height=30};
            close.Click+=delegate{Close();};
            Controls.Add(close);

            status.AutoSize=false;status.Location=new Point(270,490);status.Size=new Size(490,40);
            status.ForeColor=Color.FromArgb(70,72,78);
            status.Text="宏引擎："+(MacroEngine.Running?"运行中":"未运行")+(String.IsNullOrEmpty(MacroEngine.LastError)?"":"（"+MacroEngine.LastError+"）");
            Controls.Add(status);

            countdown.Interval=1000;countdown.Tick+=delegate{TickCountdown();};
            autoStop.Interval=1000;autoStop.Tick+=delegate{AutoStopTick();};

            RefreshList();
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            MacroEngine.Suspend();
            if(!MacroEngine.Running) {
                MacroEngine.Start();
                status.Text="宏引擎："+(MacroEngine.Running?"运行中":"未运行")+(String.IsNullOrEmpty(MacroEngine.LastError)?"":"（"+MacroEngine.LastError+"）");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            countdown.Stop();autoStop.Stop();
            if(MacroEngine.RecordingActive) MacroEngine.EndRecording();
            MacroEngine.CancelCapture();
            MacroEngine.Resume();
            SaveCurrent();
            MacroStore.Save();
            HideOverlay();
            if(overlay!=null&&!overlay.IsDisposed) overlay.Dispose();
            base.OnFormClosing(e);
        }

        void RefreshList() {
            loading=true;
            var macros=MacroStore.All;
            int keep=-1;
            if(selected!=null) {
                for(int i=0;i<macros.Count;i++) if(macros[i].Id==selected.Id) keep=i;
            }
            list.BeginUpdate();
            list.Items.Clear();
            foreach(var macro in macros) list.Items.Add((macro.Enabled?"● ":"○ ")+(String.IsNullOrEmpty(macro.Name)?"未命名":macro.Name)+"  →  "+MacroText.TriggerLabel(macro.Trigger));
            list.EndUpdate();
            if(list.Items.Count>0) {
                if(keep<0) keep=0;
                list.SelectedIndex=Math.Min(keep,list.Items.Count-1);
            } else {
                selected=null;ClearFields();
            }
            loading=false;
            if(list.SelectedIndex>=0) SelectMacro();
        }

        void SelectMacro() {
            if(loading) return;
            var macros=MacroStore.All;
            if(list.SelectedIndex<0||list.SelectedIndex>=macros.Count) return;
            SaveCurrent();
            selected=macros[list.SelectedIndex];
            loading=true;
            nameBox.Text=selected.Name;
            triggerBox.Text=MacroText.TriggerLabel(selected.Trigger);
            enabledBox.Checked=selected.Enabled;
            loading=false;
            RefreshSteps();
        }

        void RefreshSteps() {
            stepsBox.BeginUpdate();
            stepsBox.Items.Clear();
            if(selected!=null&&selected.Steps!=null) {
                int index=1;
                foreach(var step in selected.Steps) {
                    string prefix=step.DelayMs>0?"+"+step.DelayMs+"ms  ":"";
                    stepsBox.Items.Add(index+". "+prefix+MacroText.StepLabel(step));
                    index++;
                }
            }
            stepsBox.EndUpdate();
        }

        void ClearFields() {
            loading=true;
            nameBox.Text="";triggerBox.Text="";enabledBox.Checked=false;stepsBox.Items.Clear();loading=false;
        }

        void SaveCurrent() {
            if(selected==null||loading) return;
            selected.Name=nameBox.Text.Trim();
            selected.Enabled=enabledBox.Checked;
        }

        void AddMacro() {
            SaveCurrent();
            var macro=MacroStore.Add("宏 "+(MacroStore.All.Count+1));
            RefreshList();
            for(int i=0;i<list.Items.Count;i++) if(MacroStore.All[i].Id==macro.Id){list.SelectedIndex=i;break;}
            MacroStore.Save();
        }

        void DeleteMacro() {
            if(selected==null) return;
            if(MessageBox.Show("删除宏“"+selected.Name+"”？","鼠标宏",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            MacroStore.Remove(selected);
            selected=null;
            RefreshList();
            MacroStore.Save();
        }

        void CaptureTrigger() {
            if(selected==null) {status.Text="请先选择或新建一个宏。";return;}
            status.Text="请按一个键盘键或鼠标键（Esc 取消）…";
            status.ForeColor=Color.FromArgb(60,90,150);
            MacroEngine.CaptureNext(delegate(string trigger) {
                BeginInvoke((MethodInvoker)delegate {
                    if(trigger=="key:0x1B") {status.Text="已取消。";return;}
                    if(selected!=null) {
                        selected.Trigger=trigger;
                        triggerBox.Text=MacroText.TriggerLabel(trigger);
                        RefreshList();
                        status.Text="触发键已设为："+MacroText.TriggerLabel(trigger)+"，保存后生效。";
                        status.ForeColor=Color.FromArgb(60,120,70);
                    }
                });
            });
        }

        void StartRecording() {
            if(selected==null) {status.Text="请先选择或新建一个宏。";return;}
            SaveCurrent();
            recorded.Clear();
            countdownLeft=3;
            autoStopSeconds=0;
            ShowOverlay("3 秒后开始录制…\n按 Ctrl+Alt+F10 停止");
            countdown.Start();
        }

        void TickCountdown() {
            countdownLeft--;
            if(countdownLeft>0) {
                ShowOverlay(countdownLeft+" 秒后开始录制…\n按 Ctrl+Alt+F10 停止");
                return;
            }
            countdown.Stop();
            HideOverlay();
            status.Text="录制中… 按 Ctrl+Alt+F10 停止（最长 60 秒）";
            status.ForeColor=Color.FromArgb(170,90,30);
            MacroEngine.BeginRecording(delegate(MacroEvent evt) {
                if(recorded.Count<2000) recorded.Add(evt);
            },delegate {
                BeginInvoke((MethodInvoker)delegate{FinishRecording();});
            });
            autoStop.Start();
        }

        int autoStopSeconds;
        void AutoStopTick() {
            autoStopSeconds++;
            if(autoStopSeconds>=60&&MacroEngine.RecordingActive) {
                MacroEngine.EndRecording();
            }
        }

        void FinishRecording() {
            autoStop.Stop();autoStopSeconds=0;
            HideOverlay();
            if(selected==null) return;
            var steps=new List<MacroStep>();
            DateTime previous=recorded.Count>0?recorded[0].At:DateTime.UtcNow;
            bool first=true;
            foreach(var evt in recorded) {
                int delay=first?0:(int)Math.Min(3000,Math.Max(0,(evt.At-previous).TotalMilliseconds));
                first=false;previous=evt.At;
                steps.Add(new MacroStep {Kind=evt.Kind,Value=evt.Value,DelayMs=delay,Text=MacroText.StepLabel(new MacroStep {Kind=evt.Kind,Value=evt.Value})});
            }
            selected.Steps=steps;
            if(steps.Count>0) selected.Enabled=true;
            enabledBox.Checked=selected.Enabled;
            RefreshSteps();
            RefreshList();
            MacroStore.Save();
            status.Text=steps.Count==0?"没有录到任何按键，请重试。":"录制完成：共 "+steps.Count+" 步，已保存。";
            status.ForeColor=steps.Count==0?Color.FromArgb(170,60,60):Color.FromArgb(60,120,70);
        }

        void ShowOverlay(string text) {
            if(overlay==null||overlay.IsDisposed) {
                overlay=new Form {FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterScreen,TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(32,33,36),Size=new Size(420,90)};
                overlayLabel=new Label {Dock=DockStyle.Fill,ForeColor=Color.White,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Microsoft YaHei UI",10f,FontStyle.Regular,GraphicsUnit.Point)};
                overlay.Controls.Add(overlayLabel);
            }
            overlayLabel.Text=text;
            overlay.Show();
            overlay.BringToFront();
        }

        void HideOverlay() {
            if(overlay!=null&&!overlay.IsDisposed) overlay.Hide();
        }
    }
}
