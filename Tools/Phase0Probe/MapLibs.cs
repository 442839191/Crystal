using System.Collections.Generic;
using System.IO;

namespace Crystal.Assets
{
    // Port of the MapLibs index table in Client.MirGraphics.MLibrary (lines 125-186).
    public sealed class MapLibs
    {
        private readonly string _dataPath;
        private readonly Dictionary<int, LibFile> _open = new Dictionary<int, LibFile>();
        private readonly string[] _paths = new string[400];

        public MapLibs(string dataPath)
        {
            _dataPath = dataPath;

            string w2 = Path.Combine(dataPath, "Map", "WemadeMir2") + Path.DirectorySeparatorChar;
            _paths[0] = w2 + "Tiles";
            _paths[1] = w2 + "Smtiles";
            _paths[2] = w2 + "Objects";
            for (int i = 2; i < 28; i++) _paths[i + 1] = w2 + "Objects" + i;
            _paths[90] = w2 + "Objects_32bit";

            string s2 = Path.Combine(dataPath, "Map", "ShandaMir2") + Path.DirectorySeparatorChar;
            _paths[100] = s2 + "Tiles";
            for (int i = 1; i < 10; i++) _paths[100 + i] = s2 + "Tiles" + (i + 1);
            _paths[110] = s2 + "SmTiles";
            for (int i = 1; i < 10; i++) _paths[110 + i] = s2 + "SmTiles" + (i + 1);
            _paths[120] = s2 + "Objects";
            for (int i = 1; i < 31; i++) _paths[120 + i] = s2 + "Objects" + (i + 1);
            _paths[190] = s2 + "AniTiles1";

            string[] state = { "", "wood\\", "sand\\", "snow\\", "forest\\" };
            string[] names = { "Tilesc", "Tiles30c", "Tiles5c", "Smtilesc", "Housesc", "Cliffsc",
                               "Dungeonsc", "Innersc", "Furnituresc", "Wallsc", "smObjectsc",
                               "Animationsc", "Object1c", "Object2c" };
            for (int i = 0; i < state.Length; i++)
                for (int n = 0; n < names.Length; n++)
                    _paths[200 + i * 15 + n] = Path.Combine(dataPath, "Map", "WemadeMir3", state[i]) + names[n];

            string[] state2 = { "", "wood", "sand", "snow", "forest" };
            for (int i = 0; i < state2.Length; i++)
                for (int n = 0; n < names.Length; n++)
                    _paths[300 + i * 15 + n] = Path.Combine(dataPath, "Map", "ShandaMir3") + Path.DirectorySeparatorChar + names[n] + state2[i];
        }

        public LibFile Get(int index)
        {
            if (index < 0 || index >= _paths.Length || _paths[index] == null) return null;
            if (_open.TryGetValue(index, out var lib)) return lib;

            string file = _paths[index] + ".Lib";
            if (!File.Exists(file))
            {
                _open[index] = null;
                return null;
            }
            lib = new LibFile(file);
            _open[index] = lib;
            return lib;
        }

        public int OpenedCount => _open.Count;
    }
}