using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Records;

public sealed record WithdrawalDetail(int Release, PublishedWithdrawal Withdrawal);
