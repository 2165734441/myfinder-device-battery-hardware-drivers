using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AtkBatteryTray {
    internal sealed class PeripheralHubForm:Form {
        readonly DialogTheme theme=new DialogTheme(true);
        readonly ListView list=new ListView();
        readonly TextBox search=new TextBox();
        readonly ComboBox filter=new ComboBox();
        readonly Button openButton=HubButton("打开驱动");
        readonly Button officialButton=HubButton("官网 / 下载");
        readonly Button refreshButton=HubButton("刷新");
        readonly Button closeButton=HubButton("关闭");
        readonly Label summary=new Label();

        internal PeripheralHubForm() {
            Text="外设驱动中心";ShowInTaskbar=false;StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(780,580);MinimumSize=new Size(660,480);MaximizeBox=true;MinimizeBox=false;
            BackColor=theme.Card;ForeColor=theme.Foreground;
            Font=new Font("Microsoft YaHei UI",VisualDesign.BodyFontSize,FontStyle.Regular,GraphicsUnit.Pixel);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(18,14,18,14)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            Controls.Add(layout);

            var title=new Label {Text="外设驱动中心",AutoSize=true,Dock=DockStyle.Fill,ForeColor=theme.Foreground,
                Font=new Font("Microsoft YaHei UI",15f,FontStyle.Bold,GraphicsUnit.Point),TextAlign=ContentAlignment.MiddleLeft};
            layout.Controls.Add(title,0,0);
            var subtitle=new Label {Text="键盘 / 鼠标 / 耳机 / 手柄 / ARGB 灯光。已安装的直接打开厂商软件，未安装的打开官网下载；本程序不替代厂商驱动。",
                AutoSize=false,Dock=DockStyle.Fill,ForeColor=theme.Muted,TextAlign=ContentAlignment.TopLeft};
            layout.Controls.Add(subtitle,0,1);

            var searchRow=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=false,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(0,4,0,0)};
            searchRow.Controls.Add(new Label {Text="搜索",AutoSize=true,Margin=new Padding(0,8,6,0),ForeColor=theme.Foreground});
            search.Width=300;search.Margin=new Padding(0,3,14,0);search.BackColor=theme.Section;search.ForeColor=theme.Foreground;search.BorderStyle=BorderStyle.FixedSingle;
            searchRow.Controls.Add(search);
            searchRow.Controls.Add(new Label {Text="类型",AutoSize=true,Margin=new Padding(0,8,6,0),ForeColor=theme.Foreground});
            filter.DropDownStyle=ComboBoxStyle.DropDownList;filter.Width=140;filter.Margin=new Padding(0,3,0,0);filter.BackColor=theme.Section;filter.ForeColor=theme.Foreground;
            filter.Items.AddRange(new object[]{"全部类型","键盘","鼠标","耳机","手柄","灯光","主板/整机"});filter.SelectedIndex=0;
            searchRow.Controls.Add(filter);
            layout.Controls.Add(searchRow,0,2);

            list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.ShowGroups=false;list.ShowItemToolTips=true;
            list.Dock=DockStyle.Fill;list.BackColor=theme.Card;list.ForeColor=theme.Foreground;list.BorderStyle=BorderStyle.FixedSingle;
            list.Columns.Add("外设 / 驱动",330,HorizontalAlignment.Left);
            list.Columns.Add("类型",110,HorizontalAlignment.Left);
            list.Columns.Add("状态",110,HorizontalAlignment.Left);
            layout.Controls.Add(list,0,3);

            summary.AutoSize=false;summary.Dock=DockStyle.Fill;summary.ForeColor=theme.Muted;summary.TextAlign=ContentAlignment.MiddleLeft;
            layout.Controls.Add(summary,0,4);

            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,6,0,0)};
            buttons.Controls.Add(closeButton);buttons.Controls.Add(officialButton);buttons.Controls.Add(openButton);buttons.Controls.Add(refreshButton);
            layout.Controls.Add(buttons,0,5);

            search.TextChanged+=delegate{Populate();};
            filter.SelectedIndexChanged+=delegate{Populate();};
            list.SelectedIndexChanged+=delegate{UpdateButtons();};
            list.DoubleClick+=delegate{OpenSelected();};
            openButton.Click+=delegate{OpenSelected();};
            officialButton.Click+=delegate{OpenOfficial();};
            refreshButton.Click+=delegate{PeripheralCatalog.Refresh();Populate();};
            closeButton.Click+=delegate{Close();};
            CancelButton=closeButton;
            Populate();
        }

        static Button HubButton(string text) {
            var button=new Button {Text=text,AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(52,55,62),ForeColor=Color.White};
            button.FlatAppearance.BorderColor=Color.FromArgb(86,90,100);
            button.Padding=new Padding(10,2,10,2);
            return button;
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            ThemedTitleBar.Apply(Handle,theme);
            list.Focus();
        }

        PeripheralApp SelectedApp() {
            if(list.SelectedItems.Count!=1) return null;
            return list.SelectedItems[0].Tag as PeripheralApp;
        }

        void Populate() {
            string query=search.Text==null?"":search.Text.Trim();
            string category=filter.SelectedItem==null?"全部类型":filter.SelectedItem.ToString();
            list.BeginUpdate();
            try {
                list.Items.Clear();
                var categories=new[]{"键盘","鼠标","耳机","手柄","灯光","主板/整机"};
                int visible=0;
                foreach(var cat in categories) {
                    if(category!="全部类型"&&category!=cat) continue;
                    var matches=new List<PeripheralApp>();
                    foreach(var app in PeripheralCatalog.All) {
                        if(app.Category!=cat) continue;
                        if(query.Length>0&&!Matches(app,query)) continue;
                        matches.Add(app);
                    }
                    if(matches.Count==0) continue;
                    var header=new ListViewItem(cat) {Font=new Font(list.Font,FontStyle.Bold),BackColor=theme.Section,ForeColor=theme.Foreground};
                    list.Items.Add(header);
                    for(int pass=0;pass<2;pass++) {
                        foreach(var app in matches) {
                            if((pass==0)!=app.Installed) continue;
                            var item=new ListViewItem(app.Name);
                            item.SubItems.Add(app.Category);item.SubItems.Add(app.Status);
                            item.Tag=app;item.ToolTipText=app.Note;
                            item.ForeColor=app.Installed?theme.Foreground:theme.Muted;
                            list.Items.Add(item);visible++;
                        }
                    }
                }
                if(visible==0) {
                    var item=new ListViewItem("没有匹配的外设驱动，换个关键词试试");
                    item.ForeColor=theme.Muted;list.Items.Add(item);
                }
            } finally {
                list.EndUpdate();
            }
            int installed=0,online=0;foreach(var app in PeripheralCatalog.All){if(app.Installed)installed++;else if(app.WebOnly)online++;}
            summary.Text="本机已安装 "+installed+" 个 · 收录 "+PeripheralCatalog.All.Count+" 个（含 "+online+" 个在线工具）";
            UpdateButtons();
        }

        static bool Matches(PeripheralApp app,string query) {
            if(app.Name!=null&&app.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0) return true;
            if(app.Category!=null&&app.Category.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0) return true;
            if(app.Note!=null&&app.Note.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0) return true;
            return false;
        }

        void UpdateButtons() {
            var app=SelectedApp();
            bool canOpen=app!=null&&(app.Installed||app.WebOnly);
            openButton.Enabled=canOpen;
            openButton.Text=app!=null&&app.WebOnly?"打开网页":"打开驱动";
            officialButton.Enabled=app!=null&&!String.IsNullOrEmpty(app.Url);
        }

        void OpenSelected() {
            var app=SelectedApp();
            if(app==null) return;
            if(PeripheralCatalog.Open(app)) return;
            MessageBox.Show(this,"未能打开“"+app.Name+"”。\n如果它没有安装，可以点“官网 / 下载”手动获取。","外设驱动中心",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }

        void OpenOfficial() {
            var app=SelectedApp();
            if(app==null||String.IsNullOrEmpty(app.Url)) return;
            try{Process.Start(new ProcessStartInfo(app.Url) {UseShellExecute=true});}
            catch(Exception error){MessageBox.Show(this,"无法打开官网："+error.Message,"外设驱动中心",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }

        internal string DebugSummary() {
            int items=0;
            foreach(ListViewItem item in list.Items) if(item.Tag is PeripheralApp) items++;
            return summary.Text+"\r\nvisibleItems="+items+"\r\nsize="+Width+"x"+Height;
        }

        internal static void ShowHub(Form owner) {
            using(var form=new PeripheralHubForm()) {
                form.TopMost=true;
                if(owner!=null) form.ShowDialog(owner);else form.ShowDialog();
            }
        }
    }
}
