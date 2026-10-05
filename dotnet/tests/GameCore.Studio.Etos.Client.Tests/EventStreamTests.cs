// WS /v1/events through tickets: ticket binding and single use, ordered delivery, resume after a forced disconnect
// with no gap and no duplicate, cursor persistence across streams, and the reconnect schedule.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class EventStreamTests
    {
        [Test]
        public async Task Ticket_IsIssuedForThePathWithoutQueryAndIsSingleUse()
        {
            using FakeSetup setup = new FakeSetup();
            string ticket = await setup.Client.IssueTicketAsync("/v1/events");
            FakeCall issue = setup.Fake.Calls.Last();
            Assert.That(issue.Path, Is.EqualTo("/api/v1/tickets"));
            Assert.That(JObject.Parse(issue.Body)["path"]!.ToString(), Is.EqualTo("/api/v1/agents/gamecore-studio/http/v1/events"));
            Assert.That(ticket, Does.StartWith("ett_"));

            Uri uri = setup.Client.WebSocketUri("/v1/events", "after=0", ticket);
            Assert.That(uri.Scheme, Is.EqualTo("ws"));
            Assert.That(uri.AbsolutePath, Is.EqualTo("/api/v1/agents/gamecore-studio/http/v1/events"));
            Assert.That(uri.Query, Does.Contain("after=0").And.Contain("etos_ticket="));

            using (ClientWebSocket first = new ClientWebSocket())
            {
                first.Options.Proxy = null;
                await first.ConnectAsync(uri, CancellationToken.None);
                Assert.That(first.State, Is.EqualTo(WebSocketState.Open));
                FakeCall upgrade = setup.Fake.Calls.Last();
                Assert.That(upgrade.Authorized, Is.False, "the upgrade carries the ticket, not the key");
            }

            using (ClientWebSocket second = new ClientWebSocket())
            {
                second.Options.Proxy = null;
                Assert.ThrowsAsync<WebSocketException>(() => second.ConnectAsync(uri, CancellationToken.None), "a ticket opens one connection");
            }
        }

        [Test]
        public async Task Events_ResumeAfterAForcedDisconnectWithoutGapsOrDuplicates()
        {
            using FakeSetup setup = new FakeSetup();
            MemoryCursorStore cursors = new MemoryCursorStore();
            List<long> seen = new List<long>();
            using EventStream stream = new EventStream(setup.Client, cursors, new BackoffPolicy(TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1), new Random(7)));
            stream.Received += frame =>
            {
                lock (seen)
                {
                    seen.Add(frame.Cursor);
                }
            };
            for (int i = 0; i < 5; i++)
            {
                setup.Fake.Emit("task_progress", "cs_x", new JObject { ["n"] = i });
            }

            stream.Start();
            await Wait.Until(() => Count(seen) == 5, TimeSpan.FromSeconds(10), "the first five events");
            await Wait.Until(() => stream.State == EventStreamState.Connected, TimeSpan.FromSeconds(5), "connected");

            setup.Fake.DropEventConnections();
            for (int i = 5; i < 12; i++)
            {
                setup.Fake.Emit("task_progress", "cs_x", new JObject { ["n"] = i });
            }

            await Wait.Until(() => Count(seen) == 12, TimeSpan.FromSeconds(10), "the events emitted during the disconnect");
            List<long> expected = setup.Fake.Events.Select(e => (long)e["cursor"]!).ToList();
            lock (seen)
            {
                Assert.That(seen, Is.EqualTo(expected), "every event once, in order");
            }

            Assert.That(stream.Connects, Is.GreaterThanOrEqualTo(2));
            Assert.That(stream.LastReconnectMilliseconds, Is.GreaterThan(0).And.LessThan(5000), "B-AGENT-UX: reconnect within 5 s");
            Assert.That(cursors.Load(), Is.EqualTo(expected.Last()));
            Assert.That(setup.Fake.TicketsIssued, Is.GreaterThanOrEqualTo(2), "a fresh ticket per connection");
            Assert.That(setup.Fake.Calls.Where(c => c.Path.EndsWith("/v1/events", StringComparison.Ordinal)).Select(c => c.Query).Last(), Is.EqualTo("after=5"));
        }

        [Test]
        public async Task Events_TheCursorSurvivesAcrossStreams()
        {
            using FakeSetup setup = new FakeSetup();
            string file = Path.Combine(setup.TempDirectory, "state", "etos-events.cursor");
            FileCursorStore cursors = new FileCursorStore(file);
            setup.Fake.Emit("request", "cs_a", new JObject());
            setup.Fake.Emit("request", "cs_b", new JObject());
            List<long> first = new List<long>();
            using (EventStream stream = new EventStream(setup.Client, cursors))
            {
                stream.Received += frame => { lock (first) { first.Add(frame.Cursor); } };
                stream.Start();
                await Wait.Until(() => Count(first) == 2, TimeSpan.FromSeconds(10), "two events");
                await stream.StopAsync();
            }

            setup.Fake.Emit("request", "cs_c", new JObject());
            List<long> second = new List<long>();
            using (EventStream stream = new EventStream(setup.Client, new FileCursorStore(file)))
            {
                stream.Received += frame => { lock (second) { second.Add(frame.Cursor); } };
                stream.Start();
                await Wait.Until(() => Count(second) == 1, TimeSpan.FromSeconds(10), "the newer event");
                await Task.Delay(200);
            }

            Assert.That(second, Is.EqualTo(new[] { 3L }));
            Assert.That(File.ReadAllText(file).Trim(), Is.EqualTo("3"));
        }

        [Test]
        public async Task Events_ARequestLifecycleArrivesInOrder()
        {
            using FakeSetup setup = new FakeSetup();
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng());
            setup.Fake.Worker = body => new FakeCandidate(Samples.ChangeSet((string)body["changeSetId"]!, png.Sha256, png.Bytes.Length), new[] { png });
            List<EventFrame> frames = new List<EventFrame>();
            using EventStream stream = new EventStream(setup.Client, new MemoryCursorStore());
            stream.Received += frame => { lock (frames) { frames.Add(frame); } };
            stream.Start();
            await Wait.Until(() => stream.State == EventStreamState.Connected, TimeSpan.FromSeconds(5), "connected");
            string id = Samples.ChangeSetId();
            await setup.Client.SubmitAsync(Samples.Request(id));
            await Wait.Until(() => { lock (frames) { return frames.Any(f => f.Type == "candidate"); } }, TimeSpan.FromSeconds(10), "the candidate event");
            List<string> kinds;
            lock (frames)
            {
                kinds = frames.Where(f => f.RequestId == id).Select(f => f.Type == "request" ? "request:" + (string?)f.Data["state"] : f.Type).ToList();
            }

            Assert.That(kinds, Is.EqualTo(new[] { "request:requested", "request:running", "task_progress", "task_progress", "request:candidate", "candidate" }));
            Assert.That(frames.All(f => f.ReceivedAt >= f.At), Is.True);
        }

        [Test]
        public async Task Events_AStoppedNodeIsRetriedWithBackoffAndReportedAsTransport()
        {
            EtosClientOptions options = new EtosClientOptions { ProjectId = new string('a', 64), NodeUrl = "http://127.0.0.1:9", RequestTimeout = TimeSpan.FromSeconds(2) };
            using CompanionClient client = new CompanionClient(options, new EtosCredentials(FakeCompanion.AppKey, null, "fixture"));
            List<EventStreamState> states = new List<EventStreamState>();
            using EventStream stream = new EventStream(client, new MemoryCursorStore(), new BackoffPolicy(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100), new Random(1)));
            stream.StateChanged += (state, error) => { lock (states) { states.Add(state); } };
            stream.Start();
            await Wait.Until(() => stream.LastError != null, TimeSpan.FromSeconds(10), "a transport error");
            await stream.StopAsync();
            Assert.That(stream.LastError!.Code, Is.EqualTo(EtosCodes.Transport).Or.EqualTo(EtosCodes.Timeout));
            Assert.That(states, Does.Contain(EventStreamState.Reconnecting));
            Assert.That(states.Last(), Is.EqualTo(EventStreamState.Stopped));
        }

        private static int Count(List<long> list)
        {
            lock (list)
            {
                return list.Count;
            }
        }
    }

    public sealed class BackoffTests
    {
        [Test]
        public void Caps_DoubleFromTheBaseUpToTheMaximum()
        {
            BackoffPolicy policy = new BackoffPolicy(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(30));
            double[] caps = Enumerable.Range(0, 10).Select(i => policy.Cap(i).TotalMilliseconds).ToArray();
            Assert.That(caps, Is.EqualTo(new double[] { 250, 500, 1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000 }));
        }

        [Test]
        public void Delays_AreJitteredWithinHalfToFullCapAndSeedDeterministic()
        {
            BackoffPolicy a = new BackoffPolicy(random: new Random(42));
            BackoffPolicy b = new BackoffPolicy(random: new Random(42));
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double cap = a.Cap(attempt).TotalMilliseconds;
                double da = a.Delay(attempt).TotalMilliseconds;
                double db = b.Delay(attempt).TotalMilliseconds;
                Assert.That(da, Is.EqualTo(db));
                Assert.That(da, Is.InRange(cap / 2, cap));
            }

            BackoffPolicy c = new BackoffPolicy(random: new Random(3));
            double firstThree = c.Delay(0).TotalMilliseconds + c.Delay(1).TotalMilliseconds + c.Delay(2).TotalMilliseconds;
            Assert.That(firstThree, Is.LessThanOrEqualTo(1750), "three quick attempts fit the 5 s reconnect budget");
            Assert.Throws<ArgumentException>(() => new BackoffPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        }
    }
}
