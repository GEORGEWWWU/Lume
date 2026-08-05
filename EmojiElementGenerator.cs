using ICSharpCode.AvalonEdit.Rendering;
using SharpGen.Runtime;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
// Vortice 命名空间 — 注意不要和 WPF 冲突
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Lume
{
    public class EmojiElementGenerator : VisualLineElementGenerator
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> _cache = new();
        private static IDWriteFactory? _dwFactory;
        private static ID2D1Factory? _d2dFactory;
        private static readonly object _initLock = new();

        private static void EnsureFactories()
        {
            if (_dwFactory != null && _d2dFactory != null) return;
            lock (_initLock)
            {
                // Vortice 3.x: DWrite.CreateFactory / D2D1.CreateFactory
                _dwFactory ??= Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
                _d2dFactory ??= Vortice.Direct2D1.D2D1.D2D1CreateFactory<ID2D1Factory>();
            }
        }

        private static bool IsEmojiChar(char c)
        {
            if (char.IsHighSurrogate(c) || char.IsLowSurrogate(c)) return true;
            if (c >= 0xFE00 && c <= 0xFE0F) return true;
            if (c == 0x200D) return true;
            if (c >= 0x1F600 && c <= 0x1F64F) return true;
            if (c >= 0x1F300 && c <= 0x1F5FF) return true;
            if (c >= 0x1F680 && c <= 0x1F6FF) return true;
            if (c >= 0x1F900 && c <= 0x1F9FF) return true;
            if (c >= 0x1FA00 && c <= 0x1FAFF) return true;
            if (c >= 0x2600 && c <= 0x27BF) return true;
            if (c >= 0x2B50 && c <= 0x2B55) return true;
            if (c >= 0x231A && c <= 0x23F3) return true;
            if (c >= 0x23F8 && c <= 0x23FA) return true;
            if (c >= 0x2934 && c <= 0x2935) return true;
            if (c >= 0x25AA && c <= 0x25FE) return true;
            if (c >= 0x2194 && c <= 0x21AA) return true;
            if (c >= 0x2B05 && c <= 0x2B07) return true;
            if (c == 0x203C || c == 0x2049) return true;
            if (c == 0x2328 || c == 0x23CF) return true;
            if (c == 0x24C2) return true;
            if (c == 0x3030 || c == 0x303D) return true;
            if (c == 0x3297 || c == 0x3299) return true;
            if (c == 0xA9 || c == 0xAE) return true;
            if (c == 0x2122 || c == 0x2139) return true;
            return false;
        }


        private static BitmapSource RenderEmojiWithDirectWrite(string emojiText, float fontSize)
        {
            string cacheKey = $"{emojiText}_{fontSize:F1}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            EnsureFactories();

            using var textFormat = _dwFactory!.CreateTextFormat(
                "Segoe UI Emoji",
                null,
                Vortice.DirectWrite.FontWeight.Regular,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                fontSize);

            using var textLayout = _dwFactory.CreateTextLayout(
                emojiText,
                textFormat,
                float.MaxValue,
                float.MaxValue);

            var metrics = textLayout.Metrics;
            int w = Math.Max(1, (int)Math.Ceiling(metrics.Width));
            int h = Math.Max(1, (int)Math.Ceiling(metrics.Height));

            // ==========================================
            // 核心修复区：使用 Vortice 原生 WIC 类，抛弃互操作
            // ==========================================
            using var wicFactory = new IWICImagingFactory();
            using var wicBitmap = wicFactory.CreateBitmap(
                (uint)w,  // <--- 加上 (uint) 强转
                (uint)h,  // <--- 加上 (uint) 强转
                Vortice.WIC.PixelFormat.Format32bppPBGRA,
                BitmapCreateCacheOption.CacheOnDemand);

            using var rt = _d2dFactory!.CreateWicBitmapRenderTarget(wicBitmap,
                new RenderTargetProperties
                {
                    PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96,
                    DpiY = 96,
                    Type = RenderTargetType.Default,
                    Usage = RenderTargetUsage.None,
                    MinLevel = FeatureLevel.Default
                });

            using var brush = rt.CreateSolidColorBrush(new Vortice.Mathematics.Color4(0f, 0f, 0f, 1f));

            rt.BeginDraw();
            rt.Clear(new Vortice.Mathematics.Color4(0f, 0f, 0f, 0f));
            rt.DrawTextLayout(
                new Vector2(0f, 0f),
                textLayout,
                brush,
                DrawTextOptions.EnableColorFont);
            rt.EndDraw();

            // 直接通过原生接口锁定图像读取像素
            using var lockObj = wicBitmap.Lock(BitmapLockFlags.Read);
            int stride = (int)lockObj.Stride; // <--- 加上 (int) 强转
            byte[] pixels = new byte[stride * h];

            // 获取底层指针数据，拷贝到 byte 数组中
            Marshal.Copy(lockObj.Data.DataPointer, pixels, 0, pixels.Length);

            var bmpSource = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmpSource.Freeze();

            _cache.TryAdd(cacheKey, bmpSource);
            return bmpSource;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var doc = CurrentContext.Document;
            int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
            for (int i = startOffset; i < endOffset; i++)
            {
                if (IsEmojiChar(doc.GetCharAt(i)))
                    return i;
            }
            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var doc = CurrentContext.Document;
            int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

            int emojiEnd = offset;
            while (emojiEnd < endOffset && IsEmojiChar(doc.GetCharAt(emojiEnd)))
                emojiEnd++;

            if (emojiEnd <= offset) return null;

            string emojiText = doc.GetText(offset, emojiEnd - offset);

            float fontSize = 15f;
            try
            {
                var tv = CurrentContext.TextView;
                if (tv != null && tv.DefaultLineHeight > 0)
                {
                    fontSize = (float)(tv.DefaultLineHeight * 0.72);
                    if (fontSize < 8) fontSize = 15f;
                }
            }
            catch { }

            try
            {
                var bitmap = RenderEmojiWithDirectWrite(emojiText, fontSize);
                double dpiScale = 96.0 / bitmap.DpiX;

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Width = bitmap.PixelWidth * dpiScale,
                    Height = bitmap.PixelHeight * dpiScale,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };

                return new InlineObjectElement(emojiEnd - offset, image);
            }
            catch
            {
                return null;
            }
        }
    }
}