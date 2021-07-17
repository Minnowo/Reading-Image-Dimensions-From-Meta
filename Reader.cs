using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;

namespace ImageDimensionReader
{
    /// <summary>
    /// A class used for reading the width and height of an image file using their headers.
    /// </summary>
    // Mod of https://stackoverflow.com/a/60667939
    public static class ImageDimensionReader
    {
        const byte MAX_MAGIC_BYTE_LENGTH = 8;

        readonly static Dictionary<byte[], Func<BinaryReader, Size>> imageFormatDecoders = new Dictionary<byte[], Func<BinaryReader, Size>>()
        {
            { new byte[] { 0x42, 0x4D }, DecodeBitmap },
            { new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, DecodeGif },
            { new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, DecodeGif },
            { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, DecodePng },
            { new byte[] { 0xff, 0xd8 }, DecodeJfif },
            { new byte[] { 0x52, 0x49, 0x46, 0x46 }, DecodeWebP },
            { new byte[] { 0x49, 0x49, 0x2A },  DecodeTiffLE }, // little endian
            { new byte[] { 0x4D, 0x4D, 0x00, 0x2A },  DecodeTiffBE }  // big endian
        };

        /// <summary>        
        /// Gets the dimensions of an image.        
        /// </summary>        
        /// <param name="path">The path of the image to get the dimensions of.</param>        
        /// <returns>The dimensions of the specified image.</returns>        
        /// <exception cref="ArgumentException">The image was of an unrecognised format.</exception>            
        public static Size GetDimensions(BinaryReader binaryReader)
        {
            byte[] magicBytes = new byte[MAX_MAGIC_BYTE_LENGTH];

            for (int i = 0; i < MAX_MAGIC_BYTE_LENGTH; i += 1)
            {
                magicBytes[i] = binaryReader.ReadByte();

                foreach (KeyValuePair<byte[], Func<BinaryReader, Size>> kvPair in imageFormatDecoders)
                {
                    if (StartsWith(magicBytes, kvPair.Key))
                    {
                        return kvPair.Value(binaryReader);
                    }
                }
            }

            return Size.Empty;
        }

        /// <summary>
        /// Gets the dimensions of an image.
        /// </summary>
        /// <param name="path">The path of the image to get the dimensions of.</param>
        /// <returns>The dimensions of the specified image.</returns>
        /// <exception cref="ArgumentException">The image was of an unrecognized format.</exception>
        public static Size GetDimensions(string path)
        {
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader binaryReader = new BinaryReader(fileStream))
            {
                try
                {
                    return GetDimensions(binaryReader);
                }
                catch
                {
                    return Size.Empty;
                }
            }
        }


        private static Size DecodeTiffLE(BinaryReader binaryReader)
        {
            if (binaryReader.ReadByte() != 0)
                return Size.Empty;

            int idfStart = ReadInt32LE(binaryReader);

            binaryReader.BaseStream.Seek(idfStart, SeekOrigin.Begin);

            int numberOfIDF = ReadInt16LE(binaryReader);

            int width = -1;
            int height = -1;
            for (int i = 0; i < numberOfIDF; i++)
            {
                short field = ReadInt16LE(binaryReader);

                switch (field)
                {
                    // https://www.awaresystems.be/imaging/tiff/tifftags/baseline.html
                    default:
                        binaryReader.ReadBytes(10);
                        break;
                    case 256: // image width
                        binaryReader.ReadBytes(6);
                        width = ReadInt32LE(binaryReader);
                        break;
                    case 257: // image length
                        binaryReader.ReadBytes(6);
                        height = ReadInt32LE(binaryReader);
                        break;
                }
                if (width != -1 && height != -1)
                    return new Size(width, height);
            }
            return Size.Empty;
        }

        private static Size DecodeTiffBE(BinaryReader binaryReader)
        {
            int idfStart = ReadInt32BE(binaryReader);

            binaryReader.BaseStream.Seek(idfStart, SeekOrigin.Begin);

            int numberOfIDF = ReadInt16BE(binaryReader);

            int width = -1;
            int height = -1;
            for (int i = 0; i < numberOfIDF; i++)
            {
                short field = ReadInt16BE(binaryReader);

                switch (field)
                {
                    // https://www.awaresystems.be/imaging/tiff/tifftags/baseline.html
                    default:
                        binaryReader.ReadBytes(10);
                        break;
                    case 256: // image width
                        binaryReader.ReadBytes(6);
                        width = ReadInt32BE(binaryReader);
                        break;
                    case 257: // image length
                        binaryReader.ReadBytes(6);
                        height = ReadInt32BE(binaryReader);
                        break;
                }
                if (width != -1 && height != -1)
                    return new Size(width, height);
            }
            return Size.Empty;
        }

        private static Size DecodeBitmap(BinaryReader binaryReader)
        {
            binaryReader.ReadBytes(16);
            int width = binaryReader.ReadInt32();
            int height = binaryReader.ReadInt32();
            return new Size(width, height);
        }

        private static Size DecodeGif(BinaryReader binaryReader)
        {
            int width = binaryReader.ReadInt16();
            int height = binaryReader.ReadInt16();
            return new Size(width, height);
        }

        private static Size DecodePng(BinaryReader binaryReader)
        {
            binaryReader.ReadBytes(8);
            int width = ReadInt32BE(binaryReader);
            int height = ReadInt32BE(binaryReader);
            return new Size(width, height);
        }

        private static Size DecodeJfif(BinaryReader binaryReader)
        {
            while (binaryReader.ReadByte() == 0xff)
            {
                byte marker = binaryReader.ReadByte();
                short chunkLength = ReadInt16BE(binaryReader);
                if (marker == 0xc0 || marker == 0xc2) // c2: progressive
                {
                    binaryReader.ReadByte();
                    int height = ReadInt16BE(binaryReader);
                    int width = ReadInt16BE(binaryReader);
                    return new Size(width, height);
                }

                if (chunkLength < 0)
                {
                    ushort uchunkLength = (ushort)chunkLength;
                    binaryReader.ReadBytes(uchunkLength - 2);
                }
                else
                {
                    binaryReader.ReadBytes(chunkLength - 2);
                }
            }

            return Size.Empty;
        }

        private static Size DecodeWebP(BinaryReader binaryReader)
        {
            // 'RIFF' already read   

            binaryReader.ReadBytes(4);

            if (ReadInt32LE(binaryReader) != 1346520407)// 1346520407 : 'WEBP'
                return Size.Empty;

            switch (ReadInt32LE(binaryReader))
            {
                case 540561494: // 'VP8 ' : lossy
                    // skip stuff we don't need
                    binaryReader.ReadBytes(7);

                    if (ReadInt24LE(binaryReader) != 2752925) // invalid webp file
                        return Size.Empty;

                    return new Size(ReadInt16LE(binaryReader), ReadInt16LE(binaryReader));

                case 1278758998:// 'VP8L' : lossless
                    // skip stuff we don't need
                    binaryReader.ReadBytes(4);

                    if (binaryReader.ReadByte() != 47)// 0x2f : 47 1 byte signature
                        return Size.Empty;

                    byte[] b = binaryReader.ReadBytes(4);

                    return new Size(
                        1 + (((b[1] & 0x3F) << 8) | b[0]),
                        1 + ((b[3] << 10) | (b[2] << 2) | ((b[1] & 0xC0) >> 6)));
                // if something breaks put in the '& 0xF' but & oxf should do nothing in theory
                // because inclusive & with 1111 will leave the binary untouched
                //  1 + (((wh[3] & 0xF) << 10) | (wh[2] << 2) | ((wh[1] & 0xC0) >> 6))

                case 1480085590:// 'VP8X' : extended
                    // skip stuff we don't need
                    binaryReader.ReadBytes(8);
                    return new Size(1 + ReadInt24LE(binaryReader), 1 + ReadInt24LE(binaryReader));
            }

            return Size.Empty;
        }

        private static bool StartsWith(byte[] thisBytes, byte[] thatBytes)
        {
            for (int i = 0; i < thatBytes.Length; i += 1)
                if (thisBytes[i] != thatBytes[i])
                    return false;

            return true;
        }

        #region Endians

        /// <summary>
        /// Reads a 16 bit int from the stream in the Little Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static short ReadInt16LE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(2);
            return (short)((bytes[0]) | (bytes[1] << 8));
        }

        /// <summary>
        /// Reads a 24 bit int from the stream in the Little Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static int ReadInt24LE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(3);
            return ((bytes[0]) | (bytes[1] << 8) | (bytes[2] << 16));
        }

        /// <summary>
        /// Reads a 32 bit int from the stream in the Little Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static int ReadInt32LE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(4);
            return ((bytes[0]) | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
        }



        /// <summary>
        /// Reads a 32 bit int from the stream in the Big Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static int ReadInt32BE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(4);
            return ((bytes[3]) | (bytes[2] << 8) | (bytes[1] << 16) | (bytes[0] << 24));
        }

        /// <summary>
        /// Reads a 24 bit int from the stream in the Big Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static int ReadInt24BE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(3);
            return ((bytes[2]) | (bytes[1] << 8) | (bytes[0] << 16));
        }

        /// <summary>
        /// Reads a 16 bit int from the stream in the Big Endian format.
        /// </summary>
        /// <param name="binaryReader">The binary reader to read</param>
        /// <returns></returns>
        private static short ReadInt16BE(BinaryReader binaryReader)
        {
            byte[] bytes = binaryReader.ReadBytes(2);
            return (short)((bytes[1]) | (bytes[0] << 8));
        }

        #endregion
    }
}
