using System;
using Octadock.Core.Licensing;

namespace Octadock.App.Tests.Fakes;

/// <summary>
/// An <see cref="ILicenseGate"/> that always allows — the default for feature-seam
/// tests, which exercise behaviour under an active trial/license. Gate refusal logic
/// itself is covered by LicenseGateTests in Octadock.Core.Tests.
/// </summary>
internal sealed class AllowAllLicenseGate : ILicenseGate
{
    public LicenseState State { get; } = new(LicenseMode.Trial, null, null, false, false, "trial");

    public bool AllowsFullUse => true;

    // Explicit empty accessors: this fake never raises the event (avoids CS0067).
    public event EventHandler<LicenseState>? Refused { add { } remove { } }

    public bool Allow(GatedFeature feature) => true;
}
