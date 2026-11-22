using System;
using System.IO;

namespace SkinChanceMod
{
    /// <summary>
    /// 贴图解码：能在后台线程跑的格式自己解（PNG / BMP / TGA），
    /// 其余（JPG / GIF / PSD / EXR / HDR / WEBP 等）留字节给主线程用
    /// UnityEngine.ImageConversion.LoadImage 兜底。
    /// 后台线程不能碰 Unity API，所以只在主线程走引擎解码。
    /// </summary>
    internal static class ImageDecoder
    {
        /// <summary>后台线程可用的纯托管解码。成功则返回自下而上可直接上传的像素吗？不，返回 PNG 顺序（自上而下）。</summary>
        public static bool TryDecodeManaged(byte[] data, out int width, out int height, out byte[] rgba)
        {
            width = 0;
            height = 0;
            rgba = null;
            if (data == null || data.Length < 32)
            {
                return false;
            }
            if (IsPng(data))
            {
                return PngDecoder.TryDecode(data, out width, out height, out rgba);
            }
            if (IsBmp(data))
            {
                return TryDecodeBmp(data, out width, out height, out rgba);
            }
            if (IsTga(data))
            {
                return TryDecodeTga(data, out width, out height, out rgba);
            }
            return false;
        }

        /// <summary>给日志用的格式名。</summary>
        public static string Describe(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return "空文件";
            }
            if (IsPng(data))
            {
                return "PNG";
            }
            if (IsBmp(data))
            {
                return "BMP";
            }
            if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return "JPEG";
            }
            if (data.Length > 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            {
                return "GIF";
            }
            if (data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            {
                return "WEBP";
            }
            if (data.Length > 4 && data[0] == '8' && data[1] == 'B' && data[2] == 'P' && data[3] == 'S')
            {
                return "PSD";
            }
            if (data.Length > 1152 && data[0] == 0x76 && data[1] == 0x2F && data[2] == 0x31 && data[3] == 0x01)
            {
                return "EXR";
            }
            if (data.Length > 11 && data[0] == '#' && data[1] == '?')
            {
                return "HDR";
            }
            if (IsTga(data))
            {
                return "TGA";
            }
            return "未知格式";
        }

        private static bool IsPng(byte[] d)
        {
            return d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;
        }

        private static bool IsBmp(byte[] d)
        {
            return d.Length > 26 && d[0] == (byte)'B' && d[1] == (byte)'M';
        }

        /// <summary>TGA 没有 magic，只能按头部字段的合理性猜，避免把别的格式误判。</summary>
        private static bool IsTga(byte[] d)
        {
            if (d.Length < 18)
            {
                return false;
            }
            int colorMapType = d[1];
            int imageType = d[2];
            int bpp = d[16];
            int width = d[12] | (d[13] << 8);
            int height = d[14] | (d[15] << 8);
            bool typeOk = imageType == 1 || imageType == 2 || imageType == 3
                || imageType == 9 || imageType == 10 || imageType == 11;
            if (!typeOk || colorMapType > 1)
            {
                return false;
            }
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            {
                return false;
            }
            bool bppOk = bpp == 8 || bpp == 16 || bpp == 24 || bpp == 32;
            if (!bppOk)
            {
                return false;
            }
            // 未被压缩的类型，像素数据必须放得下
            if (imageType == 2 || imageType == 3)
            {
                long need = (long)width * height * ((bpp + 7) / 8);
                if (18 + (long)d[0] + need > d.Length + 8)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryDecodeBmp(byte[] d, out int width, out int height, out byte[] rgba)
        {
            width = 0;
            height = 0;
            rgba = null;
            try
            {
                int dataOffset = BitConverter.ToInt32(d, 10);
                int headerSize = BitConverter.ToInt32(d, 14);
                if (headerSize < 12)
                {
                    return false;
                }
                int w;
                int h;
                int bpp;
                int compression;
                int paletteColors;
                if (headerSize == 12)
                {
                    w = d[18] | (d[19] << 8);
                    h = d[20] | (d[21] << 8);
                    bpp = d[24] | (d[25] << 8);
                    compression = 0;
                    paletteColors = 0;
                }
                else
                {
                    w = BitConverter.ToInt32(d, 18);
                    h = BitConverter.ToInt32(d, 22);
                    bpp = BitConverter.ToInt16(d, 28);
                    compression = BitConverter.ToInt32(d, 30);
                    paletteColors = BitConverter.ToInt32(d, 46);
                }
                bool bottomUp = h > 0;
                if (h < 0)
                {
                    h = -h;
                }
                if (w <= 0 || h <= 0 || w > 16384 || h > 16384)
                {
                    return false;
                }
                if (compression != 0 && !(compression == 3 && bpp == 32))
                {
                    return false;
                }
                if (bpp != 8 && bpp != 24 && bpp != 32)
                {
                    return false;
                }
                int stride = ((w * bpp + 31) / 32) * 4;
                if (dataOffset <= 0 || dataOffset + (long)stride * h > d.Length)
                {
                    return false;
                }
                byte[] palette = null;
                if (bpp == 8)
                {
                    int count = paletteColors > 0 ? paletteColors : 256;
                    int start = 14 + headerSize;
                    if (start + count * 4 <= d.Length)
                    {
                        palette = new byte[count * 4];
                        Array.Copy(d, start, palette, 0, count * 4);
                    }
                    else
                    {
                        return false;
                    }
                }
                rgba = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    int srcRow = dataOffset + (bottomUp ? (h - 1 - y) : y) * stride;
                    int dstRow = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int dst = dstRow + x * 4;
                        if (bpp == 8)
                        {
                            int index = d[srcRow + x];
                            int p = index * 4;
                            rgba[dst] = palette[p + 2];
                            rgba[dst + 1] = palette[p + 1];
                            rgba[dst + 2] = palette[p + 0];
                            rgba[dst + 3] = 255;
                        }
                        else if (bpp == 24)
                        {
                            int src = srcRow + x * 3;
                            rgba[dst] = d[src + 2];
                            rgba[dst + 1] = d[src + 1];
                            rgba[dst + 2] = d[src];
                            rgba[dst + 3] = 255;
                        }
                        else
                        {
                            int src = srcRow + x * 4;
                            rgba[dst] = d[src + 2];
                            rgba[dst + 1] = d[src + 1];
                            rgba[dst + 2] = d[src];
                            rgba[dst + 3] = d[src + 3];
                        }
                    }
                }
                width = w;
                height = h;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeTga(byte[] d, out int width, out int height, out byte[] rgba)
        {
            width = 0;
            height = 0;
            rgba = null;
            try
            {
                int idLength = d[0];
                int colorMapType = d[1];
                int imageType = d[2];
                int colorMapFirst = d[3] | (d[4] << 8);
                int colorMapLength = d[5] | (d[6] << 8);
                int colorMapEntry = d[7];
                int w = d[12] | (d[13] << 8);
                int h = d[14] | (d[15] << 8);
                int bpp = d[16];
                int descriptor = d[17];
                bool topDown = (descriptor & 0x20) != 0;
                bool rightToLeft = (descriptor & 0x10) != 0;

                bool rle = imageType >= 9;
                bool grayscale = imageType == 3 || imageType == 11;
                bool indexed = imageType == 1 || imageType == 9;

                if (colorMapType == 1 && colorMapLength > 0)
                {
                    int entryBytes = (colorMapEntry + 7) / 8;
                    if (entryBytes != 2 && entryBytes != 3 && entryBytes != 4)
                    {
                        return false;
                    }
                }

                int offset = 18 + idLength;
                if (colorMapType == 1)
                {
                    offset += colorMapLength * ((colorMapEntry + 7) / 8);
                }
                if (offset >= d.Length)
                {
                    return false;
                }

                int bytesPerPixel = (bpp + 7) / 8;
                if (bytesPerPixel <= 0 || bytesPerPixel > 4)
                {
                    return false;
                }
                if (!grayscale && !indexed && bpp != 24 && bpp != 32)
                {
                    return false;
                }
                if (grayscale && bpp != 8 && bpp != 16)
                {
                    return false;
                }

                int pixelCount = w * h;
                byte[] pixels = new byte[pixelCount * bytesPerPixel];

                if (!rle)
                {
                    if (offset + (long)pixels.Length > d.Length)
                    {
                        return false;
                    }
                    Array.Copy(d, offset, pixels, 0, pixels.Length);
                }
                else
                {
                    int src = offset;
                    int dst = 0;
                    while (dst < pixels.Length && src < d.Length)
                    {
                        int header = d[src++];
                        int count = (header & 0x7F) + 1;
                        if ((header & 0x80) != 0)
                        {
                            if (src + bytesPerPixel > d.Length)
                            {
                                return false;
                            }
                            for (int i = 0; i < count && dst < pixels.Length; i++)
                            {
                                for (int b = 0; b < bytesPerPixel; b++)
                                {
                                    pixels[dst + b] = d[src + b];
                                }
                                dst += bytesPerPixel;
                            }
                            src += bytesPerPixel;
                        }
                        else
                        {
                            int bytes = count * bytesPerPixel;
                            if (src + bytes > d.Length)
                            {
                                return false;
                            }
                            int copy = Math.Min(bytes, pixels.Length - dst);
                            Array.Copy(d, src, pixels, dst, copy);
                            dst += copy;
                            src += bytes;
                        }
                    }
                    if (dst < pixels.Length)
                    {
                        return false;
                    }
                }

                rgba = new byte[pixelCount * 4];
                for (int y = 0; y < h; y++)
                {
                    int srcRow = (topDown ? y : (h - 1 - y)) * w * bytesPerPixel;
                    int dstRow = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int sx = rightToLeft ? (w - 1 - x) : x;
                        int src = srcRow + sx * bytesPerPixel;
                        int dst = dstRow + x * 4;
                        if (grayscale)
                        {
                            byte g = pixels[src];
                            rgba[dst] = g;
                            rgba[dst + 1] = g;
                            rgba[dst + 2] = g;
                            rgba[dst + 3] = bpp == 16 ? pixels[src + 1] : (byte)255;
                        }
                        else if (indexed)
                        {
                            int index = bpp == 16
                                ? (pixels[src] | (pixels[src + 1] << 8))
                                : pixels[src];
                            int entryBytes = (colorMapEntry + 7) / 8;
                            int entry = 18 + idLength + (index - colorMapFirst) * entryBytes;
                            if (entry < 0 || entry + entryBytes > d.Length)
                            {
                                rgba[dst + 3] = 255;
                            }
                            else if (entryBytes >= 3)
                            {
                                rgba[dst] = d[entry + 2];
                                rgba[dst + 1] = d[entry + 1];
                                rgba[dst + 2] = d[entry];
                                rgba[dst + 3] = entryBytes == 4 ? d[entry + 3] : (byte)255;
                            }
                            else
                            {
                                int v = d[entry] | (d[entry + 1] << 8);
                                rgba[dst] = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                                rgba[dst + 1] = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                                rgba[dst + 2] = (byte)((v & 0x1F) * 255 / 31);
                                rgba[dst + 3] = 255;
                            }
                        }
                        else if (bytesPerPixel == 3)
                        {
                            rgba[dst] = pixels[src + 2];
                            rgba[dst + 1] = pixels[src + 1];
                            rgba[dst + 2] = pixels[src];
                            rgba[dst + 3] = 255;
                        }
                        else
                        {
                            rgba[dst] = pixels[src + 2];
                            rgba[dst + 1] = pixels[src + 1];
                            rgba[dst + 2] = pixels[src];
                            rgba[dst + 3] = pixels[src + 3];
                        }
                    }
                }

                width = w;
                height = h;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
