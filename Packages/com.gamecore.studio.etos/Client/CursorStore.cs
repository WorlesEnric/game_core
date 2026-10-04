// GameCore.Studio.Etos.Client - where the event cursor survives a reconnect, a domain reload and an editor restart.
// The cursor is the companion ledger's event position; reconnecting with ?after=<cursor> replays exactly what was
// not yet handled (04 s2; SR-4.5). A file store writes atomically (temp file + replace).
#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Persists the last handled event cursor.</summary>
    public interface ICursorStore
    {
        long Load();

        void Save(long cursor);
    }

    /// <summary>A cursor kept in memory (tests, short-lived tools).</summary>
    public sealed class MemoryCursorStore : ICursorStore
    {
        private long _cursor;

        public MemoryCursorStore(long initial = 0)
        {
            _cursor = initial;
        }

        public long Load() => System.Threading.Interlocked.Read(ref _cursor);

        public void Save(long cursor) => System.Threading.Interlocked.Exchange(ref _cursor, cursor);
    }

    /// <summary>A cursor in a small text file (Unity: <c>Library/GameCoreStudio/etos-events.cursor</c>).</summary>
    public sealed class FileCursorStore : ICursorStore
    {
        private readonly object _gate = new object();

        public FileCursorStore(string path)
        {
            FilePath = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string FilePath { get; }

        public long Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        return 0;
                    }

                    string text = File.ReadAllText(FilePath).Trim();
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long cursor) && cursor > 0 ? cursor : 0;
                }
                catch (IOException)
                {
                    return 0;
                }
                catch (UnauthorizedAccessException)
                {
                    return 0;
                }
            }
        }

        public void Save(long cursor)
        {
            lock (_gate)
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, cursor.ToString(CultureInfo.InvariantCulture));
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                File.Move(temp, FilePath);
            }
        }
    }
}
