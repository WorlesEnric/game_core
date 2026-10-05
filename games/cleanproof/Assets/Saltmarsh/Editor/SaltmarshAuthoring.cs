#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.Quest.Editor;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using Saltmarsh.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using AuthorOperation = GameCore.Studio.Model.AuthorOperationAttribute;
using AuthorArg = GameCore.Studio.Model.AuthorArgAttribute;

namespace Saltmarsh.Authoring
{
    public static class SaltmarshAuthoring
    {
        public const string Root = "Assets/Saltmarsh";
        public const string WorldPath = Root + "/World/Saltmarsh.asset";
        public const string ContentPath = Root + "/Content/SaltmarshContent.asset";
        public const string BootPath = "Assets/Boot/Boot.unity";
        public static string Id(string name)
        {
            using var hash = SHA256.Create();
            return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes("saltmarsh/" + name)).Take(16).ToArray()).ToString();
        }

        public static void AuthorAll()
        {
            if (!SceneManager.GetSceneByPath(BootPath).isLoaded) EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions { SearchFolders = new[] { Root } });
            foreach (string phase in new[] { "world", "narrative", "presentation", "bake", "boot" })
            {
                var op = new Operation("author-" + phase, "saltmarsh.author", null, new JObject { ["phase"] = phase }, null, Preconditions.None);
                var report = runtime.Engine.Apply(StudioRuntime.Single("Saltmarsh: " + phase + " (procedural placeholder content)", IntentOrigin.Manual, op));
                if (!report.Ok) throw new InvalidOperationException(StudioJson.Serialize(report.Entry));
            }
            var verified = Entry.Verify(Load<WorldDefinition>(WorldPath), BakePaths.ConventionFor(WorldPath));
            if (!verified.Succeeded) throw new InvalidOperationException(verified.ToString());
            Debug.Log("[Saltmarsh] AuthorAll + Entry.Verify passed: " + verified);
        }

        [AuthorOperation("saltmarsh.author", Doc = "Author the clean-proof game's deterministic content through the Studio journal.")]
        public static OperationResult Author(EditContext context, [AuthorArg] string phase)
        {
            string[] before = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories);
            context.Runtime.Engine.RunOutsideAssetEditing(() =>
            {
                switch (phase)
                {
                    case "world": World(); break;
                    case "narrative": Narrative(); break;
                    case "presentation": SaltmarshPresentation.Author(); break;
                    case "bake":
                        var result = Entry.Bake(Load<WorldDefinition>(WorldPath), BakePaths.ConventionFor(WorldPath), false);
                        if (!result.Succeeded) throw new InvalidOperationException(result.ToString());
                        break;
                    case "boot": Boot(); break;
                    default: throw new ArgumentException("Unknown authoring phase");
                }
                AssetDatabase.SaveAssets();
            });
            string[] created = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories).Except(before).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            return OperationResult.Applied(new JObject { ["phase"] = phase, ["created"] = new JArray(created) })
                .WithDetail("Created paths: " + string.Join(", ", created));
        }

        public static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
        public static T Asset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AssetDatabase.Refresh();
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            var serialized = new SerializedObject(asset);
            var id = serialized.FindProperty("authoringId");
            if (id != null) { id.stringValue = Id(asset.name); serialized.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        public static void Dirty(Object asset) => EditorUtility.SetDirty(asset);
        public static RegionDefinition Region(string name) => Load<RegionDefinition>(Root + "/World/" + name + ".asset");

        private static void World()
        {
            if (AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath) != null) return;
            Directory.CreateDirectory(Root + "/Regions");
            var scratch = SceneManager.GetSceneByPath(BootPath);
            SceneManager.SetActiveScene(scratch);
            var world = Asset<WorldDefinition>(WorldPath);
            string[] names = { "Harbour", "Dunes", "Lighthouse" };
            foreach (string name in names)
            {
                var region = Asset<RegionDefinition>(Root + "/World/" + name + ".asset");
                region.Configure(name, Root + "/Regions/" + name + ".unity");
                region.SetBounds(new Vector3(Array.IndexOf(names,name)*100,10,0),new Vector3(80,30,80));
                world.AddRegion(region); Dirty(region);
            }
            world.SetStartRegion(Region("Harbour")); world.SetFocusEntity(Id("Courier"));
            var playerEntity = Entity("Courier", new Color(.15f,.65f,.8f));
            var player = Asset<PlayerDefinition>(Root + "/World/Player.asset"); player.SetEntity(playerEntity);
            var actions = Asset<InputActionAsset>(Root + "/World/Controls.asset");
            var map = actions.AddActionMap("Player");
            map.AddAction("Move",InputActionType.Value).AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
            map.AddAction("Look",InputActionType.Value,"<Mouse>/delta");
            foreach (var binding in new[] { "Jump:space", "Run:leftShift", "Interact:e", "Pause:escape", "Journal:j", "Inventory:i" })
            { var pair=binding.Split(':'); map.AddAction(pair[0],InputActionType.Button,"<Keyboard>/"+pair[1]); }
            var input = Asset<InputProfile>(Root + "/World/Input.asset"); input.Configure(actions,"Player",.15f); player.SetInput(input);
            Dirty(actions); Dirty(input); Dirty(player);
            var roster = Asset<NpcRoster>(Root + "/World/Npcs.asset");
            var interactions = Asset<InteractionRoster>(Root + "/World/Interactions.asset");
            string[] people = { "Ada", "Neri", "Sol" };
            for (int i=0;i<names.Length;i++)
            {
                var region=Region(names[i]); var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var host=new GameObject(names[i]); host.transform.position=new Vector3(i*100,0,0);
                var authored=host.AddComponent<AuthoredRegion>(); authored.Configure(region,null);
                authored.Bounds.Configure(new Vector3(0,10,0),new Vector3(80,30,80));
                EditorSceneManager.SaveScene(scene,region.ScenePath);
                WorldTools.SetSpawnPoint(authored,new Vector3(i*100,0,-8),0);
                var ground=GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name="Procedural island platform";
                ground.transform.position=new Vector3(i*100,-.5f,0); ground.transform.localScale=new Vector3(70,1,70);
                if(i==0) Place(playerEntity,new Vector3(0,0,-8),"Courier");
                var entity=Entity(people[i],new Color(.9f,.55f,.2f)); Place(entity,new Vector3(i*100+3,0,0),people[i]);
                var npc=Asset<NpcDefinition>(Root+"/World/"+people[i]+"Npc.asset"); npc.Configure(entity,people[i],1.5f,"",""); roster.Add(npc); Dirty(npc);
                if(i==2)
                {
                    var beacon=Entity("Beacon",new Color(1,.9f,.5f)); Place(beacon,new Vector3(200,0,5),"Beacon");
                    var lever=Asset<InteractableDefinition>(Root+"/World/BeaconSwitch.asset");
                    lever.Configure(beacon,InteractableKind.Switch,3f,.5f,0);
                    lever.SetStates("off",new[]{new StatePrompt("off","Restore the beacon"),new StatePrompt("on","Beacon restored")});
                    interactions.Add(lever); Dirty(lever);
                    var tower=GameObject.CreatePrimitive(PrimitiveType.Cylinder); tower.name="Procedural lighthouse"; tower.transform.position=new Vector3(200,8,12); tower.transform.localScale=new Vector3(6,8,6);
                }
                EditorSceneManager.SaveScene(scene); Dirty(region);
            }
            for(int i=0;i<3;i++)
            {
                var from=Region(names[i]); var to=Region(names[(i+1)%3]);
                var portal=WorldTools.ConnectRegions(world,from,to,string.Empty);
                foreach(var end in new[]{from,to})
                {
                    var scene=SceneManager.GetSceneByPath(end.ScenePath);
                    SceneManager.SetActiveScene(scene);
                    var marker=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<AuthoredRegion>()).Single();
                    WorldTools.AddPortal(marker,portal,marker.transform.position+new Vector3(end==from?25:-25,0,i*8-8),0,2.5f);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            Dirty(world); Dirty(roster); Dirty(interactions);
            AssetDatabase.SaveAssets();
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(BootPath));
        }
        private static EntityDefinition Entity(string name, Color color)
        {
            string path=Root+"/World/"+name+"View.prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)
            {
                var body=GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name=name+" procedural placeholder";
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor",color);
                AssetDatabase.CreateAsset(material,Root+"/World/"+name+".mat"); body.GetComponent<Renderer>().sharedMaterial=material;
                prefab=PrefabUtility.SaveAsPrefabAsset(body,path); Object.DestroyImmediate(body);
            }
            var definition=Asset<EntityDefinition>(Root+"/World/"+name+"Entity.asset"); definition.Configure(prefab,1000,true,true); Dirty(definition); return definition;
        }
        private static void Place(EntityDefinition definition, Vector3 position,string name)
        {
            var entity=EntityTools.PlaceIn(SceneManager.GetActiveScene(),definition,position,0,name);
            var serialized=new SerializedObject(entity); serialized.FindProperty("authoringId").stringValue=Id(name); serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Definition<T>(GameplayContentSet set,string name) where T : NarrativeDefinitionAsset
        {
            var asset=Asset<T>(Root+"/Content/"+name+".asset"); asset.SetAuthoringId(Id(name)); set.Add(asset); Dirty(asset); return asset;
        }
        private static void Narrative()
        {
            if(AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath)!=null) return;
            var set=Asset<GameplayContentSet>(ContentPath); set.Configure(Load<WorldDefinition>(WorldPath),Array.Empty<ScriptableObject>());
            var accepted=Definition<FactDefinition>(set,"accepted"); accepted.Configure("accepted",0,true);
            var salvage=Definition<FactDefinition>(set,"salvaged"); salvage.Configure("salvaged",0,true);
            var spare=Definition<FactDefinition>(set,"spare_used"); spare.Configure("spare_used",0,true);
            var lit=Definition<FactDefinition>(set,"beacon_lit"); lit.Configure("beacon_lit",0,true);
            var safe=Definition<FactDefinition>(set,"harbour_safe"); safe.Configure("harbour_safe",0,true);
            var items=new List<ItemDefinition>();
            foreach(string name in new[]{"SeaGlass","SpareLens","CopperWire","KeepersBadge"})
            { var item=Definition<ItemDefinition>(set,name); item.Configure(name,8,100,0,new[]{"lighthouse"},null); items.Add(item); }
            var inventory=Definition<InventoryDefinition>(set,"CourierInventory"); inventory.Configure(12,20000,0,true,string.Empty);
            var quest=Definition<QuestDefinition>(set,"RelightTheCoast"); quest.Configure("Relight the Coast",null,new[]{"Recover sea glass","Use the keeper's spare"});
            int a=QuestTools.AddStage(quest,"Answer the signal","Speak with Ada in the harbour.");
            int b=QuestTools.AddStage(quest,"Choose a lens","Neri offers a spare; Sol can help recover sea glass.");
            int c=QuestTools.AddStage(quest,"Light the coast","Take copper wire to the lighthouse and restore its beacon.");
            QuestTools.AddObjective(quest,a,ObjectiveKind.Fact,accepted,"",1,0,"Accept Ada's request");
            QuestTools.AddObjective(quest,b,ObjectiveKind.Fact,salvage,"",1,1,"Recover sea glass");
            QuestTools.AddObjective(quest,b,ObjectiveKind.Fact,spare,"",1,2,"Use the spare lens");
            QuestTools.AddObjective(quest,c,ObjectiveKind.Fact,lit,"",1,0,"Restore the beacon");
            QuestTools.LinkReward(quest,RewardKind.Item,items[3],1,0); QuestTools.LinkReward(quest,RewardKind.Fact,safe,1,0);
            var intro=Definition<ActionSetDefinition>(set,"AcceptSignal"); intro.Configure(new[]{ActionEntry.Of(ActionKind.SetFact,accepted,1),ActionEntry.Of(ActionKind.StartQuest,quest,0),ActionEntry.Of(ActionKind.Grant,items[2],1)});
            var recover=Definition<ActionSetDefinition>(set,"RecoverGlass"); recover.Configure(new[]{ActionEntry.Of(ActionKind.SetFact,salvage,1),ActionEntry.Of(ActionKind.Grant,items[0],1)});
            var borrow=Definition<ActionSetDefinition>(set,"UseSpare"); borrow.Configure(new[]{ActionEntry.Of(ActionKind.SetFact,spare,1),ActionEntry.Of(ActionKind.Grant,items[1],1)});
            var light=Definition<ActionSetDefinition>(set,"LightBeacon"); light.Configure(new[]{ActionEntry.Of(ActionKind.SetFact,lit,1),ActionEntry.Of(ActionKind.Consume,items[2],1)});
            foreach(string person in new[]{"Ada","Neri","Sol"})
            {
                var graph=Definition<DialogueGraphDefinition>(set,person+"Dialogue"); graph.Configure(person,Id(person),0);
                int line=DialogueTools.AddLine(graph,person=="Ada"?"The beacon has gone dark. Will you carry our signal?":person=="Neri"?"We can use my spare lens, or recover the sea glass with Sol. Choose what the coast will remember.":"The dunes still hold old sea glass. Leave the spare for another storm.");
                if(person=="Neri")
                {
                    int choice=DialogueTools.AddChoice(graph,new List<string>{"Recover sea glass; keep the spare safe.","Use the spare; save time before nightfall."},null,line,"Which light will guide the ships?");
                    for(int i=0;i<2;i++) { int action=graph.AddNode(new DialogueNodeEntry{kind=DialogueNodeKind.Action,actions=i==0?recover:borrow}); graph.Link(choice,DialoguePort.Option,i,action); }
                }
                else if(person=="Ada") { int action=graph.AddNode(new DialogueNodeEntry{kind=DialogueNodeKind.Action,actions=intro}); graph.Link(line,DialoguePort.Next,0,action); }
                var npc=Load<NpcDefinition>(Root+"/World/"+person+"Npc.asset"); npc.SetDialogue(graph); Dirty(npc); Dirty(graph);
            }
            var condition=Definition<ConditionSetDefinition>(set,"HasWire"); condition.Configure(ConditionMode.All,new[]{ConditionEntry.Of(ConditionKind.ItemCount,items[2],CompareOp.GreaterOrEqual,1),ConditionEntry.Of(ConditionKind.QuestStage,quest,CompareOp.Equal,2)});
            var lever=Load<InteractableDefinition>(Root+"/World/BeaconSwitch.asset"); lever.SetCondition(condition,null,CompareOp.NotEqual,0); lever.SetActions(light,""); Dirty(lever);
            var consequence=Definition<RuleDefinition>(set,"SafeHarbour"); consequence.ConfigureTrigger(TriggerKind.QuestCompleted,quest,"",true,0);
            consequence.SetActions(null,new[]{ActionEntry.Text(ActionKind.ShowMessage,"The returning boats follow your light."),ActionEntry.Text(ActionKind.PlayAudio,"music.coast")}); consequence.ConfigureLimits(true,0,0,0);
            foreach(var def in set.Definitions) Dirty(def); Dirty(set);
        }
        private static void Boot()
        {
            var scene=SceneManager.GetSceneByPath(BootPath);
            SceneManager.SetActiveScene(scene);
            var existing=Object.FindFirstObjectByType<GameBoot>();
            if(existing==null)
            {
                var go=new GameObject("Saltmarsh GameBoot"); existing=go.AddComponent<GameBoot>();
                go.AddComponent<SaltmarshAutoplay>();
                var camera=Object.FindFirstObjectByType<Camera>();
                if(camera==null) { var cam=new GameObject("Main Camera"); cam.tag="MainCamera"; camera=cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>(); }
                camera.transform.SetPositionAndRotation(new Vector3(0,8,-14),Quaternion.Euler(25,0,0));
                var sun=new GameObject("Island sunlight").AddComponent<Light>(); sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(45,-30,0);
                existing.Configure(Load<RegionManifest>(Root+"/World/Saltmarsh.manifest.asset"),false);
                existing.ConfigureNarrative(Load<GameplayContentManifest>(Root+"/Content/SaltmarshContent.content.asset"));
                existing.ConfigureGameplay(Load<PlayerDefinition>(Root+"/World/Player.asset"),Load<NpcRoster>(Root+"/World/Npcs.asset"),Load<InteractionRoster>(Root+"/World/Interactions.asset"),camera);
                EditorSceneManager.SaveScene(scene);
            }
            EditorBuildSettings.scenes=new[]{BootPath,Region("Harbour").ScenePath,Region("Dunes").ScenePath,Region("Lighthouse").ScenePath}.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
        }
    }
}
