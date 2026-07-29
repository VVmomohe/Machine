using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

/// <summary>
/// FMOD音效管理器
/// 负责管理所有FMOD音频事件的播放、停止和音量控制
/// </summary>
public class FMODSoundMgr : MonoBehaviour
{
    // 单例模式实例
    private static FMODSoundMgr _instance;
    /// <summary>
    /// 当前音效管理器；未创建或已销毁时为 null。
    /// 注意：不要在 getter 里打错误日志，否则 <c>if (Instance == null)</c> 也会误报。
    /// </summary>
    public static FMODSoundMgr Instance => _instance;

    // 正在播放的音效实例字典（事件路径 -> EventInstance）
    private Dictionary<string, EventInstance> _playingEvents;

    // 正在播放的循环音效字典（用于停止特定循环音效）
    private Dictionary<string, EventInstance> _loopingEvents;

    // 当前播放的BGM实例
    private EventInstance _currentBGM;
    private string _currentBGMPath;

    // 音效音量总线
    private Bus _sfxBus;
    private Bus _bgmBus;
    private Bus _masterBus;

    // 音量设置（0-1）
    [Range(0, 1)]
    public float sfxVolume = 1.0f;

    [Range(0, 1)]
    public float bgmVolume = 0.5f;

    [Range(0, 1)]
    public float masterVolume = 1.0f;

    // 播放限流设置
    [Header("SFX Throttle")]
    [Tooltip("每帧最多允许触发的音效次数")]
    [SerializeField, Range(1, 60)] private int maxSfxPlaysPerFrame = 12;
    private int sfxFrame = -1;
    private int sfxPlaysThisFrame = 0;

    // 多语言 eventPath 缓存：language|eventPath → 实际路径
    private Dictionary<string, string> _localizedPathCache = new Dictionary<string, string>();

    // 防重复播放（同一事件 50ms 内只播一次；下列事件需快速重触发，排除之）
    private Dictionary<string, float> lastPlayTime = new Dictionary<string, float>();
    private List<string> excludes = new List<string>()
    {
        "event:/Fruit/Score",
        "event:/Common/Coin_Loop1",
        "event:/Common/Coin_Loop2",
    };

    private void Awake()
    {
        // 确保单例模式
        if (_instance == null)
        {
            _instance = this;
            Init();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 应用已保存的音频设置
        //GameSetting.ApplyAudioSettings();
    }

    /// <summary>
    /// 初始化FMOD音效管理器
    /// </summary>
    private void Init()
    {
        _playingEvents = new Dictionary<string, EventInstance>();
        _loopingEvents = new Dictionary<string, EventInstance>();

        // 获取音频总线（如果不存在则不报错）
        try
        {
            _masterBus = RuntimeManager.GetBus("bus:/");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Master Bus not found: {e.Message}");
        }

        try
        {
            _sfxBus = RuntimeManager.GetBus("bus:/SFX");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"SFX Bus not found, will use Master Bus. Create 'SFX' bus in FMOD Studio Mixer for better control.");
        }

        try
        {
            _bgmBus = RuntimeManager.GetBus("bus:/Music");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Music Bus not found, will use Master Bus. Create 'Music' bus in FMOD Studio Mixer for better control.");
        }

        // 设置初始音量
        SetMasterVolume(masterVolume);
        SetSFXVolume(sfxVolume);
        SetBGMVolume(bgmVolume);

        Debug.Log("FMODSoundMgr initialized successfully!");
    }

    /// <summary>
    /// 根据当前语言解析 eventPath：英文(language=1)时尝试 eventPath+"_en"，
    /// 若对应 FMOD 事件不存在则回退原路径。结果缓存避免重复探测。
    /// </summary>
    private string ResolveLocalizedEventPath(string eventPath)
    {
        int lang = 0;
        if (lang != 1) return eventPath;

        string cacheKey = lang + "|" + eventPath;
        if (_localizedPathCache.TryGetValue(cacheKey, out string cached))
            return cached;

        string enPath = eventPath + "_en";
        try
        {
            var test = RuntimeManager.CreateInstance(enPath);
            test.release();
            _localizedPathCache[cacheKey] = enPath;
            return enPath;
        }
        catch
        {
            _localizedPathCache[cacheKey] = eventPath;
            return eventPath;
        }
    }

    /// <summary>
    /// 播放音效（一次性，不循环）
    /// </summary>
    /// <param name="eventPath">FMOD事件路径，例如："event:/Common/Score"</param>
    public void PlaySound(string eventPath)
    {
        eventPath = ResolveLocalizedEventPath(eventPath);
        if (!CanPlaySfx(eventPath)) return;

        try
        {
            EventInstance instance = RuntimeManager.CreateInstance(eventPath);
            RuntimeManager.AttachInstanceToGameObject(instance, transform); // 绑到管理器(原点)，避免 Editor 下 3D 事件被 FMOD 丢到无限远而静音
            instance.setVolume(masterVolume * sfxVolume);
            instance.start();
            instance.release();
            sfxPlaysThisFrame++;
        }
        catch (System.Exception e)
        {
            Debug.Log($"播放音效失败: {eventPath}\n{e.Message}");
        }
    }

    /// <summary>
    /// 播放音效（带音量控制）
    /// </summary>
    /// <param name="eventPath">FMOD事件路径</param>
    /// <param name="volume">音量 0-1（相对音量，会乘以 masterVolume * sfxVolume）</param>
    public void PlaySound(string eventPath, float volume)
    {
        eventPath = ResolveLocalizedEventPath(eventPath);
        if (!CanPlaySfx(eventPath)) return;

        try
        {
            EventInstance instance = RuntimeManager.CreateInstance(eventPath);
            RuntimeManager.AttachInstanceToGameObject(instance, transform); // 绑到管理器(原点)，避免 Editor 下 3D 事件被 FMOD 丢到无限远而静音
            instance.setVolume(Mathf.Clamp01(volume) * masterVolume * sfxVolume);
            instance.start();
            instance.release();
            sfxPlaysThisFrame++;
        }
        catch (System.Exception e)
        {
            Debug.Log($"播放音效失败: {eventPath}\n{e.Message}");
        }
    }

    /// <summary>
    /// 播放可停止的音效（循环或需要手动停止的音效）
    /// </summary>
    /// <param name="eventPath">FMOD事件路径</param>
    /// <param name="loop">是否循环播放（为 true 时实例保留至 <see cref="StopSound"/>）</param>
    /// <param name="bypassCanPlaySfx">为 true 时不经过限流/防重复（用于必须与画面对齐、且同帧可能晚于大量其它音效的金币循环等）</param>
    /// <param name="retainUntilStopped">
    /// 为 true 且 <paramref name="loop"/> 为 false 时：一次性事件仍保留实例（可 <see cref="StopSound"/>），自然播完后释放。
    /// 用于语音等需在结束前被外部打断、但 FMOD 内并非循环的事件。
    /// </param>
    public void PlaySoundStoppable(string eventPath, float volume = -1f, bool loop = false, bool bypassCanPlaySfx = false, bool retainUntilStopped = false)
    {
        string originalPath = eventPath;
        eventPath = ResolveLocalizedEventPath(eventPath);
        if (!bypassCanPlaySfx && !CanPlaySfx(eventPath)) return;

        try
        {
            // 如果已经在播放，先停止（使用原始路径查找）
            if (_loopingEvents.ContainsKey(originalPath))
            {
                StopSound(originalPath);
            }

            EventInstance instance = RuntimeManager.CreateInstance(eventPath);
            
            if (volume >= 0)
            {
                instance.setVolume(Mathf.Clamp01(volume) * masterVolume * sfxVolume);
            }
            else
            {
                instance.setVolume(masterVolume * sfxVolume);
            }

            instance.start();

            if (loop)
            {
                _loopingEvents[originalPath] = instance;
            }
            else if (retainUntilStopped)
            {
                _loopingEvents[originalPath] = instance;
                StartCoroutine(ReleaseStoppableOneShotWhenStopped(originalPath));
            }
            else
            {
                instance.release();
            }

            if (!bypassCanPlaySfx)
                sfxPlaysThisFrame++;
        }
        catch (System.Exception e)
        {
            Debug.Log($"播放音效失败: {eventPath}\n{e.Message}");
        }
    }

    private IEnumerator ReleaseStoppableOneShotWhenStopped(string eventPath)
    {
        yield return null;
        for (;;)
        {
            if (!_loopingEvents.TryGetValue(eventPath, out EventInstance instance))
                yield break;
            if (!instance.isValid())
            {
                _loopingEvents.Remove(eventPath);
                yield break;
            }
            instance.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.STOPPED)
            {
                instance.release();
                _loopingEvents.Remove(eventPath);
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>
    /// 播放背景音乐
    /// </summary>
    /// <param name="eventPath">FMOD事件路径，例如："event:/BGM/Lobby_BGM"</param>
    public void PlayBGM(string eventPath)
    {
        eventPath = ResolveLocalizedEventPath(eventPath);

        // 如果正在播放相同的BGM，不重复播放
        if (_currentBGMPath == eventPath && _currentBGM.isValid())
        {
            PLAYBACK_STATE state;
            _currentBGM.getPlaybackState(out state);
            if (state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.STARTING)
            {
                return;
            }
        }

        // 停止当前BGM
        StopBGM();

        try
        {
            _currentBGM = RuntimeManager.CreateInstance(eventPath);
            RuntimeManager.AttachInstanceToGameObject(_currentBGM, transform); // 绑到管理器(原点)，避免 Editor 下 3D 事件被 FMOD 丢到无限远而静音
            _currentBGM.setVolume(masterVolume);
            _currentBGM.start();
            _currentBGMPath = eventPath;
        }
        catch (System.Exception e)
        {
            Debug.Log($"播放BGM失败: {eventPath}\n{e.Message}");
        }
    }

    /// <summary>
    /// 停止背景音乐
    /// </summary>
    /// <param name="fadeOut">是否淡出（使用FMOD事件中设置的淡出时间）</param>
    public void StopBGM(bool fadeOut = true)
    {
        if (_currentBGM.isValid())
        {
            _currentBGM.stop(fadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
            _currentBGM.release();
            _currentBGMPath = null;
        }
    }

    /// <summary>
    /// 暂停背景音乐
    /// </summary>
    public void PauseBGM()
    {
        if (_currentBGM.isValid())
        {
            _currentBGM.setPaused(true);
        }
    }

    /// <summary>
    /// 恢复背景音乐
    /// </summary>
    public void ResumeBGM()
    {
        if (_currentBGM.isValid())
        {
            _currentBGM.setPaused(false);
        }
    }

    /// <summary>
    /// 停止指定的音效
    /// </summary>
    /// <param name="eventPath">FMOD事件路径</param>
    /// <param name="fadeOut">是否淡出</param>
    public void StopSound(string eventPath, bool fadeOut = true)
    {
        if (_loopingEvents.TryGetValue(eventPath, out EventInstance instance))
        {
            if (instance.isValid())
            {
                instance.stop(fadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }
            _loopingEvents.Remove(eventPath);
        }
    }

    /// <summary>
    /// 对正在播放的可停止音效设置 FMOD 参数（用于 RunLogic→EndLogic 等状态切换）
    /// </summary>
    public void SetStoppableSoundParameter(string eventPath, string parameterName, float value)
    {
        if (_loopingEvents.TryGetValue(eventPath, out EventInstance instance) && instance.isValid())
            instance.setParameterByName(parameterName, value);
    }

    /// <summary>
    /// 触发 Run→End 切换并延迟释放实例（跑灯倒数第 N 格时调用，FMOD 内通过参数切换到 EndLogic，淡入淡出在 FMOD 过渡区设置）
    /// </summary>
    /// <param name="eventPath">事件路径</param>
    /// <param name="endParameterName">触发 End 的参数名（与 FMOD 事件内一致，如 "End"）</param>
    /// <param name="endValue">参数值（如 1f）</param>
    /// <param name="releaseAfterSeconds">End 段播完后延迟释放时间（秒）</param>
    public void TriggerRunToEndAndReleaseLater(string eventPath, string endParameterName, float endValue, float releaseAfterSeconds)
    {
        if (!_loopingEvents.TryGetValue(eventPath, out EventInstance instance))
        {
            Debug.LogWarning($"[FMOD] TriggerRunToEndAndReleaseLater: 未找到正在播放的事件 {eventPath}");
            return;
        }
        _loopingEvents.Remove(eventPath);
        if (!instance.isValid())
            return;
        var result = instance.setParameterByName(endParameterName, endValue);
        if (result != FMOD.RESULT.OK)
            Debug.LogWarning($"[FMOD] setParameterByName(\"{endParameterName}\", {endValue}) 失败: {result}");
        else
            Debug.Log($"[FMOD] Run→End 已设置参数 {endParameterName}={endValue}");
        StartCoroutine(ReleaseInstanceAfterDelay(instance, releaseAfterSeconds));
    }

    private IEnumerator ReleaseInstanceAfterDelay(EventInstance instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance.isValid())
            instance.release();
    }

    /// <summary>
    /// 停止所有音效（不包括BGM）
    /// </summary>
    public void StopAllSounds()
    {
        foreach (var kvp in _loopingEvents)
        {
            if (kvp.Value.isValid())
            {
                kvp.Value.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                kvp.Value.release();
            }
        }
        _loopingEvents.Clear();
    }

    /// <summary>
    /// 设置主音量（同时影响BGM和音效）
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        if (_masterBus.isValid())
        {
            _masterBus.setVolume(masterVolume);
        }
        // 同步设置当前BGM实例音量，确保在masterBus不可用时也能控制音量
        if (_currentBGM.isValid())
        {
            _currentBGM.setVolume(masterVolume);
        }
        // 同步设置SFX Bus音量 = masterVolume * sfxVolume，确保总音量对音效也生效
        if (_sfxBus.isValid())
        {
            _sfxBus.setVolume(masterVolume * sfxVolume);
        }
    }

    /// <summary>
    /// 设置音效开关（0=关, 1=开），实际音量 = masterVolume * sfxVolume
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (_sfxBus.isValid())
        {
            _sfxBus.setVolume(masterVolume * sfxVolume);
        }
    }

    /// <summary>
    /// 设置BGM音量
    /// </summary>
    public void SetBGMVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        // 实际音量 = bgmVolume * masterVolume，确保主音量控制始终生效
        float actualVolume = bgmVolume * masterVolume;
        // 直接设置当前BGM实例的音量
        if (_currentBGM.isValid())
        {
            _currentBGM.setVolume(actualVolume);
        }
        // 同时尝试设置总线音量（如果有效）
        if (_bgmBus.isValid())
        {
            _bgmBus.setVolume(bgmVolume);
        }
    }

    /// <summary>
    /// 检查是否可以播放音效（限流和防重复）
    /// </summary>
    private bool CanPlaySfx(string eventPath)
    {
        if (string.IsNullOrEmpty(eventPath)) return false;

        // 每帧限流
        int frame = Time.frameCount;
        if (frame != sfxFrame)
        {
            sfxFrame = frame;
            sfxPlaysThisFrame = 0;
        }
        if (sfxPlaysThisFrame >= Mathf.Max(1, maxSfxPlaysPerFrame))
        {
            return false;
        }

        // 防重复播放（部分音效除外）
        if (!excludes.Contains(eventPath))
        {
            if (lastPlayTime.TryGetValue(eventPath, out float t))
            {
                if (Time.realtimeSinceStartup - t < 0.05f)
                {
                    return false;
                }
            }
        }

        lastPlayTime[eventPath] = Time.realtimeSinceStartup;
        return true;
    }

    private void OnDestroy()
    {
        // 清理所有音效实例
        StopAllSounds();
        StopBGM(false);
    }
}
