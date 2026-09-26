# Fixture resource policy: every signature the non-GameCore fixtures produce, each with a declared bound.

bound: Unity.Collections.NativeArray`1:.ctor(Int32,AllocatorHandle,Int32) [Unity] | capacity=8 | bytes<=65536 | released-by=UnityWorldHost.Stop world teardown (per-world allocator release)
bound: Unity.Collections.NativeList`1:.ctor(Int32,AllocatorHandle) [Unity] | capacity=4 | bytes<=16384 | released-by=probe endpoint teardown (persistent allocator disposal)
bound: System.Text.Json.JsonSerializer:Serialize(Object) [Mono JIT Code] | capacity=1 | bytes<=4096 | released-by=one-shot qualification serializer cache, released at the end of the run
