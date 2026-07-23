using Com.Controller;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Back
{
    public class TimeView : MonoBehaviour
    {

        public BaseItem m_dataText;
        public BaseItem m_timeText;

        public DateTime m_dt;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            m_dt = DateTime.Now;
        }
    }
}
