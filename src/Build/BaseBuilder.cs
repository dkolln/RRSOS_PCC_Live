using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceCraft;
using Unity.Netcode;
using UnityEngine;

namespace RRSOS.PCC.Build
{
    /// <summary>
    /// Places stage 1 of the main base (the anchor lamp on its foundation, one more foundation, one pod) in a world that has none of it, the way the game itself
    /// brings a saved piece to life: a world object with its position, panels and colour, then InstantiateWorldObject. Once per save, never over anything else.
    /// </summary>
    internal sealed class BaseBuilder : MonoBehaviour
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Live");
        public static readonly string LogFile = Path.Combine(Folder, "build-log.txt");
        private static readonly string DoneFile = Path.Combine(Folder, "build-done.txt");
        private static readonly string UndoFile = Path.Combine(Folder, "build-undo.txt");

        // Drop one of these files into the dashboard folder while in the world and the plugin answers within two seconds (and deletes the file).
        private static readonly string BuildNowFile = Path.Combine(Folder, "build-now.txt");
        private static readonly string UndoNowFile = Path.Combine(Folder, "build-undo-now.txt");

        private const string StartPlanet = "Prime";
        private const float Reach = 15f;        // nothing of the player's own may stand this close (metres, along the ground) to the anchor
        private const float SameSpot = 0.5f;    // a piece this close to a stage 1 piece, of the same kind, counts as already there

        private sealed class Piece
        {
            public string Group;
            public Vector3 Pos;
            public Quaternion Rot;
            public int[] Panels;
            public string Color;
        }

        // Stage 1, as captured from Standard-2 on 2026-10-12 (the anchor foundation is at 772, 29, 601; the lamp points north). The terrain of Prime never changes.
        private static readonly Piece[] Stage1 =
        {
            new Piece { Group = "Foundation", Pos = new Vector3(772f, 29f, 601f), Rot = new Quaternion(0f, 0f, 0f, 1f) },
            new Piece { Group = "Foundation", Pos = new Vector3(778f, 29f, 601f), Rot = new Quaternion(0f, 0f, 0f, 1f) },
            new Piece { Group = "pod", Pos = new Vector3(778f, 31.5f, 600.9f), Rot = new Quaternion(0f, 0f, 0f, 1f), Panels = new[] { 1, 1, 1, 4, 5, 7 } },
            new Piece { Group = "OutsideLamp1", Pos = new Vector3(771.9205f, 31.519f, 601.0012f), Rot = new Quaternion(0f, -0.7071068f, 0f, 0.7071068f), Color = "1-0.9696766-0.8443396-1" },
        };

        private bool _autoHandled;
        private bool _busy;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (_busy || !Plugin.Enabled.Value)
                    continue;

                var wantBuild = File.Exists(BuildNowFile);
                var wantUndo = File.Exists(UndoNowFile);
                if (!wantBuild && !wantUndo && _autoHandled)
                    continue;

                string key = null;
                int planetHash = 0;
                bool go = false;
                try
                {
                    go = Ready(out key, out planetHash);
                }
                catch (Exception e)
                {
                    Note("Could not tell whether the world is ready: " + e.GetType().Name + ": " + e.Message);
                }

                if (!go)
                    continue; // the request files wait until a world is ready

                _busy = true;
                if (wantUndo)
                {
                    Consume(UndoNowFile);
                    Note("Undo requested.");
                    try
                    {
                        Undo(key);
                    }
                    catch (Exception e)
                    {
                        Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                    }
                }
                else if (wantBuild)
                {
                    Consume(BuildNowFile);
                    Note("Build requested for save " + key + ".");
                    yield return PlaceIfEmpty(key, planetHash, false);
                }
                else
                {
                    _autoHandled = true; // once per game session; a request file can still ask again
                    yield return Decide(key, planetHash);
                }

                _busy = false;
            }
        }

        private static void Consume(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Note("Could not delete " + path + ": " + e.Message);
            }
        }

        /// <summary>True once a world is fully loaded, we are the host, and the player is on the starting planet.</summary>
        private static bool Ready(out string key, out int planetHash)
        {
            key = null;
            planetHash = 0;

            var handler = WorldObjectsHandler.Instance;
            var loader = Managers.GetManager<PlanetLoader>();
            var players = Managers.GetManager<PlayersManager>();
            var settings = Managers.GetManager<GameSettingsHandler>();
            if (handler == null || loader == null || players == null || settings == null)
                return false;
            if (!handler.GetHasInitiatedAllObjects() || !loader.GetIsLoaded() || loader.GetCurrentPlanetData() == null)
                return false;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
                return false;

            var player = players.GetActivePlayerController();
            if (player == null || player.GetPlayerGaugesHandler() == null || !player.GetPlayerGaugesHandler().HasStarted())
                return false;
            if (loader.GetCurrentPlanetData().GetPlanetId() != StartPlanet)
                return false;

            var state = settings.GetCurrentGameSettings();
            if (state == null || string.IsNullOrEmpty(state.saveDisplayName))
                return false;

            key = state.saveDisplayName + "|" + state.worldSeed;
            planetHash = loader.GetCurrentPlanetData().GetPlanetHash();
            return true;
        }

        private IEnumerator Decide(string key, int planetHash)
        {
            if (Plugin.UndoLastBuild.Value)
            {
                try
                {
                    Undo(key);
                }
                catch (Exception e)
                {
                    Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                }

                Plugin.UndoLastBuild.Value = false;
                yield break;
            }

            if (ReadLines(DoneFile).Contains(key))
            {
                Note("Save " + key + ": stage 1 was already handled once; nothing to do (a build-now.txt request would place it again).");
                yield break;
            }

            yield return PlaceIfEmpty(key, planetHash, true);
        }

        /// <summary>Places stage 1 if the site is empty. The automatic planetfall run marks the save as handled even when it places nothing; a request does not.</summary>
        private IEnumerator PlaceIfEmpty(string key, int planetHash, bool automatic)
        {
            // Only into a world that has none of it, and with nothing of the player's own standing near the anchor.
            var handler = WorldObjectsHandler.Instance;
            var anchor = Stage1[0].Pos;
            var present = 0;
            var near = new List<string>();
            foreach (var wo in handler.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null)
                    continue;

                var pos = wo.GetPosition();
                if (pos.sqrMagnitude <= 0f)
                    continue; // an item inside an inventory

                if (Stage1.Any(p => p.Group == wo.GetGroup().GetId() && (pos - p.Pos).sqrMagnitude < SameSpot * SameSpot))
                    present++;

                var flat = new Vector2(pos.x - anchor.x, pos.z - anchor.z);
                if (flat.magnitude < Reach && wo.GetGroup() is GroupConstructible)
                    near.Add(wo.GetGroup().GetId());
            }

            if (present > 0 || near.Count > 0)
            {
                Note("Save " + key + ": the site is not empty (" + present + " stage 1 pieces already there; near the anchor: " + (near.Count == 0 ? "nothing" : string.Join(", ", near.Distinct().ToArray())) + "). Nothing placed.");
                if (automatic)
                    Append(DoneFile, key);
                yield break;
            }

            Note("Save " + key + ": planetfall with an empty site. Placing stage 1 (" + Stage1.Length + " pieces).");
            var created = new List<WorldObject>();
            var failed = false;
            foreach (var piece in Stage1)
            {
                try
                {
                    created.Add(Place(piece, planetHash));
                }
                catch (Exception e)
                {
                    Note("FAILED placing " + piece.Group + ": " + e.GetType().Name + ": " + e.Message);
                    failed = true;
                }

                if (failed)
                    break;

                yield return null;
            }

            if (failed)
            {
                foreach (var wo in created)
                {
                    try
                    {
                        handler.DestroyWorldObject(wo, true);
                    }
                    catch (Exception e)
                    {
                        Note("Could not roll back object " + wo.GetId() + ": " + e.Message);
                    }
                }

                Note("Rolled back " + created.Count + " pieces. The save is not marked, so a later start tries again.");
                yield break;
            }

            try
            {
                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                Note("Could not refresh the energy network (harmless): " + e.Message);
            }

            foreach (var wo in created)
                Append(UndoFile, key + "|" + wo.GetId() + "|" + wo.GetGroup().GetId());
            if (!ReadLines(DoneFile).Contains(key))
                Append(DoneFile, key);
            Note("Stage 1 placed: " + string.Join(", ", created.Select(w => w.GetGroup().GetId() + " #" + w.GetId()).ToArray()) + ".");
        }

        private static WorldObject Place(Piece piece, int planetHash)
        {
            var handler = WorldObjectsHandler.Instance;
            var group = GroupsHandler.GetGroupViaId(piece.Group);
            if (group == null)
                throw new Exception("the game has no group " + piece.Group);

            var wo = handler.CreateNewWorldObject(group, 0, null, true);
            try
            {
                wo.SetPositionAndRotation(piece.Pos, piece.Rot, planetHash);
                if (piece.Panels != null)
                    wo.SetPanelsId(new List<int>(piece.Panels));
                if (piece.Color != null)
                    wo.SetColor(DataTreatments.StringToColor(piece.Color));

                var made = handler.InstantiateWorldObject(wo, true, 0uL);
                if (made == null)
                    throw new Exception("InstantiateWorldObject gave nothing back");
            }
            catch
            {
                handler.DestroyWorldObject(wo, true);
                throw;
            }

            Note("Placed " + piece.Group + " #" + wo.GetId() + " at " + piece.Pos.x.ToString("0.###") + ", " + piece.Pos.y.ToString("0.###") + ", " + piece.Pos.z.ToString("0.###") + ".");
            return wo;
        }

        private static void Undo(string key)
        {
            var handler = WorldObjectsHandler.Instance;
            var lines = ReadLines(UndoFile);
            var mine = lines.Where(l => l.StartsWith(key + "|")).ToList();
            if (mine.Count == 0)
            {
                Note("Undo: nothing recorded for save " + key + ".");
                return;
            }

            foreach (var line in mine)
            {
                var parts = line.Split('|');
                if (int.TryParse(parts[parts.Length - 2], out var id) && handler.GetWorldObjectViaId(id) != null)
                {
                    handler.DestroyWorldObject(id, true);
                    Note("Undo: removed " + parts[parts.Length - 1] + " #" + id + ".");
                }
            }

            File.WriteAllLines(UndoFile, lines.Where(l => !l.StartsWith(key + "|")).ToArray());
            File.WriteAllLines(DoneFile, ReadLines(DoneFile).Where(l => l != key).ToArray());
        }

        private static List<string> ReadLines(string path) => File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : new List<string>();

        private static void Append(string path, string line)
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(path, line + Environment.NewLine);
        }

        private static void Note(string message)
        {
            Plugin.Log.LogInfo(message);
            try
            {
                Append(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message);
            }
            catch (Exception)
            {
            }
        }
    }
}
