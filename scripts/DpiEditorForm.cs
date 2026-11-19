using System;
using System.Drawing;
using System.Windows.Forms;
using AtkBattery;

namespace AtkBatteryTray {
    internal sealed class DpiEditorForm : Form {
        readonly TextBox[] xBoxes=new TextBox[7];
        readonly TextBox[] yBoxes=new TextBox[7];
        readonly NumericUpDown countBox=new NumericUpDown();
        readonly NumericUpDown currentBox=new NumericUpDown();
        readonly CheckBox sameBox=new CheckBox();
        readonly Label status=new Label();
        int[] loadedX;
        int[] loadedY;

        internal DpiEditorForm() {
            Text="编辑 DPI 档位（直接写入鼠标）";
            Font=new Font("Microsoft YaHei UI",9f,FontStyle.Regular,GraphicsUnit.Point);
            FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(560,470);
            BackColor=Color.FromArgb(247,248,250);

            var title=new Label {Text="这些档位会直接写进鼠标，之后用鼠标上的 DPI 键循环切换。",AutoSize=true,Location=new Point(20,14),ForeColor=Color.FromArgb(60,62,68)};
            Controls.Add(title);

            var countLabel=new Label {Text="启用档位数：",AutoSize=true,Location=new Point(20,48)};
            Controls.Add(countLabel);
            countBox.Minimum=1;countBox.Maximum=7;countBox.Value=7;countBox.Location=new Point(110,45);countBox.Width=60;
            countBox.ValueChanged+=delegate{ApplyEnabledState();};
            Controls.Add(countBox);

            var currentLabel=new Label {Text="当前档位：",AutoSize=true,Location=new Point(200,48)};
            Controls.Add(currentLabel);
            currentBox.Minimum=1;currentBox.Maximum=7;currentBox.Location=new Point(280,45);currentBox.Width=60;
            Controls.Add(currentBox);
            currentBox.ValueChanged+=delegate{};

            sameBox.Text="X / Y 使用相同 DPI";sameBox.Checked=true;sameBox.AutoSize=true;sameBox.Location=new Point(370,46);
            sameBox.CheckedChanged+=delegate{SyncSameAxis();ApplyEnabledState();};
            Controls.Add(sameBox);

            var headerX=new Label {Text="X 轴 DPI",AutoSize=true,Location=new Point(150,78),ForeColor=Color.FromArgb(110,112,118)};
            var headerY=new Label {Text="Y 轴 DPI",AutoSize=true,Location=new Point(300,78),ForeColor=Color.FromArgb(110,112,118)};
            Controls.Add(headerX);Controls.Add(headerY);

            for(int i=0;i<7;i++) {
                int y=102+i*34;
                var label=new Label {Text="第 "+(i+1)+" 档",AutoSize=true,Location=new Point(20,y+4)};
                Controls.Add(label);

                var x=new TextBox {Location=new Point(150,y),Width=110,TextAlign=HorizontalAlignment.Right};
                x.TextChanged+=delegate{if(sameBox.Checked){int index=Array.IndexOf(xBoxes,x);if(index>=0)yBoxes[index].Text=x.Text;}};
                xBoxes[i]=x;Controls.Add(x);

                var yy=new TextBox {Location=new Point(300,y),Width=110,TextAlign=HorizontalAlignment.Right};
                yBoxes[i]=yy;Controls.Add(yy);
            }

            var hint=new Label {Text="范围 100–30000，常用值：400 / 800 / 1200 / 1600 / 2000 / 2400 / 3200 / 4000 / 6400 / 8000",AutoSize=true,Location=new Point(20,352),ForeColor=Color.FromArgb(120,122,128)};
            Controls.Add(hint);

            status.AutoSize=false;status.Location=new Point(20,376);status.Size=new Size(520,40);status.ForeColor=Color.FromArgb(70,72,78);
            Controls.Add(status);

            var read=new Button {Text="重新读取鼠标",Location=new Point(20,424),Width=120,Height=30};
            read.Click+=delegate{LoadFromMouse(true);};
            Controls.Add(read);

            var write=new Button {Text="写入鼠标并验证",Location=new Point(300,424),Width=140,Height=30};
            write.Click+=delegate{WriteToMouse();};
            Controls.Add(write);

            var close=new Button {Text="关闭",Location=new Point(450,424),Width=90,Height=30};
            close.Click+=delegate{Close();};
            Controls.Add(close);

            var writeButton=write;
            Shown+=delegate{LoadFromMouse(false);};
        }

        void ApplyEnabledState() {
            int count=(int)countBox.Value;
            for(int i=0;i<7;i++) {
                bool on=i<count;
                xBoxes[i].Enabled=on;
                yBoxes[i].Enabled=on&&!sameBox.Checked;
            }
            currentBox.Maximum=count;
        }

        void SyncSameAxis() {
            if(!sameBox.Checked) return;
            for(int i=0;i<7;i++) yBoxes[i].Text=xBoxes[i].Text;
        }

        void LoadFromMouse(bool announce) {
            try {
                using(var mouse=RapooHid.Open()) {
                    if(mouse==null) {
                        status.Text="未检测到雷柏鼠标。请插好 2.4G 接收器或数据线后点“重新读取鼠标”。";
                        status.ForeColor=Color.FromArgb(170,60,60);
                        return;
                    }
                    loadedX=mouse.ReadGearValues(false);
                    loadedY=mouse.ReadGearValues(true);
                    int count=mouse.ReadEnabledGearCount();
                    int current=mouse.ReadCurrentGear();
                    if(loadedX==null) {
                        status.Text="读取 DPI 档位失败，请重新插拔接收器后重试。";
                        status.ForeColor=Color.FromArgb(170,60,60);
                        return;
                    }
                    countBox.Value=Math.Max(1,Math.Min(7,count>0?count:7));
                    currentBox.Value=Math.Max(1,Math.Min((int)countBox.Value,current+1));
                    for(int i=0;i<7;i++) {
                        xBoxes[i].Text=loadedX[i].ToString();
                        yBoxes[i].Text=(loadedY!=null&&loadedY[i]>0?loadedY[i]:loadedX[i]).ToString();
                    }
                    ApplyEnabledState();
                    status.Text=(announce?"已重新读取：":"已读取：")+count+" 个启用档位，当前第 "+(current+1)+" 档。";
                    status.ForeColor=Color.FromArgb(60,120,70);
                }
            } catch(Exception error) {
                status.Text="读取失败："+error.Message;
                status.ForeColor=Color.FromArgb(170,60,60);
            }
        }

        void WriteToMouse() {
            int count=(int)countBox.Value;
            var x=new int[7];var y=new int[7];
            for(int i=0;i<7;i++) {
                int value;
                if(!Int32.TryParse(xBoxes[i].Text.Trim(),out value)||value<100||value>30000) {
                    status.Text="第 "+(i+1)+" 档的 X 轴数值无效（100–30000）。";
                    status.ForeColor=Color.FromArgb(170,60,60);
                    return;
                }
                int yValue;
                if(sameBox.Checked) yValue=value;
                else if(!Int32.TryParse(yBoxes[i].Text.Trim(),out yValue)||yValue<100||yValue>30000) {
                    status.Text="第 "+(i+1)+" 档的 Y 轴数值无效（100–30000）。";
                    status.ForeColor=Color.FromArgb(170,60,60);
                    return;
                }
                x[i]=value;y[i]=yValue;
            }
            int current=Math.Max(1,Math.Min(count,(int)currentBox.Value));
            try {
                using(var mouse=RapooHid.Open()) {
                    if(mouse==null) {
                        status.Text="未检测到雷柏鼠标，无法写入。";
                        status.ForeColor=Color.FromArgb(170,60,60);
                        return;
                    }
                    bool ok=true;
                    for(int i=0;i<7;i++) {
                        ok&=mouse.WriteGearValue(i,x[i],false);
                        ok&=mouse.WriteGearValue(i,y[i],true);
                    }
                    ok&=mouse.WriteEnabledGearCount(count);
                    ok&=mouse.WriteCurrentGear(current-1);
                    if(!ok) {
                        status.Text="写入过程中有一步失败，可能鼠标进入休眠。请动一下鼠标后重试。";
                        status.ForeColor=Color.FromArgb(170,60,60);
                        return;
                    }
                    var readX=mouse.ReadGearValues(false);
                    var readY=mouse.ReadGearValues(true);
                    int readCount=mouse.ReadEnabledGearCount();
                    int readCurrent=mouse.ReadCurrentGear();
                    bool verified=readX!=null&&readY!=null;
                    if(verified) for(int i=0;i<7;i++) {
                        if(readX[i]!=x[i]||readY[i]!=y[i]) verified=false;
                    }
                    if(verified&&readCount==count&&readCurrent==current-1) {
                        loadedX=x;loadedY=y;
                        status.Text="写入成功并已读回验证："+count+" 个档位，当前第 "+current+" 档 = "+x[current-1]+" DPI。";
                        status.ForeColor=Color.FromArgb(60,120,70);
                    } else {
                        status.Text="写入已发送，但读回数值不一致（可能鼠标休眠或固件限制）。请再点一次“重新读取鼠标”确认。";
                        status.ForeColor=Color.FromArgb(170,120,40);
                    }
                }
            } catch(Exception error) {
                status.Text="写入失败："+error.Message;
                status.ForeColor=Color.FromArgb(170,60,60);
            }
        }
    }
}
