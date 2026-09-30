using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Builds up <c>planets.json</c> as the owner visits worlds: which planet id (its own name, "Prime", "Humble", ...)
    /// goes with which hash (the number every save, and every section of the live files, uses to mark what belongs to
    /// that planet — the game's own <c>GetStableHashCode()</c> of the id). A save or the world file only ever carries the
    /// hash, never the name, so the dashboard cannot put a name on one it has not been told; this is the only way it
    /// learns them, one planet at a time as the owner lands on each. Read-only towards the game: it only ever adds an
    /// entry this file does not already have (a name never changes once learned), and does nothing until asked.
    /// </summary>
    internal static class PlanetCatalog
    {
        public static readonly string FilePath = Path.Combine(LiveFile.Folder, "planets.json");

        private static readonly Regex Entry = new("\"(-?\\d+)\":\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Dictionary<int, string> Known = new();
        private static bool _loaded;

        /// <summary>Call once per tick while in a world. Writes the file only the first time a hash is seen, or seen under a different name.</summary>
        public static void Note(string planetId, int planetHash)
        {
            if (string.IsNullOrEmpty(planetId) || planetHash == 0)
                return;

            if (!_loaded)
                Load();

            if (Known.TryGetValue(planetHash, out var already) && already == planetId)
                return;

            Known[planetHash] = planetId;
            Save();
        }

        private static void Load()
        {
            _loaded = true;

            try
            {
                if (!File.Exists(FilePath))
                    return;

                foreach (Match m in Entry.Matches(File.ReadAllText(FilePath)))
                {
                    if (int.TryParse(m.Groups[1].Value, out var hash))
                        Known[hash] = m.Groups[2].Value;
                }
            }
            catch (Exception)
            {
                // Starts empty; every planet still in play gets re-learned as the owner visits it again.
            }
        }

        private static void Save()
        {
            try
            {
                var json = "{" + string.Join(",", Known.OrderBy(p => p.Key)
                    .Select(p => "\"" + p.Key + "\":\"" + p.Value.Replace("\"", "") + "\"")) + "}";
                LiveFile.Write(FilePath, json);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("planets", $"Could not write planets.json: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
