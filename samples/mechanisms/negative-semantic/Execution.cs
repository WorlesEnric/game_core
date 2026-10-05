#nullable enable
using Launch = System.Diagnostics.Process;
using Web = System.Net.Http.HttpClient;
using Emit = System.Reflection.Emit.DynamicMethod;
public sealed class Execution
{
    public void Run()
    {
        Launch.Start("forbidden");
        new Web();
    }
    public Emit? Generated { get; set; }
    [System.Runtime.InteropServices.DllImport("forbidden")]
    private static extern void Native();
    public unsafe void Pointer() { int* pointer = null; }
}
