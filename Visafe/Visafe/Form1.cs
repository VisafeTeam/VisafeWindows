﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Net;
using Visafe.Properties;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using RestSharp.Extensions.MonoHttp;
using static Visafe.Helper;
using RestSharp;
using Newtonsoft.Json;
using System.Threading;
using System.Security.AccessControl;
using System.Collections.Generic;

namespace Visafe
{
    public partial class Form1 : Form
    {
        private EventLog _eventLog;

        private DeviceInfoObtainer deviceInfoObtainer = new DeviceInfoObtainer();

        private Updater _updater;

        public Form1()
        {
            _eventLog = new EventLog("Application");
            _eventLog.Source = "Visafe";

            _updater = new Updater(Constant.VERSION_INFO_URL);

            InitializeComponent();
        }

        //function to start visafe application
        private void Form1_Load(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;

            bool started = startService();

            if (started == false)
            {
                MessageBox.Show("Không thể khởi động Visafe", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            notifyIcon1.Visible = true;
            item_turnoff.Visible = true;
            item_turnon.Visible = false;
            Hide();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;

            //string user_id = this.deviceInfoObtainer.GetID();
            string invitingURL = this.deviceInfoObtainer.GetUrl();

            text_url.Text = invitingURL;

            checkForUpdate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Hide();
        }

        public void SendInvitingUrl()
        {
            string url = text_url.Text;

            status_label.Text = "Đang lưu...";

            string Pattern = @"^(?:http(s)?:\/\/)?[\w.-]+(?:\.[\w\.-]+)+[\w\-\._~:/?#[\]@!\$&'\(\)\*\+,;=. ]+$";
            Regex Rgx = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            if ((url == "") || !url.Contains("http"))
            {
                status_label.Text = "";
                MessageBox.Show("URL không hợp lệ", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (!Rgx.IsMatch(url))
            {
                status_label.Text = "";
                MessageBox.Show("URL không hợp lệ", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                string realUrl = Helper.UrlLengthen(url);

                string deviceId;
                string groupId;
                string groupName;
                string deviceName;
                string macAddress;
                string ipAddress;
                string deviceType;
                string deviceOwner;
                string deviceDetail;

                //deviceId = this.deviceInfoObtainer.GetID();

                string signalDataString = "signal << get_id;";
                var tempId = Helper.SendSignal(signalDataString);

                if (tempId != null)
                {
                    deviceId = tempId;
                }
                else
                {
                    deviceId = "";
                }

                try
                {
                    Uri myUri = new Uri(realUrl);
                    groupId = HttpUtility.ParseQueryString(myUri.Query).Get("groupId");
                    groupName = HttpUtility.ParseQueryString(myUri.Query).Get("groupName");
                }
                catch
                {
                    groupId = "";
                    groupName = "";
                }


                try
                {
                    deviceName = System.Environment.GetEnvironmentVariable("COMPUTERNAME");
                }
                catch
                {
                    deviceName = "unknown";
                }

                try
                {
                    macAddress = DeviceInfoObtainer.GetMac();
                }
                catch
                {
                    macAddress = "unknown";
                }

                try
                {
                    ipAddress = DeviceInfoObtainer.GetIpAddress();
                }
                catch
                {
                    ipAddress = "unknown";
                }

                deviceType = "Windows";

                try
                {
                    deviceOwner = Environment.UserName;
                }
                catch
                {
                    deviceOwner = "unknown";
                }

                try
                {
                    deviceDetail = Environment.OSVersion.ToString();
                }
                catch
                {
                    deviceDetail = "unknown";
                }

                //establish secure channel
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var client = new RestClient(realUrl);
                var request = new RestRequest(Method.POST);
                request.RequestFormat = DataFormat.Json;

                try
                {
                    request.AddJsonBody(new
                    {
                        deviceId = deviceId,
                        groupName = groupName,
                        groupId = groupId,
                        deviceName = deviceName,
                        macAddress = macAddress,
                        ipAddress = ipAddress,
                        deviceType = deviceType,
                        deviceOwner = deviceOwner,
                        deviceDetail = deviceDetail,
                    });

                    var response = client.Execute(request);

                    JoiningGroupResp respContent = JsonConvert.DeserializeObject<JoiningGroupResp>(response.Content);

                    //var responseString = response.Content.ReadAsStringAsync();

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        status_label.Text = "";
                        MessageBox.Show(respContent.local_msg, Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        string visafeFolder = Path.Combine(appDataFolder, "Visafe");

                        if (Directory.Exists(visafeFolder) == false)
                        {
                            Directory.CreateDirectory(visafeFolder);
                        }

                        string urlConfig = Path.Combine(visafeFolder, Constant.URL_CONFIG_FILE);

                        if (!File.Exists(urlConfig))
                        {
                            File.Create(urlConfig).Dispose();
                        }

                        FileInfo fi = new FileInfo(urlConfig);
                        using (TextWriter writer = new StreamWriter(fi.Open(FileMode.Truncate)))
                        {
                            writer.WriteLine(url);
                            writer.Close();
                        }

                        status_label.Text = "";
                        MessageBox.Show(Constant.SAVING_SUCCESS_MSG, Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception e)
                {
                    status_label.Text = "";
                    Console.WriteLine(e.Message);
                    MessageBox.Show(Constant.SAVING_ERROR_MSG + "\n\nURL không hợp lệ hoặc thiết bị đã tham gia vào nhóm hoặc thiết bị đang ở nhóm khác", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        //function used to start service
        private bool startService()
        {
            string signalDataString = "signal << check_start";
            string sendResult = Helper.SendSignal(signalDataString);
            int elapsed = 0;
            while (sendResult != Constant.STARTED_NOTI_STRING) {
                sendResult = Helper.SendSignal(signalDataString);
                Thread.Sleep(2000);
                elapsed += 2000;
                if (elapsed > 8000)
                {
                    return false;
                }
            }

            signalDataString = "signal << start;";

            sendResult = Helper.SendSignal(signalDataString);

            if (sendResult == null)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        //function used to stop service
        private void stopService()
        {
            string signalDataString = "signal << stop;";

            var sendResult = Helper.SendSignal(signalDataString);

            if (sendResult == null)
            {
                //show message box
                MessageBox.Show("Không thể tắt Visafe", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void exitService()
        {
            string signalDataString = "signal << exit;";

            var sendResult = Helper.SendSignal(signalDataString);

            if (sendResult == null)
            {
                //show message box
                MessageBox.Show("Không thể tắt Visafe", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkForUpdate()
        {
            bool newVersion = _updater.CheckForUpdate();

            if (newVersion == true)
            {
                string message = "Visafe có bản cập nhật mới, bạn có muốn cài đặt?";

                if (_updater.NewVersionDescription != "")
                {
                    message = message + "\n\n" + "Những sự thay đổi ở bản cập nhật:";
                    message = message + "\n" + _updater.NewVersionDescription;
                }

                MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                DialogResult result = MessageBox.Show(message, Constant.NOTI_TITLE, buttons);
                if (result == DialogResult.Yes)
                {
                    string signalDataString1 = "signal << update;";

                    var sendResult1 = Helper.SendSignal(signalDataString1);

                    if (sendResult1 == null)
                    {
                        //show message box
                        MessageBox.Show("Không thể cập nhật Visafe", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _updater.Upgrade();
                }
            }
        }

        //Disable close button
        private const int CP_DISABLE_CLOSE_BUTTON = 0x200;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle = cp.ClassStyle | CP_DISABLE_CLOSE_BUTTON;
                return cp;
            }
        }

        //show the form if the notify icon is clicked by the left mouse
        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Show();
                this.ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
            }
        }

        //when click Turn on in tray icon
        //restart program and service
        private void item_turnon_Click(object sender, EventArgs e)
        {
            //start service
            bool started = startService();

            if (started == false)
            {
                MessageBox.Show("Không thể khởi động Visafe", Constant.NOTI_TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            //start program
            notifyIcon1.Icon = Resources.turnon;

            item_turnoff.Visible = true;
            item_turnon.Visible = false;
            ShowInTaskbar = false;
            //WindowState = FormWindowState.Minimized;
            Hide();
        }

        // When click Turn off in tray icon
        //kill dnsproxy.exe
        //stop service
        private void item_turnoff_Click(object sender, EventArgs e)
        {
            item_turnoff.Visible = false;
            item_turnon.Visible = true;
            notifyIcon1.Icon = Resources.turnoff;
            stopService();
        }

        //When click Exit in tray icon
        //close application
        //stop service
        private void item_exit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Thiết bị của bạn có thể bị ảnh hưởng bởi tấn công mạng. \nBạn muốn tắt bảo vệ?", "Bạn đang tắt chế độ bảo vệ!", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            {
                exitService();
                Application.Exit();
            }
        }

        private void openSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
            this.ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            //Hide();
            //WindowState = FormWindowState.Minimized;
            SendInvitingUrl();
        }

        private void button_cancel_Click(object sender, EventArgs e)
        {
            Hide();
            WindowState = FormWindowState.Minimized;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(Constant.ADMIN_DASHBOARD_URL);
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(Constant.VISAFE_DOC_URL);
        }

        private void FormDisplay(object state)
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
        }
    }
}
