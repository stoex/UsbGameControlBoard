using Microsoft.Extensions.Configuration;
using HidSharp;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static ETSSimulator.Program;

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

            // Check if test mode is selected in appsettings
            bool testMode = _configuration.GetSection("TestMode").Value == "1";

            if(!testMode)
            {
                UserInteraction();
            }

            // List with game control boards
            List<HidDevice> devices = new List<HidDevice>();

            // Get usb controllers
            while (true)
            {
                // Search for ETS2 process
                Process[] processes = Process.GetProcessesByName("eurotrucks2");
                Process process = processes.FirstOrDefault();

                // Get all connected hid devices
                var usbDevices = DeviceList.Local.GetHidDevices().ToList();

                // Get number of devices in appsettings
                int countUsedDevices = (int)_configuration.GetSection("UsbDevices").GetChildren().Count();
                for (int i = 0; i < countUsedDevices; i++)
                {
                    for(int j = 0; j < usbDevices.Count; j++)
                    {
                        string devicePath = _configuration.GetSection("UsbDevices:" + i).GetSection("DevicePath").Value;
                        if (usbDevices[j].DevicePath == devicePath)
                            devices.Add(usbDevices[j]);
                    }
                }

                if (process != null && devices.Count > 0)
                {
                    IntPtr handle = process.MainWindowHandle;
                    SetForegroundWindow(handle);
                    break;
                }  
                else if (devices.Count > 0 && testMode) 
                {
                    break;
                }

                await Task.Delay(1000);
            }

            if (testMode)
            {
                CheckBitStream(devices[0]);
            }
            else
            {
                await EvaluateUsbStreams(devices);
            }        
        }

        static void UserInteraction()
        {
            // Give hint to reset all Buttons
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Warning: Before starting the game please turn of all buttons!");
            Console.ForegroundColor = ConsoleColor.White;

            ConsoleKey response;
            do
            {
                Console.Write("Confirm by pressing \"y\": ");
                response = Console.ReadKey(false).Key;
                if (response != ConsoleKey.Enter)
                {
                    Console.WriteLine();
                }
            } while (response != ConsoleKey.Y);
            Console.WriteLine();

            // Ask if game should be started
            Console.Write("Start game automatically by pressing \"y\": ");
            response = Console.ReadKey(false).Key;

            if (response == ConsoleKey.Y)
                Process.Start(@"C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2\bin\win_x64\eurotrucks2");

            Console.WriteLine();
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

            using (var stream = device.Open())
            {
                while (true)
                {
                    bytesRead = await stream.ReadAsync(inputBuffer, 0, inputBuffer.Length);

                    if (bytesRead > 0 && ready)
                    {
                        // Set initial byte
                        byteNo = 6;
                        bits = new BitArray(new byte[] { inputBuffer[byteNo] });
                        compareBits = new BitArray(new byte[] { compareBytes[byteNo] });

                        // Digital Input 0 (K1)
                        bitNo = 4;
                        configValue = config.GetSection("DI_0").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 1 (K2)
                        bitNo = 5;
                        configValue = config.GetSection("DI_1").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 2 (K3)
                        bitNo = 6;
                        configValue = config.GetSection("DI_2").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 3 (K4)
                        bitNo = 7;
                        configValue = config.GetSection("DI_3").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Change Byte
                        byteNo = 7;
                        bits = new BitArray(new byte[] { inputBuffer[byteNo] });
                        compareBits = new BitArray(new byte[] { compareBytes[byteNo] });

                        // Digital Input 4 (L2)
                        bitNo = 0;
                        configValue = config.GetSection("DI_4").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 5 (R2)
                        bitNo = 1;
                        configValue = config.GetSection("DI_5").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 6 (L1)
                        bitNo = 2;
                        configValue = config.GetSection("DI_6").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 7 (R1)
                        bitNo = 3;
                        configValue = config.GetSection("DI_7").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 8 (SE)
                        bitNo = 4;
                        configValue = config.GetSection("DI_8").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 9 (ST)
                        bitNo = 5;
                        configValue = config.GetSection("DI_9").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 10 (K11)
                        bitNo = 6;
                        configValue = config.GetSection("DI_10").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);

                        // Digital Input 11 (K12)
                        bitNo = 7;
                        configValue = config.GetSection("DI_11").Value;
                        SimulateKeyIfDiffers(compareBits[bitNo], bits[bitNo], configValue);
                    }

                    compareBytes = (byte[])inputBuffer.Clone();
                    ready = true;
                }
            }
        }

        static void SimulateKeyIfDiffers(bool compareBit, bool bit, string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                ScanCode scanCode = (ScanCode)Enum.Parse(typeof(ScanCode), key);

                if (compareBit && !bit)
                    SimulateKeyboardInput(scanCode);
                else if (!compareBit && bit)
                    SimulateKeyboardInput(scanCode);
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
            bool ready = false;

            byte[] inputBuffer = new byte[device.GetMaxInputReportLength()];
            byte[] compareBytes = null;

            using (var stream = device.Open())
            {           
                while (true)
                {
                    int bytesRead = stream.Read(inputBuffer, 0, inputBuffer.Length);

                    if(ready)
                    {
                        for(int i = 0; i < inputBuffer.Length; i++)
                        {
                            if (inputBuffer[i] != compareBytes[i])
                            {
                                string byteNo = i.ToString();
                                string bitNo = "";

                                BitArray bits = new BitArray(new byte[] { inputBuffer[i] });
                                BitArray compareBits = new BitArray(new byte[] { compareBytes[i] });

                                for(int j = 0; j < bits.Length; j++)
                                {
                                    if (bits[j] != compareBits[j])
                                    {
                                        bitNo = j.ToString();
                                    }
                                }

                                if(byteNo != "3")
                                    Console.WriteLine($"Byte: {byteNo}; Bit: {bitNo}");
                            }
                        }
                    }

                    compareBytes = (byte[])inputBuffer.Clone();
                    ready = true;

                    //Console.WriteLine("Bitstream:");

                    //for (var i = 0; i < bytesRead; i++)
                    //{
                    //    var bits = Convert.ToString(inputBuffer[i], 2).PadLeft(8, '0');
                    //    Console.WriteLine($"Byte{i}: {bits}");
                    //}

                    //Console.WriteLine("");
                }
            }
        }
    }
}