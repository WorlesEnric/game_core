#nullable enable
using Disk = System.IO.File;
public sealed class StateAndPaths
{
    private static readonly int[] Shared = new int[1];
    public void Run()
    {
        Disk.ReadAllText("/outside");
        Disk.WriteAllText("Assets/com.example.semantic-negative/../../escape", "data");
        Disk.WriteAllText("/outside", UnityEngine.Application.persistentDataPath);
        Disk.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "../escape"));
        UnityEngine.Resources.Load("/absolute");
        UnityEditor.AssetDatabase.Refresh();
    }
}
