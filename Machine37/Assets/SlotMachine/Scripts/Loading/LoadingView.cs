using Com.Back;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityXML;

public class LoadingView : MonoBehaviour
{
    public bool m_isLoading;
    public bool m_isCopying;
    public float m_cdTime1 = 3f;
    public float m_cdTime2 = 0.5f;

    private VideoFileManager videoManager;

    void Start()
    {

        // 初始化视频管理器并开始复制
        videoManager = gameObject.AddComponent<VideoFileManager>();
        videoManager.CopyAllVideos();

        DataHelper.Instance.Error_Num = 0;
        DataManager.Instance.InitData();
        StartCoroutine(StartLoad());

        //FMODSoundMgr.Instance.PlaySound("event:/Common/开机界面");
    }

    void Update()
    {
        m_cdTime1 -= Time.deltaTime;
        m_cdTime1 = m_cdTime1 < 0 ? 0 : m_cdTime1;

        if (m_cdTime1 <= 0)
        {
            m_cdTime2 -= Time.deltaTime;
            m_cdTime2 = m_cdTime2 < 0 ? 0 : m_cdTime2;
            if (m_cdTime2 <= 0 && !m_isLoading && !m_isCopying && !videoManager.isCopying)
            {
                m_isLoading = true;
                SceneManager.LoadSceneAsync(1);
            }
        }
    }

    IEnumerator StartLoad()
    {
        m_isCopying = true;

        while (DataHelper.Instance.Error_Num != 0)
        {
            Debug.Log("等待数据初始化...");
            yield return new WaitForSeconds(1f);
        }

        while (DataHelper.Instance.Error_Num != 0)
        {
            Debug.Log("等待数据加载...");
            yield return new WaitForSeconds(1f);
        }

        m_isCopying = false;
    }
}
