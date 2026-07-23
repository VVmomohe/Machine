#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using com.slot;

[CustomEditor(typeof(UIImageAnimator))]
public class UIImageAnimatorEditor : Editor
{
    private bool isPreviewing = false;
    private double lastTime = 0;
    private float editorTimer = 0;

    // 【核心机制】放弃异步后台 Update，改用官方 UI 刷新驱动机制
    // 当开启预览且未运行游戏时，通知 Unity 自动安全地每帧刷新此 Inspector 界面
    public override bool RequiresConstantRepaint()
    {
        return isPreviewing && !Application.isPlaying;
    }

    void OnDisable()
    {
        isPreviewing = false;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        UIImageAnimator animator = (UIImageAnimator)target;
        if (animator == null || animator.frames == null || animator.frames.Length == 0)
        {
            EditorGUILayout.HelpBox("请先在上方 Frames 数组中放入序列帧图片！", MessageType.Info);
            return;
        }

        // 运行时状态提示：反映 playOnEnable / IsPlaying
        if (Application.isPlaying)
        {
            string state = animator.IsPlaying ? "播放中" : "已暂停";
            EditorGUILayout.HelpBox($"游戏运行中（当前：{state}，打开自动播放={animator.playOnEnable}）。请在游戏画面中查看效果。", MessageType.Info);
            isPreviewing = false;
            return;
        }

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("🎬 场景视图预览工具", EditorStyles.boldLabel);

        if (!animator.playOnEnable)
        {
            EditorGUILayout.HelpBox("已关闭「打开就播放」：运行时不会自动播放，需要在代码里调用 Play() / Restart()。下方预览仅供编辑器查看。", MessageType.None);
        }

        // 自动播放/停止预览按钮
        GUI.color = isPreviewing ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
        if (GUILayout.Button(isPreviewing ? "■ 停止预览 (Stop)" : "▶ 播放预览 (Play)", GUILayout.Height(30)))
        {
            isPreviewing = !isPreviewing;
            if (isPreviewing)
            {
                lastTime = EditorApplication.timeSinceStartup;
            }
        }
        GUI.color = Color.white;

        // 【安全转移】将核心时钟步进完全放入 UI 渲染管线内部
        // Unity 在进行域备份或编译时，会无条件切断此段代码的执行流，实现绝对安全的物理隔离
        if (isPreviewing)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(currentTime - lastTime);
            lastTime = currentTime;

            // 过滤掉首次点开或突发的卡顿时间帧巨变
            if (deltaTime < 0 || deltaTime > 1f) deltaTime = 0;

            editorTimer += deltaTime;
            float frameDuration = 1f / Mathf.Max(animator.fps, 1f);

            if (editorTimer >= frameDuration)
            {
                editorTimer %= frameDuration;

                if (animator.loop)
                {
                    animator.index = (animator.index + 1) % animator.frames.Length;
                }
                else
                {
                    if (animator.index < animator.frames.Length - 1)
                    {
                        animator.index++;
                    }
                    else
                    {
                        isPreviewing = false;
                    }
                }

                animator.SetFrame(animator.index);
            }
        }

        EditorGUILayout.Space(5);
        EditorGUI.BeginChangeCheck();

        int newIndex = EditorGUILayout.IntSlider("当前预览帧 (Index)", animator.index, 0, animator.frames.Length - 1);

        if (EditorGUI.EndChangeCheck())
        {
            isPreviewing = false;
            animator.index = newIndex;
            animator.SetFrame(animator.index);
        }

        if (animator.index >= 0 && animator.index < animator.frames.Length)
        {
            Sprite current = animator.frames[animator.index];
            string spriteName = current != null ? current.name : "Null";
            EditorGUILayout.LabelField($"当前图片名: {spriteName}", EditorStyles.miniLabel);
        }
    }
}
#endif
