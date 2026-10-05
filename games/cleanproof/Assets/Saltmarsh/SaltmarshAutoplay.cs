#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Rules.Gameplay.World;
using Saltmarsh.Boot;
using UnityEngine;

namespace Saltmarsh
{
    public sealed class SaltmarshAutoplay : MonoBehaviour
    {
        private bool endingShown;
        private void Update()
        {
            var boot=GetComponent<GameBoot>();
            if(boot.Narrative==null || endingShown || SaltmarshScenario.Fact(boot,"harbour_safe")!=1) return;
            var rig=UiAudioBootstrap.RigOf(gameObject);
            if(rig==null) return;
            rig.Ui.SetEnding("The coast shines again",SaltmarshScenario.Fact(boot,"salvaged")==1
                ?"Sea glass guides the boats home. The keeper's spare remains ready for the next storm."
                :"The spare lens guides the boats home. Tomorrow the island must make a new reserve.");
            rig.Ui.Commands!.Open(UiScreen.Ending);
            endingShown=true;
        }
        private IEnumerator Start()
        {
            if(!Environment.GetCommandLineArgs().Contains("-saltmarshAutoplay")) yield break;
            Application.targetFrameRate=60;
            var boot=GetComponent<GameBoot>();
            yield return SaltmarshScenario.Until(()=>boot.Narrative!=null,"real GameBoot");
            int start=Time.frameCount;
            var scenario=SaltmarshScenario.Play(boot,0);
            // Flatten nested iterators so any failure exits the standalone with a nonzero status.
            var stack=new System.Collections.Generic.Stack<IEnumerator>(); stack.Push(scenario);
            while(stack.Count>0)
            {
                bool moved; object? next=null;
                try { moved=stack.Peek().MoveNext(); if(moved) next=stack.Peek().Current; }
                catch(Exception error) { Debug.LogError("[Saltmarsh] AUTOPLAY FAIL "+error); Application.Quit(1); yield break; }
                if(!moved) { stack.Pop(); continue; }
                if(next is IEnumerator nested) { stack.Push(nested); continue; }
                yield return next;
            }
            int idleFrame=Time.frameCount; int idlePumps=boot.World!.Root.PumpCounter.SanctionedPumps;
            while(Time.frameCount-start<600) yield return null;
            var counter=boot.World!.Root.PumpCounter;
            bool healthy=counter.Violations==0 && Math.Abs(counter.SanctionedPumps-idlePumps-(Time.frameCount-idleFrame))<=1;
            Debug.Log("[Saltmarsh] AUTOPLAY "+(healthy?"PASS":"FAIL")+" frames="+(Time.frameCount-start)+" pumps="+counter.SanctionedPumps+" violations="+counter.Violations);
            Application.Quit(healthy?0:1);
        }
    }
    public static class SaltmarshScenario
    {
        public static string Id(string name)
        {
            using var hash=SHA256.Create();
            return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes("saltmarsh/"+name)).Take(16).ToArray()).ToString();
        }
        public static void Require(bool ok,string what) { if(!ok) throw new InvalidOperationException(what); }
        public static IEnumerator Until(Func<bool> condition,string what)
        {
            for(int frame=0;frame<300 && !condition();frame++) yield return null;
            Require(condition(),"Timeout: "+what);
        }
        public static int Fact(GameBoot boot,string name) => boot.Narrative!.Runtime.Models.TryGetFactByName(name,out FactModel? fact) && fact!=null ? boot.Narrative.Runtime.State.Fact(fact.Key) : int.MinValue;
        public static int Quest(GameBoot boot,SlotId slot)
        {
            var rt=boot.Narrative!.Runtime; Require(rt.Models.TryResolve("RelightTheCoast",out int key),"quest model");
            return boot.World!.Slots.ReadOrDefault(rt.Index.TargetOf(NarrativeTargetKind.Quest,key),QuestIds.Owner,slot,-1);
        }
        public static int Held(GameBoot boot,string item)
        {
            var rt=boot.Narrative!.Runtime; Require(rt.Models.TryResolve(item,out int key),item);
            return rt.State.ItemCount(0,key,rt.ActorKey);
        }
        public static IEnumerator Travel(GameBoot boot,string region)
        {
            Require(boot.World!.Commands.Travel(boot.World.Focus,Id(region)).Admitted,"travel "+region);
            yield return Until(()=>boot.World!.Slots.ReadOrDefault(boot.World.Focus,GameplaySlots.WorldOwner,GameplaySlots.Region,0)==AuthoringIds.StableKey(Id(region)) && boot.World.Streamer.IsSettled,region+" resident");
        }
        public static IEnumerator Play(GameBoot boot,int branch)
        {
            var rig=UiAudioBootstrap.RigOf(boot.gameObject)!;
            Require(rig.Ui.Commands!.Command(UiAction.Loaded).Admitted,"HUD");
            Require(boot.Narrative!.Conversations.TryStart(Id("Ada"),Id("AdaDialogue")).Started,"Ada dialogue");
            yield return Until(()=>boot.Modules!.Dialogue.Presenter!.Last.Active,"Ada line");
            Require(boot.Modules!.Dialogue.Runner!.Advance(),"advance Ada");
            yield return Until(()=>Quest(boot,QuestIds.Stage)==1 && Held(boot,"CopperWire")==1,"accepted signal");
            yield return Travel(boot,"Dunes");
            Require(boot.Narrative!.Conversations.TryStart(Id("Neri"),Id("NeriDialogue")).Started,"Neri dialogue");
            yield return Until(()=>boot.Modules!.Dialogue.Presenter!.Last.Active,"Neri line");
            Require(boot.Modules!.Dialogue.Runner!.Advance(),"advance Neri");
            yield return Until(()=>boot.Modules!.Dialogue.Presenter!.Last.Kind=="choice","lens choice");
            Require(boot.Modules!.Dialogue.Runner!.Choose(branch),"choose lens");
            yield return Until(()=>Quest(boot,QuestIds.Stage)==2 && boot.Narrative!.Delivery.Owner.Outbox.OpenCount==0,"branch committed");
            Require(Quest(boot,QuestIds.Branch)==branch+1,"branch slot");
            Require(Held(boot,branch==0?"SeaGlass":"SpareLens")==1,"lens granted");
            var saved=boot.Saves!.Capture("slot-1"); Require(saved.Succeeded,saved.ToString());
            var restored=boot.Saves.Restore("slot-1"); Require(restored.Succeeded,restored.ToString());
            var roundtrip=boot.Saves.TestRoundTrip(); Require(roundtrip.Refusal==null && roundtrip.Equal,roundtrip.ToString());
            Require(roundtrip.SourceSlotHash==saved.SlotHash,"same slot hash after restore");
            Require(boot.Reattachments==1,"real GameBoot restore reattachment");
            Debug.Log("[Saltmarsh] branch="+branch+" captured/restored slot hash="+saved.SlotHash);
            yield return Travel(boot,"Lighthouse");
            var beacon=AuthoringIds.TargetIdFor(Id("Beacon"));
            Require(boot.World!.Commands.Place(boot.World.Focus,200000,0,4000,0).Admitted,"stand by beacon");
            yield return Until(()=>boot.World!.Slots.ReadOrDefault(boot.World.Focus,GameplaySlots.WorldOwner,GameplaySlots.PosZ,0)==4000,"beacon position");
            Require(new InteractionCommands(boot.World).Use(boot.World.Focus,boot.Narrative!.Runtime.ActorKey,beacon).Admitted,"use beacon");
            yield return Until(()=>Quest(boot,QuestIds.Status)==QuestIds.Completed && Fact(boot,"harbour_safe")==1 && Held(boot,"KeepersBadge")==1,"quest consequence and reward");
            Require(Held(boot,"CopperWire")==0,"wire consumed");
            yield return Until(()=>rig.Ui.Screen==UiScreen.Ending,"ending screen");
            var root=boot.World!.Root; int frame=Time.frameCount; int pumps=root.PumpCounter.SanctionedPumps;
            for(int i=0;i<20;i++) yield return null;
            Require(Math.Abs(root.PumpCounter.SanctionedPumps-pumps-(Time.frameCount-frame))<=1,"one pump per frame");
            Require(root.PumpCounter.Violations==0,"pump violations");
            Require(Held(boot,"KeepersBadge")==1,"reward is not duplicated after settling");
            Require(Fact(boot,branch==0?"spare_used":"salvaged")==0,"opposite branch remains unchosen");
            Require(rig.Ui.Models.Ending.Body.Contains(branch==0?"spare remains ready":"make a new reserve"),"branch-specific ending consequence");
        }
    }
}
