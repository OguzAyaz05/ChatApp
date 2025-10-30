// Form1.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatApp
{
    public partial class Form1 : Form
    {
        // UI
        RadioButton rbServer;
        RadioButton rbClient;
        TextBox txtIP;
        TextBox txtPort;
        Button btnStartStop;
        TextBox txtChat;      // multiline readonly
        TextBox txtMessage;   // single-line to type
        Button btnSend;
        Label lblStatus;

        // Networking
        TcpListener listener;
        TcpClient connectedClient; // server side: accepted client; client side: connected to server
        NetworkStream netStream;
        StreamReader reader;
        StreamWriter writer;
        CancellationTokenSource cts;

        bool isRunning = false;

        public Form1()
        {
            Text = "LAN Chat - Basit";
            Width = 700;
            Height = 480;
            StartPosition = FormStartPosition.CenterScreen;

            InitializeUI();
        }

        void InitializeUI()
        {
            rbServer = new RadioButton { Left = 10, Top = 10, Text = "Server", Checked = true };
            rbClient = new RadioButton { Left = 90, Top = 10, Text = "Client" };

            Label lblIp = new Label { Left = 10, Top = 40, Text = "Server IP (Client için):", AutoSize = true };
            txtIP = new TextBox { Left = 160, Top = 36, Width = 150, Text = "127.0.0.1" }; // default localhost for test

            Label lblPort = new Label { Left = 330, Top = 40, Text = "Port:", AutoSize = true };
            txtPort = new TextBox { Left = 370, Top = 36, Width = 80, Text = "9000" };

            btnStartStop = new Button { Left = 470, Top = 32, Width = 120, Text = "Start Server" };
            btnStartStop.Click += BtnStartStop_Click;

            lblStatus = new Label { Left = 10, Top = 70, Width = 600, Text = "Durum: Hazýr", AutoSize = false };

            txtChat = new TextBox { Left = 10, Top = 100, Width = 660, Height = 280, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            txtMessage = new TextBox { Left = 10, Top = 390, Width = 560, Height = 30 };
            txtMessage.KeyDown += TxtMessage_KeyDown;

            btnSend = new Button { Left = 580, Top = 388, Width = 90, Height = 34, Text = "Gönder" };
            btnSend.Click += BtnSend_Click;

            Controls.AddRange(new Control[] {
                rbServer, rbClient, lblIp, txtIP, lblPort, txtPort, btnStartStop, lblStatus, txtChat, txtMessage, btnSend
            });

            // When user toggles mode, update start button text
            rbServer.CheckedChanged += (s, e) => UpdateStartButtonText();
            rbClient.CheckedChanged += (s, e) => UpdateStartButtonText();
        }

        void UpdateStartButtonText()
        {
            if (isRunning) return;
            btnStartStop.Text = rbServer.Checked ? "Start Server" : "Connect";
        }

        private async void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (!isRunning)
            {
                int port = int.Parse(txtPort.Text);
                cts = new CancellationTokenSource();

                if (rbServer.Checked)
                {
                    await StartServerAsync(port, cts.Token);
                }
                else
                {
                    string ip = txtIP.Text.Trim();
                    await StartClientAsync(ip, port, cts.Token);
                }
            }
            else
            {
                StopAll();
            }
        }

        private void StopAll()
        {
            isRunning = false;
            btnStartStop.Text = rbServer.Checked ? "Start Server" : "Connect";
            lblStatus.Text = "Durum: Durduruldu";
            try
            {
                cts?.Cancel();
                reader?.Dispose();
                writer?.Dispose();
                netStream?.Dispose();
                connectedClient?.Close();
                listener?.Stop();
            }
            catch { }
        }

        private async Task StartServerAsync(int port, CancellationToken token)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                isRunning = true;
                btnStartStop.Text = "Stop";
                lblStatus.Text = $"Durum: Server dinliyor (port {port}) — client bekleniyor";
                AppendChat($"[System] Server baþladý - port {port}. Gelen client bekleniyor...");

                // Accept only one client (ödev için iki kiþi: server + 1 client)
                connectedClient = await listener.AcceptTcpClientAsync().WithCancellation(token);
                if (connectedClient == null) { StopAll(); return; }

                netStream = connectedClient.GetStream();
                reader = new StreamReader(netStream, Encoding.UTF8);
                writer = new StreamWriter(netStream, Encoding.UTF8) { AutoFlush = true };
                AppendChat($"[System] Client baðlandý: {connectedClient.Client.RemoteEndPoint}");
                lblStatus.Text = "Durum: Client baðlý. Mesajlaþabilirsiniz.";
                _ = Task.Run(() => ReceiveLoopAsync(reader, token));
            }
            catch (OperationCanceledException) { AppendChat("[System] Operation cancelled."); }
            catch (Exception ex)
            {
                AppendChat("[Hata] " + ex.Message);
                StopAll();
            }
        }

        private async Task StartClientAsync(string ip, int port, CancellationToken token)
        {
            try
            {
                var client = new TcpClient();
                isRunning = true;
                btnStartStop.Text = "Disconnect";
                lblStatus.Text = $"Durum: {ip}:{port} adresine baðlanýlýyor...";
                AppendChat($"[System] {ip}:{port} adresine baðlanýlýyor...");

                await client.ConnectAsync(ip, port).WithCancellation(token);

                connectedClient = client;
                netStream = client.GetStream();
                reader = new StreamReader(netStream, Encoding.UTF8);
                writer = new StreamWriter(netStream, Encoding.UTF8) { AutoFlush = true };

                AppendChat("[System] Server'a baðlandý. Mesajlaþabilirsiniz.");
                lblStatus.Text = "Durum: Baðlandý";
                _ = Task.Run(() => ReceiveLoopAsync(reader, token));
            }
            catch (OperationCanceledException) { AppendChat("[System] Operation cancelled."); }
            catch (Exception ex)
            {
                AppendChat("[Hata] " + ex.Message);
                StopAll();
            }
        }

        private async Task ReceiveLoopAsync(StreamReader r, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && connectedClient != null && connectedClient.Connected)
                {
                    var line = await r.ReadLineAsync().WithCancellation(token);
                    if (line == null) break;
                    AppendChat($"Karþý: {line}");
                }
            }
            catch { }
            finally
            {
                AppendChat("[System] Karþý taraf baðlantýyý kapattý veya baðlantý koptu.");
                StopAll();
            }
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            await SendCurrentMessageAsync();
        }

        private async Task SendCurrentMessageAsync()
        {
            var text = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (!isRunning || writer == null)
            {
                AppendChat("[System] Baðlý deðilsiniz.");
                return;
            }

            try
            {
                await writer.WriteLineAsync(text);
                AppendChat($"Ben: {text}");
                txtMessage.Clear();
            }
            catch (Exception ex)
            {
                AppendChat("[Hata - send] " + ex.Message);
            }
        }

        private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = SendCurrentMessageAsync();
            }
        }

        private void AppendChat(string text)
        {
            if (txtChat.InvokeRequired)
            {
                txtChat.BeginInvoke(new Action(() => {
                    txtChat.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
                    txtChat.SelectionStart = txtChat.Text.Length;
                    txtChat.ScrollToCaret();
                }));
            }
            else
            {
                txtChat.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
                txtChat.SelectionStart = txtChat.Text.Length;
                txtChat.ScrollToCaret();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            StopAll();
        }
    }

    // helper for cancellation support in async reads
    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            using var tcs = new CancellationTokenTaskSource(cancellationToken);
            var completed = await Task.WhenAny(task, tcs.Task);
            if (completed == tcs.Task) throw new OperationCanceledException(cancellationToken);
            return await task;
        }

        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            using var tcs = new CancellationTokenTaskSource(cancellationToken);
            var completed = await Task.WhenAny(task, tcs.Task);
            if (completed == tcs.Task) throw new OperationCanceledException(cancellationToken);
            await task;
        }

        private sealed class CancellationTokenTaskSource : IDisposable
        {
            private readonly TaskCompletionSource<bool> _tcs = new();
            private readonly CancellationTokenRegistration _reg;
            public Task Task => _tcs.Task;
            public CancellationTokenTaskSource(CancellationToken ct) => _reg = ct.Register(() => _tcs.TrySetResult(true));
            public void Dispose() => _reg.Dispose();
        }
    }
}
