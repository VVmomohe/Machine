using UnityEngine;

/// <summary>
/// 轻量日志封装。
/// 原工程依赖外部 GameLog，克隆时未随之带入，这里补一个最小实现：
/// - Log / Warning / Error 分别映射到 Debug.Log / LogWarning / LogError
/// 如需统一开关（发布版屏蔽 Info），改 ENABLE_LOG 即可。
/// </summary>
public static class GameLog
{
    public static bool EnableLog = true;

    public static void Log(object message)
    {
        if (EnableLog) Debug.Log(message);
    }

    public static void Warning(object message)
    {
        if (EnableLog) Debug.LogWarning(message);
    }

    public static void Error(object message)
    {
        // 错误始终输出，不随 EnableLog 关闭
        Debug.LogError(message);
    }
}
