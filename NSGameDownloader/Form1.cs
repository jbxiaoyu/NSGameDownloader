﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using NSGameDownloader.Properties;

namespace NSGameDownloader
{
    //todo 增加在外部浏览器打开的功能
    //todo 尝试进行从百度云直接得到真实下载地址 
    public partial class Form1 : Form
    {
        private const string PanUrlHead = "https://pan.baidu.com/s/";
        private const string NspPanKey = "1tOFTvpJwikcdo2W12Z8dEw";
        private const string XciPanKey = "1cwIw1-qsNOKaq6xrK0VUqQ";
        private const string NspPw = "vb4v";
        private const string XciPw = "fi4r";
        private const string NutdbUrl = "https://snip.li/nutdb";
        private const string TitleKeysPath = "keys.json";
        private const int EM_SETCUEBANNER = 0x1501;

        /// <summary>
        ///     原始值
        /// </summary>
        private string _curTid;

        private JObject _titlekeys;

        public Form1()
        {
            InitializeComponent();
        }


        private void Form1_Load(object sender, EventArgs e)
        {
            var tl = new Thread(ThreadLoad);
            tl.Start();
        }

        /// <summary>
        ///     线程初始,不会占用启动时间
        /// </summary>
        private void ThreadLoad()
        {
            if (!File.Exists(TitleKeysPath))
            {
                var t = new Thread(UpdateTitleKey);
                t.Start();
            }
            else
            {
                _titlekeys = JObject.Parse(File.ReadAllText(TitleKeysPath));
            }

            if (!Directory.Exists("image")) Directory.CreateDirectory("image");
            //使用api 做占位符
            Invoke(new Action(() => { SendMessage(textBox_keyword.Handle, EM_SETCUEBANNER, 0, "在这里输入游戏名关键字.."); }));

            SearchGameName();
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam,
            [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        public void UpdateTitleKey()
        {
            //todo 从nswdb 上得到xci的文件的列表.
            Invoke(new Action(() =>
            {
                toolStripProgressBar_download.Visible = true;
                toolStripProgressBar_download.Maximum = 2;
                toolStripProgressBar_download.Value = 1;
                button_search.Text = "下载中";
                button_search.Enabled = false;
                textBox_keyword.Enabled = false;
            }));

            var http = new WebClient {Encoding = Encoding.UTF8};

            ServicePointManager.ServerCertificateValidationCallback += delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            List<string> keys;
            try
            {
                var html = http.DownloadString(NutdbUrl);
                keys = new List<string>(html.Split(new[] {"\n"}, StringSplitOptions.RemoveEmptyEntries));
                if (keys.Count == 0) throw new Exception("没有得到数据");
                _titlekeys = new JObject();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "更新titleid出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            //前3行 不要
            keys.RemoveAt(0);
            keys.RemoveAt(0);
            keys.RemoveAt(0);
            Invoke(new Action(() => { toolStripProgressBar_download.Maximum = keys.Count; }));
            var count = 0;
            foreach (var key in keys)
            {
                var kan = key.Split('|');
                //0                 |1                                  |2                                  |3          |4      |5      |6              |7              |8      |9
                //id                |rightsId                           |key                                |isUpdate   |isDLC  |isDemo |baseName       |name           |version|region
                //01000320000CC000  |01000320000CC0000000000000000000   |F64FBE562E753B662F7CC8D6C8B4EE79   |0          |0      |0      |1-2-Switch™    |1-2-Switch™    |0      |US

                var tid = kan[0];
                var name = kan[7];
                var ver = kan[8];
                var region = kan[9] == "US" ? "AU" : kan[9]; //美区用不了.换掉

                switch (kan[3] + kan[4] + kan[5])
                {
                    case "000": //base

                        break;
                    case "100": //upd
                        tid = tid.Substring(0, 13) + "000";
                        _titlekeys[tid]["upd"] = true;
                        continue;
                    case "010": //dlc
                        tid = GetBaseTidFormDlcTid(tid);
                        _titlekeys[tid]["dlc"] = true;
                        continue;
                    case "001": //demo 无用
                        continue;
                }

                var jtemp = new JObject
                {
                    ["tid"] = tid,
                    ["name"] = name,
                    ["dlc"] = false,
                    ["upd"] = false,
                    ["ver"] = ver,
                    ["region"] = region
                };
                Invoke(new Action(() => { toolStripProgressBar_download.Value = ++count; }));
                _titlekeys[tid] = jtemp;
            }


            File.WriteAllText(TitleKeysPath, _titlekeys.ToString());

            Invoke(new Action(() =>
            {
                button_search.Text = "搜索";
                button_search.Enabled = true;
                textBox_keyword.Enabled = true;
                toolStripProgressBar_download.Visible = false;
                SearchGameName();
            }));
        }

        private string GetPanUrl(string tid, bool dlc)
        {
            return PanUrlHead + (radioButton_xci.Checked ? XciPanKey : NspPanKey)
                   + "#list/path=/"
                   + (radioButton_xci.Checked ? "XCI" : "Nintendo Switch Games")
                   + (radioButton_xci.Checked ? "" : dlc ? "/UPD + DLC" : "/NSP")
                   + "/" + tid.Substring(0, 5)
                   + "/" + tid
                   + "&parentPath=/";
        }

        private void writePw(string pw)
        {
            var element = panWebBrowser.Document.GetElementById("nwtz3z5E");
            if (element == null) return;
            element.SetAttribute("value", pw);

            var alla = panWebBrowser.Document.GetElementsByTagName("a");
            foreach (HtmlElement a in alla)
                if (a.InnerText == "提取文件")
                {
                    a.InvokeMember("Click");
                    break;
                }
        }

        private void webBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            var oUrl = WebUtility.UrlDecode(e.Url.ToString());


            Console.WriteLine("log:" + oUrl);
            if (panWebBrowser.Document.Body == null) return;
            if (panWebBrowser.Document.Body.InnerText != null &&
                panWebBrowser.Document.Body.InnerText.Contains("请输入提取码"))
            {
                if (e.Url.ToString().Contains(NspPanKey.Substring(2))) writePw(NspPw);
                else if (e.Url.ToString().Contains(XciPanKey.Substring(2))) writePw(XciPw);
            }
            else
            {
                if (oUrl == "https://pan.baidu.com/s/1tOFTvpJwikcdo2W12Z8dEw#list/path=/" ||
                    oUrl == "https://pan.baidu.com/s/1cwIw1-qsNOKaq6xrK0VUqQ#list/path=/")
                    WebRefresh(); //输入密码后会再一次来到根目录,要再跳一次
            }
        }

        private void WebRefresh()
        {
            if (_curTid == null) return;
            Navigate(GetPanUrl(_curTid, false)); //点击刷新 只找本体
        }

        private void radioButton_nsp_CheckedChanged(object sender, EventArgs e)
        {
            WebRefresh();
        }

        private void radioButton_xci_CheckedChanged(object sender, EventArgs e)
        {
            WebRefresh();
        }

        private void Navigate(string url)
        {
            //todo 多线程

            panWebBrowser.Url = new Uri(url);
        }

        private void button_search_Click(object sender, EventArgs e)
        {
            //var keys = Titlekeys.Root.Where(x => x.Contains(textBox_keyword.Text.Trim()));

            SearchGameName(textBox_keyword.Text);
        }

        private void SearchGameName(string keywords = "")
        {
            if (_titlekeys == null) return;
            if (_titlekeys.Count == 0) return;

            Invoke(new Action(() =>
            {
                //todo 多关键字处理
                //todo 查找时 对大小写进行转换
                listView1.Items.Clear();
                foreach (var titlekey in _titlekeys)
                    if (titlekey.Value["name"].ToString().Contains(keywords.Trim()))
                        listView1.Items.Add(new ListViewItem(new[]
                        {
                            titlekey.Value["tid"].ToString(),
                            titlekey.Value["name"].ToString(),
                            titlekey.Value["dlc"].ToObject<bool>() ? "●" : "",
                            titlekey.Value["upd"].ToObject<bool>() ? "●" : ""
                        }));
                label_count.Text = "count:" + listView1.Items.Count;
            }));
        }


        private string GetBaseTidFormDlcTid(string tid)
        {
            //后3位置改000
            //13位退1
            var t16 = Convert.ToInt64("0x" + tid.Substring(0, 13) + "000", 16) - 4096;
            return t16.ToString("x16").ToUpper();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            _curTid = listView1.SelectedItems[0].Text;


            //如果点击的是dlc 或者是 upd 那要跳到upd+dlc的目录
            button_lookUdpDlc.Visible = _titlekeys[_curTid]["dlc"].ToObject<bool>() ||
                                        _titlekeys[_curTid]["upd"].ToObject<bool>();

            WebRefresh();


            Console.WriteLine(_curTid);

            //刷新来自eshop的信息
            var t = new Thread(GetGameInfoFromEShop);
            t.Start();


            //得到图片
            //todo 更好的图片地址
            t = new Thread(GetGameImage);
            t.Start();
        }

        private void GetGameInfoFromEShop()
        {
            //todo 从http://www.eshop-switch.com 拿数据
            var g = _titlekeys[_curTid].ToObject<JObject>();
            if (!g.ContainsKey("info"))
                using (var web = new WebClient {Encoding = Encoding.UTF8})
                {
                    try
                    {
                        var url = $"https://ec.nintendo.com/apps/{_curTid}/{g["region"]}";
                        Console.WriteLine(url);
                        var html = web.DownloadString(url);
                        html = html.Split(new[] {"NXSTORE.titleDetail.jsonData = "},
                                StringSplitOptions.RemoveEmptyEntries)[1]
                            .Split(new[] {"NXSTORE.titleDetail"}, StringSplitOptions.RemoveEmptyEntries)[0]
                            .Replace(";", "");

                        _titlekeys[_curTid]["info"] = JObject.Parse(html);
                    }
                    catch
                    {
                        Invoke(new Action(() => { label_info.Text = "0KB\n获取信息错误"; }));
                        return;
                    }
                }

            var info = _titlekeys[_curTid]["info"];
            var size = info["total_rom_size"].ToObject<long>();
            Invoke(new Action(() => { label_info.Text = $"{ConvertBytes(size)}\n{info["description"]}"; }));
        }

        private void GetGameImage()
        {
            pictureBox_gameicon.Image = Resources.load;


            var filename = "image\\" + _curTid + ".jpg";

            if (File.Exists(filename))
                try
                {
                    pictureBox_gameicon.Image = Image.FromFile(filename);
                    return;
                }
                catch
                {
                    // ignored
                }

            var web = new WebClient {Encoding = Encoding.UTF8};
            // 解决WebClient不能通过https下载内容问题
            ServicePointManager.ServerCertificateValidationCallback += delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                web.DownloadFile("https://terannet.sirv.com/CDNSP/" + _curTid.ToLower() + ".jpg", filename);
                pictureBox_gameicon.Image = Image.FromFile(filename);
            }
            catch
            {
                if (File.Exists(filename))
                    File.Delete(filename);


                pictureBox_gameicon.Image = Resources.error;
            }
        }

        /// <summary>
        ///     对文件大小进行转换
        /// </summary>
        /// <param name="len"></param>
        /// <returns></returns>
        public static string ConvertBytes(long len)
        {
            if (len > 1073741824)
                return (len / 1073741824.0).ToString("F") + "GB";
            if (len > 1048576)
                return (len / 1048576.0).ToString("F") + "MB";
            return (len / 1024.0).ToString("F") + "KB";
        }

        private void textBox_keyword_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != (char) Keys.Enter) return;
            e.Handled = true; //防止向上冒泡
            SearchGameName(textBox_keyword.Text);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            File.WriteAllText(TitleKeysPath, _titlekeys.ToString());
        }

        private void 更新TitleId文件ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var t = new Thread(UpdateTitleKey);
            t.Start();
        }

        private void label1_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.91wii.com/thread-104797-1-1.html");
        }

        private void 查看帮助ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/ningxiaoxiao/NSGameDownloader/wiki");
        }

        private void 发送反馈ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/ningxiaoxiao/NSGameDownloader/issues");
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/ningxiaoxiao/NSGameDownloader");
        }

        private void button_lookUdpDlc_Click(object sender, EventArgs e)
        {
            if (_curTid == null) return;
            Navigate(GetPanUrl(_curTid, true));
        }
    }
}