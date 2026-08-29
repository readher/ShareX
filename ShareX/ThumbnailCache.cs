#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ShareX
{
    /// <summary>
    /// Caches resized thumbnail bitmaps so thumbnail view refreshes (startup, size change,
    /// view toggle) don't re-decode every AVIF/WebP file from disk. Returned bitmaps are
    /// clones; the caller owns and disposes them.
    /// </summary>
    internal static class ThumbnailCache
    {
        // Roughly 500 entries at 200x150 32bpp ≈ 60MB; entries beyond this evict oldest-first.
        private const int MaxEntries = 500;

        private class CacheEntry
        {
            public Bitmap Bitmap;
            public DateTime FileDate;
            public long LastUsedTicks;
        }

        private static readonly object lockObject = new object();
        private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static long useCounter;
        private static int generation;

        public static Bitmap GetOrLoad(string filePath, Size thumbnailSize, Func<Bitmap> loader)
        {
            string key = $"{filePath}|{thumbnailSize.Width}x{thumbnailSize.Height}";
            DateTime fileDate = File.GetLastWriteTime(filePath);
            long currentUse = System.Threading.Interlocked.Increment(ref useCounter);

            lock (lockObject)
            {
                if (cache.TryGetValue(key, out CacheEntry entry) && entry.FileDate == fileDate)
                {
                    entry.LastUsedTicks = currentUse;
                    return (Bitmap)entry.Bitmap.Clone();
                }
            }

            Bitmap created = loader();

            if (created == null)
            {
                return null;
            }

            lock (lockObject)
            {
                cache[key] = new CacheEntry { Bitmap = created, FileDate = fileDate, LastUsedTicks = currentUse };
                EvictIfNeeded();
            }

            return (Bitmap)created.Clone();
        }

        public static void Clear()
        {
            lock (lockObject)
            {
                System.Threading.Interlocked.Increment(ref generation);
                foreach (CacheEntry entry in cache.Values)
                {
                    entry.Bitmap.Dispose();
                }

                cache.Clear();
            }
        }

        /// <summary>
        /// Pre-loads thumbnails for the given files in the background so the first
        /// scroll through the grid doesn't pay disk + decode cost per tile.
        /// Already-cached entries are skipped; work is silently abandoned if the cache
        /// is cleared (e.g. thumbnail size change) while running.
        /// </summary>
        public static void WarmUp(IEnumerable<string> filePaths, Size thumbnailSize)
        {
            if (thumbnailSize.IsEmpty)
            {
                return;
            }

            string[] files = filePaths?.Where(File.Exists).ToArray();

            if (files == null || files.Length == 0)
            {
                return;
            }

            int startGeneration = System.Threading.Volatile.Read(ref generation);

            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (string filePath in files)                {
                    if (System.Threading.Volatile.Read(ref generation) != startGeneration)
                    {
                        return;
                    }

                    if (Contains(filePath, thumbnailSize))
                    {
                        continue;
                    }

                    try
                    {
                        Bitmap loaded = ImageHelpers.LoadImage(filePath);

                        if (loaded != null)
                        {
                            using (loaded)
                            {
                                Bitmap resized = ImageHelpers.ResizeImage(loaded, thumbnailSize, false);
                                Store(filePath, thumbnailSize, resized);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Unloadable/corrupt files simply stay uncached; UpdateThumbnail
                        // handles them the same way when it paints the tile.
                    }
                }
            });
        }

        public static bool Contains(string filePath, Size thumbnailSize)
        {
            string key = $"{filePath}|{thumbnailSize.Width}x{thumbnailSize.Height}";
            DateTime fileDate = File.GetLastWriteTime(filePath);

            lock (lockObject)
            {
                return cache.TryGetValue(key, out CacheEntry entry) && entry.FileDate == fileDate;
            }
        }

        private static void Store(string filePath, Size thumbnailSize, Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return;
            }

            string key = $"{filePath}|{thumbnailSize.Width}x{thumbnailSize.Height}";
            DateTime fileDate = File.GetLastWriteTime(filePath);
            long currentUse = System.Threading.Interlocked.Increment(ref useCounter);

            lock (lockObject)
            {
                if (cache.TryGetValue(key, out CacheEntry existing) && existing.FileDate == fileDate)
                {
                    bitmap.Dispose();
                    return;
                }

                existing?.Bitmap.Dispose();
                cache[key] = new CacheEntry { Bitmap = bitmap, FileDate = fileDate, LastUsedTicks = currentUse };
                EvictIfNeeded();
            }
        }

        private static void EvictIfNeeded()
        {
            if (cache.Count <= MaxEntries)
            {
                return;
            }

            string oldestKey = null;
            long oldestTicks = long.MaxValue;

            foreach (KeyValuePair<string, CacheEntry> kvp in cache)
            {
                if (kvp.Value.LastUsedTicks < oldestTicks)
                {
                    oldestTicks = kvp.Value.LastUsedTicks;
                    oldestKey = kvp.Key;
                }
            }

            if (oldestKey != null)
            {
                cache[oldestKey].Bitmap.Dispose();
                cache.Remove(oldestKey);
            }
        }
    }
}
