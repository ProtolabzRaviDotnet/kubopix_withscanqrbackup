using System;
using System.Runtime.InteropServices;

namespace kuboPix
{
    class CamHelp
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void VideoStreamCallBack(IntPtr rgbData, int iWidth, int iHeight);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_SetVideoStreamCallBack(VideoStreamCallBack? cb);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_InitSDK();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static void HIGH_ReleaseSDK();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_GetPixelNum();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_GetHPixelByIndex(int nIndex);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_GetVPixelByIndex(int nIndex);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_SetCameraPixel(int nHPixel, int nVPixel);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_StartCamera();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_StopCamera();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_CreateWindowEx(long ulWndHandle, int iX, int iY, int iWidth, int iHeight);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_CloseWindow();

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_TakePicture(byte[] strFilePath);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_SetAutoCrop(bool bAuto);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_SetRotateSize(int nRotateSize);

        [DllImport("campro.dll", CallingConvention = CallingConvention.Cdecl)]
        public extern static int HIGH_SetColorType(int nColorType);
    }
}
