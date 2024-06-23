using Microsoft.Extensions.Configuration;
using HidSharp;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ETSSimulator
{
    public class Program
    {
        static IConfiguration _configuration;

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HardwareInput
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mi;
            [FieldOffset(0)] public KeyboardInput ki;
            [FieldOffset(0)] public HardwareInput hi;
        }

        public struct Input
        {
            public int type;
            public InputUnion u;
        }

        [Flags]
        public enum InputType
        {
            Mouse = 0,
            Keyboard = 1,
            Hardware = 2
        }

        [Flags]
        public enum KeyEventF
        {
            KeyDown = 0x0000,
            ExtendedKey = 0x0001,
            KeyUp = 0x0002,
            Unicode = 0x0004,
            Scancode = 0x0008
        }

        public enum ScanCode : ushort
        {
            a = 0x1e,
            b = 0x30,
            c = 0x2e,
            d = 0x20,
            e = 0x12,
            f = 0x21,
            g = 0x22,
            h = 0x23,
            i = 0x17,
            j = 0x24,
            k = 0x25,
            l = 0x26,
            m = 0x32,
            n = 0x31,
            o = 0x18,
            p = 0x19,
            q = 0x10,
            r = 0x13,
            s = 0x1f,
            t = 0x14,
            u = 0x16,
            v = 0x2f,
            w = 0x11,
            x = 0x2d,
            y = 0x15,
            z = 0x2c
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        static async Task Main(string[] args)
        {
            // Get data form configuration file
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false);
            _configuration = builder.Build();

            // List with game control boards
            List<HidDevice> devices = new List<HidDevice>();

            // Start ET2 executable
            //Process.Start(@"C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2\bin\win_x64\eurotrucks2");

            while (true)
            {
                // Search for ETS2 process
                Process[] processes = Process.GetProcessesByName("WINWORD"); //eurotrucks2
                Process process = processes.FirstOrDefault();

                // First usb game control board (VID = 121; PID = 6)
                var device1 = DeviceList.Local.GetHidDeviceOrNull(121, 6);
                if (device1 != null)
                    devices.Add(device1);

                // Second usb game control board
                //var device2 = DeviceList.Local.GetHidDeviceOrNull(121, 6);
                //if (device2 != null)
                //    devices.Add(device2);

                if (/*process != null &&*/ devices.Count > 0)
                {
                    IntPtr handle = process.MainWindowHandle;
                    SetForegroundWindow(handle);
                    break;
                }  

                await Task.Delay(1000);
            }

            await EvaluateUsbStreams(devices); 
        }

        /// <summary>
        /// Create a task for multiple HidDevices to evaluate their usb stream
        /// </summary>
        /// <param name="devices">List of HidDevices</param>
        /// <returns>Tasks that evaluate each device</returns>
        static async Task EvaluateUsbStreams(List<HidDevice> devices)
        {
            List<Task> tasks = new List<Task>();

            for(var i = 0; i < devices.Count; i++)
            {
                tasks.Add(EvaluateUsbStream(devices[i], _configuration.GetSection("UsbDevices:" + i)));
            }

            await Task.WhenAll(tasks);
        }

        static async Task EvaluateUsbStream(HidDevice device, IConfigurationSection config)
        {
            bool ready = false;

            byte[] inputBuffer = new byte[device.GetMaxInputReportLength()];
            byte[] compareBytes = null;

            int bytesRead, byteNo, bitNo;
            BitArray bits, compareBits;
            string configValue;
            ScanCode scanCode;

            using (var stream = device.Open())
            {
                while (true)
                {
                    bytesRead = await stream.ReadAsync(inputBuffer, 0, inputBuffer.Length);

                    if (bytesRead > 0 && ready)
                    {
                        byteNo = 6;
                        bits = new BitArray(new byte[] { inputBuffer[byteNo] });
                        compareBits = new BitArray(new byte[] { compareBytes[byteNo] });

                        // Digital Input K1
                        bitNo = 4;
                        configValue = config.GetSection("DI_K1").Value;

                        if(!string.IsNullOrEmpty(configValue)) 
                        {
                            scanCode = (ScanCode)Enum.Parse(typeof(ScanCode), configValue);

                            if (compareBits[bitNo] && !bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                            else if (!compareBits[bitNo] && bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                        }

                        // Digital Input K2
                        bitNo = 5;
                        configValue = config.GetSection("DI_K2").Value;

                        if (!string.IsNullOrEmpty(configValue))
                        {
                            scanCode = (ScanCode)Enum.Parse(typeof(ScanCode), configValue);

                            if (compareBits[bitNo] && !bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                            else if (!compareBits[bitNo] && bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                        }

                        // Digital Input K3
                        bitNo = 6;
                        configValue = config.GetSection("DI_K3").Value;

                        if (!string.IsNullOrEmpty(configValue))
                        {
                            scanCode = (ScanCode)Enum.Parse(typeof(ScanCode), configValue);

                            if (compareBits[bitNo] && !bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                            else if (!compareBits[bitNo] && bits[bitNo])
                                SimulateKeyboardInput(scanCode);
                        }                   
                    }

                    compareBytes = (byte[])inputBuffer.Clone();
                    ready = true;
                }
            }
        }

        static void SimulateKeyboardInput(ScanCode scanCode)
        {
            Input[] inputs = new Input[]
            {
                new Input
                {
                    type = (int)InputType.Keyboard,
                    u = new InputUnion
                    {
                        ki = new KeyboardInput
                        {
                            wVk = 0,
                            wScan = (ushort)scanCode,
                            dwFlags = (uint)(KeyEventF.KeyDown | KeyEventF.Scancode),
                            dwExtraInfo = GetMessageExtraInfo()
                        }
                    }
                },
                new Input
                {
                    type = (int)InputType.Keyboard,
                    u = new InputUnion
                    {
                        ki = new KeyboardInput
                        {
                            wVk = 0,
                            wScan = (ushort)scanCode,
                            dwFlags = (uint)(KeyEventF.KeyUp | KeyEventF.Scancode),
                            dwExtraInfo = GetMessageExtraInfo()
                        }
                    }
                }
            };

            uint intReturn = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
            
            if(intReturn != inputs.Length)
            {
                Console.WriteLine("Error SendInput-Method: " + Marshal.GetLastWin32Error());
            }
        }

        static void CheckBitStream(HidDevice device)
        {
            using (var stream = device.Open())
            {
                byte[] inputBuffer = new byte[device.GetMaxInputReportLength()];

                while (true)
                {
                    int bytesRead = stream.Read(inputBuffer, 0, inputBuffer.Length);

                    Console.WriteLine("Bitstream:");

                    for (var i = 0; i < bytesRead; i++)
                    {
                        var bits = Convert.ToString(inputBuffer[i], 2).PadLeft(8, '0');
                        Console.WriteLine($"Byte{i}: {bits}");
                    }

                    Console.WriteLine("");
                    Thread.Sleep(1000);
                }
            }
        }
    }
}