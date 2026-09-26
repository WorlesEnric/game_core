# Fixture resource policy with one incomplete declaration: capacity and a byte ceiling are stated, but nothing says
# what releases it, so it is a policy problem and cannot bound the signature it names.

bound: System.Text.Json.JsonSerializer:Serialize(Object) [Mono JIT Code] | capacity=1 | bytes<=4096
