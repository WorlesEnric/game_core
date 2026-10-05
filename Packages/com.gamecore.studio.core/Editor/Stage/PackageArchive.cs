#nullable enable
// GameCore.Studio.Edit - reads a staged mechanism package archive (P2.4): the .tgz / .tar a mechanic worker writes
// (studio/etos/agent/workers/gc-mechanic.md), the same bytes the staging lane extracted and the verdict covers.
// Only regular files and directories are accepted; absolute paths, '..', links, devices and oversize archives are
// refused (the same rules as studio/stage/make-slot.py), so admission never writes outside the package directory.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    /// <summary>The files of a package archive.</summary>
    public sealed class PackageArchive
    {
        public const long MaxBytes = 64L * 1024 * 1024;
        public const int MaxFiles = 4000;

        private readonly SortedDictionary<string, byte[]> _files;

        private PackageArchive(SortedDictionary<string, byte[]> files)
        {
            _files = files;
            SortedDictionary<string, string> digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                digests[file.Key] = ContentStamp.Sha256Hex(file.Value);
            }

            Digests = digests;
        }

        /// <summary>Files (path relative to the package root, '/' separated) to their bytes.</summary>
        public IReadOnlyDictionary<string, byte[]> Files => _files;

        /// <summary>Files to their sha256 (lowercase hex).</summary>
        public IReadOnlyDictionary<string, string> Digests { get; }

        /// <summary>The package name from its package.json, or null.</summary>
        public string? PackageName
        {
            get
            {
                if (!_files.TryGetValue("package.json", out byte[]? bytes))
                {
                    return null;
                }

                try
                {
                    Newtonsoft.Json.Linq.JObject manifest = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(bytes));
                    return (string?)manifest["name"];
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>Reads a gzip-compressed or plain tar archive; null with a problem when it is not a safe package archive.</summary>
        public static PackageArchive? Read(byte[] bytes, out string? problem)
        {
            problem = null;
            if (bytes == null || bytes.Length == 0)
            {
                problem = "the package archive is empty";
                return null;
            }

            byte[] tar;
            try
            {
                tar = bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b ? Gunzip(bytes) : bytes;
            }
            catch (InvalidDataException error)
            {
                problem = "the package archive is not valid gzip: " + error.Message;
                return null;
            }
            catch (IOException error)
            {
                problem = "the package archive could not be decompressed: " + error.Message;
                return null;
            }

            SortedDictionary<string, byte[]> files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            int offset = 0;
            string? longName = null;
            while (offset + 512 <= tar.Length)
            {
                if (IsZeroBlock(tar, offset))
                {
                    break;
                }

                string name = Field(tar, offset, 100);
                string prefix = Field(tar, offset + 345, 155);
                char type = (char)tar[offset + 156];
                long size;
                if (!TryOctal(tar, offset + 124, 12, out size) || size < 0)
                {
                    problem = "the package archive has a malformed header at byte " + offset;
                    return null;
                }

                int dataStart = offset + 512;
                long padded = (size + 511) / 512 * 512;
                if (dataStart + size > tar.Length)
                {
                    problem = "the package archive is truncated";
                    return null;
                }

                if (type == 'L')
                {
                    longName = Encoding.UTF8.GetString(tar, dataStart, (int)size).TrimEnd('\0');
                    offset = checked((int)(dataStart + padded));
                    continue;
                }

                if (type == 'x' || type == 'g')
                {
                    // pax headers: the path record (if any) replaces the name of the next entry.
                    string pax = Encoding.UTF8.GetString(tar, dataStart, (int)size);
                    string? paxPath = PaxPath(pax);
                    if (type == 'x' && paxPath != null)
                    {
                        longName = paxPath;
                    }

                    offset = checked((int)(dataStart + padded));
                    continue;
                }

                string path = longName ?? (prefix.Length > 0 ? prefix + "/" + name : name);
                longName = null;
                while (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path.Substring(2);
                }

                path = path.TrimEnd('/');
                if (path.Length == 0 || path == ".")
                {
                    offset = checked((int)(dataStart + padded));
                    continue;
                }

                if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains("\\") || Array.IndexOf(path.Split('/'), "..") >= 0 || path.Contains("//"))
                {
                    problem = "unsafe path in the package archive: '" + path + "'";
                    return null;
                }

                if (type == '5')
                {
                    offset = checked((int)(dataStart + padded));
                    continue;
                }

                if (type != '0' && type != '\0' && type != '7')
                {
                    problem = "the package archive may hold only files and directories: '" + path + "'";
                    return null;
                }

                total += size;
                if (files.Count + 1 > MaxFiles || total > MaxBytes)
                {
                    problem = "the package archive is too large";
                    return null;
                }

                byte[] data = new byte[size];
                Buffer.BlockCopy(tar, dataStart, data, 0, (int)size);
                files[path] = data;
                offset = checked((int)(dataStart + padded));
            }

            if (!files.ContainsKey("package.json"))
            {
                problem = "the package archive has no package.json at its root";
                return null;
            }

            return new PackageArchive(files);
        }

        private static byte[] Gunzip(byte[] bytes)
        {
            using (MemoryStream input = new MemoryStream(bytes))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > MaxBytes + (MaxFiles * 1024L))
                    {
                        throw new InvalidDataException("the decompressed archive exceeds the size limit");
                    }
                }

                return output.ToArray();
            }
        }

        private static bool IsZeroBlock(byte[] tar, int offset)
        {
            for (int i = 0; i < 512; i++)
            {
                if (tar[offset + i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Field(byte[] tar, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && tar[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(tar, offset, end - offset);
        }

        private static bool TryOctal(byte[] tar, int offset, int length, out long value)
        {
            value = 0;
            bool any = false;
            for (int i = offset; i < offset + length; i++)
            {
                byte c = tar[i];
                if (c == 0 || c == (byte)' ')
                {
                    if (any)
                    {
                        break;
                    }

                    continue;
                }

                if (c < (byte)'0' || c > (byte)'7')
                {
                    return false;
                }

                value = (value * 8) + (c - (byte)'0');
                any = true;
            }

            return true;
        }

        private static string? PaxPath(string pax)
        {
            foreach (string line in pax.Split('\n'))
            {
                int space = line.IndexOf(' ');
                if (space < 0)
                {
                    continue;
                }

                string record = line.Substring(space + 1);
                if (record.StartsWith("path=", StringComparison.Ordinal))
                {
                    return record.Substring(5);
                }
            }

            return null;
        }
    }
}
