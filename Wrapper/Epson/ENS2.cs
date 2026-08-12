using System;
using System.Runtime.InteropServices;

namespace com.epson.label.driver
{
    public abstract class ENS2 : IDisposable
    {
        #region Fields

        protected IntPtr _ptrToStruct = IntPtr.Zero;
        private bool _disposed = false;

        #endregion

        #region Constructors/Destructors

        ~ENS2()
        {
            Dispose(false);
        }
        
        #endregion

        #region Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Release managed resources.
                }

                // Release unmanaged resources.
                if (_ptrToStruct != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(_ptrToStruct);
                    _ptrToStruct = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        public abstract void PtrToStructure();
        public abstract void StructureToPtr();

        public IntPtr StructurePointer
        {
            get
            {
                StructureToPtr();
                return _ptrToStruct;
            }
        }

        #endregion
    }
}
