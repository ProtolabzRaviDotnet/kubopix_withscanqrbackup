using System;

namespace com.epson.label.driver
{
    [Serializable]
    class ENSException : Exception
    {
        #region Property

        public int ErrCode { get; set; }

        #endregion

        #region Methods

        public ENSException(int e) : base($"EpsonNet SDK Error: {e}")
        {
            ErrCode = e;
        }

        #endregion
    }
}
