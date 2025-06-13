using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RawInputScannerApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // RawInput 定数
        private const int WM_INPUT = 0x00FF;
        private const int RID_INPUT = 0x10000003;
        private const int RIM_TYPEKEYBOARD = 1;

        // 各スキャナごとの一時バッファと履歴
        private Dictionary<IntPtr, string> deviceBuffers = new();
        private Dictionary<IntPtr, List<string>> deviceHistories = new();

        // 最初に接続された2台のスキャナ識別用
        private IntPtr? device1 = null;
        private IntPtr? device2 = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // RawInputイベントを受取るためにウィンドウハンドル取得
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(WndProc);

            // RawInputデバイスを登録
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01;
            rid[0].usUsage = 0x06;
            rid[0].dwFlags = 0;
            rid[0].hwndTarget = hwnd;

            RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                uint dwSize = 0;
                GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);

                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) != dwSize)
                        return IntPtr.Zero;

                    RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
                    if (raw.header.dwType == RIM_TYPEKEYBOARD)
                    {
                        IntPtr device = raw.header.hDevice;

                        // 最初に使用された2台を記憶
                        if (device1 == null)
                            device1 = device;
                        else if (device2 == null && device != device1)
                            device2 = device;

                        // バッファ初期化
                        if (!deviceBuffers.ContainsKey(device))
                            deviceBuffers[device] = "";

                        // キー押下イベントのみ処理
                        if (raw.keyboard.Message == 0x0100)
                        {
                            char ch = KeyToChar(raw.keyboard.VKey, raw.keyboard.MakeCode);
                            if (ch != '\0')
                            {
                                if (ch == '\r') // Enter → バーコード確定
                                {
                                    string code = deviceBuffers[device];
                                    deviceBuffers[device] = "";

                                    if (!deviceHistories.ContainsKey(device))
                                        deviceHistories[device] = new List<string>();

                                    deviceHistories[device].Add(code);

                                    // スキャナ別にListBoxに出力
                                    if (device == device1)
                                        Scanner1Box.Items.Add(code);
                                    else if (device == device2)
                                        Scanner2Box.Items.Add(code);

                                    // 一致確認
                                    int count = deviceHistories[device].FindAll(c => c == code).Count;
                                    if (count == 2)
                                    {
                                        string matchMsg = $"✔ 一致確認: {code}";
                                        if (device == device1)
                                            Scanner1Box.Items.Add(matchMsg);
                                        else if (device == device2)
                                            Scanner2Box.Items.Add(matchMsg);
                                    }
                                }
                                else
                                {
                                    // バーコード文字列を蓄積
                                    deviceBuffers[device] += ch;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        // 仮想キーコード → 文字変換（ToUnicodeExを使用）
        private char KeyToChar(int vkey, int scanCode)
        {
            byte[] keyboardState = new byte[256];
            if (!GetKeyboardState(keyboardState))
                return '\0';

            StringBuilder sb = new(2);
            int result = ToUnicodeEx((uint)vkey, (uint)scanCode, keyboardState, sb, sb.Capacity, 0, GetKeyboardLayout(0));
            return result > 0 ? sb[0] : '\0';
        }

        #region Raw Input & WinAPI 定義

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWKEYBOARD keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("User32.dll")]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll")]
        static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll")]
        static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff, uint wFlags, IntPtr dwhkl);

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint idThread);

        #endregion
    }
}