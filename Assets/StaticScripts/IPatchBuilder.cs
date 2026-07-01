using System;

public interface IPatchBuilder
{
        event Action OnPatchBuilt;
        bool HasBuiltPatch { get; } // Generic way to check if the patch is built
} 