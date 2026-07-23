namespace Com
{
    public interface ISerialPort
    {
        void Close();
        bool Open(string name);
        void Write(byte[] buffer);
        int Read(byte[] buffer);
    }
}