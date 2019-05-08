using System;
using System.IO;
using ImageMagick;
using NuGet;

namespace Web.Business.Helpers
{
    public class ImageCompressor
    {
        private readonly int _compressionLevel;
        public static float TotallySavedMbs = 0;
        public static int OptimizedImagesCount;
        public static int SkippedImagesCount;

        public ImageCompressor(int compressionLevel)
        {
            _compressionLevel = compressionLevel;
        }

        public byte[] MakeCompressedPng(byte[] originalPngImage)
        {
            using (var memStream = new MemoryStream())
            {
                MagickImage imageMagic = new MagickImage(originalPngImage);

                imageMagic.Quality = _compressionLevel;
                imageMagic.Format = MagickFormat.Png;
                imageMagic.Interlace = Interlace.Line;
                imageMagic.Strip();
                imageMagic.Write(memStream);

                var compressedImageSize = memStream.Length;

                if (compressedImageSize < originalPngImage.LongLength)
                {
                    var difference = (originalPngImage.LongLength - compressedImageSize);
                    TotallySavedMbs += ConvertBytesToMegabytes(difference);
                    OptimizedImagesCount++;

                    return memStream.ReadAllBytes();
                }
                SkippedImagesCount++;

                return null;

            }
        }

        public byte[] MakeCompressedJpg(Byte[] originalJpgImage)
        {
            using (var memStream = new MemoryStream())
            {
                MagickImage imageMagic = new MagickImage(originalJpgImage);

                imageMagic.Quality = _compressionLevel;
                imageMagic.Format = MagickFormat.Jpg;
                imageMagic.Interlace = Interlace.Line;
                imageMagic.Strip();
                imageMagic.Write(memStream);

                var compressedImageSize = memStream.Length;

                if (compressedImageSize < originalJpgImage.LongLength)
                {
                    var difference = (originalJpgImage.LongLength - compressedImageSize);
                    TotallySavedMbs += ConvertBytesToMegabytes(difference);
                    OptimizedImagesCount++;

                    return memStream.ReadAllBytes();
                }

                SkippedImagesCount++;
                return null;

            }
        }

        private float ConvertBytesToMegabytes(long bytes)
        {
            return (bytes / 1024f) / 1024f;
        }

    }
}