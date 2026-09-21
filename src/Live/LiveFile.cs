using System;
using System.IO;
using System.Text;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Where the live file goes and how it is written. It is written to a temporary file and then moved into
    /// place, so a reader never sees half a file (see docs/contract.md).
    /// </summary>
    internal static class LiveFile
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Live");

        public static readonly string FilePath = Path.Combine(Folder, "live.json");

        /// <summary>The slow file: bases, containers and extractors (see <see cref="WorldPoller"/>).</summary>
        public static readonly string WorldFilePath = Path.Combine(Folder, "live-world.json");

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(string json) => Write(FilePath, json);

        public static void Write(string path, string json)
        {
            Directory.CreateDirectory(Folder);

            var temp = path + ".tmp";
            File.WriteAllText(temp, json, Utf8NoBom);

            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }
}
