// GameCore.Studio.Edit - the change-set journal (docs/studio/03-authoring-contracts.md s6, 02 s5, SADR-009).
//   Studio/History/YYYY/MM/<id>.json   one change set per file, written atomically with StudioJson (strict, '\n').
// YYYY/MM comes from the 48-bit time of the id's ULID, so an entry's path is a pure function of its id and never moves
// when its state changes. The engine writes an entry `Interrupted` before the first write of an apply and checkpoints
// per-op outcomes, so a crash leaves an entry the recovery API can resume or roll back. A small index
// (Library/GameCoreStudio/history-index.json) caches id -> state for fast listing; it is rebuilt from the files when
// missing or stale, the files are the source of truth.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>One row of the journal listing.</summary>
    public sealed class JournalRecord
    {
        public JournalRecord(string id, ChangeSetState state, string path, string? applied, string intent)
        {
            Id = id;
            State = state;
            Path = path;
            Applied = applied;
            Intent = intent;
        }

        public string Id { get; }

        public ChangeSetState State { get; }

        /// <summary>Absolute path of the entry file.</summary>
        public string Path { get; }

        /// <summary>The applied timestamp, when the entry was applied.</summary>
        public string? Applied { get; }

        public string Intent { get; }
    }

    /// <summary>Reads and writes journal entries.</summary>
    public sealed class Journal
    {
        private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        private readonly StudioPaths _paths;
        private SortedDictionary<string, JournalRecord>? _records;

        public Journal(StudioPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public string Root => _paths.HistoryRoot;

        /// <summary>Number of entry writes (tests count checkpoints).</summary>
        public int WriteCount { get; private set; }

        /// <summary>Raised after an entry is written (id, state).</summary>
        public event Action<string, ChangeSetState>? Written;

        /// <summary>The entry file of <paramref name="changeSetId"/>: History/YYYY/MM/&lt;id&gt;.json from the ULID time.</summary>
        public string PathOf(string changeSetId)
        {
            if (!IdDerivation.IsChangeSetId(changeSetId))
            {
                throw new ArgumentException("'" + changeSetId + "' is not a change-set id.", nameof(changeSetId));
            }

            DateTime time = TimeOf(changeSetId);
            return System.IO.Path.Combine(
                _paths.HistoryRoot,
                time.Year.ToString("0000", CultureInfo.InvariantCulture),
                time.Month.ToString("00", CultureInfo.InvariantCulture),
                changeSetId + ".json");
        }

        /// <summary>The UTC time encoded in a change-set id's ULID.</summary>
        public static DateTime TimeOf(string changeSetId)
        {
            string ulid = changeSetId.Substring(3, 10);
            long milliseconds = 0;
            foreach (char character in ulid)
            {
                int value = Crockford.IndexOf(char.ToUpperInvariant(character));
                if (value < 0)
                {
                    throw new ArgumentException("'" + changeSetId + "' has an invalid ULID.", nameof(changeSetId));
                }

                milliseconds = (milliseconds << 5) | (long)value;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        /// <summary>ISO-8601 UTC text for timestamps.</summary>
        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>Writes (creates or replaces) an entry.</summary>
        public void Write(ChangeSet entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            string path = PathOf(entry.Id);
            StudioPaths.WriteAllTextAtomic(path, StudioJson.Serialize(entry) + "\n");
            WriteCount++;
            Records[entry.Id] = Record(entry, path);
            SaveIndex();
            Written?.Invoke(entry.Id, entry.EffectiveState);
        }

        /// <summary>The entry, or null when there is none (a damaged entry throws <see cref="JsonException"/>).</summary>
        public ChangeSet? Read(string changeSetId)
        {
            string path = PathOf(changeSetId);
            if (!File.Exists(path))
            {
                return null;
            }

            return StudioJson.Deserialize<ChangeSet>(File.ReadAllText(path, Encoding.UTF8));
        }

        public bool Exists(string changeSetId) => File.Exists(PathOf(changeSetId));

        /// <summary>Every entry, oldest first (ULID order).</summary>
        public IReadOnlyList<JournalRecord> List()
        {
            return new List<JournalRecord>(Records.Values);
        }

        /// <summary>Entries in <paramref name="state"/>, oldest first.</summary>
        public IReadOnlyList<JournalRecord> List(ChangeSetState state)
        {
            List<JournalRecord> matching = new List<JournalRecord>();
            foreach (JournalRecord record in Records.Values)
            {
                if (record.State == state)
                {
                    matching.Add(record);
                }
            }

            return matching;
        }

        /// <summary>Forgets the cached listing and rescans the files.</summary>
        public void Rescan()
        {
            _records = Scan();
            SaveIndex();
        }

        private SortedDictionary<string, JournalRecord> Records => _records ??= LoadIndex() ?? Scan();

        private SortedDictionary<string, JournalRecord> Scan()
        {
            SortedDictionary<string, JournalRecord> records = new SortedDictionary<string, JournalRecord>(StringComparer.Ordinal);
            if (!Directory.Exists(_paths.HistoryRoot))
            {
                return records;
            }

            foreach (string file in Directory.GetFiles(_paths.HistoryRoot, "cs_*.json", SearchOption.AllDirectories))
            {
                try
                {
                    ChangeSet entry = StudioJson.Deserialize<ChangeSet>(File.ReadAllText(file, Encoding.UTF8));
                    records[entry.Id] = Record(entry, file);
                }
                catch (JsonException)
                {
                    // A damaged entry is skipped in the listing; Read() reports it when asked for directly.
                }
                catch (IOException)
                {
                }
            }

            return records;
        }

        private SortedDictionary<string, JournalRecord>? LoadIndex()
        {
            string path = _paths.HistoryIndexPath;
            if (!File.Exists(path) || !Directory.Exists(_paths.HistoryRoot))
            {
                return null;
            }

            try
            {
                JObject document = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (!(document["entries"] is JArray rows) || !string.Equals((string?)document["root"], _paths.HistoryRoot, StringComparison.Ordinal))
                {
                    return null;
                }

                SortedDictionary<string, JournalRecord> records = new SortedDictionary<string, JournalRecord>(StringComparer.Ordinal);
                foreach (JToken row in rows)
                {
                    string? id = (string?)row["id"];
                    string? state = (string?)row["state"];
                    if (id == null || state == null || !Enum.TryParse(state, out ChangeSetState parsed))
                    {
                        return null;
                    }

                    string file = PathOf(id);
                    if (!File.Exists(file))
                    {
                        return null;
                    }

                    records[id] = new JournalRecord(id, parsed, file, (string?)row["applied"], (string?)row["intent"] ?? string.Empty);
                }

                int files = Directory.GetFiles(_paths.HistoryRoot, "cs_*.json", SearchOption.AllDirectories).Length;
                return files == records.Count ? records : null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private void SaveIndex()
        {
            JArray rows = new JArray();
            foreach (JournalRecord record in Records.Values)
            {
                JObject row = new JObject { ["id"] = record.Id, ["state"] = record.State.ToString(), ["intent"] = record.Intent };
                if (record.Applied != null)
                {
                    row["applied"] = record.Applied;
                }

                rows.Add(row);
            }

            JObject document = new JObject { ["schema"] = "gamecore.studio.historyindex/1", ["root"] = _paths.HistoryRoot, ["entries"] = rows };
            try
            {
                StudioPaths.WriteAllTextAtomic(_paths.HistoryIndexPath, document.ToString(Formatting.None));
            }
            catch (IOException)
            {
                // The index is a cache; the next listing rescans.
            }
        }

        private static JournalRecord Record(ChangeSet entry, string path)
        {
            return new JournalRecord(entry.Id, entry.EffectiveState, path, entry.Timestamps?.Applied, entry.Intent.Text);
        }
    }
}
