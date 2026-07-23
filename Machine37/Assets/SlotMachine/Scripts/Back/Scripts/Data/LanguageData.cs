using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityXML;

namespace Com.Back
{
    [System.Serializable]
    public class LanguageData : BaseData
    {

        public int id;
        public string chs_str;
        public string en_str;

        public string GetStr
        {
            get
            {
                return 0 == 0 ? chs_str : en_str;
            }
        }
    }
}
