using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace com.slot
{
    [RequireComponent(typeof(Image))]
    public class UIImageAnimator : MonoBehaviour
{
    public Sprite[] frames;         // 序列帧数组
    public float fps = 24f;         // 帧率
    public bool loop = true;        // 是否循环
    public bool playOnEnable = true; // 是否打开(启用)就自动播放

    [HideInInspector] public int index;

    // 当前是否处于播放状态（运行时由 Play/Stop 控制；playOnEnable=false 时初始为暂停）
    public bool IsPlaying { get; private set; }

    private Image image;
    private RectTransform rectTransform;
    private float timer;
    private float frameDuration;

    void OnEnable()
    {
        InitComponents();
        frameDuration = 1f / Mathf.Max(fps, 1f);

        if (frames != null && frames.Length > 0)
        {
            index = 0;
            SetFrame(index);
        }

        if (playOnEnable)
        {
            Play();
        }
        else
        {
            Stop();
        }
    }

    void Update()
    {
        if (!IsPlaying) return;
        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        frameDuration = 1f / Mathf.Max(fps, 1f);

        while (timer >= frameDuration)
        {
            timer -= frameDuration;

            if (loop)
            {
                index = (index + 1) % frames.Length;
                SetFrame(index);
            }
            else
            {
                if (index < frames.Length - 1)
                {
                    index++;
                    SetFrame(index);
                }
                else
                {
                    Stop();
                    break;
                }
            }
        }
    }

    /// <summary>从当前帧开始播放。</summary>
    public void Play()
    {
        if (frames == null || frames.Length == 0) return;
        IsPlaying = true;
    }

    /// <summary>停止播放（停在当前帧）。</summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    /// <summary>从第 0 帧重新播放。</summary>
    public void Restart()
    {
        index = 0;
        timer = 0f;
        SetFrame(index);
        Play();
    }

    private void InitComponents()
    {
        if (image == null) image = GetComponent<Image>();
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
    }

    public void SetFrame(int frameIndex)
    {
        InitComponents();

        if (frames == null || frameIndex < 0 || frameIndex >= frames.Length) return;

        Sprite currentSprite = frames[frameIndex];
        if (currentSprite == null) return;

        image.sprite = currentSprite;
        image.SetNativeSize();

        Vector2 normalizedPivot = new Vector2(
            currentSprite.pivot.x / currentSprite.rect.width,
            currentSprite.pivot.y / currentSprite.rect.height
        );

        rectTransform.pivot = normalizedPivot;

        #if UNITY_EDITOR
        if (!Application.isPlaying && this != null && gameObject != null)
        {
            EditorUtility.SetDirty(gameObject);
        }
        #endif
    }
}

}
