using UnityEngine;

[ExecuteAlways] // 允许在非运行状态（编辑模式）下也能实时预览效果
[RequireComponent(typeof(CanvasGroup))]
public class RandomUITransparency : MonoBehaviour
{
    private CanvasGroup canvasGroup;

    [Header("透明度范围设置")]
    [Range(0f, 1f), Tooltip("允许的最低透明度")] 
    public float minAlpha = 0.1f;
    
    [Range(0f, 1f), Tooltip("允许的最高透明度")] 
    public float maxAlpha = 1.0f;

    [Header("动态渐变设置")]
    [Tooltip("勾选后，UI会在随机透明度之间平滑渐变循环")]
    public bool isDynamic = true;
    
    [Tooltip("每次透明度渐变持续的时间（秒）")]
    public float fadeDuration = 1.0f;

    private float currentAlphaStart;
    private float targetAlpha;
    private float timer;

    void Awake()
    {
        // 自动获取组件
        canvasGroup = GetComponent<CanvasGroup>();
    }

    void Start()
    {
        InitializeAlpha();
    }

    void OnEnable()
    {
        // 确保重新激活时状态正确
        InitializeAlpha();
    }

    void Update()
    {
        if (!isDynamic || fadeDuration <= 0f) return;

        // 计算时间增量（兼容编辑模式下的预览和游戏运行时的计时）
        float deltaTime = Application.isPlaying ? Time.deltaTime : 0.02f;

        timer += deltaTime;
        float progress = timer / fadeDuration;

        // 使用 Mathf.Lerp 实现平滑渐变
        if (canvasGroup != null)
        {
            canvasGroup.alpha = Mathf.Lerp(currentAlphaStart, targetAlpha, progress);
        }

        // 当当前渐变完成后，将当前位置设为起点，并随机生成下一个目标透明度
        if (progress >= 1f)
        {
            currentAlphaStart = targetAlpha;
            SetNewTargetAlpha();
            timer = 0f;
        }

        // 在编辑模式下强制刷新视图，以便肉眼能看到实时动画
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
        #endif
    }

    /// <summary>
    /// 初始化透明度
    /// </summary>
    void InitializeAlpha()
    {
        if (canvasGroup != null)
        {
            currentAlphaStart = canvasGroup.alpha;
            SetNewTargetAlpha();
            timer = 0f;
        }
    }

    /// <summary>
    /// 生成一个新的随机目标透明度
    /// </summary>
    void SetNewTargetAlpha()
    {
        targetAlpha = Random.Range(minAlpha, maxAlpha);
    }

    /// <summary>
    /// 外部调用：立即重置并切换到一个新的随机透明度渐变
    /// </summary>
    public void RandomizeNow()
    {
        InitializeAlpha();
    }
}