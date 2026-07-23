using System;
using Com.Tool;
using UnityEngine;


/// <summary>
///     串口指令解析处理
/// </summary>
public static class ComHelper
{
    public enum ComType : byte
    {
        None = 0,
        PUSH,
        REQ,
        RESP,
    }

 

    public static bool TryParse(byte[] comData, out CommandEnum comm, out ComType t, out byte[] payload)
    {
        try
        {
            // pid = GetPackageId(comData);
            comm = (CommandEnum)comData[CommIndex];
            t = (ComType)comData[TypeIndex];

            var dataLen = comData[PayloadLen];
            payload = new byte[dataLen];
            Array.Copy(comData, PlayLoadIndex, payload, 0, dataLen);
        }
        catch (Exception e)
        {
            comm = CommandEnum.UnKnow;
            payload = comData;
            t = ComType.None;
            return false;
        }

        return true;
    }

    public static CommandEnum GetComm(byte[] comData)
    {
        try
        {
            return (CommandEnum)comData[CommIndex];
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            return CommandEnum.UnKnow;
        }
    }


    public static byte[] BuildPackage(byte[] data)
    {
        var bytes = new byte[data.Length - 1];

        for (int i = 0; i < data.Length - 1; i++)
        {
            bytes[i] = data[i + 1];
        }

        return BuildPackage((CommandEnum)data[0], bytes);
    }

    /// <summary>
    ///     构造包
    /// </summary>
    /// <param name="comm"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public const byte ReqType = (byte)ComType.REQ;

    public static byte[] BuildPackage(CommandEnum comm, params byte[] payload)
    {
        // STX1 | STX2 | TYPE | CMD  | LEN  |  PAYLOAD  | CHECKSUM |
        var packLen = MinLen + payload.Length;
        var dataToSend = new byte[packLen];
        dataToSend[0] = HeadFlag;
        dataToSend[1] = HeadFlag2;

        dataToSend[2] = ReqType;
        dataToSend[3] = (byte)comm;
        dataToSend[4] = (byte)payload.Length;

        payload.CopyTo(dataToSend, PlayLoadIndex);

        int sum = 0;
        foreach (var t in payload)
        {
            sum += t;
        }

        dataToSend[^1] = (byte)(sum & 0xFF);

        return dataToSend;
    }

    /// <summary>
    ///     和校验:type+包长+指令+数据
    ///     结果&ff
    /// </summary>
    /// <param name="comData"></param>
    /// <returns></returns>
    public static bool CheckSum(byte[] comData)
    {
        try
        {
            var sum = 0;
            var parityIndex = comData.Length - 1;
            for (var i = CheckStartIndex; i < parityIndex; i++) sum += comData[i];

            if ((sum & 0xFF) == comData[parityIndex]) return true;

            Debug.LogWarning(
                $"和校验不通过：\n{BitConverter.ToString(comData)}\n计算和：{sum} \t 目标和：{comData[parityIndex]}");

            return false;
        }
        catch (Exception e)
        {
            Debug.LogError("合校验错误:" + BitConverter.ToString(comData));
            return false;
        }
    }

    #region 下标   STX1 | STX2 | TYPE | CMD  | LEN  |  PAYLOAD  | CHECKSUM

    public const byte HeadFlag = 0xAC;
    public const byte HeadFlag2 = 0xA5;

    /// <summary>
    ///     包长位下标
    /// </summary>
    public const int PayloadLen = 4;

    /// <summary>
    ///     校验起始位
    /// </summary>
    public const int CheckStartIndex = 2;


    public const int TypeIndex = 2;


    /// <summary>
    ///     指令位下标
    /// </summary>
    private static int CommIndex = 3;

    /// <summary>
    ///     数据位下标
    /// </summary>
    private static int PlayLoadIndex = 5;

    /// <summary>
    ///     基础长度没有数据位
    /// </summary>
    public static int MinLen = 6;

    public static int MaxLen = 6;

    #endregion
}