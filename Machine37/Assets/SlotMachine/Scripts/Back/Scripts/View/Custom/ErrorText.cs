using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class ErrorText : MonoBehaviour
{

    public Text m_errorText;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void StartIE(string _str)
    {
        if (m_errorText == null) return;
        gameObject.SetActive(true); // 确保自身激活（CloseAll 可能已把它停用），否则协程不会运行
        m_errorText.text = _str;
        StartCoroutine(StartAni());
    }

    IEnumerator StartAni()
    {
        m_errorText.color = new Color(1, 0, 0, 0);
        m_errorText.rectTransform.anchoredPosition3D = Vector3.zero;
        m_errorText.DOFade(1, .5f);
        m_errorText.rectTransform.DOLocalMoveY(100, 1);

        yield return new WaitForSeconds(.5f);
        m_errorText.DOFade(0, .5f);

        yield return new WaitForSeconds(.5f);
        m_errorText.color = new Color(1, 0, 0, 0);
    }
}
