#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using SkiaSharp;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ShareX.HelpersLib;

public static class AvifJxlCodec
{
    private const uint AvifAddImageFlagSingle = 1u << 1;

    public static bool IsAvifOrJxl(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jxl", StringComparison.OrdinalIgnoreCase);
    }

    public static SKBitmap LoadAvif(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        IntPtr decoder = IntPtr.Zero;
        AvifRGBImage rgb = default;
        // libavif keeps the pointer from avifDecoderSetIOMemory for the decoder lifetime.
        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        SKBitmap bitmap = null;
        try
        {
            Marshal.Copy(data, 0, dataPtr, data.Length);
            decoder = avifDecoderCreate();
            if (decoder == IntPtr.Zero) return null;

            int result = avifDecoderSetIOMemory(decoder, dataPtr, (IntPtr)data.Length);
            if (result != (int)AvifResult.OK) return null;
            result = avifDecoderParse(decoder);
            if (result != (int)AvifResult.OK) return null;
            result = avifDecoderNextImage(decoder);
            if (result != (int)AvifResult.OK) return null;

            IntPtr image = Marshal.PtrToStructure<AvifDecoder>(decoder).image;
            if (image == IntPtr.Zero) return null;

            avifRGBImageSetDefaults(ref rgb, image);
            rgb.depth = 8;
            rgb.format = AvifRGBFormat.BGRA;
            result = avifRGBImageAllocatePixels(ref rgb);
            if (result != (int)AvifResult.OK) return null;
            result = avifImageYUVToRGB(image, ref rgb);
            if (result != (int)AvifResult.OK)
            {
                avifRGBImageFreePixels(ref rgb);
                rgb.pixels = IntPtr.Zero;
                return null;
            }

            bitmap = SkiaImageHelpers.CreateBitmap((int)rgb.width, (int)rgb.height);
            using (SkiaPixelBuffer pixels = new(bitmap, true, PixelAccess.WriteOnly))
            {
                CopyRows(rgb.pixels, (int)rgb.rowBytes, PixelPointer(pixels), bitmap.Width * 4, bitmap.Width * 4, bitmap.Height);
            }
            avifRGBImageFreePixels(ref rgb);
            rgb.pixels = IntPtr.Zero;
            return bitmap;
        }
        catch (Exception exception)
        {
            DebugHelper.WriteException(exception, "Error loading AVIF: " + filePath);
            bitmap?.Dispose();
            return null;
        }
        finally
        {
            if (rgb.pixels != IntPtr.Zero) avifRGBImageFreePixels(ref rgb);
            if (decoder != IntPtr.Zero) avifDecoderDestroy(decoder);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static bool SaveAvif(SKBitmap bitmap, Stream stream, int quality, int speed = 6, AvifTuneIQ tune = AvifTuneIQ.Default)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        quality = Math.Clamp(quality, 0, 100);
        speed = Math.Clamp(speed, 0, 10);

        IntPtr image = IntPtr.Zero;
        IntPtr encoder = IntPtr.Zero;
        AvifRWData encoded = default;
        try
        {
            image = avifImageCreate((uint)bitmap.Width, (uint)bitmap.Height, 8, AvifPixelFormat.YUV444);
            if (image == IntPtr.Zero) return false;

            using (SkiaPixelBuffer pixels = new(bitmap, true, PixelAccess.ReadOnly))
            {
                AvifRGBImage rgb = default;
                avifRGBImageSetDefaults(ref rgb, image);
                rgb.format = AvifRGBFormat.BGRA;
                rgb.pixels = PixelPointer(pixels);
                rgb.rowBytes = (uint)(bitmap.Width * 4);
                if (avifImageRGBToYUV(image, ref rgb) != (int)AvifResult.OK) return false;
            }

            encoder = avifEncoderCreate();
            if (encoder == IntPtr.Zero) return false;
            avifEncoder settings = Marshal.PtrToStructure<avifEncoder>(encoder);
            settings.quality = quality;
            settings.qualityAlpha = quality;
            settings.speed = speed;
            settings.maxThreads = Math.Max(1, Environment.ProcessorCount - 1);
            settings.autoTiling = 1;
            Marshal.StructureToPtr(settings, encoder, false);
            if (tune == AvifTuneIQ.PSNR) avifEncoderSetCodecSpecificOption(encoder, "tune", "psnr");
            else if (tune == AvifTuneIQ.SSIM) avifEncoderSetCodecSpecificOption(encoder, "tune", "ssim");

            if (avifEncoderAddImage(encoder, image, 1, AvifAddImageFlagSingle) != (int)AvifResult.OK) return false;
            if (avifEncoderFinish(encoder, ref encoded) != (int)AvifResult.OK) return false;
            long size = encoded.size.ToInt64();
            if (encoded.data == IntPtr.Zero || size <= 0) return false;

            byte[] buffer = new byte[Math.Min(size, 81920)];
            long offset = 0;
            while (offset < size)
            {
                int count = (int)Math.Min(buffer.Length, size - offset);
                Marshal.Copy(IntPtr.Add(encoded.data, (int)offset), buffer, 0, count);
                stream.Write(buffer, 0, count);
                offset += count;
            }
            return true;
        }
        catch (Exception exception)
        {
            DebugHelper.WriteException(exception, "AVIF encoding failed.");
            return false;
        }
        finally
        {
            if (encoded.data != IntPtr.Zero) avifRWDataFree(ref encoded);
            if (encoder != IntPtr.Zero) avifEncoderDestroy(encoder);
            if (image != IntPtr.Zero) avifImageDestroy(image);
        }
    }

    public static SKBitmap LoadJxl(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        IntPtr decoder = IntPtr.Zero;
        IntPtr output = IntPtr.Zero;
        IntPtr basicInfo = IntPtr.Zero;
        SKBitmap bitmap = null;
        try
        {
            Marshal.Copy(data, 0, dataPtr, data.Length);
            if (JxlSignatureCheck(dataPtr, (IntPtr)data.Length) == 0) return null;

            decoder = JxlDecoderCreate(IntPtr.Zero);
            if (decoder == IntPtr.Zero) return null;
            JxlDecoderSubscribeEvents(decoder, JxlDecoderEvents.BasicInfo | JxlDecoderEvents.FullImage);
            JxlDecoderSetInput(decoder, dataPtr, (IntPtr)data.Length);
            JxlDecoderCloseInput(decoder);

            JxlPixelFormat format = new()
            {
                num_channels = 4,
                data_type = JxlDataType.Uint8,
                endianness = JxlEndianness.Native,
                align = IntPtr.Zero
            };
            int width = 0;
            int height = 0;
            basicInfo = Marshal.AllocHGlobal(256);

            while (true)
            {
                JxlDecoderStatus status = JxlDecoderProcessInput(decoder);
                if (status == JxlDecoderStatus.Error) return null;
                if (status == JxlDecoderStatus.Success) break;
                if (status == JxlDecoderStatus.BasicInfo)
                {
                    if (JxlDecoderGetBasicInfo(decoder, basicInfo) != JxlDecoderStatus.Success) return null;
                    width = Marshal.ReadInt32(basicInfo, 4);
                    height = Marshal.ReadInt32(basicInfo, 8);
                }
                else if (status == JxlDecoderStatus.NeedImageOutBuffer)
                {
                    if (JxlDecoderImageOutBufferSize(decoder, ref format, out IntPtr size) != JxlDecoderStatus.Success) return null;
                    output = Marshal.AllocHGlobal(size);
                    if (JxlDecoderSetImageOutBuffer(decoder, ref format, output, size) != JxlDecoderStatus.Success) return null;
                }
                else if (status == JxlDecoderStatus.FullImage)
                {
                    if (width <= 0 || height <= 0 || output == IntPtr.Zero) return null;
                    bitmap = SkiaImageHelpers.CreateBitmap(width, height);
                    using SkiaPixelBuffer pixels = new(bitmap, true, PixelAccess.WriteOnly);
                    SwapChannels(output, PixelPointer(pixels), width * height);
                    return bitmap;
                }
            }
            return null;
        }
        catch (Exception exception)
        {
            DebugHelper.WriteException(exception, "Error loading JPEG XL: " + filePath);
            bitmap?.Dispose();
            return null;
        }
        finally
        {
            if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            if (basicInfo != IntPtr.Zero) Marshal.FreeHGlobal(basicInfo);
            if (decoder != IntPtr.Zero) JxlDecoderDestroy(decoder);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static bool SaveJxl(SKBitmap bitmap, Stream stream, int quality, int effort = 7)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        quality = Math.Clamp(quality, 0, 100);
        effort = Math.Clamp(effort, 1, 10);

        IntPtr encoder = IntPtr.Zero;
        IntPtr pixels = IntPtr.Zero;
        IntPtr output = IntPtr.Zero;
        try
        {
            encoder = JxlEncoderCreate(IntPtr.Zero);
            if (encoder == IntPtr.Zero) return false;
            IntPtr frame = JxlEncoderFrameSettingsCreate(encoder, IntPtr.Zero);
            if (frame == IntPtr.Zero) return false;
            if (JxlEncoderFrameSettingsSetOption(frame, JxlEncoderFrameSettingId.Effort, effort) != JxlEncoderStatus.Success) return false;

            bool lossless = quality >= 100;
            IntPtr info = Marshal.AllocHGlobal(256);
            try
            {
                JxlEncoderInitBasicInfo(info);
                Marshal.WriteInt32(info, 4, bitmap.Width);
                Marshal.WriteInt32(info, 8, bitmap.Height);
                Marshal.WriteInt32(info, 12, 8);
                Marshal.WriteInt32(info, 16, 0);
                Marshal.WriteInt32(info, 36, lossless ? 1 : 0);
                Marshal.WriteInt32(info, 44, 0);
                Marshal.WriteInt32(info, 48, 1);
                Marshal.WriteInt32(info, 52, 3);
                Marshal.WriteInt32(info, 56, 1);
                Marshal.WriteInt32(info, 60, 8);
                Marshal.WriteInt32(info, 64, 0);
                Marshal.WriteInt32(info, 68, 0);
                if (JxlEncoderSetBasicInfo(encoder, info) != JxlEncoderStatus.Success) return false;
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }

            if (lossless) JxlEncoderSetFrameLossless(frame, 1);
            else if (JxlEncoderSetFrameDistance(frame, JxlEncoderDistanceFromQuality(quality)) != JxlEncoderStatus.Success) return false;

            int byteCount = bitmap.Width * bitmap.Height * 4;
            pixels = Marshal.AllocHGlobal(byteCount);
            using (SkiaPixelBuffer source = new(bitmap, true, PixelAccess.ReadOnly))
            {
                SwapChannels(PixelPointer(source), pixels, bitmap.Width * bitmap.Height);
            }

            JxlPixelFormat format = new()
            {
                num_channels = 4,
                data_type = JxlDataType.Uint8,
                endianness = JxlEndianness.Native,
                align = IntPtr.Zero
            };
            if (JxlEncoderAddImageFrame(frame, ref format, pixels, (IntPtr)byteCount) != JxlEncoderStatus.Success) return false;
            JxlEncoderCloseInput(encoder);

            const int chunkSize = 65536;
            output = Marshal.AllocHGlobal(chunkSize);
            while (true)
            {
                IntPtr next = output;
                IntPtr available = (IntPtr)chunkSize;
                JxlEncoderStatus status = JxlEncoderProcessOutput(encoder, ref next, ref available);
                int written = chunkSize - available.ToInt32();
                if (written > 0)
                {
                    byte[] chunk = new byte[written];
                    Marshal.Copy(output, chunk, 0, written);
                    stream.Write(chunk, 0, written);
                }
                if (status == JxlEncoderStatus.Success) return true;
                if (status != JxlEncoderStatus.NeedMoreOutput) return false;
            }
        }
        catch (Exception exception)
        {
            DebugHelper.WriteException(exception, "JPEG XL encoding failed.");
            return false;
        }
        finally
        {
            if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            if (pixels != IntPtr.Zero) Marshal.FreeHGlobal(pixels);
            if (encoder != IntPtr.Zero) JxlEncoderDestroy(encoder);
        }
    }

    private static unsafe IntPtr PixelPointer(SkiaPixelBuffer pixels) => (IntPtr)pixels.Pointer;

    private static unsafe void CopyRows(IntPtr source, int sourceStride, IntPtr destination, int destinationStride, int rowBytes, int height)
    {
        byte* src = (byte*)source;
        byte* dst = (byte*)destination;
        for (int y = 0; y < height; y++)
        {
            Buffer.MemoryCopy(src + y * sourceStride, dst + y * destinationStride, rowBytes, rowBytes);
        }
    }

    private static unsafe void SwapChannels(IntPtr source, IntPtr destination, int pixelCount)
    {
        byte* src = (byte*)source;
        byte* dst = (byte*)destination;
        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 4;
            dst[offset] = src[offset + 2];
            dst[offset + 1] = src[offset + 1];
            dst[offset + 2] = src[offset];
            dst[offset + 3] = src[offset + 3];
        }
    }

    private const string AvifDll = "avif.dll";
    private const string JxlDll = "jxl.dll";

    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr avifDecoderCreate();
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifDecoderSetIOMemory(IntPtr decoder, IntPtr data, IntPtr size);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifDecoderParse(IntPtr decoder);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifDecoderNextImage(IntPtr decoder);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifDecoderDestroy(IntPtr decoder);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifImageYUVToRGB(IntPtr image, ref AvifRGBImage rgb);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifRGBImageSetDefaults(ref AvifRGBImage rgb, IntPtr image);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifRGBImageAllocatePixels(ref AvifRGBImage rgb);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifRGBImageFreePixels(ref AvifRGBImage rgb);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr avifImageCreate(uint width, uint height, uint depth, AvifPixelFormat yuvFormat);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifImageDestroy(IntPtr image);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifImageRGBToYUV(IntPtr image, ref AvifRGBImage rgb);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr avifEncoderCreate();
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int avifEncoderSetCodecSpecificOption(IntPtr encoder, string key, string value);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifEncoderDestroy(IntPtr encoder);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifEncoderAddImage(IntPtr encoder, IntPtr image, ulong durationInTimescales, uint addImageFlags);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int avifEncoderFinish(IntPtr encoder, ref AvifRWData output);
    [DllImport(AvifDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void avifRWDataFree(ref AvifRWData data);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JxlEncoderCreate(IntPtr memoryManager);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlEncoderDestroy(IntPtr encoder);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JxlEncoderFrameSettingsCreate(IntPtr encoder, IntPtr frameSettings);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlEncoderInitBasicInfo(IntPtr info);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlEncoderStatus JxlEncoderSetBasicInfo(IntPtr encoder, IntPtr info);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlEncoderSetFrameLossless(IntPtr frameSettings, int lossless);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlEncoderStatus JxlEncoderSetFrameDistance(IntPtr frameSettings, float distance);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlEncoderStatus JxlEncoderFrameSettingsSetOption(IntPtr frameSettings, JxlEncoderFrameSettingId option, int value);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlEncoderStatus JxlEncoderAddImageFrame(IntPtr frameSettings, ref JxlPixelFormat pixelFormat, IntPtr buffer, IntPtr size);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlEncoderCloseInput(IntPtr encoder);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlEncoderStatus JxlEncoderProcessOutput(IntPtr encoder, ref IntPtr nextOut, ref IntPtr availableOut);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JxlDecoderCreate(IntPtr memoryManager);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderDestroy(IntPtr decoder);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderSubscribeEvents(IntPtr decoder, int events);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderSetInput(IntPtr decoder, IntPtr data, IntPtr size);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderCloseInput(IntPtr decoder);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlDecoderStatus JxlDecoderProcessInput(IntPtr decoder);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlDecoderStatus JxlDecoderGetBasicInfo(IntPtr decoder, IntPtr info);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlDecoderStatus JxlDecoderImageOutBufferSize(IntPtr decoder, ref JxlPixelFormat format, out IntPtr size);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern JxlDecoderStatus JxlDecoderSetImageOutBuffer(IntPtr decoder, ref JxlPixelFormat format, IntPtr buffer, IntPtr size);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlSignatureCheck(IntPtr data, IntPtr size);
    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern float JxlEncoderDistanceFromQuality(float quality);

    private enum AvifPixelFormat
    {
        None = 0,
        YUV444,
        YUV422,
        YUV420,
        YUV400
    }

    private enum AvifRGBFormat
    {
        RGB = 0,
        RGBA,
        ARGB,
        BGR,
        BGRA
    }

    private enum AvifChromaUpsampling
    {
        Automatic = 0
    }

    private enum AvifChromaDownsampling
    {
        Automatic = 0
    }

    private enum AvifResult
    {
        OK = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifDecoder
    {
        public int codecChoice;
        public int maxThreads;
        public int requestedSource;
        public int allowProgressive;
        public int allowIncremental;
        public int ignoreExif;
        public int ignoreXMP;
        public uint imageSizeLimit;
        public uint imageDimensionLimit;
        public uint imageCountLimit;
        public uint strictFlags;
        public IntPtr image;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct avifEncoder
    {
        public int codecChoice;
        public int maxThreads;
        public int speed;
        public int keyframeInterval;
        public ulong timescale;
        public int repetitionCount;
        public uint extraLayerCount;
        public int quality;
        public int qualityAlpha;
        public int tileRowsLog2;
        public int tileColsLog2;
        public int autoTiling;
        public AvifScalingMode scalingMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifFraction
    {
        public int n;
        public int d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifScalingMode
    {
        public AvifFraction horizontal;
        public AvifFraction vertical;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifRWData
    {
        public IntPtr data;
        public IntPtr size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifRGBImage
    {
        public uint width;
        public uint height;
        public uint depth;
        public AvifRGBFormat format;
        public AvifChromaUpsampling chromaUpsampling;
        public AvifChromaDownsampling chromaDownsampling;
        public uint avoidLibYUV;
        public uint ignoreAlpha;
        public uint alphaPremultiplied;
        public uint isFloat;
        public int maxThreads;
        public IntPtr pixels;
        public uint rowBytes;
    }

    private enum JxlEncoderStatus
    {
        Success = 0,
        Error = 1,
        NeedMoreOutput = 2
    }

    private enum JxlDecoderStatus
    {
        Success = 0,
        Error = 1,
        NeedMoreInput = 2,
        NeedPreviewOutBuffer = 3,
        NeedImageOutBuffer = 5,
        JpegNeedMoreOutput = 6,
        BoxNeedMoreOutput = 7,
        BasicInfo = 0x40,
        ColorEncoding = 0x100,
        PreviewImage = 0x200,
        Frame = 0x400,
        FullImage = 0x1000,
        JpegReconstruction = 0x2000,
        Box = 0x4000,
        FrameProgression = 0x8000
    }

    private static class JxlDecoderEvents
    {
        public const int BasicInfo = 0x40;
        public const int FullImage = 0x1000;
    }

    private enum JxlDataType
    {
        Float = 0,
        Uint8 = 2
    }

    private enum JxlEndianness
    {
        Native = 0
    }

    private enum JxlEncoderFrameSettingId
    {
        Effort = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JxlPixelFormat
    {
        public uint num_channels;
        public JxlDataType data_type;
        public JxlEndianness endianness;
        public IntPtr align;
    }

}
