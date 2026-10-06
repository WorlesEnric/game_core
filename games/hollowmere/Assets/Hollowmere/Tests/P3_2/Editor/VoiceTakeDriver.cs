// Workflow-only push-to-talk lifecycle. A take never submits a request or retries a refused start.
#nullable enable
using System;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;

namespace Hollowmere.P3_2.Workflows
{
    public sealed class VoiceTakeDriver : IDisposable
    {
        private readonly IVoiceSession session;
        private readonly Func<bool> ready;
        private readonly Action<TranscriptUpdate> transcript;
        private Task? start;
        private Task? stop;
        private double began;
        private double playback;
        private double released;

        public VoiceTakeDriver(IVoiceSession session, Func<bool> ready, Action<TranscriptUpdate> transcript)
        {
            this.session = session;
            this.ready = ready;
            this.transcript = transcript;
            session.Transcript += transcript;
            session.Error += Failed;
        }

        public string Phase { get; private set; } = "start";
        public string? Error { get; private set; }
        public bool Played { get; private set; }
        public bool Drained => stop?.Status == TaskStatus.RanToCompletion;
        public bool PlaybackRequested { get; private set; }

        // Poll on the Editor thread. Stop completion includes EtosVoiceSession's transcript delivery barrier.
        public bool Tick(double now, bool played, Action play)
        {
            switch (Phase)
            {
                case "start":
                    began = now;
                    Phase = "starting";
                    try { start = session.StartAsync(); }
                    catch (Exception error) { Error = error.Message; BeginStop(now); }
                    return false;
                case "starting":
                    if (start!.IsFaulted || start.IsCanceled || Error != null)
                    {
                        Error = Error ?? start.Exception?.GetBaseException().Message ?? "Voice start cancelled";
                        BeginStop(now);
                    }
                    else if (start.IsCompleted && ready())
                    {
                        playback = now;
                        PlaybackRequested = true;
                        Phase = "playing";
                        play();
                    }
                    else if (now - began > 40)
                    {
                        Error = "Voice capture/provider readiness timed out";
                        BeginStop(now);
                    }
                    return false;
                case "playing":
                    Played = played;
                    if (Error != null || played || now - playback > 75)
                    {
                        if (!played) Error = Error ?? "Voice playback did not complete";
                        BeginStop(now);
                    }
                    return false;
                case "draining":
                    if (!stop!.IsCompleted && now - released <= 40) return false;
                    if (!stop.IsCompleted) Error = Error ?? "Voice Stop/drain timed out";
                    else if (stop.IsFaulted || stop.IsCanceled)
                        Error = Error ?? stop.Exception?.GetBaseException().Message ?? "Voice Stop cancelled";
                    Phase = "done";
                    return true;
                default:
                    return true;
            }
        }

        private void BeginStop(double now)
        {
            released = now;
            Phase = "draining";
            // Explicit Stop, even after failed Start. Never Toggle (which would start another paid session).
            try { stop = session.StopAsync(); }
            catch (Exception error) { Error = Error ?? error.Message; stop = Task.CompletedTask; }
        }

        private void Failed(Diagnostic diagnostic) => Error = diagnostic.Code + ": " + diagnostic.Message;

        public void Dispose()
        {
            session.Transcript -= transcript;
            session.Error -= Failed;
            (session as IDisposable)?.Dispose();
        }
    }
}
