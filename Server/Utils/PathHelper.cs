using System.IO;

namespace Server.Utils
{
    public static class PathHelper
    {
        public static string Combine(string basePath, string path)
        {
            if (string.IsNullOrEmpty(path)) return basePath;

            path = path.Replace('\\', Path.DirectorySeparatorChar)
                       .Replace('/', Path.DirectorySeparatorChar);

            return Path.Combine(basePath, path);
        }
    }
}
