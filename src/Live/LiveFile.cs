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

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(string json)
        {
            Directory.CreateDirectory(Folder);

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, Utf8NoBom);

            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);
        }
    }
}
