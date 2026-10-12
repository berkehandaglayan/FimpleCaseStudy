using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed record DepositRequest(IReadOnlyList<DepositedNote>? Notes);
