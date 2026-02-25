using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;
using AForge.Video;
using AForge.Video.DirectShow;
using ChatApp.Shared;

namespace ChatClient
{
    public partial class Form1 : Form
    {
        // --- 1. BIẾN GIAO DIỆN (UI) ---
        private TextBox txtUser, txtPass, txtMsg, txtRecipient;
        private Button btnConnect, btnDisconnect, btnSend, btnVideo, btnChatHistory, btnCallHistory, btnFile;
        private RichTextBox txtChat;
        private PictureBox picRemote, picLocal;
        private Label lblStatus;

        // --- 2. BIẾN HỆ THỐNG MẠNG ---
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private string myName;

        // --- 3. BIẾN CAMERA ---
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;
        private bool isCalling = false;
        private string currentRemoteCaller = "";

        // --- 4. BIẾN BẢO MẬT ---
        private ECDiffieHellmanCng dh = new ECDiffieHellmanCng();
        private byte[] sharedKey;

        public Form1()
        {
            SetupUI_FinalFix(); // Khởi tạo giao diện đã fix
            InitCamera();       // Tìm Webcam
        }

        private void InitCamera()
        {
            try { videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice); } catch { }
        }

        // --- 5. THIẾT KẾ GIAO DIỆN (ĐÃ FIX LỖI CHE CHỮ) ---
        private void SetupUI_FinalFix()
        {
            this.Text = "Chat Client (Webcam - Chat - History - File)";
            this.Size = new Size(1200, 780);
            this.StartPosition = FormStartPosition.CenterScreen;

            // A. TOP: ĐĂNG NHẬP (Tăng chiều cao lên 100 để thoáng hơn)
            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Color.WhiteSmoke, Padding = new Padding(10) };
            GroupBox grpAuth = new GroupBox { Text = "Hệ thống xác thực & Nhật ký", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) };

            // HẠ THẤP TỌA ĐỘ Y XUỐNG 45 (Thay vì 25-30 như cũ) ĐỂ KHÔNG BỊ CHE
            int Y_POS = 45;

            Label lblU = new Label { Text = "User:", Location = new Point(20, Y_POS + 3), AutoSize = true };
            txtUser = new TextBox { Location = new Point(60, Y_POS), Width = 100, Text = "admin" };

            Label lblP = new Label { Text = "Pass:", Location = new Point(170, Y_POS + 3), AutoSize = true };
            txtPass = new TextBox { Location = new Point(210, Y_POS), Width = 80, PasswordChar = '*', Text = "123" };

            btnConnect = new Button { Text = "KẾT NỐI", Location = new Point(310, Y_POS - 2), Width = 80, Height = 28, BackColor = Color.Teal, ForeColor = Color.White };
            btnDisconnect = new Button { Text = "NGẮT", Location = new Point(400, Y_POS - 2), Width = 80, Height = 28, BackColor = Color.IndianRed, ForeColor = Color.White, Enabled = false };

            btnChatHistory = new Button { Text = "💬 L.SỬ CHAT", Location = new Point(500, Y_POS - 2), Width = 100, Height = 28, BackColor = Color.DimGray, ForeColor = Color.White };
            btnChatHistory.Click += (s, e) => OpenLogFile("History.txt");

            btnCallHistory = new Button { Text = "📞 L.SỬ GỌI", Location = new Point(610, Y_POS - 2), Width = 100, Height = 28, BackColor = Color.DimGray, ForeColor = Color.White };
            btnCallHistory.Click += (s, e) => OpenLogFile("CallHistory.txt");

            lblStatus = new Label { Text = "OFFLINE", Location = new Point(720, Y_POS + 3), AutoSize = true, Font = new Font("Arial", 10, FontStyle.Bold), ForeColor = Color.Red };

            btnConnect.Click += (s, e) => Connect();
            btnDisconnect.Click += (s, e) => Disconnect();

            grpAuth.Controls.AddRange(new Control[] { lblU, txtUser, lblP, txtPass, btnConnect, btnDisconnect, btnChatHistory, btnCallHistory, lblStatus });
            pnlTop.Controls.Add(grpAuth);

            // B. RIGHT: VIDEO UI
            Panel pnlRight = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Color.FromArgb(40, 40, 40) };

            GroupBox grpCall = new GroupBox { Text = "Điều khiển Video", Dock = DockStyle.Top, Height = 140, ForeColor = Color.White, Padding = new Padding(10) };
            Label lblRec = new Label { Text = "Người nhận (Để trống = Gọi Nhóm):", Location = new Point(10, 25), AutoSize = true, ForeColor = Color.LightGray };
            txtRecipient = new TextBox { Location = new Point(10, 45), Width = 300, Font = new Font("Arial", 11) };

            btnVideo = new Button { Text = "📹 BẮT ĐẦU GỌI", Location = new Point(10, 80), Width = 300, Height = 40, BackColor = Color.SeaGreen, ForeColor = Color.White, Font = new Font("Arial", 10, FontStyle.Bold) };
            btnVideo.Click += (s, e) => ToggleVideo();
            grpCall.Controls.AddRange(new Control[] { lblRec, txtRecipient, btnVideo });

            GroupBox grpLocal = new GroupBox { Text = "Webcam của bạn", Dock = DockStyle.Bottom, Height = 200, ForeColor = Color.White };
            picLocal = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            grpLocal.Controls.Add(picLocal);

            GroupBox grpRemote = new GroupBox { Text = "Màn hình người lạ", Dock = DockStyle.Fill, ForeColor = Color.Yellow };
            picRemote = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            grpRemote.Controls.Add(picRemote);

            pnlRight.Controls.Add(grpRemote);
            pnlRight.Controls.Add(grpCall);
            pnlRight.Controls.Add(grpLocal);

            // C. CENTER: CHAT
            Panel pnlCenter = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            // Input Panel (Dock Bottom)
            Panel pnlInput = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 5, 0, 0) };
            txtMsg = new TextBox { Dock = DockStyle.Fill, Font = new Font("Arial", 14) };
            btnFile = new Button { Text = "📁 File", Dock = DockStyle.Right, Width = 80, BackColor = Color.Orange };
            btnSend = new Button { Text = "GỬI", Dock = DockStyle.Right, Width = 80, BackColor = Color.DodgerBlue, ForeColor = Color.White };

            btnSend.Click += (s, e) => SendMsg();
            btnFile.Click += (s, e) => SendFile();
            pnlInput.Controls.AddRange(new Control[] { txtMsg, btnFile, btnSend });

            // Chat Box (Dock Fill)
            txtChat = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Segoe UI", 11), BorderStyle = BorderStyle.FixedSingle };

            // Add vào Center (Thứ tự quan trọng)
            pnlCenter.Controls.Add(txtChat);
            pnlCenter.Controls.Add(pnlInput);
            pnlInput.BringToFront();

            // D. ADD VÀO FORM
            this.Controls.Add(pnlCenter);
            this.Controls.Add(pnlRight);
            this.Controls.Add(pnlTop);

            pnlTop.SendToBack(); pnlRight.SendToBack(); pnlCenter.BringToFront();
        }

        // --- 6. LOGIC LỊCH SỬ ---
        private void LogToHistory(string filename, string msg)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, filename);
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n";
                File.AppendAllText(path, log);
            }
            catch { }
        }

        private void OpenLogFile(string filename)
        {
            string path = Path.Combine(Application.StartupPath, filename);
            if (File.Exists(path)) Process.Start("notepad.exe", path);
            else MessageBox.Show($"Chưa có dữ liệu trong {filename}!");
        }

        // --- 7. NHẬN DỮ LIỆU ---
        private void ReceiveLoop()
        {
            try
            {
                while (client != null && client.Connected)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    var p = JsonSerializer.Deserialize<DataPacket>(line);

                    Invoke(new Action(() => {
                        if (p.Type == PacketType.Message)
                        {
                            // Sử dụng khóa (DefaultKey hoặc sharedKey từ DH) và IV kèm theo để giải mã
                            string msg = SimpleAES.DecryptString(p.Data, SimpleAES.DefaultKey, p.IV);
                            AppendChat($"{p.Sender}: {msg}\n", string.IsNullOrEmpty(p.Recipient) ? Color.Black : Color.DeepPink);
                            LogToHistory("History.txt", $"{p.Sender}: {msg}");
                        }
                        else if (p.Type == PacketType.Video)
                        {
                            if (currentRemoteCaller != p.Sender)
                            {
                                currentRemoteCaller = p.Sender;
                                LogToHistory("CallHistory.txt", $"NHẬN CUỘC GỌI TỪ: {p.Sender}");
                            }
                            using (var ms = new MemoryStream(p.Data))
                            {
                                if (picRemote.Image != null) picRemote.Image.Dispose();
                                picRemote.Image = Image.FromStream(ms);
                            }
                        }
                        else if (p.Type == PacketType.Auth) { lblStatus.Text = "ONLINE"; lblStatus.ForeColor = Color.Green; }
                        else if (p.Type == PacketType.File)
                        {
                            byte[] fileData = SimpleAES.DecryptBytes(p.Data, SimpleAES.DefaultKey, p.IV);
                            using (SaveFileDialog sfd = new SaveFileDialog() { FileName = p.FileName })
                            {
                                if (sfd.ShowDialog() == DialogResult.OK)
                                {
                                    File.WriteAllBytes(sfd.FileName, fileData);
                                    AppendChat($"Hệ thống: Đã nhận file [{p.FileName}]\n", Color.Green);
                                    LogToHistory("History.txt", $"[FILE] Nhận {p.FileName} từ {p.Sender}");
                                }
                            }
                        }
                        else if (p.Type == PacketType.KeyExchange)
                        {
                            using (var otherPubKey = ECDiffieHellman.Create())
                            {
                                otherPubKey.ImportSubjectPublicKeyInfo(p.Data, out _);
                                sharedKey = dh.DeriveKeyMaterial(otherPubKey.PublicKey);
                                AppendChat("Hệ thống: Kênh DH Sẵn sàng.\n", Color.Purple);
                            }
                        }
                    }));
                }
            }
            catch { }
            if (!IsDisposed) Invoke(new Action(() => Disconnect()));
        }

        // --- 8. CAMERA LOGIC (AForge) ---
        private void ToggleVideo() { if (isCalling) StopCamera(); else StartCamera(); }

        private void StartCamera()
        {
            if (videoDevices == null || videoDevices.Count == 0) { MessageBox.Show("Không tìm thấy Webcam!"); return; }
            isCalling = true;
            btnVideo.Text = "⏹ DỪNG GỌI"; btnVideo.BackColor = Color.Crimson;

            string target = txtRecipient.Text.Trim();
            LogToHistory("CallHistory.txt", $"BẮT ĐẦU GỌI TỚI: {(string.IsNullOrEmpty(target) ? "Nhóm" : target)}");

            videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            videoSource.NewFrame += Camera_NewFrame;
            videoSource.Start();
        }

        private void StopCamera()
        {
            isCalling = false;
            btnVideo.Text = "📹 BẮT ĐẦU GỌI"; btnVideo.BackColor = Color.SeaGreen;
            LogToHistory("CallHistory.txt", "KẾT THÚC CUỘC GỌI.");

            if (videoSource != null && videoSource.IsRunning) { videoSource.SignalToStop(); videoSource = null; }
            if (picLocal.Image != null) picLocal.Image = null;
        }

        private void Camera_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!isCalling || client == null) return;
            try
            {
                Bitmap frame = (Bitmap)eventArgs.Frame.Clone();
                if (picLocal.Image != null) picLocal.Image.Dispose();
                picLocal.Image = (Bitmap)frame.Clone();

                using (MemoryStream ms = new MemoryStream())
                {
                    frame.Save(ms, ImageFormat.Jpeg);
                    string target = ""; Invoke(new Action(() => target = txtRecipient.Text.Trim()));
                    SendPacket(new DataPacket { Type = PacketType.Video, Sender = myName, Recipient = target, Data = ms.ToArray() });
                }
                frame.Dispose();
            }
            catch { }
        }

        // --- 9. KẾT NỐI & GỬI DỮ LIỆU ---
        private void Connect()
        {
            try
            {
                client = new TcpClient("127.0.0.1", 9000);
                var stream = client.GetStream();
                writer = new StreamWriter(stream) { AutoFlush = true };
                reader = new StreamReader(stream);
                myName = txtUser.Text;
                SendPacket(new DataPacket { Type = PacketType.KeyExchange, Sender = myName, Data = dh.ExportSubjectPublicKeyInfo() });
                SendPacket(new DataPacket { Type = PacketType.Auth, Sender = myName, Password = txtPass.Text });
                new Thread(ReceiveLoop) { IsBackground = true }.Start();
                lblStatus.Text = "Đang xác thực..."; lblStatus.ForeColor = Color.Orange;
                btnConnect.Enabled = false; btnDisconnect.Enabled = true;
            }
            catch { MessageBox.Show("Lỗi: Server chưa bật!"); }
        }

        private void SendMsg()
        {
            if (string.IsNullOrWhiteSpace(txtMsg.Text)) return;
            byte[] iv;
            // Dòng này thực hiện mã hóa nội dung từ ô nhập tin nhắn
            byte[] encrypted = SimpleAES.EncryptString(txtMsg.Text, SimpleAES.DefaultKey, out iv);
            string target = txtRecipient.Text.Trim();
            // Đóng gói dữ liệu đã mã hóa và Vector khởi tạo (IV) vào DTO
            SendPacket(new DataPacket { Type = PacketType.Message, Sender = myName, Recipient = target, Data = encrypted, IV = iv });
            AppendChat($"Tôi: {txtMsg.Text}\n", Color.Blue);
            LogToHistory("History.txt", $"Tôi (tới {target}): {txtMsg.Text}");
            txtMsg.Clear();
        }

        private void SendFile()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                byte[] fileBytes = File.ReadAllBytes(ofd.FileName);
                byte[] iv;
                byte[] encryptedFile = SimpleAES.EncryptBytes(fileBytes, SimpleAES.DefaultKey, out iv);
                string target = txtRecipient.Text.Trim();
                SendPacket(new DataPacket { Type = PacketType.File, Sender = myName, Recipient = target, FileName = Path.GetFileName(ofd.FileName), Data = encryptedFile, IV = iv });
                AppendChat($"Tôi: Đã gửi file tới {(string.IsNullOrEmpty(target) ? "Tất cả" : target)}\n", Color.Blue);
                LogToHistory("History.txt", $"Tôi: Gửi file {ofd.FileName} tới {target}");
            }
        }

        private void SendPacket(DataPacket p) { try { lock (writer) writer.WriteLine(JsonSerializer.Serialize(p)); } catch { } }
        private void AppendChat(string m, Color c) { txtChat.SelectionColor = c; txtChat.AppendText(m); txtChat.ScrollToCaret(); }
        private void Disconnect() { StopCamera(); if (client != null) client.Close(); Invoke(new Action(() => { lblStatus.Text = "OFFLINE"; lblStatus.ForeColor = Color.Red; btnConnect.Enabled = true; btnDisconnect.Enabled = false; })); }
        protected override void OnFormClosing(FormClosingEventArgs e) { StopCamera(); Disconnect(); Environment.Exit(0); }
    }
}