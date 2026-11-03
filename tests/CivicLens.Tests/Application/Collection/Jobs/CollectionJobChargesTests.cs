using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobChargesTests
{
    private static readonly CollectionRequest Reserved = CollectionFixtures.Request();

    [Fact]
    public void ATransferWithinTheReservedRequestIsChargedBecauseTheUnusedAllowanceIsRefunded()
    {
        var transfer = new AttemptTransfer(Reserved.MaxRequests, Reserved.MaxBytes);

        Assert.Equal(transfer, CollectionJobCharges.Chargeable(Reserved, Reserved, transfer));
    }

    [Fact]
    public void ATransferOfADifferentRequestIsNotChargedBecauseItDoesNotProveUseOfTheReservation()
    {
        var other = Reserved with { MaxRequests = Reserved.MaxRequests + 1 };

        Assert.Null(CollectionJobCharges.Chargeable(Reserved, other, new AttemptTransfer(1, 1)));
    }

    [Fact]
    public void ATransferAboveTheReservedLimitIsNotChargedBecauseTheReservationBoundsWhatTheAttemptMayUse()
    {
        Assert.Null(CollectionJobCharges.Chargeable(Reserved, Reserved, new AttemptTransfer(Reserved.MaxRequests + 1, 1)));
        Assert.Null(CollectionJobCharges.Chargeable(Reserved, Reserved, new AttemptTransfer(1, Reserved.MaxBytes + 1)));
    }

    [Fact]
    public void AMissingTransferStaysFullyChargedBecauseUnknownUseIsNeverRefunded()
    {
        Assert.Null(CollectionJobCharges.Chargeable(Reserved, Reserved, null));
    }
}
