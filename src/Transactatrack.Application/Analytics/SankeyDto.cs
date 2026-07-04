namespace Transactatrack.Application.Analytics;

/// <param name="Id">Namespaced node id, e.g. "income:{ownerId}", "account:{id}", "category:{id}", "transfersout".</param>
/// <param name="Kind">"income" | "account" | "category" | "sink".</param>
public record SankeyNodeDto(string Id, string Label, string Kind);

public record SankeyLinkDto(string Source, string Target, decimal Value);

public record SankeyDto(IReadOnlyList<SankeyNodeDto> Nodes, IReadOnlyList<SankeyLinkDto> Links);
