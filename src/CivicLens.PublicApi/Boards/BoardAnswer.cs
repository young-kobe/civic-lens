namespace CivicLens.PublicApi.Boards;

public sealed record BoardAnswer(int Release, BoardPageAnswer[] Boards, BoardAgreementAnswer[] Agreements);
