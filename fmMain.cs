using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;
using ExtensionMethods;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace warmup
{
    using Request = WFunctions.Request;
    using Options = WFunctions.Options;

    public partial class fmMain : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public RECT(System.Drawing.Rectangle r) : this(r.Left, r.Top, r.Right, r.Bottom) { }

            public int X
            {
                get { return Left; }
                set { Right -= (Left - value); Left = value; }
            }

            public int Y
            {
                get { return Top; }
                set { Bottom -= (Top - value); Top = value; }
            }

            public int Height
            {
                get { return Bottom - Top; }
                set { Bottom = value + Top; }
            }

            public int Width
            {
                get { return Right - Left; }
                set { Right = value + Left; }
            }

            public System.Drawing.Point Location
            {
                get { return new System.Drawing.Point(Left, Top); }
                set { X = value.X; Y = value.Y; }
            }

            public System.Drawing.Size Size
            {
                get { return new System.Drawing.Size(Width, Height); }
                set { Width = value.Width; Height = value.Height; }
            }

            public static implicit operator System.Drawing.Rectangle(RECT r)
            {
                return new System.Drawing.Rectangle(r.Left, r.Top, r.Width, r.Height);
            }

            public static implicit operator RECT(System.Drawing.Rectangle r)
            {
                return new RECT(r);
            }

            public static bool operator ==(RECT r1, RECT r2)
            {
                return r1.Equals(r2);
            }

            public static bool operator !=(RECT r1, RECT r2)
            {
                return !r1.Equals(r2);
            }

            public bool Equals(RECT r)
            {
                return r.Left == Left && r.Top == Top && r.Right == Right && r.Bottom == Bottom;
            }

            public override bool Equals(object obj)
            {
                if (obj is RECT)
                    return Equals((RECT)obj);
                else if (obj is System.Drawing.Rectangle)
                    return Equals(new RECT((System.Drawing.Rectangle)obj));
                return false;
            }

            public override int GetHashCode()
            {
                return ((System.Drawing.Rectangle)this).GetHashCode();
            }

            public override string ToString()
            {
                return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{{Left={0},Top={1},Right={2},Bottom={3}}}", Left, Top, Right, Bottom);
            }
        }

        [Flags]
        public enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(uint hObject);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, uint lpBaseAddress, byte[] lpBuffer, uint dwSize, ref uint lpNumberOfBytesRead);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
        
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd,
                              int Msg, int wParam, int lParam);

    
        enum States
        {
            NONE = -1,
            GAME_NOT_RUNNING = 0,
            NOT_IN_GAME = 2,
            MATCH_FOUND = 3,
            WAITING_FOR_PLAYERS = 5,
            ALL_PLAYERS_CONNECTED = 6,
            GAME_STARTED = 7
        }

        States lastState = States.NONE;

        string lastMessage = string.Empty;

        string currentSteamId = string.Empty;



        public fmMain()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void fmMain_Load(object sender, EventArgs e)
        {
            bgwMain.RunWorkerAsync();
        }


        private Options DecodeResponse(string response)
        {
            Options result;

            List<string> parts;


            result = new Options();
            response = response.GetStringBetween("<data>", "</data>");

            foreach (string s in response.Split("&").ToList())
            {
                parts = s.Split(new char[] { '=' }, 2).ToList();
                result[parts[0]] = parts[1];
            }

            return result;
        }


        private bool IsGameRunning(Process p)
        {
            bool bRunning;


            bRunning = false;

            try
            {
                bRunning = !p.HasExited;
            }
            catch { }

            return bRunning;
        }

        private void ReportLastError(string lastError)
        {
            labNotice.Text = "         " + lastError;   
        }

        private uint FindPattern(byte[] buffer, string pattern, string mask)
        {
            uint dwResult;


            int patternIndex = 0;
            dwResult = 0;


            for (uint i = 0; i < buffer.Length - mask.Length; i++)
            {
                if ((byte)buffer[i] == (byte)pattern[patternIndex] || mask[patternIndex] == '?')
                {
                    patternIndex++;

                    if (patternIndex == mask.Length)
                    {
                        dwResult = i - (uint)mask.Length + 1;
                        break;
                    }
                }
                else
                    patternIndex = 0;

                //if (i == 0x485077)
                //{
                //    ReportLastError("");

                //}
                //if ((byte)buffer[i] == (byte)(pattern[0] & 0xFF) || mask[0] == '?')
                //{
                //    uint startSearch = i;

                //    for(int k = 0; k != mask.Length; k++)
                //    {
                //        if ((byte)(pattern[k] & 0xFF) != (byte)buffer[startSearch] && mask[k] != '?')
                //            break;

                //        if (k > 7)
                //        {

                //        }
                //        if (((byte)(pattern[k] & 0xFF) == (byte)buffer[startSearch] || mask[k] == '?') && k == mask.Length)
                //            dwResult = i;

                //        startSearch++;
                //    }
                //}
            }

            return dwResult;
        }

        private bool IsPlayerInGame(IntPtr hGame, uint dwClient, uint dwOffset)
        {
            bool bIsPlayerInGame;
            
            byte[] buffer;

            uint dwBytesRead,
                dwAddress;


            dwBytesRead = 0;
            buffer = new byte[4];


            ReadProcessMemory(hGame, dwClient + dwOffset, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress + 0xe8, buffer, 4, ref dwBytesRead);

            bIsPlayerInGame = BitConverter.ToUInt32(buffer, 0) == 6;

            return bIsPlayerInGame;
        }

        private float GetCurrentTime(IntPtr hGame, uint dwClient, uint dwOffset)
        {
            float fResult;

            byte[] buffer;

            uint dwBytesRead,
                dwAddress;


            fResult = 0.0f;
            dwBytesRead = 0;
            buffer = new byte[4];


            ReadProcessMemory(hGame, dwClient + dwOffset, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress + 0x10, buffer, 4, ref dwBytesRead);

            fResult = BitConverter.ToSingle(buffer, 0);

            return fResult;
        }

        private float GetStartTime(IntPtr hGame, uint dwClient, uint dwOffset)
        {
            float fResult;

            byte[] buffer;

            uint dwBytesRead,
                dwAddress;


            fResult = 0.0f;
            dwBytesRead = 0;
            buffer = new byte[4];


            ReadProcessMemory(hGame, dwClient + dwOffset, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            fResult = BitConverter.ToSingle(buffer, 0);

            return fResult;
        }

        private float GetTimeOffset(IntPtr hGame, uint dwClient, uint dwOffset)
        {
            float fResult;

            byte[] buffer;

            uint dwBytesRead,
                dwAddress;


            fResult = 0.0f;
            dwBytesRead = 0;
            buffer = new byte[4];


            ReadProcessMemory(hGame, dwClient + dwOffset, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress + 0x28, buffer, 4, ref dwBytesRead);

            fResult = BitConverter.ToSingle(buffer, 0);

            return fResult;
        }

        private bool IsMatchFound(IntPtr hGame, uint dwClient, uint dwOffset)
        {
            bool bResult;

            byte[] buffer;

            uint dwBytesRead,
                dwAddress;


            bResult = false;
            dwBytesRead = 0;
            buffer = new byte[4];


            ReadProcessMemory(hGame, dwClient + dwOffset, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            dwAddress = BitConverter.ToUInt32(buffer, 0);

            ReadProcessMemory(hGame, dwAddress, buffer, 4, ref dwBytesRead);

            bResult = BitConverter.ToSingle(buffer, 0) != 0;

            return bResult;
        }

        private void UpdateState(States state)
        {
            Request req;

            Options postData;


            if (state != lastState)
            {
                postData = new Options();
                postData["steamId"] = currentSteamId;
                postData["action"] = "updateState";
                postData["state"] = state.ToString();

                req = new Request();
                req.method = "POST";
                req.url = "http://www.doyour.biz/api.php";
                req.accept = "*/*";
                req.contentType = "text/html; charset=utf-8";
                req.userAgent = "DoYourBiz Client";
                req.DownloadString(postData.Http_build_query());


                switch (state)
                {
                    case States.GAME_NOT_RUNNING:
                        setStatus("CSGO is not currently running.");
                        break;

                    case States.NOT_IN_GAME:
                        setStatus("You are currently not in a match.");
                        break;

                    case States.MATCH_FOUND:
                        setStatus("A match was found for you.");
                        break;
                        
                    case States.WAITING_FOR_PLAYERS:
                        setStatus("Waiting for all players to connect.");
                        break;

                    case States.ALL_PLAYERS_CONNECTED:
                        setStatus("All players have connected.");
                        break;

                    case States.GAME_STARTED:
                        setStatus("The match has now started.");
                        break;
                }
            }
        }

        private string GetCurrentSteamUser()
        {
            Process steam;

            Process[] temp;

            string fileName,
                   contents;

            string result;

            FileStream fs;

            
            steam = null;
            result = string.Empty;

            try
            {
                temp = Process.GetProcessesByName("steam");

                if (temp.Length == 1)
                    steam = temp[0];
                else if (temp.Length > 1)
                    ReportLastError("You have more than one steam running.");
                else
                {
                    temp = Process.GetProcessesByName("steam.exe");

                    if (temp.Length == 1)
                        steam = temp[0];
                    else if (temp.Length > 1)
                        ReportLastError("You have more than one steam running.");
                }


                if (steam != null)
                {
                    fileName = steam.MainModule.FileName.ToLower();

                    fileName = fileName.Replace("steam.exe.exe", "logs\\connection_log.txt").Replace("steam.exe", "logs\\connection_log.txt");

                    if (File.Exists(fileName) == true)
                    {
                        fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                        using (StreamReader sr = new StreamReader(fs))
                            contents = sr.ReadToEnd();

                        contents = contents.GetStringBetween("RecvMsgClientLogOnResponse() : [", "]");
                        result = contents;
                    }
                }
            }
            catch { }

            return result;
        }

        private void bgwMain_DoWork(object sender, DoWorkEventArgs e)
        {
            Process game;

            Process[] processes;

            uint dwClient,
                 dwSize,
                 dwBytesRead;

            byte[] buffer;

            IntPtr hGame;

            uint dwCurrentWarmupAddress,
                 dwWarmupStartAddress,
                 dwWarmupOffsetAddress,
                 dwInGameAddress,
                 dwMatchFound,
                 dwEngine;

            float fCurrentTime,
                fStartTime;

            float fTimeOffset;

            int iFinalTime;

            Point ptTarget;

            RECT rect;

            string steamId,
                   lastSteamId;


            game = null;
            lastSteamId = string.Empty;

            while (true)
            {
                try
                {
                    steamId = GetCurrentSteamUser();

                    if (steamId != string.Empty)
                    {
                        if (lastSteamId != steamId)
                        {
                            lastSteamId = steamId;
                            setStatus("Current steam user: " + steamId);
                        }

                        if (game == null)
                        {
                            processes = Process.GetProcessesByName("csgo");

                            if (processes.Length > 0)
                            {
                                if (processes.Length == 1)
                                {
                                    dwEngine = 0;
                                    dwClient = 0;
                                    dwWarmupStartAddress = 0;
                                    dwCurrentWarmupAddress = 0;
                                    dwWarmupOffsetAddress = 0;
                                    dwInGameAddress = 0;
                                    dwMatchFound = 0;

                                    game = processes[0];
                                    hGame = OpenProcess(ProcessAccessFlags.All, false, (uint)game.Id);

                                    if (hGame != IntPtr.Zero)
                                    {
                                        foreach (ProcessModule mod in game.Modules)
                                        {
                                            if (mod.FileName.ToLower().EndsWith("\\client.dll") == true)
                                            {
                                                dwClient = (uint)mod.BaseAddress;
                                                buffer = new byte[mod.ModuleMemorySize];
                                                dwBytesRead = 0;

                                                ReadProcessMemory(hGame, dwClient, buffer, (uint)buffer.Length, ref dwBytesRead);

                                                if (dwBytesRead != buffer.Length)
                                                {
                                                    dwClient = 0;
                                                    ReportLastError("Unable to read client.dll");
                                                }
                                                else
                                                {
                                                    dwMatchFound = FindPattern(buffer, "\x8B\x0D\x00\x00\x00\x00\x85\xC9\x74\x06\x51\xE8\x00\x00\x00\x00\xC7\x05\x00\x00\x00\x00\x00\x00\x00\x00\x5F", "xx????xxxxxx????xx????????x") + 2;
                                                    dwCurrentWarmupAddress = FindPattern(buffer, "\x8B\x0D\x00\x00\x00\x00\xF3\x0F\x2C\x75", "xx????xxxx") + 2;
                                                    dwWarmupStartAddress = FindPattern(buffer, "\xF3\x0F\x10\x0D\x00\x00\x00\x00\xF3\x0F\x58\xC1\x5E", "xxxx????xxxxx") + 4;
                                                    dwWarmupOffsetAddress = FindPattern(buffer, "\x8B\x0D\x00\x00\x00\x00\xF3\x0F\x5C\x05", "xx????xxxx") + 2;
                                                }
                                            }
                                            else if (mod.FileName.ToLower().EndsWith("\\engine.dll") == true)
                                            {
                                                dwEngine = (uint)mod.BaseAddress;
                                                buffer = new byte[mod.ModuleMemorySize];
                                                dwBytesRead = 0;

                                                ReadProcessMemory(hGame, dwEngine, buffer, (uint)buffer.Length, ref dwBytesRead);

                                                if (dwBytesRead != buffer.Length)
                                                {
                                                    dwClient = 0;
                                                    ReportLastError("Unable to read engine.dll");
                                                }
                                                else
                                                {
                                                    dwInGameAddress = FindPattern(buffer, "\xA1\x00\x00\x00\x00\x8B\x88\x00\x00\x00\x00\x85\xC9\x74\x15", "x????xx????xxxx") + 1;
                                                }
                                            }
                                        }

                                        if (dwClient != 0)
                                        {
                                            while (true)
                                            {
                                                if (IsGameRunning(game) == true)
                                                {
                                                    if (IsMatchFound(hGame, dwClient, dwMatchFound) == true)
                                                    {
                                                        UpdateState(States.MATCH_FOUND);

                                                        GetClientRect(game.MainWindowHandle, out rect);

                                                        ptTarget = new Point((int)((float)rect.Right * 0.25f), (int)((float)rect.Bottom * 0.24f));

                                                        ClientToScreen(game.MainWindowHandle, ref ptTarget);

                                                        //Cursor.Position = ptTarget;

                                                        //mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)ptTarget.X, (uint)ptTarget.Y, 0, 0);
                                                    }
                                                    else
                                                    {
                                                        if (IsPlayerInGame(hGame, dwEngine, dwInGameAddress) == true)
                                                        {
                                                            fTimeOffset = GetTimeOffset(hGame, dwClient, dwWarmupOffsetAddress);
                                                            fStartTime = GetStartTime(hGame, dwClient, dwWarmupStartAddress);
                                                            fCurrentTime = GetCurrentTime(hGame, dwClient, dwCurrentWarmupAddress);

                                                            iFinalTime = (int)(fStartTime - fCurrentTime + fTimeOffset);

                                                            if (iFinalTime < 59)
                                                            {
                                                                UpdateState(States.ALL_PLAYERS_CONNECTED);
                                                            }
                                                            else if (iFinalTime <= 0)
                                                            {
                                                                UpdateState(States.GAME_STARTED);
                                                            }
                                                            else
                                                            {
                                                                UpdateState(States.WAITING_FOR_PLAYERS);
                                                            }

                                                            //setStatus("Time: " + iFinalTime / 60 + ":" + iFinalTime % 60);
                                                        }
                                                        else
                                                        {
                                                            UpdateState(States.NOT_IN_GAME);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    game = null;
                                                    break;
                                                }

                                                Thread.Sleep(1000);
                                            }
                                        }
                                        else
                                        {
                                            ReportLastError("Unable to find client.dll");
                                            game = null;
                                        }
                                    }
                                    else
                                    {
                                        ReportLastError("Unable to open a handle to CSGO");
                                        game = null;
                                    }
                                }
                                else
                                    ReportLastError("More than one instance of CSGO is running");
                            }
                        }

                        if (game == null)
                            UpdateState(States.GAME_NOT_RUNNING);
                    }
                    else
                        ReportLastError("Steam is not running.");
                }
                catch (Exception x)
                {
                    x.ToString();
                    game = null;
                }

                Thread.Sleep(1000);
            }
        }


        private void setStatus(string s)
        {
            int lines;

            List<string> statuses;


            if (s != lastMessage)
            {
                lines = tbStatus.Height / tbStatus.Font.Height;
                statuses = tbStatus.Text.Split('\n').ToList();

                if (statuses.Count > lines)
                {
                    statuses.RemoveRange(lines, statuses.Count - lines);

                    tbStatus.Text = string.Empty;

                    for (int i = 0; i < statuses.Count; i++)
                        tbStatus.Text += statuses[i] + "\n";
                }

                lastMessage = s;
                tbStatus.Text = (s + "\r\n" + tbStatus.Text).Trim('\n');
            }
        }

        private void fmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(0);
        }

        private void tbStatus_Enter(object sender, EventArgs e)
        {
            this.ActiveControl = label1;
        }

        private void fmMain_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void pictureBox1_MouseEnter(object sender, EventArgs e)
        {
            pictureBox1.Image = warmup.Properties.Resources.a;
        }

        private void pictureBox1_MouseLeave(object sender, EventArgs e)
        {
            pictureBox1.Image = warmup.Properties.Resources.b;
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            niMain.ShowBalloonTip(10000, "Do Your Biz", "This tool has been minimized to the tray. Close Here.", ToolTipIcon.Info);
        }
        
        private void exitToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void niMain_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
        }

        private void showToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
        }
    }
}
