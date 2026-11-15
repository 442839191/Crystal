using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Client.MirObjects;
using Crystal.Assets;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Phase0
{
    public static class Program
    {
        private const int CellWidth = 48, CellHeight = 32;

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Phase0 probe — engine-agnostic validation of the Crystal client rewrite");
                Console.WriteLine("  libinfo <lib>                          header + embedded animation frames");
                Console.WriteLine("  lib     <lib> <index> <out.png>        decode one sprite to PNG");
                Console.WriteLine("  map     <map> <cx> <cy> <out.png> [r]  render an isometric floor window");
                Console.WriteLine("  net     <host> <port> <exe> [secs]     connect + version handshake, list packets");
                return 1;
            }

            try
            {
                switch (args[0])
                {
                    case "libinfo": return LibInfo(args[1]);
                    case "lib": return LibDump(args[1], int.Parse(args[2]), args[3]);
                    case "map": return MapRender(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4],
                                                 args.Length > 5 ? int.Parse(args[5]) : 18);
                    case "net": return NetProbe(args[1], int.Parse(args[2]), args[3],
                                                 args.Length > 4 ? int.Parse(args[4]) : 8);
                    default:
                        Console.WriteLine("unknown mode: " + args[0]);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }
        }

        private static int LibInfo(string path)
        {
            using var lib = new LibFile(path);
            Console.WriteLine($"file      : {path}");
            Console.WriteLine($"version   : {lib.Version}");
            Console.WriteLine($"images    : {lib.Count}");
            Console.WriteLine($"frames    : {lib.Frames.Count} (embedded animation table, lib v3)");
            foreach (var f in lib.Frames)
                Console.WriteLine($"  action={f.Action,3} start={f.Start,5} count={f.Count,3} skip={f.Skip,2} interval={f.Interval,5} effectStart={f.EffectStart,5} effectCount={f.EffectCount,3}");

            int nonEmpty = 0;
            for (int i = 0; i < lib.Count; i++)
            {
                var m = lib.GetMeta(i);
                if (m != null && m.Width > 0 && m.Height > 0) nonEmpty++;
            }
            Console.WriteLine($"non-empty : {nonEmpty}");
            return 0;
        }

        private static int LibDump(string path, int index, string outPng)
        {
            using var lib = new LibFile(path);
            var sw = Stopwatch.StartNew();
            var img = lib.Decode(index);
            sw.Stop();
            if (img == null)
            {
                Console.WriteLine($"index {index}: empty or out of range (count={lib.Count})");
                return 1;
            }

            int expected = img.Width * img.Height * 4;
            Console.WriteLine($"index     : {index}");
            Console.WriteLine($"size      : {img.Width}x{img.Height}  offset=({img.X},{img.Y})  shadow=({img.ShadowX},{img.ShadowY})");
            Console.WriteLine($"compressed: {img.Length} bytes -> decoded {img.Pixels.Length} bytes (expected {expected}) {(img.Pixels.Length == expected ? "OK" : "MISMATCH")}");
            Console.WriteLine($"mask      : {(img.HasMask ? $"{img.MaskWidth}x{img.MaskHeight} len={img.MaskLength} decoded={img.MaskPixels?.Length ?? 0}" : "none")}");
            Console.WriteLine($"decode    : {sw.Elapsed.TotalMilliseconds:F2} ms");

            Png.Save(outPng, img.Width, img.Height, ToRgba(img.Pixels));
            Console.WriteLine($"saved     : {outPng}");
            return 0;
        }

        private static int MapRender(string mapPath, int cx, int cy, string outPng, int radius)
        {
            var sw = Stopwatch.StartNew();
            var reader = new MapReader(mapPath);
            Console.WriteLine($"map       : {mapPath}  {reader.Width}x{reader.Height} cells");
            if (cx < 0 || cy < 0 || cx >= reader.Width || cy >= reader.Height)
            {
                Console.WriteLine("center outside map bounds");
                return 1;
            }

            string dataPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(mapPath)), "Data") + Path.DirectorySeparatorChar;
            if (!Directory.Exists(dataPath))
                dataPath = Path.Combine(Directory.GetCurrentDirectory(), "Data") + Path.DirectorySeparatorChar;
            var libs = new MapLibs(dataPath);

            int topPad = 320;
            int w = 96 + (2 * radius + 1) * CellWidth;
            int h = topPad + (2 * radius + 1) * CellHeight + 64;
            int originX = 48 + radius * CellWidth;
            int originY = topPad + radius * CellHeight;
            var canvas = new byte[w * h * 4];

            int x0 = Math.Max(0, cx - radius), x1 = Math.Min(reader.Width - 1, cx + radius);
            int y0 = Math.Max(1, cy - radius), y1 = Math.Min(reader.Height - 1, cy + radius);

            int drawn = 0, missing = 0, decoded = 0;
            var decodeSw = new Stopwatch();

            // Pass 1: floor layers, exactly as MapControl.DrawFloor selects them.
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var cell = reader.MapCells[x, y];
                    int dx = (x - cx) * CellWidth + originX;
                    int dy = (y - cy) * CellHeight + originY;

                    if (y % 2 == 0 && x % 2 == 0 && cell.BackImage != 0 && cell.BackIndex != -1)
                        Blit(libs, cell.BackIndex, (cell.BackImage & 0x1FFFFFFF) - 1, dx, dy, false, canvas, w, h, ref drawn, ref missing, ref decoded, decodeSw);

                    int mid = cell.MiddleImage - 1;
                    if (mid >= 0 && cell.MiddleIndex != -1 && IsFloorSize(libs, cell.MiddleIndex, mid))
                        Blit(libs, cell.MiddleIndex, mid, dx, dy, false, canvas, w, h, ref drawn, ref missing, ref decoded, decodeSw);

                    int front = (cell.FrontImage & 0x7FFF) - 1;
                    if (front >= 0 && cell.FrontIndex != -1 && cell.FrontIndex != 200 && IsFloorSize(libs, cell.FrontIndex, front))
                        Blit(libs, cell.FrontIndex, front, dx, dy, false, canvas, w, h, ref drawn, ref missing, ref decoded, decodeSw);
                }
            }

            // Pass 2: oversized middle/front tiles (buildings, walls) drawn with their
            // own X/Y offset, as MapControl.DrawObjects does.
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var cell = reader.MapCells[x, y];
                    int dx = (x - cx) * CellWidth + originX;
                    int dy = (y - cy) * CellHeight + originY;

                    int mid = cell.MiddleImage - 1;
                    if (mid >= 0 && cell.MiddleIndex != -1 && !IsFloorSize(libs, cell.MiddleIndex, mid))
                        Blit(libs, cell.MiddleIndex, mid, dx, dy, true, canvas, w, h, ref drawn, ref missing, ref decoded, decodeSw);

                    int front = (cell.FrontImage & 0x7FFF) - 1;
                    if (front >= 0 && cell.FrontIndex != -1 && cell.FrontIndex != 200 && !IsFloorSize(libs, cell.FrontIndex, front))
                        Blit(libs, cell.FrontIndex, front, dx, dy, true, canvas, w, h, ref drawn, ref missing, ref decoded, decodeSw);
                }
            }

            sw.Stop();
            Console.WriteLine($"window    : cells {x0}..{x1} x {y0}..{y1}  ->  {w}x{h} px");
            Console.WriteLine($"tiles     : {drawn} drawn, {missing} missing lib/image, {decoded} unique decodes");
            Console.WriteLine($"decode    : {decodeSw.Elapsed.TotalMilliseconds:F1} ms total");
            Console.WriteLine($"elapsed   : {sw.Elapsed.TotalMilliseconds:F1} ms (map parse + decode + composite)");
            Console.WriteLine($"libs open : {libs.OpenedCount}");

            Png.Save(outPng, w, h, canvas);
            Console.WriteLine($"saved     : {outPng}");
            return 0;
        }

        private static bool IsFloorSize(MapLibs libs, int libIndex, int imageIndex)
        {
            var lib = libs.Get(libIndex);
            var m = lib?.GetMeta(imageIndex);
            if (m == null) return false;
            return (m.Width == CellWidth && m.Height == CellHeight) ||
                   (m.Width == CellWidth * 2 && m.Height == CellHeight * 2);
        }

        private static void Blit(MapLibs libs, int libIndex, int imageIndex, int x, int y, bool useOffset,
                                 byte[] canvas, int cw, int ch, ref int drawn, ref int missing, ref int decoded, Stopwatch sw)
        {
            var lib = libs.Get(libIndex);
            if (lib == null) { missing++; return; }

            sw.Start();
            var img = lib.Decode(imageIndex);
            sw.Stop();
            if (img == null) { missing++; return; }
            decoded++;

            if (useOffset) { x += img.X; y += img.Y; }

            for (int row = 0; row < img.Height; row++)
            {
                int dy = y + row;
                if (dy < 0 || dy >= ch) continue;
                for (int col = 0; col < img.Width; col++)
                {
                    int dx = x + col;
                    if (dx < 0 || dx >= cw) continue;

                    int s = (row * img.Width + col) * 4;
                    byte a = img.Pixels[s + 3];
                    if (a == 0) continue;

                    int d = (dy * cw + dx) * 4;
                    if (a == 255)
                    {
                        canvas[d] = img.Pixels[s + 2];
                        canvas[d + 1] = img.Pixels[s + 1];
                        canvas[d + 2] = img.Pixels[s];
                        canvas[d + 3] = 255;
                    }
                    else
                    {
                        int inv = 255 - a;
                        canvas[d] = (byte)((img.Pixels[s + 2] * a + canvas[d] * inv) / 255);
                        canvas[d + 1] = (byte)((img.Pixels[s + 1] * a + canvas[d + 1] * inv) / 255);
                        canvas[d + 2] = (byte)((img.Pixels[s] * a + canvas[d + 2] * inv) / 255);
                        canvas[d + 3] = (byte)Math.Min(255, a + canvas[d + 3] * inv / 255);
                    }
                }
            }
            drawn++;
        }

        private static byte[] ToRgba(byte[] bgra)
        {
            var rgba = new byte[bgra.Length];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }
            return rgba;
        }

        // Connect-level handshake only: no login, no account or character state is created.
        private static int NetProbe(string host, int port, string exePath, int seconds)
        {
            byte[] hash;
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(exePath))
                hash = md5.ComputeHash(fs);
            Console.WriteLine($"client hash: {BitConverter.ToString(hash).Replace("-", "")} (from {Path.GetFileName(exePath)})");

            using var tcp = new TcpClient { NoDelay = true };
            var sw = Stopwatch.StartNew();
            tcp.Connect(host, port);
            Console.WriteLine($"connected  : {host}:{port} in {sw.Elapsed.TotalMilliseconds:F0} ms");

            var stream = tcp.GetStream();
            var version = new C.ClientVersion { VersionHash = hash };
            byte[] outbound = new List<byte>(version.GetPacketBytes()).ToArray();
            stream.Write(outbound, 0, outbound.Length);
            Console.WriteLine($"sent       : ClientVersion ({outbound.Length} bytes on the wire)");

            var buffer = new byte[8192];
            var pending = new byte[0];
            var counts = new Dictionary<string, int>();
            int total = 0;
            var deadline = Environment.TickCount64 + seconds * 1000;

            while (Environment.TickCount64 < deadline)
            {
                if (!stream.DataAvailable)
                {
                    Thread.Sleep(20);
                    continue;
                }
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;

                var combined = new byte[pending.Length + read];
                Buffer.BlockCopy(pending, 0, combined, 0, pending.Length);
                Buffer.BlockCopy(buffer, 0, combined, pending.Length, read);
                pending = combined;

                Packet p;
                while ((p = Packet.ReceivePacket(pending, out pending)) != null)
                {
                    string name = p.GetType().Name;
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + 1;
                    total++;
                    if (total <= 25) Console.WriteLine($"  <- {name}");
                    if (p is S.ClientVersion cv) Console.WriteLine($"  version result = {cv.Result} (1 = accepted)");
                }
            }

            Console.WriteLine($"received   : {total} packets in {seconds}s, {counts.Count} distinct types");
            foreach (var kv in counts) Console.WriteLine($"  {kv.Key,-28} {kv.Value}");
            Console.WriteLine(total > 0 ? "PROTOCOL OK: Shared packet classes parse the live server stream." : "NO DATA: server sent nothing.");
            return total > 0 ? 0 : 3;
        }
    }
}