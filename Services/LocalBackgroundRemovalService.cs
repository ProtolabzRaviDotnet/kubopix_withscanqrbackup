using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using kuboPix.Helpers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace kuboPix.Services
{
    public class LocalBackgroundRemovalService
    {
        private static LocalBackgroundRemovalService? _instance;
        private static readonly object _lock = new();

        private InferenceSession? _session;
        private readonly string _modelPath;
        private bool _isInitialized = false;

        public static LocalBackgroundRemovalService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LocalBackgroundRemovalService();
                    }
                }
                return _instance;
            }
        }

        private LocalBackgroundRemovalService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string primaryPath = Path.Combine(baseDir, "Models", "modnet.onnx");
            if (File.Exists(primaryPath))
            {
                _modelPath = primaryPath;
            }
            else
            {
                string fallback = Path.Combine(baseDir, "..", "..", "..", "Models", "modnet.onnx");
                _modelPath = File.Exists(fallback) ? Path.GetFullPath(fallback) : primaryPath;
            }
            AppLogger.Log($"LocalBackgroundRemovalService configured with model: {_modelPath} (Exists: {File.Exists(_modelPath)})");
        }

        public bool IsModelAvailable => File.Exists(_modelPath);

        private void EnsureInitialized()
        {
            if (_isInitialized && _session != null) return;

            lock (_lock)
            {
                if (_isInitialized && _session != null) return;

                if (!File.Exists(_modelPath))
                {
                    throw new FileNotFoundException($"ONNX Model not found at {_modelPath}");
                }

                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1)
                };

                _session = new InferenceSession(_modelPath, options);
                _isInitialized = true;
                AppLogger.Log("LocalBackgroundRemovalService: ONNX session initialized successfully.");
            }
        }

        public async Task<string?> RemoveBackgroundAsync(string inputPhotoPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(inputPhotoPath))
                    {
                        AppLogger.Log($"LocalBackgroundRemovalService: Photo not found: {inputPhotoPath}");
                        return null;
                    }

                    EnsureInitialized();
                    if (_session == null) return null;

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    using var originalBitmap = new Bitmap(inputPhotoPath);
                    int origWidth = originalBitmap.Width;
                    int origHeight = originalBitmap.Height;

                    // 1. Resize to model input: 512x512
                    const int targetSize = 512;
                    using var resized = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(originalBitmap, 0, 0, targetSize, targetSize);
                    }

                    // 2. Prepare Tensor [1, 3, 512, 512]
                    var inputTensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });

                    BitmapData bmpData = resized.LockBits(
                        new Rectangle(0, 0, targetSize, targetSize),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);

                    unsafe
                    {
                        byte* ptr = (byte*)bmpData.Scan0;
                        int stride = bmpData.Stride;

                        for (int y = 0; y < targetSize; y++)
                        {
                            byte* row = ptr + (y * stride);
                            for (int x = 0; x < targetSize; x++)
                            {
                                int px = x * 3;
                                float b = row[px];
                                float gVal = row[px + 1];
                                float r = row[px + 2];

                                // MODNet normalization: (x - 127.5) / 127.5
                                inputTensor[0, 0, y, x] = (r - 127.5f) / 127.5f;
                                inputTensor[0, 1, y, x] = (gVal - 127.5f) / 127.5f;
                                inputTensor[0, 2, y, x] = (b - 127.5f) / 127.5f;
                            }
                        }
                    }
                    resized.UnlockBits(bmpData);

                    // 3. Run inference
                    string inputName = _session.InputMetadata.Keys.First();
                    var inputs = new NamedOnnxValue[]
                    {
                        NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
                    };

                    using var results = _session.Run(inputs);
                    var outputTensor = results.First().AsTensor<float>();

                    // 4. Create 512x512 mask bitmap
                    using var mask512 = new Bitmap(targetSize, targetSize, PixelFormat.Format8bppIndexed);
                    ColorPalette palette = mask512.Palette;
                    for (int i = 0; i < 256; i++)
                    {
                        palette.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                    }
                    mask512.Palette = palette;

                    BitmapData maskData = mask512.LockBits(
                        new Rectangle(0, 0, targetSize, targetSize),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format8bppIndexed);

                    unsafe
                    {
                        byte* mPtr = (byte*)maskData.Scan0;
                        int mStride = maskData.Stride;

                        for (int y = 0; y < targetSize; y++)
                        {
                            byte* mRow = mPtr + (y * mStride);
                            for (int x = 0; x < targetSize; x++)
                            {
                                float val = outputTensor[0, 0, y, x];
                                if (val < 0f) val = 0f;
                                if (val > 1f) val = 1f;

                                mRow[x] = (byte)(val * 255f);
                            }
                        }
                    }
                    mask512.UnlockBits(maskData);

                    // 5. Upscale mask to original image size
                    using var fullMask = new Bitmap(origWidth, origHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(fullMask))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(mask512, 0, 0, origWidth, origHeight);
                    }

                    // 6. Apply fullMask alpha to original image and save as transparent PNG
                    using var finalOutput = new Bitmap(origWidth, origHeight, PixelFormat.Format32bppArgb);

                    BitmapData origData = originalBitmap.LockBits(
                        new Rectangle(0, 0, origWidth, origHeight),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

                    BitmapData maskFullData = fullMask.LockBits(
                        new Rectangle(0, 0, origWidth, origHeight),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

                    BitmapData outData = finalOutput.LockBits(
                        new Rectangle(0, 0, origWidth, origHeight),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    unsafe
                    {
                        byte* origPtr = (byte*)origData.Scan0;
                        byte* maskPtr = (byte*)maskFullData.Scan0;
                        byte* outPtr = (byte*)outData.Scan0;

                        int origStride = origData.Stride;
                        int maskStride = maskFullData.Stride;
                        int outStride = outData.Stride;

                        for (int y = 0; y < origHeight; y++)
                        {
                            byte* oRow = origPtr + (y * origStride);
                            byte* mRow = maskPtr + (y * maskStride);
                            byte* dRow = outPtr + (y * outStride);

                            for (int x = 0; x < origWidth; x++)
                            {
                                int px = x * 4;
                                byte b = oRow[px];
                                byte gVal = oRow[px + 1];
                                byte r = oRow[px + 2];
                                byte a = mRow[px]; // grayscale value from mask

                                dRow[px] = b;
                                dRow[px + 1] = gVal;
                                dRow[px + 2] = r;
                                dRow[px + 3] = a;
                            }
                        }
                    }

                    originalBitmap.UnlockBits(origData);
                    fullMask.UnlockBits(maskFullData);
                    finalOutput.UnlockBits(outData);

                    sw.Stop();
                    string outPath = Path.Combine(Path.GetTempPath(), $"local_bg_removed_{Guid.NewGuid()}.png");
                    finalOutput.Save(outPath, ImageFormat.Png);
                    AppLogger.Log($"Local ONNX background removal completed in {sw.ElapsedMilliseconds}ms! Saved: {outPath}");
                    return outPath;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("LocalBackgroundRemovalService exception: " + ex.Message);
                    return null;
                }
            });
        }
    }
}
