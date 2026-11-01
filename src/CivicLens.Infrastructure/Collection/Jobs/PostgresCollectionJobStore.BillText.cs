using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    public Task<DiscoveryAdmissionResult> AdmitTextVersionsAsync(ConfiguredCollectionSource source, string versionsJobId,
        CongressBillTextVersion? newest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionsJobId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await ReadGateOfPageJobAsync(db, versionsJobId, cancellationToken);
            if (newest is null)
            {
                gate.TextState = BillTextState.NoText;
                return NoAdmission;
            }
            if (await HasTextVersionAsync(db, gate, newest.PackageId, cancellationToken))
            {
                gate.TextState = BillTextState.Read;
                gate.TextPackageId = newest.PackageId;
                return NoAdmission with { DuplicateCount = 1 };
            }
            var textSource = source.EnabledBillTextSource()
                ?? throw new InvalidDataException("The configuration has no enabled bill text source.");
            var job = textSource.CreateBillTextJob(versionsJobId, newest.PackageId, BillGate.DayOf(UpdateDateOf(gate)), Today(now));
            return await AdmitFollowOnsAsync(db, textSource, gate.DetailJobId, [job], now, cancellationToken);
        }, cancellationToken);
    }

    public Task AdmitTextAsync(string textJobId, CongressBillTextEvidence evidence, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textJobId);
        ArgumentNullException.ThrowIfNull(evidence);
        return TransactionAsync(async (db, _) =>
        {
            await EnsureExtractionIsOfJobAsync(db, textJobId, evidence.ExtractionId, cancellationToken);
            var gate = await ReadGateOfPageJobAsync(db, textJobId, cancellationToken);
            gate.TextState = BillTextState.Read;
            gate.TextPackageId = evidence.Version.PackageId;
            if (!await HasTextVersionAsync(db, gate, evidence.Version.PackageId, cancellationToken))
                db.Add(NewTextVersionRow(gate, evidence));
            return 0;
        }, cancellationToken);
    }

    private static Task<bool> HasTextVersionAsync(CollectionAttemptDbContext db, BillGateRow gate, string packageId,
        CancellationToken cancellationToken) =>
        db.Set<BillTextVersionRow>().AnyAsync(row => row.Congress == gate.Congress && row.BillType == gate.BillType &&
            row.BillNumber == gate.BillNumber && row.PackageId == packageId, cancellationToken);

    private static BillTextVersionRow NewTextVersionRow(BillGateRow gate, CongressBillTextEvidence evidence) => new()
    {
        Congress = gate.Congress,
        BillType = gate.BillType,
        BillNumber = gate.BillNumber,
        PackageId = evidence.Version.PackageId,
        VersionType = evidence.Version.Type,
        VersionDate = evidence.Version.Date is { } date ? BillGate.DayOf(date) : null,
        SourceUrl = evidence.Version.Url,
        ExtractionId = evidence.ExtractionId,
        CharsTotal = evidence.Reading.CharsTotal,
        CharsRead = evidence.Reading.CharsRead
    };
}
