using System;
using System.Diagnostics;
using System.IO;
using Client.MirGraphics;

namespace Client.Utils
{
    // Read-only timing probe. Writes one line per second to perf.log so the 10Hz
    // scene bake can be profiled without changing what is rendered.
    public static class Perf
    {
        public static int BakeCount, FloorBakeCount, TextureLoadCount, DrawCount, FrameCount;
        public static long BakeTicks, FloorTicks, ObjectTicks, LightTicks, TextureLoadTicks;
        public static long UpdateTicks, RenderTicks;

        // Camera state, logged so a stepped scroll can be told apart from a smooth one.
        public static int OffX, OffY, MvX, MvY;

        private static StreamWriter _log;
        private static long _nextDump;

        public static long Now()
        {
            return Stopwatch.GetTimestamp();
        }

        public static double Ms(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        public static void Frame(long updateTicks, long renderTicks)
        {
            UpdateTicks += updateTicks;
            RenderTicks += renderTicks;
            FrameCount++;
        }

        public static void TextureLoad(long fromTs, long toTs)
        {
            TextureLoadCount++;
            TextureLoadTicks += toTs - fromTs;
        }

        public static void Dump()
        {
            try
            {
                if (CMain.Time < _nextDump)
                    return;

                _nextDump = CMain.Time + 1000;

                if (_log == null)
                    _log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.log"), false);

                int bakes = BakeCount;
                int floorBakes = FloorBakeCount;
                int loads = TextureLoadCount;
                int frames = FrameCount;

                _log.WriteLine(string.Format(
                    "t={0} fps={1} bake_hz={2} bake_ms={3:F2} floor_ms={4:F2} objects_ms={5:F2} lights_ms={6:F2} draws_per_bake={7} texload_hz={8} texload_ms={9:F2} textures={10} update_ms={11:F2} render_ms={12:F2} off={13},{14} mv={15},{16}",
                    CMain.Time, CMain.FPS, bakes,
                    bakes > 0 ? Ms(BakeTicks) / bakes : 0,
                    floorBakes > 0 ? Ms(FloorTicks) / floorBakes : 0,
                    bakes > 0 ? Ms(ObjectTicks) / bakes : 0,
                    bakes > 0 ? Ms(LightTicks) / bakes : 0,
                    bakes > 0 ? DrawCount / bakes : 0,
                    loads,
                    loads > 0 ? Ms(TextureLoadTicks) / loads : 0,
                    DXManager.TextureList.Count,
                    frames > 0 ? Ms(UpdateTicks) / frames : 0,
                    frames > 0 ? Ms(RenderTicks) / frames : 0,
                    OffX, OffY, MvX, MvY));

                BakeCount = 0;
                FloorBakeCount = 0;
                TextureLoadCount = 0;
                DrawCount = 0;
                FrameCount = 0;
                BakeTicks = 0;
                FloorTicks = 0;
                ObjectTicks = 0;
                LightTicks = 0;
                TextureLoadTicks = 0;
                UpdateTicks = 0;
                RenderTicks = 0;

                _log.Flush();
            }
            catch
            {
            }
        }
    }
}
