using UnityEngine;
using UnityEngine.UI;
using Com.Controller;

namespace Com.Back
{
    public class MoveView : MonoBehaviour
    {

        //[HideInInspector]
        public bool isCur;

        public bool m_isSetColor;
        public bool isHorizontal;

        //[HideInInspector]
        public int index = 0;
        public Vector3 m_offset = new Vector3(-80, 5);

        public Image image;
        public BaseItem[] m_texts;
        public BaseItem[] tabelArray;
        public Text[] textArray;

        private float m_cdTime = 0.2f;

        // Use this for initialization
        protected virtual void Start()
        {
            isCur = true;
        }

        protected virtual void OnEnable()
        {
            index = 0;
            m_cdTime = 0.2f;
            for (int i = 0; i < m_texts.Length; i++)
            {
                BaseItem text = m_texts[i];
                if (text != null && DataManager.Instance.Language.ContainsKey(text.m_key))
                    text.SetStr(DataManager.Instance.Language[text.m_key].GetStr.Replace("\\n", "\n"));
            }

            for (int i = 0; i < tabelArray.Length; i++)
            {
                BaseItem text = tabelArray[i];
                if (text != null && DataManager.Instance.Language.ContainsKey(text.m_key))
                    text.SetStr(DataManager.Instance.Language[text.m_key].GetStr);
            }

            // 本屏成为当前可操作屏（配合整屏切换：父屏停用时 OnDisable 会把 isCur 置 false，暂停其输入）
            isCur = true;
        }

        protected virtual void OnDisable()
        {
            isCur = false;
        }

        // Update is called once per frame
        protected virtual void Update()
        {
            if (!isCur)
                return;

            // 防连按
            m_cdTime -= Time.deltaTime;
            if (m_cdTime > 0)
                return;

            // 返回上一屏（街机返回键）：走插件 Menu 的 OnBackPressed（根层=退出后台，子屏=弹栈回父屏）
            if (GameController.Instance.m_keys[(int)InputAction.Cancel] == (int)InputPhase.Down)
                OnCancel();

            // 底层按键修改
            if (GameController.Instance.m_keys[(int)InputAction.Confirm] == (int)InputPhase.Down)
                OnEnter();

            InputAction next = isHorizontal ? InputAction.Right : InputAction.Down;
            if (GameController.Instance.m_keys[(int)next] == (int)InputPhase.Down)
                UpAndDown(1);
            InputAction prev = isHorizontal ? InputAction.Left : InputAction.Up;

            if (GameController.Instance.m_keys[(int)prev] == (int)InputPhase.Down)
                UpAndDown(-1);    

            InputAction left = !isHorizontal ? InputAction.Right : InputAction.Down;
            if (GameController.Instance.m_keys[(int)left] == (int)InputPhase.Down)
                LeftAndRight(1);
            InputAction right = !isHorizontal ? InputAction.Left : InputAction.Up;
            if (GameController.Instance.m_keys[(int)right] == (int)InputPhase.Down)
                LeftAndRight(-1);

            SelectItem();
        }

        public virtual void InitData()
        {

        }

        public virtual void SaveDave()
        {

        }

        protected virtual void OnEnter()
        {
            
        }

        protected virtual void OnCancel()
        {
            
        }

        protected virtual void UpAndDown(int num)
        {
            index += num;
            index = index < 0 ? tabelArray.Length - 1 : index;
            index = index > tabelArray.Length - 1 ? 0 : index;
        }

        protected virtual void LeftAndRight(int num)
        {

        }

        protected void UpdateNum(int _num, int _min, int _max, ref int _upNum)
        {
            _upNum += _num;
            _upNum = _upNum < _min ? _max : _upNum;
            _upNum = _upNum > _max ? _min : _upNum;
        }

        public virtual void SelectItem()
        {
            for (int i = 0; i < tabelArray.Length; i++)
            {
                tabelArray[i].SetToggle(i == index);
            }
        }

    }
}
