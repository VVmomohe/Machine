using Com.Back;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class VideoFileManager : MonoBehaviour
{
    private string videoSavePath;
    public bool isCopying = false;          // 防止重复复制
    public int pendingCopies = 0;           // 待完成的文件数

    private void Awake()
    {
        // 使用 Path.Combine 确保路径分隔符正确
        videoSavePath = Path.Combine(Application.persistentDataPath, "VideoData");
    }

    /// <summary>
    /// 开始复制所有视频文件（异步）
    /// </summary>
    public void CopyAllVideos()
    {
        // 避免重复复制
        if (isCopying)
        {
            Debug.LogWarning("[VideoFileManager] Already copying, ignore duplicate call.");
            return;
        }

        // 检查是否需要复制（根据 saveTag 判断）
        if (PlayerPrefs.GetString("saveTag") == DataManager.Instance.saveTag)
        {
            Debug.Log("[VideoFileManager] saveTag matched, skip copying videos.");
            return;
        }

        StartCoroutine(CopyAllVideosCoroutine());
    }

    private IEnumerator CopyAllVideosCoroutine()
    {
        isCopying = true;
        pendingCopies = 0;

        // 1. 清理并重建目录
        try
        {
            if (Directory.Exists(videoSavePath))
                Directory.Delete(videoSavePath, true);
            Directory.CreateDirectory(videoSavePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VideoFileManager] Failed to init directory: {e.Message}");
            isCopying = false;
            yield break;
        }

        // 2. 定义所有需要复制的文件 (相对路径)
        string[] relativePaths = {
            //"Move/beupbet1.mp4",
        };

        pendingCopies = relativePaths.Length;
        foreach (string relPath in relativePaths)
        {
            StartCoroutine(CopyMedia(relPath));
        }

        // 等待所有复制完成
        while (pendingCopies > 0)
            yield return null;

        isCopying = false;
        Debug.Log("[VideoFileManager] All videos copied successfully.");
    }

    private IEnumerator CopyMedia(string relativePath)
    {
        string destPath = Path.Combine(videoSavePath, relativePath);
        string srcPath = Path.Combine(Application.streamingAssetsPath, "VideoData", relativePath);

        // 确保目标文件的目录存在
        string destDir = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        using (UnityWebRequest www = UnityWebRequest.Get(srcPath))
        {
            yield return www.SendWebRequest();

            if (www.isHttpError || www.isNetworkError)
            {
                Debug.LogError($"[VideoFileManager] Failed to download: {srcPath}\nError: {www.error}");
                pendingCopies--;
                yield break;
            }

            try
            {
                byte[] bytes = www.downloadHandler.data;
                File.WriteAllBytes(destPath, bytes);
                Debug.Log($"[VideoFileManager] Copied: {relativePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VideoFileManager] Failed to write file: {destPath}\n{e.Message}");
            }
        }

        pendingCopies--;
    }
}