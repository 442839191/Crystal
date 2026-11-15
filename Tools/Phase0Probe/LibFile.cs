using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Crystal.Assets
{
    public sealed class LibImage
    {
        public short Width, Height, X, Y, ShadowX, ShadowY;
        public byte Shadow;
        public int Length;
        public bool HasMask;
        public short MaskWidth, MaskHeight, MaskX, MaskY;
        public int MaskLength;

        // BGRA byte order, matching D3D9 A8R8G8B8 in memory.
        public byte[] Pixels;
        public byte[] MaskPixels;
    }

    public sealed class LibFrame
    {
        public byte Action;
        public int Start, Count, Skip, Interval, EffectStart, EffectCount, EffectSkip, EffectInterval;
    }

    // Pure-data port of Client.MirGraphics.MLibrary: same header/index/pixel layout,
    // no SlimDX and no render-thread dependency, so it can run on a worker thread
    // and be dropped into Unity unchanged.
    public sealed class LibFile : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly int[] _index;
        private readonly LibImage[] _meta;

        public string FileName { get; }
        public int Version { get; }
        public int Count { get; }
        public List<LibFrame> Frames { get; } = new List<LibFrame>();

        public LibFile(string fileName)
        {
            FileName = fileName;
            _stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);

            Version = _reader.ReadInt32();
            if (Version < 2)
                throw new InvalidDataException($"{fileName}: lib version {Version}, expected >= 2");

            Count = _reader.ReadInt32();

            int frameSeek = 0;
            if (Version >= 3)
                frameSeek = _reader.ReadInt32();

            _index = new int[Count];
            for (int i = 0; i < Count; i++)
                _index[i] = _reader.ReadInt32();

            _meta = new LibImage[Count];

            if (Version >= 3 && frameSeek > 0)
            {
                _stream.Seek(frameSeek, SeekOrigin.Begin);
                int frameCount = _reader.ReadInt32();
                for (int i = 0; i < frameCount; i++)
                {
                    Frames.Add(new LibFrame
                    {
                        Action = _reader.ReadByte(),
                        Start = _reader.ReadInt32(),
                        Count = _reader.ReadInt32(),
                        Skip = _reader.ReadInt32(),
                        Interval = _reader.ReadInt32(),
                        EffectStart = _reader.ReadInt32(),
                        EffectCount = _reader.ReadInt32(),
                        EffectSkip = _reader.ReadInt32(),
                        EffectInterval = _reader.ReadInt32()
                    });
                }
            }
        }

        public LibImage GetMeta(int index)
        {
            if (index < 0 || index >= Count) return null;
            if (_meta[index] != null) return _meta[index];

            _stream.Seek(_index[index], SeekOrigin.Begin);
            var m = new LibImage
            {
                Width = _reader.ReadInt16(),
                Height = _reader.ReadInt16(),
                X = _reader.ReadInt16(),
                Y = _reader.ReadInt16(),
                ShadowX = _reader.ReadInt16(),
                ShadowY = _reader.ReadInt16(),
                Shadow = _reader.ReadByte(),
                Length = _reader.ReadInt32()
            };
            m.HasMask = (m.Shadow >> 7) == 1;
            if (m.HasMask)
            {
                _stream.Seek(m.Length, SeekOrigin.Current);
                m.MaskWidth = _reader.ReadInt16();
                m.MaskHeight = _reader.ReadInt16();
                m.MaskX = _reader.ReadInt16();
                m.MaskY = _reader.ReadInt16();
                m.MaskLength = _reader.ReadInt32();
            }
            _meta[index] = m;
            return m;
        }

        public LibImage Decode(int index)
        {
            var m = GetMeta(index);
            if (m == null || m.Width == 0 || m.Height == 0) return null;
            if (m.Pixels != null) return m;

            _stream.Seek(_index[index] + 17, SeekOrigin.Begin);
            m.Pixels = Inflate(_reader.ReadBytes(m.Length));

            if (m.HasMask)
            {
                // The shipped client re-reads Length here instead of MaskLength; the
                // correct mask size is used so the probe can report the difference.
                m.MaskPixels = Inflate(_reader.ReadBytes(m.MaskLength));
            }
            return m;
        }

        private static byte[] Inflate(byte[] gzip)
        {
            using var source = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
            using var output = new MemoryStream();
            source.CopyTo(output);
            return output.ToArray();
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}