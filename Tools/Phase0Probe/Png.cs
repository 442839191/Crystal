using System;
using System.IO;
using System.IO.Compression;

namespace Crystal.Assets
{
    // Dependency-free RGBA PNG writer. Uses DeflateStream plus a hand-written zlib
    // wrapper so it also compiles under Unity's .NET Standard 2.1 profile.
    public static class Png
    {
        public static void Save(string path, int width, int height, byte[] rgba)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // RGBA
            Chunk(fs, "IHDR", ihdr);

            var raw = new byte[height * (1 + width * 4)];
            for (int y = 0; y < height; y++)
            {
                raw[y * (1 + width * 4)] = 0; // filter: none
                Buffer.BlockCopy(rgba, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
            }

            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);
                compressed.WriteByte(0x9C);
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                uint adler = Adler32(raw);
                compressed.WriteByte((byte)(adler >> 24));
                compressed.WriteByte((byte)(adler >> 16));
                compressed.WriteByte((byte)(adler >> 8));
                compressed.WriteByte((byte)adler);
                Chunk(fs, "IDAT", compressed.ToArray());
            }

            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteInt(len, 0, data.Length);
            s.Write(len, 0, 4);

            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            s.Write(body, 0, body.Length);

            var crc = new byte[4];
            WriteInt(crc, 0, (int)Crc32(body));
            s.Write(crc, 0, 4);
        }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}