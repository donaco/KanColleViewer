using System;
using System.Collections.Generic;

namespace MetroTrilithon.Lifetime
{
    // MetroTrilithon.Lifetime の内製化 (Phase 1)
    public interface IDisposableHolder : IDisposable
    {
        ICollection<IDisposable> CompositeDisposable { get; }
    }
}
