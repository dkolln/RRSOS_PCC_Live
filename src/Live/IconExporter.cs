using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// Writes each item's and building's icon as <c>icons\&lt;group id&gt;.png</c>, so the dashboard can show it beside the name. The game holds them as sprites cut out of
    /// shared texture atlases (not always readable by code), so each is copied through a render texture, which works for any texture. A file that is already there is
    /// left alone, so this does its work once per install and again only for a group the game adds. A few icons a frame, so the game does not stutter. Read-only
    /// towards the game, like everything else here (see docs/contract.md).
    /// </summary>
    internal sealed class IconExporter : MonoBehaviour
    {
        public static readonly string Folder = Path.Combine(LiveFile.Folder, "icons");

        private const float RetrySeconds = 5f;
        private const int PerFrame = 8;

        private IEnumerator Start()
        {
            // Wait for a world, as the recipe list does: by then the game has built every group and its sprite.
            List<Group> groups = null;
            while (groups == null)
            {
                yield return new WaitForSecondsRealtime(RetrySeconds);

                try
                {
                    if (Managers.GetManager<UnlockingHandler>() != null)
                    {
                        var all = GroupsHandler.GetAllGroups();
                        if (all != null && all.Count > 0)
                            groups = all.Where(g => g != null).ToList();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("icons", "Could not list the groups for their icons: " + e.Message);
                }
            }

            Directory.CreateDirectory(Folder);

            int written = 0, skipped = 0, failed = 0, inFrame = 0;

            foreach (var group in groups)
            {
                string path = null;
                try
                {
                    var sprite = group.GetImage();
                    if (sprite == null || sprite.texture == null)
                        continue;

                    path = Path.Combine(Folder, FileName(group.GetId()));
                    if (File.Exists(path))
                    {
                        skipped++;
                        continue;
                    }

                    File.WriteAllBytes(path, ToPng(sprite));
                    written++;
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.LogOnce("icons-one", "Could not write the icon " + path + ": " + e.Message);
                }

                if (++inFrame >= PerFrame)
                {
                    inFrame = 0;
                    yield return null;
                }
            }

            Plugin.Log.LogInfo("Icons in " + Folder + ": " + written + " written, " + skipped + " already there, " + failed + " failed.");
        }

        /// <summary>The sprite's own rectangle of its texture, as a PNG. Copied through a render texture, since the texture itself may not be readable.</summary>
        private static byte[] ToPng(Sprite sprite)
        {
            var rect = sprite.textureRect;
            var source = sprite.texture;
            var width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target,
                    new Vector2(rect.width / source.width, rect.height / source.height),
                    new Vector2(rect.x / source.width, rect.y / source.height));

                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();

                var png = copy.EncodeToPNG();
                Destroy(copy);
                return png;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>A group id as a file name. Ids are plain words ("Rod-iridium"), but a stray character must not break the path.</summary>
        internal static string FileName(string id)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string(id.Select(c => bad.Contains(c) ? '_' : c).ToArray()) + ".png";
        }
    }
}
