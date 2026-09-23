using CodedThought.Core.Data;

// CodedThought.Core scans for assemblies carrying this attribute when it builds the ORM map, so
// the entities in this assembly are discovered whether or not this is the calling assembly.
[assembly: DataAwareAssembly("true")]
