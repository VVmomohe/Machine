using System.Text;
using UnityEngine;

namespace Com.AndroidCOM
{
    public class AndroidSerialPort : ISerialPort
    {
        public void Close()
        {
            AndroidSerialPortDLL.Serial_Close();
        }

        public bool Open(string name)
        {
            var devPath = name;
            var deviceModel = SystemInfo.deviceModel;

            var buad = -1;
            if (devPath == null)
            {
                if (deviceModel.Contains("3288"))
                {
                    devPath = "/dev/ttyS1";
                }
                else if (deviceModel.Contains("3566"))
                {
                    buad = 115200;
                    devPath = "/dev/ttyS1";
                }
                else if (deviceModel.Contains("3128"))
                {
                    devPath = "/dev/ttyS2";
                }
                else if (deviceModel.Contains("ZC-356"))
                {
                    devPath = "/dev/ttyS0";
                }
                else if (deviceModel.Contains("H618"))
                {
                    buad = 9600;
                    devPath = "/dev/ttyAS3";
                }
            }

            Debug.Log("open android serial port:" + devPath);

            var path = Encoding.UTF8.GetBytes(devPath);
            if (buad != -1) return AndroidSerialPortDLL.Serial_Open_Baud(path, buad);

            return AndroidSerialPortDLL.Serial_Open(path);
        }

        public void Write(byte[] buffer)
        {
            AndroidSerialPortDLL.Serial_SendData(buffer, buffer.Length);
        }

        public int Read(byte[] buffer)
        {
            return AndroidSerialPortDLL.Serial_RecvData(buffer, buffer.Length);
        }
    }
}