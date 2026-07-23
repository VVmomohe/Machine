using System.Runtime.InteropServices;

namespace Com.AndroidCOM
{
    public static class AndroidSerialPortDLL
    {
        [DllImport("serialport")]
        public static extern bool Serial_Open_Baud(byte[] dev_path, int baud);

        [DllImport("serialport")]
        public static extern bool Serial_Open(byte[] dev_path);

        [DllImport("serialport")]
        public static extern void Serial_Close();

        [DllImport("serialport")]
        public static extern int Serial_SendData(byte[] com_data, int size);

        [DllImport("serialport")]
        public static extern int Serial_RecvData(byte[] com_data, int size);
    }
}