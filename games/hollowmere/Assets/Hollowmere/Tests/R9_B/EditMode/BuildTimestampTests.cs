#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Build.Reporting;

namespace Hollowmere.R9_B.EditMode.Tests
{
    public sealed class BuildTimestampTests
    {
        [Test]
        public void R9B_BuildTimestamp_NonUtcHostPreservesUtcInstantAndRawUnityValue()
        {
            string? previousZone = Environment.GetEnvironmentVariable("TZ");
            try
            {
                Environment.SetEnvironmentVariable("TZ", "Asia/Shanghai");
                TimeZoneInfo.ClearCachedData();
                var expectedUtc = new DateTime(2026, 10, 7, 6, 34, 27, DateTimeKind.Utc).AddTicks(1234567);
                Assert.That(TimeZoneInfo.Local.GetUtcOffset(expectedUtc), Is.EqualTo(TimeSpan.FromHours(8)),
                    "This regression requires the Linux Editor to honor TZ=Asia/Shanghai.");

                // Populate Unity's actual tick backing field: its public getter supplies the raw Kind.
                object boxedSummary = new BuildSummary();
                typeof(BuildSummary).GetField("buildStartTimeTicks", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(boxedSummary, expectedUtc.Ticks);
                var summary = (BuildSummary)boxedSummary;
                DateTime raw = summary.buildStartedAt;
                Assert.That(raw.Kind, Is.EqualTo(DateTimeKind.Unspecified), "Unity 6000.0 BuildSummary contract changed.");

                // Exercise the complete production report formatter without a player build or native BuildReport.
                string json = (string)typeof(Build).GetMethod("ReportJson", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { summary, Array.Empty<BuildStep>(), "r9-b-timestamp", false,
                        new List<string> { Build.BootScene }, false, false })!;
                JObject report = JsonConvert.DeserializeObject<JObject>(json,
                    new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;
                TestContext.WriteLine("Local zone: " + TimeZoneInfo.Local.Id + "; raw kind: " + raw.Kind);
                TestContext.WriteLine(json);

                Assert.That((string?)report["startedUtc"], Is.EqualTo("2026-10-07T06:34:27Z"),
                    "UTC ticks from Unity must not be shifted by the host's eight-hour offset.");
                Assert.That((string?)report["startedRaw"], Is.EqualTo("2026-10-07T06:34:27.1234567"),
                    "Retain the original wall-clock value and all fractional ticks without inventing a zone suffix.");
                Assert.That((string?)report["startedRawKind"], Is.EqualTo("Unspecified"));
                DateTime retained = DateTime.Parse((string)report["startedRaw"]!, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
                Assert.That(retained.Ticks, Is.EqualTo(raw.Ticks));
                Assert.That(retained.Kind, Is.EqualTo(raw.Kind));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TZ", previousZone);
                TimeZoneInfo.ClearCachedData();
            }
        }
    }
}
