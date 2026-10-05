#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace Saltmarsh
{
    public static class Build
    {
        public static void BuildLinuxPlayer()
        {
            var verify=GameCore.Gameplay.Compile.Entry.Verify(Saltmarsh.Authoring.SaltmarshAuthoring.Load<GameCore.Gameplay.World.WorldDefinition>(Saltmarsh.Authoring.SaltmarshAuthoring.WorldPath),GameCore.Gameplay.Compile.BakePaths.ConventionFor(Saltmarsh.Authoring.SaltmarshAuthoring.WorldPath));
            if(!verify.Succeeded) throw new InvalidOperationException(verify.ToString());
            Directory.CreateDirectory("Builds/Linux");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.IL2CPP);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName="Builds/Linux/Saltmarsh.x86_64",target=BuildTarget.StandaloneLinux64,options=BuildOptions.None });
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result+": "+report.summary.totalErrors);
            UnityEngine.Debug.Log("[Saltmarsh] Linux IL2CPP build "+report.summary.totalSize+" bytes in "+report.summary.totalTime);
        }
    }
}
